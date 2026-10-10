#!/usr/bin/env python3
"""Fails if a simulated class has state that its SyncState does not save (spec D20).

Usage: tools/state-coverage.py

For each file in tools/sim-files.txt that defines `SyncState(StateStream ...)`, this
finds every field and auto-property of that class that code assigns anywhere outside
its declaration: `x = ...`, `x += ...`, `x++`, and assignment to a member, `x.y = ...`.
Each one must be either:

- named with nameof(...) inside the class's SyncState, or
- marked on its declaration line (or the line above) with
  `// sim-state: exclude <reason>`.

Fields that nothing assigns after their declaration are inspector tuning values or
constants. They need nothing.

A field that is missing from SyncState is the classic rollback bug: a Rollback does not
restore it, and the result is a Desync somewhere far from the cause. This check finds
it at compile time instead.

Like sim-guard.py, this matches lines; it is not a compiler. It cannot see state that
changes inside another object's method, and a local variable with a field's name counts
as an assignment to the field (which only means an extra decision). The Sync Test is the
proof; this is the safety net.
"""
import importlib.util
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "HohimBrueh", "Assets")

_spec = importlib.util.spec_from_file_location("sim_guard", os.path.join(ROOT, "tools", "sim-guard.py"))
sim_guard = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(sim_guard)
strip_code = sim_guard.strip_code

MODIFIERS = r"(?:(?:public|private|protected|internal|static|readonly|new)\s+)*"
TYPE = r"[A-Za-z_][\w.]*(?:<[^;=(){}]*>)?(?:\[\])*\??"
FIELD = re.compile(r"^    " + MODIFIERS + r"(" + TYPE + r")\s+([A-Za-z_]\w*(?:\s*,\s*[A-Za-z_]\w*)*)\s*(?:=[^;]*)?;")
AUTO_PROP = re.compile(r"^    " + MODIFIERS + r"(" + TYPE + r")\s+([A-Za-z_]\w*)\s*\{[^}]*\bset;[^}]*\}")
# The first line of a property whose accessors are on the following lines.
PROP_HEAD = re.compile(r"^    " + MODIFIERS + r"(" + TYPE + r")\s+([A-Za-z_]\w*)\s*$")
COLLECTION = re.compile(r"<|\[\]")
EXCLUDE = re.compile(r"//\s*sim-state:\s*exclude\b(.*)")
SYNC_START = re.compile(r"\bvoid\s+SyncState\s*\(\s*StateStream\b")
NAMEOF = re.compile(r"nameof\(\s*([A-Za-z_]\w*)\s*\)")
ASSIGN_OP = r"(?:=(?!=)|\+=|-=|\*=|/=|\|=|&=|\+\+|--)"


def read_lines(path):
    with open(path, encoding="utf-8-sig") as f:
        return f.read().splitlines()


def code_lines(lines):
    out, in_block = [], False
    for line in lines:
        code, in_block = strip_code(line, in_block)
        out.append(code)
    return out


def block_range(code, start):
    """Line range of the brace block that starts at or after line `start`."""
    depth, opened = 0, False
    for i in range(start, len(code)):
        for ch in code[i]:
            if ch == "{":
                depth += 1
                opened = True
            elif ch == "}":
                depth -= 1
        if opened and depth == 0:
            return start, i
    return start, len(code) - 1


def declarations(lines, code):
    """Fields and auto-properties declared at class level (four-space indent).

    Returns {name: (declaration line, is a collection)}.
    """
    found = {}
    for i, line in enumerate(code):
        if " const " in line or line.strip().startswith(("return", "using")):
            continue
        m = AUTO_PROP.match(line)
        if m:
            found[m.group(2)] = (i, bool(COLLECTION.search(m.group(1))))
            continue
        m = PROP_HEAD.match(line)
        if m and m.group(1) not in ("return", "else", "new"):
            # An auto-property written over several lines: "{", "get; set;", "}".
            body = " ".join(code[i + 1:i + 6])
            end = body.find("}")
            accessors = body[:end] if end >= 0 else body
            if "set;" in accessors and "get;" in accessors and "return" not in accessors:
                found[m.group(2)] = (i, bool(COLLECTION.search(m.group(1))))
            continue
        m = FIELD.match(line)
        if m and m.group(1) not in ("return", "else", "new", "throw", "yield"):
            for name in re.split(r"\s*,\s*", m.group(2)):
                found[name] = (i, bool(COLLECTION.search(m.group(1))))
    return found


def exclusion(lines, decl_line):
    for i in (decl_line, decl_line - 1):
        if i >= 0:
            m = EXCLUDE.search(lines[i])
            if m:
                return m.group(1).strip()
    return None


def assigned_names(all_code, names, skip):
    """Names assigned anywhere in the simulation files, outside declarations and SyncState."""
    assigned = set()
    patterns = {
        n: re.compile(
            r"(?:(?<![\w.])" + n + r"|\." + n + r")\s*" + ASSIGN_OP
            + r"|(?:\+\+|--)\s*(?:this\.)?" + n + r"\b"
            + r"|(?<![\w.])" + n + r"\.\w+(?:\.\w+)*\s*" + ASSIGN_OP)
        for n in names
    }
    for path, code in all_code.items():
        for i, line in enumerate(code):
            if (path, i) in skip:
                continue
            for n, p in patterns.items():
                if n not in assigned and p.search(line):
                    assigned.add(n)
    return assigned


def main():
    files = sim_guard.read_file_list()
    all_lines = {f: read_lines(os.path.join(ASSETS, f)) for f in files if os.path.exists(os.path.join(ASSETS, f))}
    all_code = {f: code_lines(l) for f, l in all_lines.items()}

    problems, checked = [], []
    for rel, lines in all_lines.items():
        code = all_code[rel]
        starts = [i for i, l in enumerate(code) if SYNC_START.search(l)]
        if not starts:
            continue
        checked.append(rel)
        sync_lo, sync_hi = block_range(code, starts[0])
        synced = set()
        for i in range(sync_lo, sync_hi + 1):
            synced.update(NAMEOF.findall(code[i]))

        decls = declarations(lines, code)
        skip = {(rel, i) for i, _ in decls.values()} | {(rel, i) for i in range(sync_lo, sync_hi + 1)}
        assigned = assigned_names(all_code, decls.keys(), skip)

        for name, (decl_line, is_collection) in sorted(decls.items(), key=lambda kv: kv[1][0]):
            reason = exclusion(lines, decl_line)
            where = f"{rel}:{decl_line + 1}"
            if reason is not None and not reason:
                problems.append(f"ERROR {where}: 'sim-state: exclude' on {name} needs a reason")
            elif reason is not None and name in synced:
                problems.append(f"ERROR {where}: {name} is both synced and excluded")
            elif reason is None and name in assigned and name not in synced:
                problems.append(f"MISSING {where}: {name} is assigned but not in SyncState and not excluded")
            elif reason is None and is_collection and name not in synced:
                # A collection can change through method calls (Add, Remove, Clear) that a
                # line match cannot see, so it always needs a decision.
                problems.append(f"MISSING {where}: {name} is a collection; sync it or exclude it with a reason")

    for p in problems:
        print(p)
    status = "FAILED" if problems else "ok"
    print(f"state-coverage: {len(checked)} classes, {len(problems)} problems -- {status}")
    sys.exit(1 if problems else 0)


if __name__ == "__main__":
    main()
