# 3. One online Tick

Checked against: frogsmashers `7a5783a`, hindsight `c1e061b`.

[Back to the vault README](../../README.md) · Previous: [Inside one game](02-inside-one-game.md) · Next: [Connecting](04-connecting.md)

What happens in one Tick of an Online Round, in order. `SimulationDriver.Update` adds the Render
Frame's time to an accumulator, and runs `AdvanceOneOnlineTick` once for every 1/60 s it holds,
at most 8 times per Render Frame.

## The sequence

```text
 Participants:  Driver = SimulationDriver      Link = LiteNetLibLink     Session = PeerToPeerSession
                Proto  = PeerProtocol          Sync = Sync (input queues + saved-state ring)
                Sim    = FrogSimulation        Peer = the other Peer

 from        to         message                     what happens
 ──────────  ─────────  ──────────────────────────  ──────────────────────────────────────────────────────

 1. RECEIVE
 Driver   ─► Link       Poll()                      LiteNetLib events: arrived datagrams go into a queue
 Driver   ─► Session    Poll()
 Session  ─► Link       TryReceive(), until empty   one datagram at a time, into the session's own buffer
 Session  ─► Proto      OnMessage(packet)           wrong magic or an old sequence number: dropped
 Proto    ─► Sync       AddRemoteInput(slot, f, i)  only the next frames needed; each is compared with the
                                                    Prediction that was used; the first wrong one is kept
 Proto    ─┐            drop pending frames ≤ ack   the other Peer holds them, so stop resending them
 Session  ─► Proto      OnTick()                    resend inputs if quiet 200 ms · keep-alive 200 ms ·
                                                    quality report 1 s · Interrupted 750 ms ·
                                                    Disconnected 5 s

 2. ROLL BACK (only if a Prediction was wrong)
 Session  ─► Sync       RollbackTo(first wrong f)   InRollback = true: no sounds, particles or popups
 Sync     ─► Sim        LoadState(saved f)          the state at the start of the first wrong frame
 Sync     ─► Sim        SaveState, AdvanceFrame     once for every frame up to now, with better inputs

 3. HOUSEKEEPING
 Session  ─┐            frame advantage             how far ahead of the other Peer this one is
 Session  ─► Sync       DiscardConfirmedFrames      inputs no Rollback can need any more
 Session  ─┐            checkpoints                 every 60 frames: checksum of a confirmed frame,
                                                    compared with the other Peer's report
 Session  ─┐            TimeSync                    ahead on average? recommend waiting 3 to 9 frames
 Session  ─► Driver     events                      WaitRecommended · DesyncDetected · Interrupted ·
                                                    Resumed · SyncFailed / Disconnected (end the Match)

 4. LOCAL INPUT
 Driver   ─┐            waiting?                    skip this Tick (one at a time: no visible freeze)
 Driver   ─┐            InputSampler.SampleLocal()  every device combined into this Peer's Slot
 Driver   ─► Session    AddLocalInput(slot, input)  false at 8 frames ahead of confirmed: skip the Tick
 Session  ─► Sync       AddLocalInput               queued for frame + 2 (Frame Delay)

 5. ADVANCE
 Driver   ─► Session    AdvanceFrame()              unused Slots 2 to 5 get a blank input
 Session  ─► Proto      SendInput(f)                append to pending, then send ALL unacked frames
 Proto    ─► Link       Send(input packet)
 Link     ─► Peer       UDP datagram                direct, or through the relay
 Session  ─► Sync       AdvanceSimulation()
 Sync     ─► Sim        SaveState(ring buffer)      the state at the start of this frame, + checksum
 Sync     ─► Sim        AdvanceFrame(inputs[6])     confirmed inputs, or predicted = last known input

 6. ROUND END
 Driver   ─┐            CheckOnlineRoundEnd         the Round ends only when the Tick that ended it is
                                                    CONFIRMED (LastConfirmedFrame ≥ RoundOverTick)
```

`─┐` marks a step inside one participant, with no call to another.

## Prediction, in one paragraph

A remote input that hasn't arrived is predicted by repeating that player's last known input. The
simulation runs ahead on that guess, saving its state at the start of every frame. When the real
input arrives and differs, the session loads the Saved State of the first wrong frame and runs
every frame since again with the real inputs. It all happens inside one `Poll`, before this Tick's
own input. A Peer may run at most 8 frames ahead of the newest frame confirmed for every player.
Beyond that `AddLocalInput` refuses and the Tick is skipped. Frame Delay 2 holds the local input
back 2 Ticks (about 33 ms), which hides that much latency without any Rollback.

## The input packet

Little-endian. The 5-byte header is on every message.

```text
 offset  size  field
 ──────  ────  ─────────────────────────────────────────────────────────────────────
   0      2    magic          random per session; old sessions' packets are ignored
   2      2    sequence       out-of-order packets are dropped (the newest has everything)
   4      1    type           4 = Input
   5      4    first frame    the oldest frame in this packet
   9      1    frame count    how many frames follow
  10      4    ack frame      the newest of THEIR frames this Peer holds
  14      1    advantage      this Peer's frame advantage, for TimeSync
  15      4    checksum frame the frame of the latest checksum report
  19      4    checksum       that frame's Saved State checksum
  23    4 × n  inputs         one FrogInput per frame (1 local player × 4 bytes)
```

**Acknowledgements.** Each packet repeats every local frame the other Peer hasn't acknowledged,
and carries this Peer's own ack of theirs. A lost packet costs nothing: the next one carries the
same frames. When an ack arrives, the frames up to it leave the pending buffer. Packets stay under
1200 bytes.

The other message types are `SyncRequest`, `SyncReply` and `SyncReject` (see
[Connecting](04-connecting.md)), `QualityReport` and `QualityReply` (round-trip time),
`KeepAlive`, and `Disconnect` (sent 3 times).

## Code map

| Step | Where |
|---|---|
| Accumulator, `AdvanceOneOnlineTick`, events, Round end | `frogsmashers: HohimBrueh/Assets/Scripts/Netcode/SimulationDriver.cs` |
| Local input | `frogsmashers: .../Netcode/InputSampler.cs` (`SampleLocal`) |
| `Poll`, `AddLocalInput`, `AdvanceFrame`, checkpoints, wait | `hindsight: src/Hindsight.Core/PeerToPeerSession.cs` |
| Packets, acks, timers | `hindsight: src/Hindsight.Core/PeerProtocol.cs` (constants at the top) |
| Packet layout | `hindsight: src/Hindsight.Core/Messages.cs` |
| Rollback and the save-then-advance step | `hindsight: src/Hindsight.Core/Sync.cs` (`RollbackTo`, `AdvanceSimulation`) |
| Prediction, Frame Delay | `hindsight: src/Hindsight.Core/InputQueue.cs` |
| Saved States | `hindsight: src/Hindsight.Core/SavedStateRing.cs` |
| Waits | `hindsight: src/Hindsight.Core/TimeSync.cs` |
| Datagram queue | `frogsmashers: .../Online/LiteNetLibLink.cs` |
