# Frog Smashers Online (modified fork)

**This is a modified version of Frog Smashers, not the original.** It adds online play with
rollback netcode. Frog Smashers is copyright Ruan Rothmann; this fork is based on the
Frog Smashers source code and follows its license ([LICENSE.md](./LICENSE.md)): no commercial
use, credit the original, mark changes as ours. The original README is below.

Related repositories:

| Repo | What |
|---|---|
| [L3monz/rollback](https://github.com/L3monz/rollback) (Hindsight) | The rollback netcode library, engine-agnostic C#. The game uses it as two DLLs. |
| L3monz/hootenanny (private; ask for access) | The server that matches players by Room Code and relays traffic. Runs on AWS. |

Work is tracked in Linear (project "FrogSmashers: Rollback Netcode").

## Setup

- **Unity 2019.2.12f1** (exactly this version) with the **Windows Build Support (IL2CPP)**
  module, installed through Unity Hub at its default path.
- **Visual Studio 2019 or later with "Desktop development with C++"**, which IL2CPP needs to
  build Windows players.
- **WSL (Ubuntu)** for the scripts in `tools/`. They call the Windows Unity and .NET.
- **.NET SDK** on Windows, for `tools/update-hindsight.sh`.
- **butler** (itch.io's upload tool), only to publish releases: see below.

Open `HohimBrueh/` in Unity Hub once before using the scripts; they need its `Library/` folder.

## Day to day

Close Unity before running a script that opens the project: the editor locks it.

```
tools/compile-check.sh            # compile, determinism guard, state coverage (Unity may stay open)
tools/run-editmode-tests.sh       # EditMode tests in batch mode (Unity closed)
tools/update-hindsight.sh         # rebuild Hindsight from ../hindsight and copy in its DLLs (Unity closed)
```

`compile-check.sh` must pass before every commit that touches game code. Simulation files
(listed in `tools/sim-files.txt`) must not read the clock, draw unsynced random numbers or read
devices: the guard enforces it.

Never edit scripts while the editor is in Play mode: Unity reloads them mid-play.

## Building

All builds use IL2CPP and run on your own PC. (CI builds wait for a Unity upgrade: there is no
GameCI image that builds Unity 2019.2 with IL2CPP for Windows.)

```
tools/build.sh dev        # builds/dev/FrogSmashers.exe: development build, diagnostics on
tools/build.sh release    # builds/release/FrogSmashers.exe: what players get
```

Every build carries a **build ID**: the short commit hash, plus `-dirty` when tracked files
had uncommitted changes. It shows small on the title screen and in `Player.log`. Online,
two different builds refuse to connect, because they might simulate differently, so both
players must run the identical build. `LICENSE.md` is copied next to the exe, as the license
requires.

The first build takes several minutes; later ones about a minute and a half. Logs:
`HohimBrueh/Logs/build-dev.log` and `build-release.log`.

## Publishing a release to itch.io

Releases go to [blemonz.itch.io/frog-smashers-online](https://blemonz.itch.io/frog-smashers-online),
channel `windows`. The page is **Restricted**: players need a download key, which the page owner
creates on the itch.io dashboard (Distribute → Download keys).

One-time setup for each person who publishes:

1. The page owner adds your itch.io account as an admin of the project, in the project's settings on itch.io.
2. Install butler in WSL (https://itch.io/docs/butler/installing.html), for example into
   `~/.local/bin/butler`.
3. Run `butler login` and approve it in the browser. Your key is stored in
   `~/.config/itch/butler_creds`. Never commit it or paste it anywhere.

To publish:

```
git commit ...                          # the release must match a commit
tools/release-itch.sh 0.2.0 --dry-run   # optional: shows what would be uploaded
tools/release-itch.sh 0.2.0             # builds the release player and pushes it
git tag v0.2.0 <commit> && git push origin v0.2.0   # the script prints this line
```

The script refuses to run with uncommitted changes, builds into a clean folder, checks that
`LICENSE.md` is there, and leaves IL2CPP's debug symbols
(`*_BackUpThisFolder_ButDontShipItWithYourGame`) out of the upload. Players who installed
through the itch app get the update automatically.

Use a new version number for every push. Bump the middle number for a release with new
features, the last for fixes.

## The server

Online play finds the other player through the Hootenanny server (private repo above). It
deploys itself: every merge to its `main` branch runs tests, applies Terraform and restarts the
server on AWS. Its README covers logs, alarms and setup. When the game and the server both
change their shared protocol, deploy the server first: it accepts the current and the previous
protocol version.

## Diagnosing

- Player builds log to `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Free Lives Template\Player.log`.
- `tools/sim-log-report.py player` summarises the `[SIM]` diagnostics from that log (tick
  timing, Rollbacks, garbage). Without an argument it reads the editor's log.
- In development builds: F4 switches the next Round between Sync Test and Direct mode, F9 plants
  a Desync to prove the check works, F10 replays the last Round to check the frame rate doesn't
  change the simulation.

---

# Original README

# frogsmashers
Open source release for Frog Smashers

raithza.itch.io/frogsmashers

A few people have, over time, asked for updates or the source to Frog Smashers. I really like the game as it is, and although there certainly is plenty that could be added or improved, I just don't see myself having the time to work on it anytime soon. I've asked permission from my main collaborator, Mike, and I can finally at least release the Unity project for the game.

I think this is a relatively clean and straightforward project, and might be good for people wanting to learn from it.  Like any piece of software, there are some weird decisions, perhaps specifically with how input is being handled as I've switched away from using InControl to using Unity's new input system, but overall I think things are done in a sane, readable way (much easier when the scope is as small as this game). I'm happy to answer any questions anyone might have on the itch.io forum for the game.

Everyone is welcome to take this and make any kind of spinoff or modded version of the game. License is WIP but hopefully very permissive as long as you don't charge money for anything you make using this without our permission, or try to pass it off as entirely your own work.

If you REALLY like Frog Smashers and might be interested (and capable) of working on it in a more serious fashion, or perhaps interested in publishing, contact me or Mike.

FROG SMASHERS is a game by:

Programming/Design: Ruan Rothmann @rrza
Art/Animation: Mike Scott @mikescottiskiff
Additional Art: Ben Rausch (@Ben_Rausch ) & Stuart Coutts
Sound: Jason Sutherland