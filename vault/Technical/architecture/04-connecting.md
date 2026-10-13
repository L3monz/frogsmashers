# 4. Connecting

Checked against: frogsmashers `7a5783a`, hindsight `c1e061b`, hootenanny `691b776`.

[Back to the vault README](../../README.md) · Previous: [One online Tick](03-one-online-tick.md) · Next: [Infrastructure and CI/CD](05-infrastructure-and-ci.md)

From "Create Room" to Tick 0. Each game does everything through one LiteNetLib socket: the NAT
introduction gives the other game the address the server saw, which is that socket's.

```text
 Participants:  Owner = the Room Owner's game    Server = Hootenanny    Friend = the friend's game

 from        to         message                          what happens
 ──────────  ─────────  ───────────────────────────────  ─────────────────────────────────────────────

 A. CREATE A ROOM
 Owner    ─► Server     connect ("HOOT" + version)       unknown clients dropped silently; an old
                                                         version or a full server refused with a reason
 Owner    ─► Server     CreateRoom                       at most 5 per minute per address, 500 in all
 Server   ─► Owner      RoomCreated("K7QXM")             the code is shown; the friend is told it

 B. JOIN BY CODE
 Friend   ─► Server     connect ("HOOT" + version)
 Friend   ─► Server     JoinRoom("K7QXM")                typed, or picked letter by letter on a pad
 Server   ─► Friend     RoomJoined
 Server   ─► Owner      PeerArrived(token, owner)        both get the same punch token
 Server   ─► Friend     PeerArrived(token, guest)

 C. NAT INTRODUCTION (skipped when the relay is forced)
 Owner    ─► Server     NatIntroduceRequest(token)       accepted only from a member's own public address
 Friend   ─► Server     NatIntroduceRequest(token)       the server pairs the two requests by token
 Server   ─► Owner      friend's LAN + public address
 Server   ─► Friend     owner's LAN + public address
 Owner    ─► Friend     connect(PeerKey)                 both send at once, which opens both NATs;
 Friend   ─► Owner      connect(PeerKey)                 each accepts only addresses it was introduced
                                                         to; the first connection to succeed wins

 D. DIRECT OR RELAY (the Room Owner decides)
 Owner    ─► Friend     PathDirect   (via the relay)     if the direct connection is up within 3 s
 Friend   ─► Owner      AnswerDirect (via the relay)     or AnswerRelay if not up within 2 s
 Owner    ─► Friend     PathRelay    (via the relay)     if it isn't
 both     ─┐            give up after 12 s               "Couldn't connect to your friend."
 both     ─┐            LiteNetLibLink                   direct, or through the relay ─► OnlineMatchStart

 E. COUNTDOWN AND MATCH SETUP
 both     ─┐            5 s countdown                    either player can still back out
 both     ─┐            OnlineMatch.Begin
 Owner    ─► Friend     Match setup (reliable)           seed (8 bytes) + option flags
 both     ─┐            load the first Level             Slots 0 and 1, default colours, free-for-all,
                                                         no drop-in joins

 F. HANDSHAKE (at the start of EVERY Round)
 both     ─┐            SimulationDriver.BeginScene      Round seed = SplitMix(Match seed, Level,
                                                         Showdown); setup hash = FNV(Level, seed,
                                                         options, roster, teams, Round wins, colours);
                                                         a new PeerToPeerSession
 Owner    ─► Friend     SyncRequest                      5 round trips each way, resent every 500 ms
 Friend   ─► Owner      SyncReply(nonce)
 Friend   ─► Owner      SyncRequest
 Owner    ─► Friend     SyncReply(nonce)
 either   ─► other      SyncReject(reason)               only on a mismatch: both show the same
                                                         message and go back to the title screen
 both     ─┐            Synchronized                     TICK 0
```

`─┐` marks a step inside one game, with no message to another. During the Match, both games stay
connected to the server.

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
