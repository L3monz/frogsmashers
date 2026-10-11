using System.Collections.Generic;
using Hindsight.Core;
using UnityEngine;

namespace FrogSmashers.Netcode
{
    /// <summary>
    /// Frog Smashers as Hindsight sees it: something that can save itself, restore itself and
    /// advance exactly one Tick. One instance per scene with a GameController, created when
    /// the scene loads.
    ///
    /// The Saved State has a fixed layout, in this order:
    /// 1. Global: the Tick number and the random stream.
    /// 2. The Round (GameController): match flow, timers, team scores, the winner.
    /// 3. The roster: one membership flag per Slot.
    /// 4. The six Players: score and respawn delay.
    /// 5. The six Characters, with their alive flags.
    /// 6. The fly, with its alive flag.
    /// Every section is written every time, so any Saved State can be decoded on its own
    /// (<see cref="Describe"/>), which is how a Desync report names the field that differs.
    ///
    /// A Tick runs in a fixed order too: apply the input, sync transforms into Box2D, then
    /// GameController, the Characters in Slot order, and the fly; then advance the clock.
    /// The order matters: when two frogs act on each other, the one that ticks first decides
    /// what the other sees, so every machine must use the same order.
    /// </summary>
    public sealed class FrogSimulation : IRollbackSimulation<FrogInput>
    {
        private readonly GameController round;
        private readonly StateStream stream = new StateStream();

        // Development check: the Slot of a Character to nudge at the end of the next first-run
        // Frame, or -1. See InjectDesyncAfterThisFrame.
        private int nudgeSlot = -1;

        public FrogSimulation(GameController round)
        {
            this.round = round;
        }

        /// <summary>
        /// True once the Round's end delay has run out. The simulation never changes scene
        /// itself (a scene load cannot be rolled back); the driver does it after a first-run
        /// Tick that ends with this set.
        /// </summary>
        public bool RoundOver
        {
            get { return round.RoundOver; }
        }

        public byte[] SaveState()
        {
            stream.BeginWrite();
            SyncAll(stream);
            return stream.EndWrite();
        }

        public void LoadState(byte[] state)
        {
            stream.BeginRead(state);
            SyncAll(stream);
            stream.EndRead();
        }

        public void AdvanceFrame(FrogInput[] inputs)
        {
            TickInput.Apply(inputs);

            // Push every transform into Box2D before any hit check runs. After a load, this
            // is what moves the colliders to the restored positions.
            Physics2D.SyncTransforms();

            float dt = SimClock.FixedDeltaTime;
            round.SimulateTick(dt);

            var characters = SimWorld.Characters;
            if (characters != null)
            {
                for (int slot = 0; slot < characters.Count; slot++)
                {
                    // Checked per Character, because one that Despawns stops at once, and one
                    // that Spawns earlier in this Tick (in GameController) ticks now.
                    if (characters.IsAlive(slot) && characters.Get(slot).isActiveAndEnabled)
                        characters.Get(slot).SimulateTick(dt);
                }
            }

            var flies = SimWorld.Flies;
            if (flies != null && flies.IsAlive(0) && flies.Get(0).isActiveAndEnabled)
                flies.Get(0).SimulateTick(dt);

            SimClock.Advance();

            // The nudge happens only on the first run of a Frame, never when a Rollback runs it
            // again. So the replay cannot reproduce it, exactly like a real determinism bug.
            if (nudgeSlot >= 0 && !SimulationDriver.InRollback)
            {
                if (characters != null && characters.IsAlive(nudgeSlot))
                    characters.Get(nudgeSlot).transform.position += new Vector3(0.001f, 0f, 0f);
                nudgeSlot = -1;
            }
        }

        /// <summary>
        /// For a development check only: moves a Character by a tiny amount at the end of the
        /// next first-run Frame. A Sync Test must then report a Desync that names
        /// Character[slot].position, which proves the checker catches a real one.
        /// </summary>
        public void InjectDesyncAfterThisFrame(int slot)
        {
            nudgeSlot = slot;
        }

        /// <summary>Every field of a Saved State as <c>path = value</c>, in order.</summary>
        public List<KeyValuePair<string, string>> Describe(byte[] state)
        {
            stream.BeginDescribe(state);
            SyncAll(stream);
            return stream.EndDescribe();
        }

        private void SyncAll(StateStream s)
        {
            s.BeginSection("Global");
            int tick = SimClock.Tick;
            s.Int("tick", ref tick);
            ulong random = SimRng.State;
            s.ULong("random", ref random);
            if (s.IsReading)
            {
                SimClock.SeekTo(tick);
                SimRng.State = random;
            }

            s.BeginSection("Round");
            round.SyncState(s);
            GameController.SyncRoster(s);

            for (int slot = 0; slot < 6; slot++)
            {
                s.BeginSection(PlayerSections[slot]);
                Player.BySlot(slot).SyncState(s);
            }

            if (SimWorld.Characters != null)
                SimWorld.Characters.SyncState(s);
            if (SimWorld.Flies != null)
                SimWorld.Flies.SyncState(s);
        }

        private static readonly string[] PlayerSections =
        {
            "Player[0]", "Player[1]", "Player[2]", "Player[3]", "Player[4]", "Player[5]",
        };
    }
}
