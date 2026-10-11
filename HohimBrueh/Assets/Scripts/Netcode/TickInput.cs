using System;

namespace FrogSmashers.Netcode
{
    /// <summary>
    /// Applies one Tick's input to the simulation: step 1 of the tick order.
    ///
    /// Each alive Character gets its Slot's input through <see cref="Character.ApplyInput"/>,
    /// which also rolls the previous-input flags forward. Those flags are Simulation State, so
    /// a Rollback restores them, and "a new press" means the same thing in a replay.
    ///
    /// A Slot that is not in the roster uses its input to drop in: holding the join button
    /// (X) adds the Player (GameController.ApplyJoins).
    ///
    /// The inputs of the current Tick are kept for Characters that Spawn during the Tick
    /// (<see cref="For"/>). They are this Tick's input, not state: in a Rollback, Apply is
    /// called again with the inputs of the Tick being run again.
    /// </summary>
    public static class TickInput
    {
        private static readonly FrogInput[] current = new FrogInput[6];

        public static void Apply(FrogInput[] inputs)
        {
            Array.Copy(inputs, current, Math.Min(inputs.Length, current.Length));

            var characters = SimWorld.Characters;
            if (characters != null)
            {
                for (int slot = 0; slot < characters.Count; slot++)
                {
                    if (characters.IsAlive(slot))
                        characters.Get(slot).ApplyInput(For(slot));
                }
            }

            GameController.ApplyJoins(current);
        }

        /// <summary>
        /// The input the Character in <paramref name="slot"/> uses this Tick. After a Round is
        /// won, only the winner can act; everyone else gets an empty input. (This rule used to
        /// be in Character.CheckInput, which read the device.)
        /// </summary>
        public static FrogInput For(int slot)
        {
            var characters = SimWorld.Characters;
            if (characters != null && GameController.HasInstance && GameController.State == GameState.RoundFinished
                && !characters.Get(slot).IsWinningPlayer())
            {
                return default(FrogInput);
            }
            return current[slot];
        }
    }
}
