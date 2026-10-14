# Frog Smashers Online: project notes

Notes for the project "FrogSmashers: Rollback Netcode": the game, the rollback library and the
server, and how they fit together. Markdown with Mermaid diagrams, so it reads the same in
Obsidian (open this `FrogSmashers/` folder as a vault, or add it to one) and on GitHub.

## Architecture

How the whole system fits together, from the widest view to the narrowest. Start with the first
one; each stands on its own.

1. [System context](Technical/01-system-context.md): the two players' PCs, the
   Hootenanny server on AWS, itch.io and GitHub, and what travels between them.
2. [Inside one game](Technical/02-inside-one-game.md): the Unity adapter,
   `SimulationDriver`, Hindsight and `FrogSimulation`; the lobby and transport seams, and what a
   Steam build would swap.
3. [One online Tick](Technical/03-one-online-tick.md): Poll, Rollback,
   `AddLocalInput`, `AdvanceFrame`, with prediction, the input packet and acknowledgements.
4. [Connecting](Technical/04-connecting.md): create a Room, join by code, NAT
   introduction, direct or relay, handshake, Tick 0.
5. [Infrastructure and CI/CD](Technical/05-infrastructure-and-ci.md): the Terraform
   stacks, GitHub OIDC roles, deploying through SSM, logs and alarms; the game's local IL2CPP
   build and the push to itch.io.

Each diagram names the commits it was checked against. When the code changes what a diagram
shows, update the diagram in the same pull request and move its commits forward.

## The repositories

| Repo | What |
|---|---|
| [L3monz/frogsmashers](https://github.com/L3monz/frogsmashers) | The Unity game, with online play. These notes live here, in `FrogSmashers/`. |
| [L3monz/hindsight](https://github.com/L3monz/hindsight) | Hindsight, the engine-agnostic rollback netcode library. The game uses it as two DLLs. |
| L3monz/hootenanny (private) | The rendezvous and relay server, its shared protocol DLL, and its AWS infrastructure. |

## Vocabulary

Each repo has a `CONTEXT.md` with its terms. The diagrams use the same words. Some names differ
between the repos:

| Hindsight | Frog Smashers | Hootenanny |
|---|---|---|
| Frame | Tick (a Frame is a Render Frame in the game) | — |
| Peer (a machine) | Peer | Client (a connection to the server) |
| Player (an input slot) | Slot (Player is the person) | — |
| — | Room Owner / friend | Room Owner / Guest |
