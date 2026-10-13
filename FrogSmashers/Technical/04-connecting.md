# 4. Connecting

Checked against: frogsmashers `7a5783a`, hindsight `c1e061b`, hootenanny `691b776`.

[Back to the project README](../README.md) · Previous: [One online Tick](03-one-online-tick.md) · Next: [Infrastructure and CI/CD](05-infrastructure-and-ci.md)

From "Create Room" to Tick 0. Each game does everything through one LiteNetLib socket: the NAT
introduction gives the other game the address the server saw, which is that socket's.

```mermaid
sequenceDiagram
  participant O as Room Owner's game
  participant H as Hootenanny
  participant G as friend's game

  Note over O,G: A. Create a Room
  O->>H: connect ("HOOT" + version)
  Note right of H: unknown clients dropped silently. An old version<br/>or a full server is refused with a reason
  O->>H: CreateRoom
  Note right of H: at most 5 per minute per address, 500 in all
  H-->>O: RoomCreated("K7QXM")
  Note left of O: shows the code, the friend is told it

  Note over O,G: B. Join by code
  G->>H: connect ("HOOT" + version)
  G->>H: JoinRoom("K7QXM")
  H-->>G: RoomJoined
  H-->>O: PeerArrived(token, owner)
  H-->>G: PeerArrived(token, guest)

  Note over O,G: C. NAT introduction (skipped when the relay is forced)
  O->>H: NatIntroduceRequest(token)
  G->>H: NatIntroduceRequest(token)
  Note right of H: pairs the two requests by token, accepted only<br/>from the members' own public addresses
  H-->>O: friend's LAN + public address
  H-->>G: owner's LAN + public address
  par both send at once, which opens both NATs
    O->>G: connect(PeerKey)
  and
    G->>O: connect(PeerKey)
  end
  Note over O,G: each accepts only addresses it was introduced to. The first connection to succeed wins

  Note over O,G: D. Direct or relay: the Room Owner decides
  alt direct connection up within 3 s
    O->>G: PathDirect (through the relay, reliable)
    G->>O: AnswerDirect, or AnswerRelay if not up within 2 s
  else no direct connection
    O->>G: PathRelay (through the relay, reliable)
  end
  Note over O,G: nothing works within 12 s: "Couldn't connect to your friend."<br/>Result: a LiteNetLibLink, direct or through the relay, in OnlineMatchStart

  Note over O,G: E. Countdown and Match setup
  Note over O,G: 5 s countdown, either player can still back out. Then OnlineMatch.Begin
  O->>G: Match setup, reliable: seed (8 bytes) + option flags
  Note over O,G: both: Slots 0 and 1, default colours, free-for-all, no drop-in joins. Load the first Level

  Note over O,G: F. Handshake, at the start of EVERY Round
  Note over O,G: SimulationDriver.BeginScene: Round seed = SplitMix(Match seed, Level, Showdown)<br/>setup hash = FNV(Level, seed, options, roster, teams, Round wins, colours)<br/>then a new PeerToPeerSession
  loop 5 round trips each way, resent every 500 ms
    O->>G: SyncRequest(nonce, protocol, build, setup hash, players, delay, prediction, Slot masks)
    G-->>O: SyncReply(nonce)
    G->>O: SyncRequest(...)
    O-->>G: SyncReply(nonce)
  end
  alt any field differs
    G-->>O: SyncReject(reason)
    Note over O,G: both show the same message and go back to the title screen
  else everything agrees
    Note over O,G: Synchronized: Tick 0
  end
```

During the Match, both games stay connected to the server.

## What the handshake compares

`SyncRequest` carries a nonce and the facts both Peers must agree on. A mismatch is refused with
the first reason that applies:

| Field | Mismatch reason | What the player sees |
|---|---|---|
| Hindsight protocol version | `ProtocolVersion` | "Your friend is on a different version of the game." |
| Build ID hash (commit + `-dirty`) | `BuildId` | same |
| Round Setup hash | `RoundSetup` | "The two games disagree on the Round (…)." |
| Player count, Frame Delay, prediction limit, who owns which Slot | `SessionSettings` | same |

Five round trips prove the path works both ways and measure a first round-trip time.

## Leaving and failures

- Before the Match, any failure ends the lobby with a reason for the player:
  - "Couldn't reach the server." (8 s)
  - "Room not found."
  - "That Room is full."
  - "The Room expired." (a Room nobody joins closes after 10 minutes)
  - "Too many Rooms created."
  - "The server is full."
  - "This version of the game is too old for the server."
- During the Match, the leaving Peer's reason travels with its goodbye, so both players see the
  same message. Direct: in the LiteNetLib disconnect packet. Relay: as a reliable goodbye message.
- `-online-relay` (either player) forces the relay. `-rendezvous host:port` uses another server.
  Development builds can also skip the server: `-online-host PORT` / `-online-join HOST:PORT`
  (`DirectLobby`).

## Code map

| Step | Where |
|---|---|
| A to D, the game's side | `frogsmashers: HohimBrueh/Assets/Scripts/Online/ServerLobby.cs` (timeouts at the top) |
| A to C, the server's side | `hootenanny: src/Hootenanny.Server/HootServer.cs`, `RoomRegistry.cs`, `PunchMatcher.cs` |
| Messages and Room Codes | `hootenanny: src/Hootenanny.Protocol/Protocol.cs` |
| Countdown | `frogsmashers: .../UI/OnlineScreen.cs` (`CountdownSeconds`) |
| E, Match setup, seeds, setup hash | `frogsmashers: .../Online/OnlineMatch.cs` |
| F, starting the session per Round | `frogsmashers: .../Netcode/SimulationDriver.cs` (`BeginScene`, `StartOnlineSession`) |
| F, the handshake | `hindsight: src/Hindsight.Core/PeerProtocol.cs` (`SendSyncRequest`, `OnSyncRequest`) |
| Reasons shown to players | `frogsmashers: .../Netcode/SimulationDriver.cs` (`HandleOnlineEvents`), `ServerLobby.cs` (`Explain`) |
