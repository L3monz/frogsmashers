#!/usr/bin/env python3
"""Summarises the [SIM] lines that SimDiagnostics writes to the Unity Editor log.

Usage: tools/sim-log-report.py [path/to/Editor.log]

The default path is the Windows Editor.log, read through WSL. Unity overwrites
that file each time the editor starts and keeps the old one as Editor-prev.log.
Thus run this before you restart the editor.

Checks for the fixed tick (SMA-19):
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

SECOND = re.compile(r"\[SIM\] second fps=([\d.]+) ticks=(\d+) tick=(\d+) drops=(\d+) cap=(\w+) timeScale=([\d.]+)(?: wall=([\d.]+) sim=([\d.]+))?")
JUMP = re.compile(r"\[SIM\] jump P(\S+) apex=(\S+) ticks=(\d+) hold=(\d+) clean=(\w+) cap=(\w+)")
HIT = re.compile(r"\[SIM\] hit P(\S+) hitsTaken=(\d+) by=P(\S+) velocity=\((\S+), (\S+)\) tick=(\d+)")
CAP_CHANGE = re.compile(r"\[SIM\] frame cap set to (\w+)")
SESSION = re.compile(r"\[SIM\] session start")


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_LOG
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
    print(f"  hitch seconds (not counted): {len(hitch_seconds)}")
    for line in hitch_seconds[:5]:
        print("   ", line)
    tested = ", ".join(sorted(seconds_by_cap)) or "none"
    print(f"  caps tested: {tested} (the plan covers none, 144, 60, 30, 20)")
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

    print("\n== Hits")
    hits = [HIT.search(l) for l in lines]
    hits = [h for h in hits if h]
    for h in hits:
        print(f"  P{h[3]} hit P{h[1]} (hitsTaken={h[2]}) velocity=({h[4]}, {h[5]}) tick={h[6]}")
    if not hits:
        print("  no hits recorded")


if __name__ == "__main__":
    main()
