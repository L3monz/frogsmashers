#!/usr/bin/env python3
"""Summarises the [SIM] lines that SimDiagnostics writes to the Unity Editor log.

Usage: tools/sim-log-report.py [path/to/Editor.log]

The default path is the Windows Editor.log, read through WSL. Unity overwrites
that file each time the editor starts and keeps the old one as Editor-prev.log.
Thus run this before you restart the editor.

Checks for the fixed tick:
- Tick rate: each full second at timeScale 1 must run 60 ticks (59-61 is
  acceptable, because a second boundary can fall between two frames).
- Jumps: holding jump keeps the frog rising, so the apex depends on how many
  ticks the button was held. Clean jumps (no ceiling, attack or hit) with the
  same hold length must give the same apex at every frame-rate cap. Apexes are
  compared to 4 decimals: the last float bits vary with the absolute start
  height, which is not a simulation difference.

A second with a frame rate below half the expected rate is a hitch (focus loss,
a device reconnecting, a long load). The driver deliberately skips that time
instead of fast-forwarding, so a hitch second runs fewer than 60 ticks. Hitches
are listed separately and are not failures.
"""
import collections
import os
import re
import sys

DEFAULT_LOG = "/mnt/c/Users/Lemons/AppData/Local/Unity/Editor/Editor.log"
# A development build's log. Pass "player" as the path to read it.
PLAYER_LOG = "/mnt/c/Users/Lemons/AppData/LocalLow/DefaultCompany/Free Lives Template/Player.log"

