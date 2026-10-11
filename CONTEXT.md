# Frog Smashers

A local-multiplayer platform-fighter: frogs knock each other off-stage with a charge-swipe attack and a grapple tongue, scoring points (or, in Showdown, surviving) across a series of levels.

## Language

**Player**:
A persistent match participant — owns an input device, a color, a team (if team mode), and running scores. Exists independently of whether they currently have a live Character on-stage.
_Avoid_: Character (see below), user

**Character**:
The physical frog body a Player currently controls in a Round. Despawned on death and (usually) Spawned again after a delay; a Player can exist with no live Character between deaths.
_Avoid_: Player, Frog, body

**Percent**:
The knockback-scaling counter that accumulates as a Character takes hits, resetting when the Character recovers from Hitstun. Directly scales knockback power on the next hit, and is the point value awarded to whoever lands the finishing blow.
_Avoid_: hitsTaken (the current code field name — rewrite may rename to match this)

**Hitstun**:
The state a Character enters immediately after being hit — launched by knockback, unable to act normally, until it recovers. Distinct from Bounce (below): a Character can be in Hitstun without ever bouncing off anything.
_Avoid_: Bouncing (as a state name — reserve "Bounce" for the collision behavior)

**Bounce**:
The physics behavior of a Hitstunned Character colliding with terrain or another Hitstunned Character and ricocheting off it, redirecting its velocity. A collision event, not a state.
_Avoid_: Bouncing (as a synonym for the Hitstun state)

**Round**:
One played-out level. Ends when a Player's Percent-derived score reaches the round's win threshold (5 points with 2 players, 10 with more), or — in Showdown — when only one Player/team remains.
_Avoid_: Match, Game, Level (Level refers to the physical stage/scene; a Round is played on one Level)

**Match**:
The full sequence of Rounds played across the configured level list. Tracked via each Player's round-win count, independent of their per-Round score.
_Avoid_: Round, Series, Game

**Showdown**:
A sudden-death Match mode: landing a kill removes the victim from active play entirely (no points awarded) instead of scoring, continuing until one Player or team remains.
_Avoid_: Elimination mode, Sudden Death (these are fine as plain-English descriptions, but "Showdown" is the term the code and this model use)

### The simulation

**Tick**:
One fixed step of the game's simulation, 1/60 of a second of game time. Ticks are numbered from 0 at the start of each Round. Hindsight calls this a Frame.
_Avoid_: frame (in this game, a frame is a Render Frame), step, update

**Render Frame**:
One image Unity draws. A Render Frame can run zero, one or several Ticks, depending on the frame rate.
_Avoid_: tick, frame (when the context does not make it clear)

**Simulation State**:
Every gameplay fact of a Round at the start of a Tick: what a Rollback must restore exactly, and what two machines must agree on. Hindsight stores it as opaque Saved State bytes.
_Avoid_: game state, snapshot, world

**Round Setup**:
The facts fixed before Tick 0 of a Round and constant through it: the Level, the starting roster, teams, the game mode, the option toggles and the random seed. Agreed before the Round starts, so it is never part of Simulation State.
_Avoid_: config, settings, match state

**Slot**:
A Player's fixed position in the six-player roster (four gamepads, two keyboard halves). A Player and that Player's Character share one Slot, and Slot order is the order the simulation processes them in.
_Avoid_: index, id, player number

**Spawn**:
A Character or the fly entering play during a Round. The opposite is **Despawn**. Both are changes to Simulation State, so a Rollback can undo them.
_Avoid_: instantiate, destroy, create, kill

**Cosmetic Effect**:
A sound, particle, popup or screen shake that the simulation triggers but that is not part of Simulation State. It plays when a Tick first runs and does not play again when a Rollback runs that Tick again.
_Avoid_: effect (alone), feedback, juice

### Playing online

**Peer**:
One running copy of the game taking part in an Online Match, normally one per machine. Hindsight's term.
_Avoid_: client, host, player (a Peer is a machine, a Player is a person)

**Online Match**:
A Match played between two Peers over a network.
_Avoid_: netplay, online game

**Room**:
A meeting place for two Peers before an Online Match. It holds no game state.
_Avoid_: lobby, session, server

**Room Code**:
The short text that lets a friend join a Room.
_Avoid_: join code, password

**Room Owner**:
The Peer that created the Room. It decides the Round Setup of an Online Match and plays in Slot 0.
_Avoid_: host (in this project, a host is the machine that runs the server), server
