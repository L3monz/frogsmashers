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
