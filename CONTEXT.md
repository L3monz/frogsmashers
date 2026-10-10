# Frog Smashers

A local-multiplayer platform-fighter: frogs knock each other off-stage with a charge-swipe attack and a grapple tongue, scoring points (or, in Showdown, surviving) across a series of levels.

## Language

**Player**:
A persistent match participant — owns an input device, a color, a team (if team mode), and running scores. Exists independently of whether they currently have a live Character on-stage.
_Avoid_: Character (see below), user

**Character**:
The physical frog body a Player currently controls in a Round. Destroyed on death and (usually) respawned after a delay; a Player can exist with no live Character between deaths.
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