SECOND = re.compile(r"\[SIM\] second fps=([\d.]+) ticks=(\d+) tick=(\d+) drops=(\d+) cap=(\w+) timeScale=([\d.]+)(?: wall=([\d.]+) sim=([\d.]+))?")
JUMP = re.compile(r"\[SIM\] jump P(\S+) apex=(\S+) ticks=(\d+) hold=(\d+) clean=(\w+) cap=(\w+)")
HIT = re.compile(r"\[SIM\] hit P(\S+) hitsTaken=(\d+) by=P(\S+) velocity=\((\S+), (\S+)\) tick=(\d+)")
CAP_CHANGE = re.compile(r"\[SIM\] frame cap set to (\w+)")
SESSION = re.compile(r"\[SIM\] session start")
SYNC = re.compile(r"mode=(\w+) rollbacks=(\d+) rollbackTicks=(\d+) msPerTick=([\d.]+) desyncs=(\d+)")
ROUND = re.compile(r"\[SIM\] round start mode=(\w+) seed=(\d+) scene=(\S+)")
DESYNC = re.compile(r"\[SIM\] desync (.*)")
GC = re.compile(r"gc=(\d+) allocKB=(\d+)(?: tickKB=(\d+) saveKB=(\d+) build=(\w+))?")
FPS_CAP = re.compile(r"fps=([\d.]+) ticks=(\d+) .*cap=(\w+)")
INJECT = re.compile(r"\[SIM\] injected desync: nudging (Character\[\d+\])")


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_LOG
    if path == "player":
        path = PLAYER_LOG
    if not os.path.exists(path):
        sys.exit(f"log not found: {path}")

    with open(path, encoding="utf-8", errors="replace") as f:
        lines = [l.replace("\x00", "") for l in f if "[SIM]" in l]

    # Only the most recent play session. Each press of Play starts a new one.
    starts = [i for i, l in enumerate(lines) if SESSION.search(l)]
    if not starts:
        sys.exit("no [SIM] session in the log. Did the playtest run with the new scripts?")
    lines = lines[starts[-1]:]

    seconds_by_cap = collections.defaultdict(list)
    bad_seconds = []
    hitch_seconds = []
    idle_seconds = 0
    drops = 0
    # The first two report seconds cover the editor entering play mode and any one-time
    # work at scene start, so their tick counts mean nothing.
    startup_seconds = 2
    skip_next_second = False
    for line in lines:
        if CAP_CHANGE.search(line):
            skip_next_second = True  # that second mixes two caps
            continue
        m = SECOND.search(line)
        if not m:
            continue
        fps, ticks, _, d, cap, scale = float(m[1]), int(m[2]), int(m[3]), int(m[4]), m[5], float(m[6])
        drops += d
        if startup_seconds > 0:
            startup_seconds -= 1
            continue
        if skip_next_second or scale != 1.0:
            skip_next_second = False
            continue
        if m[8] is not None:
            # Newer logs carry the summed Time.deltaTime. The driver must turn exactly that
            # much time into ticks, give or take the fraction of a tick it carries over. A
            # stall makes the simulated time shorter than the wall time; that is a hitch.
            wall, sim = float(m[7]), float(m[8])
            if ticks == 0 and sim == 0:
                # No simulation in this scene (title, menus, score screen): the driver idles.
                idle_seconds += 1
                continue
            if d > 0 or sim < wall * 0.9:
                hitch_seconds.append(line.strip())
                continue
            seconds_by_cap[cap].append((fps, ticks))
            if abs(ticks - sim * 60) > 1.5:
                bad_seconds.append(line.strip())
            continue
        expected_fps = 60.0 if cap == "none" else float(cap)
        if fps < expected_fps / 2:
            hitch_seconds.append(line.strip())
            continue
        seconds_by_cap[cap].append((fps, ticks))
        if not 59 <= ticks <= 61:
            bad_seconds.append(line.strip())

    print("== Tick rate (target: 60 ticks per second at every cap)")
    for cap, rows in seconds_by_cap.items():
        fps = [r[0] for r in rows]
        ticks = [r[1] for r in rows]
        print(f"  cap={cap:>5}: {len(rows):3d}s  fps {min(fps):6.1f}-{max(fps):6.1f}  ticks {min(ticks)}-{max(ticks)}")
    print(f"  backlog drops: {drops}")
    print(f"  idle seconds, no simulation in the scene (not counted): {idle_seconds}")
    print(f"  hitch seconds (not counted): {len(hitch_seconds)}")
    for line in hitch_seconds[:5]:
        print("   ", line)
    tested = ", ".join(sorted(seconds_by_cap)) or "none"
    print(f"  caps tested: {tested} (a full check uses none, 144, 60, 30, 20; F3 cycles them)")
    print("  RESULT:", "PASS" if not bad_seconds else f"FAIL ({len(bad_seconds)} seconds outside 59-61)")
    for line in bad_seconds[:10]:
        print("   ", line)

    # A human cannot repeat a jump exactly, and some obstructions (a ceiling clamping the
    # position) are not caught by the clean check, so a hold length can give several apexes.
    # Obstruction only ever lowers a jump. So compare the highest apex per cap, and only for
    # caps with enough samples to have one unobstructed jump.
    print("\n== Jumps (indicative: highest apex per hold length, caps with 3+ samples)")
    by_hold = collections.defaultdict(lambda: collections.defaultdict(list))
    dirty = 0
    for line in lines:
        m = JUMP.search(line)
        if not m:
            continue
        if m[5] != "yes":
            dirty += 1
            continue
        by_hold[int(m[4])][m[6]].append(round(float(m[2]), 3))
    compared = differs = 0
    for hold in sorted(by_hold):
        caps = {c: v for c, v in by_hold[hold].items() if len(v) >= 3}
        if len(caps) < 2:
            continue
        compared += 1
        maxima = {c: max(v) for c, v in caps.items()}
        flag = ""
        if len(set(maxima.values())) > 1:
            differs += 1
            flag = "  <-- differs"
        detail = "  ".join(f"{c}: {mx:.3f} (n={len(caps[c])})" for c, mx in sorted(maxima.items()))
        print(f"  hold={hold:3d}  {detail}{flag}")
    print(f"  jumps with a ceiling, attack or hit (not counted): {dirty}")
    if compared == 0:
        print("  RESULT: INCOMPLETE (no hold length had 3+ samples at two caps)")
    else:
        print(f"  RESULT: {compared - differs} of {compared} hold lengths agree across caps")

    print("\n== Sync Test (target: rollbacks keep happening, zero desyncs)")
    rounds = [ROUND.search(l) for l in lines]
    for r in (r for r in rounds if r):
        print(f"  round start: {r[3]}  mode={r[1]}  seed={r[2]}")
    per_mode = collections.defaultdict(lambda: [0, 0, 0, 0.0])  # seconds, rollbacks, rollback ticks, ms sum
    last_desyncs = 0
    for line in lines:
        m = SYNC.search(line)
        if not m:
            continue
        acc = per_mode[m[1]]
        acc[0] += 1
        acc[1] += int(m[2])
        acc[2] += int(m[3])
        acc[3] += float(m[4])
        last_desyncs = int(m[5])
    for mode, (secs, rb, rbt, ms) in sorted(per_mode.items()):
        print(f"  {mode:>8}: {secs:4d}s  rollbacks={rb}  rollback ticks={rbt}  avg ms per tick={ms / max(secs, 1):.3f}")
    # A desync right after F9 is the checker working; any other desync is a real bug.
    expected_target = None
    unexpected, caught, missed = [], [], []
    for line in lines:
        inj = INJECT.search(line)
        if inj:
            if expected_target:
                missed.append(expected_target)
            expected_target = inj[1]
            continue
        d = DESYNC.search(line)
        if not d:
            continue
        text = d[1]
        if text.startswith("tick="):
            current = {"header": text, "fields": [], "expected": expected_target}
            (caught if expected_target else unexpected).append(current)
            expected_target = None
        elif text.startswith("field ") and (caught or unexpected):
            (caught + unexpected)[-1]["fields"].append(text[6:])
    if expected_target:
        missed.append(expected_target)

    for c in caught:
        named = any(f.startswith(c["expected"] + ".position") for f in c["fields"])
        print(f"  injected desync on {c['expected']}: caught ({c['header']}), names the position: {'yes' if named else 'NO'}")
        for f in c["fields"][:3]:
            print(f"      {f}")
    for m in missed:
        print(f"  injected desync on {m}: NOT caught")
    for u in unexpected:
        print(f"  UNEXPECTED DESYNC {u['header']}")
        for f in u["fields"]:
            print(f"      {f}")

    if "SyncTest" in per_mode:
        rolled = per_mode["SyncTest"][1] > 0
        ok = rolled and not unexpected
        reason = "unexpected desyncs" if unexpected else "no rollbacks recorded"
        print("  RESULT:", "PASS" if ok else f"FAIL ({reason})")
    else:
        print("  no Sync Test seconds recorded")

    print("\n== Replay check (target: every replay PASS, at a different cap than recorded)")
    replay_lines = [l for l in lines if re.search(r"\[SIM\] (recording saved|recording not saved|replay )", l)]
    for line in replay_lines:
        print("  " + line.split("[SIM] ", 1)[1].strip())
    if not replay_lines:
        print("  no recordings or replays in this log")

    print("\n== Garbage (target: near-zero allocation per Tick; no frequent collections)")
    gc_rows = collections.defaultdict(lambda: [0, 0, 0, 0, 0, 0.0])  # seconds, collections, total, tick, save, fps
    where = "editor"
    for line in lines:
        g, m, f = GC.search(line), SYNC.search(line), FPS_CAP.search(line)
        if not (g and m and f) or int(f[2]) == 0:
            continue
        if g[5]:
            where = g[5]
        acc = gc_rows[(m[1], f[3])]
        acc[0] += 1
        acc[1] += int(g[1])
        acc[2] += int(g[2])
        acc[3] += int(g[3] or 0)
        acc[4] += int(g[4] or 0)
        acc[5] += float(f[1])
    print(f"  measured in: {where}  (KB per second; 'other' is view code and, in the editor, the editor)")
    print("      mode   cap   secs   fps  gc/s   total    tick (save)   other   KB/frame")
    for (mode, cap), (secs, gcs, total, tick, save, fps) in sorted(gc_rows.items()):
        n = max(secs, 1)
        other = max(0, total - tick)
        print(f"  {mode:>8} {cap:>5} {secs:5d} {fps / n:6.1f} {gcs / n:5.2f} {total / n:7.0f} {tick / n:7.0f} ({save / n:4.0f}) {other / n:7.0f} {other / max(fps, 1):8.2f}")

    print("\n== Hits")
    hits = [HIT.search(l) for l in lines]
    hits = [h for h in hits if h]
    for h in hits:
        print(f"  P{h[3]} hit P{h[1]} (hitsTaken={h[2]}) velocity=({h[4]}, {h[5]}) tick={h[6]}")
    if not hits:
        print("  no hits recorded")


if __name__ == "__main__":
    main()
