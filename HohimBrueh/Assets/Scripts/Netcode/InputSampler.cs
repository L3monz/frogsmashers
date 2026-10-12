using FreeLives;

namespace FrogSmashers.Netcode
{
    /// <summary>
    /// Reads the six input devices once per Tick, before the Tick runs. The one
    /// place the simulation's input comes from.
    ///
    /// Not simulation code: it reads hardware, so it is off the sim-guard list, like
    /// InputReader. A Rollback never calls it; it runs a past Tick again with the inputs
    /// Hindsight kept for that Tick.
    ///
    /// Slot N is the Player whose sortPriority is N, with that Player's device. A Slot with
    /// no Player gets an empty input.
    /// </summary>
    public static class InputSampler
    {
        // Reused for every read. InputReader writes into an InputState; FrogInput keeps only
        // what the simulation needs.
        static readonly InputState scratch = new InputState();

        static readonly InputReader.Device[] LocalDevices =
        {
            InputReader.Device.Gamepad1, InputReader.Device.Gamepad2, InputReader.Device.Gamepad3,
            InputReader.Device.Gamepad4, InputReader.Device.Keyboard1, InputReader.Device.Keyboard2,
        };

        // One per device: InputReader keeps each state's previous values for its own use.
        static readonly InputState[] localStates =
        {
            new InputState(), new InputState(), new InputState(),
            new InputState(), new InputState(), new InputState(),
        };

        /// <summary>
        /// Online, this machine controls one Slot, from any of its devices: every gamepad and
        /// both keyboard halves are combined, so whichever the player picks up works. Buttons
        /// held on any device count; each stick axis takes the device pushed furthest.
        /// </summary>
        public static FrogInput SampleLocal()
        {
            uint buttons = 0;
            sbyte x = 0, y = 0;
            for (int i = 0; i < LocalDevices.Length; i++)
            {
                InputReader.GetInput(LocalDevices[i], localStates[i]);
                var input = FrogInput.From(localStates[i]);
                buttons |= input.ToBits() >> 16;
                if (System.Math.Abs(input.XAxis) > System.Math.Abs(x)) x = input.XAxis;
                if (System.Math.Abs(input.YAxis) > System.Math.Abs(y)) y = input.YAxis;
            }
            return FrogInput.FromBits((uint)(byte)x | ((uint)(byte)y << 8) | (buttons << 16));
        }

        /// <summary>
        /// Development testing: input that changes every few Ticks (run, jump, attack), so an
        /// automatic online test has real mispredictions and Rollbacks. Different per Slot.
        /// </summary>
        public static FrogInput Scripted(int tick, int slot)
        {
            uint x = unchecked((uint)(tick / 9) * 2654435761u + (uint)slot * 40503u);
            uint pick = x >> 28;
            return FrogInput.Make(
                left: pick % 4 == 0,
                right: pick % 4 == 1,
                up: pick % 5 == 2,
                a: pick % 3 == 0,
                b: pick % 7 == 3,
                x: pick % 6 == 5);
        }

        // Ticks each Slot's chase bot has held its swing.
        static readonly int[] chaseCharge = new int[6];

        /// <summary>
        /// Development testing: walks to the nearest other frog, charges a swing on the way and
        /// releases it in reach, so an automatic online test scores points and reaches the Round
        /// end. It reads this machine's view of the frogs, which may be a prediction; that is
        /// fine, because the result is only input, sent to the other Peer like a player's.
        /// While either frog is missing (respawning), it plays <see cref="Scripted"/> input.
        /// </summary>
        public static FrogInput Chase(int tick, int slot)
        {
            var self = Player.BySlot(slot);
            Character me = self != null ? self.character : null;
            Character target = null;
            float best = float.MaxValue;
            for (int other = 0; other < 6 && me != null; other++)
            {
                var player = other == slot ? null : Player.BySlot(other);
                Character character = player != null ? player.character : null;
                if (character == null)
                    continue;
                float distance = (character.transform.position - me.transform.position).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    target = character;
                }
            }
            if (target == null)
            {
                chaseCharge[slot] = 0;
                return Scripted(tick, slot);
            }

            // Holding the swing roots the frog, so it walks first and charges only in reach.
            // A charge, once started, runs its full length and then releases.
            UnityEngine.Vector3 d = target.transform.position - me.transform.position;
            bool inReach = System.Math.Abs(d.x) < 3.5f && System.Math.Abs(d.y) < 2.5f;
            bool charging = chaseCharge[slot] > 0 || inReach;
            bool release = charging && chaseCharge[slot] >= 20;
            chaseCharge[slot] = release || !charging ? 0 : chaseCharge[slot] + 1;

            // Jumps when the target is above, and now and then anyway, to climb out of pits
            // and over walls. Walking toward the target also faces it, so the swing goes its way.
            bool jump = !charging && (d.y > 1.5f || (tick + slot * 37) % 90 < 6);
            return FrogInput.Make(
                left: d.x < -0.5f,
                right: d.x > 0.5f,
                a: jump,
                x: charging && !release);
        }

        public static void Sample(FrogInput[] into)
        {
            for (int slot = 0; slot < into.Length; slot++)
            {
                var player = Player.BySlot(slot);
                if (player == null)
                {
                    into[slot] = default(FrogInput);
                    continue;
                }

                InputReader.GetInput(player.inputDevice, scratch);
                into[slot] = FrogInput.From(scratch);
            }
        }
    }
}
