namespace FrogSmashers.Netcode
{
    /// <summary>
    /// The pooled simulated objects of the current Round: six Characters, one per Slot, and
    /// the one fly (spec D5). GameController creates the pools when its scene loads and clears
    /// them when it is destroyed. FrogSimulation takes over ownership in P6.
    /// </summary>
    public static class SimWorld
    {
        public static SimPool<Character> Characters { get; private set; }
        public static SimPool<Fly> Flies { get; private set; }

        /// <summary>The fly object, alive or not, or null in a scene without a fly.</summary>
        public static Fly Fly
        {
            get { return Flies != null ? Flies.Get(0) : null; }
        }

        /// <summary>
        /// Creates the pools. Each Character gets the Player of its Slot here, once: a Player's
        /// Character is always the pooled Character in the same Slot.
        /// </summary>
        public static void CreatePools(Character characterPrefab, Fly flyPrefab)
        {
            Characters = characterPrefab != null ? new SimPool<Character>(characterPrefab, 6, "Character") : null;
            Flies = flyPrefab != null ? new SimPool<Fly>(flyPrefab, 1, "Fly") : null;

            if (Characters != null)
            {
                for (int slot = 0; slot < Characters.Count; slot++)
                    Characters.Get(slot).player = Player.BySlot(slot);
            }
        }

        public static void Clear()
        {
            Characters = null;
            Flies = null;
        }
    }
}
