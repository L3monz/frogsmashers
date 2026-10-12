#!/usr/bin/env python3
"""Compare two replay recordings Tick by Tick.

Usage: tools/compare-recordings.py A.frogreplay B.frogreplay
       tools/compare-recordings.py --latest-online      (the newest slot0/slot1 pair in the
                                                        development build's replays folder)

Both Peers of an Online Match record the same Round. Their recordings must agree on the Round
Setup, the starting checksum, and every Tick's inputs and checksum, over the Ticks both kept.
Exits 0 when they do, 1 at the first difference.
"""
import glob
import os
import struct
import sys

SLOTS = 6
REPLAYS = "/mnt/c/Users/*/AppData/LocalLow/DefaultCompany/Free Lives Template/replays"


class Reader:
    def __init__(self, data):
        self.data, self.at = data, 0

    def take(self, fmt):
        size = struct.calcsize(fmt)
        value = struct.unpack_from("<" + fmt, self.data, self.at)
        self.at += size
        return value if len(value) > 1 else value[0]

    def string(self):
        # .NET BinaryWriter: a 7-bit-encoded length, then UTF-8 bytes.
        length, shift = 0, 0
        while True:
            b = self.take("B")
            length |= (b & 0x7F) << shift
            shift += 7
            if b < 0x80:
                break
        text = self.data[self.at:self.at + length].decode("utf-8")
        self.at += length
        return text


def read(path):
    r = Reader(open(path, "rb").read())
    if r.take("I") != 0x50525346:
        sys.exit(f"{path}: not a Frog Smashers round recording")
    version = r.take("i")
    rec = {"version": version, "level": r.string(), "seed": r.take("Q"),
           "flags": [r.take("?") for _ in range(6)]}
    rec["roster"] = [r.take("i") for _ in range(r.take("i"))]
    rec["slots"] = [(r.take("i"), r.take("i"), r.take("4f")) for _ in range(SLOTS)]
    rec["cap"] = r.take("i")
    rec["start"] = r.take("I")
    rec["round_over"] = r.take("?")
    ticks = r.take("i")
    rec["inputs"] = [r.take(f"{SLOTS}I") for _ in range(ticks)]
    rec["checksums"] = [r.take("I") for _ in range(ticks)]
    return rec


def latest_online_pair():
    files = sorted(f for d in glob.glob(REPLAYS) for f in glob.glob(os.path.join(d, "*-online-slot*.frogreplay")))
    slot0 = [f for f in files if "-online-slot0" in f]
    slot1 = [f for f in files if "-online-slot1" in f]
    if not slot0 or not slot1:
        sys.exit("No online recordings for both Slots in " + REPLAYS)
    return slot0[-1], slot1[-1]


def main():
    args = sys.argv[1:]
    if args == ["--latest-online"]:
        args = list(latest_online_pair())
    if len(args) != 2:
        sys.exit(__doc__)
    a, b = read(args[0]), read(args[1])
    print(f"A: {os.path.basename(args[0])}: {a['level']}, {len(a['checksums'])} ticks, round over: {a['round_over']}")
    print(f"B: {os.path.basename(args[1])}: {b['level']}, {len(b['checksums'])} ticks, round over: {b['round_over']}")

    for key in ("level", "seed", "flags", "roster", "slots", "start"):
        if a[key] != b[key]:
            print(f"DIFFERENT Round Setup: {key}: {a[key]} vs {b[key]}")
            return 1

    common = min(len(a["checksums"]), len(b["checksums"]))
    for tick in range(common):
        if a["inputs"][tick] != b["inputs"][tick]:
            print(f"DIFFERENT inputs at tick {tick}: {a['inputs'][tick]} vs {b['inputs'][tick]}")
            return 1
        if a["checksums"][tick] != b["checksums"][tick]:
            print(f"DIFFERENT state after tick {tick}: {a['checksums'][tick]:08X} vs {b['checksums'][tick]:08X}")
            return 1
    print(f"SAME: Round Setup, starting state, and all {common} ticks both kept (inputs and checksums)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
