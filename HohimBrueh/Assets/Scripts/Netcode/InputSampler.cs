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
