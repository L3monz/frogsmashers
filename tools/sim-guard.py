#!/usr/bin/env python3
"""Fails if simulation code uses a construct that cannot be deterministic (SMA-18).

Usage: tools/sim-guard.py [--strict]

Checks every file listed in tools/sim-files.txt. A rollback replays simulation code
and expects the same result, so that code must not read the wall clock, draw from an
unsynced random stream, call a transcendental function, depend on unordered
iteration, or read devices directly.

A one-time determinism test cannot catch a regression that someone adds later. This
guard can, so run it with every change. tools/compile-check.sh runs it after a clean
compile.

To accept a line, put a marker comment on that line or the line above it:

    // sim-guard: allow <reason>       Not simulation, or safe here. Say why.
    // sim-guard: todo SMA-NN <note>   A known problem with a ticket.

An "allow" with no reason is an error. A "todo" is reported but does not fail the
check, unless --strict is given. A marker that matches nothing is reported too, so
stale markers do not pile up.

This is a line-based check, not a compiler. It strips comments and string literals,
then matches patterns. It can miss a construct that is hidden behind an alias or a
helper method, so it is a safety net, not a proof. The proof is the sync test.
"""
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "HohimBrueh", "Assets")
FILE_LIST = os.path.join(ROOT, "tools", "sim-files.txt")

RULES = [
    ("wall-clock",
     re.compile(r"\bTime\.\w+|\bDateTime\b|\bStopwatch\b|\bEnvironment\.TickCount"),
     "reads the wall clock or Unity's frame time; use the tick (SimClock) instead"),
    ("unsynced-random",
     re.compile(r"\bRandom\b"),
     "uses an unsynced random stream; use SimRng instead"),
    ("transcendental",
     re.compile(r"\b(Mathf|Math|MathF)\.(Sin|Cos|Tan|Asin|Acos|Atan2?|Sinh|Cosh|Tanh|Pow|Exp|Log|Log10|Log2|Cbrt|PerlinNoise)\b"),
     "IEEE 754 does not specify this function, so platforms disagree; use SimMath"),
    ("unordered",
     re.compile(r"\bHashSet<|\bDictionary<|\.GetHashCode\(|\bGetInstanceID\(|\bFindObjectsOfType\b|\bFindObjectOfType\b"),
     "iteration order or the value can differ between machines or runs"),
    ("direct-input",
     re.compile(r"\bInput\.\w+"),
     "reads a device directly; input must come through SampleInput so a replay can supply it"),
    ("editor-only",
     re.compile(r"\bApplication\.isEditor\b"),
     "behaves differently in the editor and in a build"),
]

MARKER = re.compile(r"//\s*sim-guard:\s*(allow|todo)\b(.*)")
TODO_TICKET = re.compile(r"^\s*SMA-\d+\b")
STRING = re.compile(r'@"(?:[^"]|"")*"|\$?"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])\'')


def strip_code(line, in_block):
    """Returns the line without comments and string literals, and the block-comment state."""
    out = ""
    i = 0
    while i < len(line):
        if in_block:
            end = line.find("*/", i)
            if end < 0:
                return out, True
            i = end + 2
            in_block = False
            continue
        rest = line[i:]
        if rest.startswith("//"):
            break
        if rest.startswith("/*"):
            in_block = True
            i += 2
            continue
        m = STRING.match(rest)
        if m:
            out += '""'
            i += len(m.group(0))
            continue
        out += line[i]
        i += 1
    return out, in_block


def read_file_list():
    with open(FILE_LIST, encoding="utf-8") as f:
        return [l.strip() for l in f if l.strip() and not l.strip().startswith("#")]


def check_file(rel):
    path = os.path.join(ASSETS, rel)
    with open(path, encoding="utf-8-sig") as f:
        lines = f.read().splitlines()

    markers = {}  # line index -> (kind, text)
    for i, line in enumerate(lines):
        m = MARKER.search(line)
        if m:
            markers[i] = (m.group(1), m.group(2).strip())

    violations, todos, errors = [], [], []
    used = set()
    in_block = False
    for i, line in enumerate(lines):
        code, in_block = strip_code(line, in_block)
        for name, pattern, why in RULES:
            if not pattern.search(code):
                continue
            # A marker applies to its own line, or to the next line if it stands alone.
            marker_line = None
            if i in markers:
                marker_line = i
            elif i - 1 in markers and not strip_code(lines[i - 1], False)[0].strip():
                marker_line = i - 1
            where = f"{rel}:{i + 1}"
            if marker_line is None:
                violations.append(f"{where}: [{name}] {why}\n    {line.strip()}")
                continue
            used.add(marker_line)
            kind, text = markers[marker_line]
            if kind == "allow" and not text:
                errors.append(f"{where}: 'sim-guard: allow' needs a reason")
            elif kind == "todo" and not TODO_TICKET.match(text):
                errors.append(f"{where}: 'sim-guard: todo' needs a ticket, like 'todo SMA-23 ...'")
            elif kind == "todo":
                todos.append(f"{where}: [{name}] {text}")

    for i in sorted(set(markers) - used):
        errors.append(f"{rel}:{i + 1}: marker matches nothing; remove it")
    return violations, todos, errors


def main():
    strict = "--strict" in sys.argv[1:]
    files = read_file_list()
    violations, todos, errors = [], [], []
    for rel in files:
        if not os.path.exists(os.path.join(ASSETS, rel)):
            errors.append(f"{rel}: listed in tools/sim-files.txt but not found")
            continue
        v, t, e = check_file(rel)
        violations += v
        todos += t
        errors += e

    for line in violations:
        print("VIOLATION " + line)
    for line in errors:
        print("ERROR " + line)
    for line in todos:
        print("TODO " + line)

    failed = violations or errors or (strict and todos)
    print(f"sim-guard: {len(files)} files, {len(violations)} violations, "
          f"{len(errors)} errors, {len(todos)} known todos"
          + (" -- FAILED" if failed else " -- ok"))
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
