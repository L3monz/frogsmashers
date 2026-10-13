using UnityEngine;

namespace FrogSmashers.Netcode
{
    /// <summary>
    /// The pooled simulated objects of the current Round: six Characters, one per Slot, and
    /// the one fly. GameController creates the pools when its scene loads and clears
    /// them when it is destroyed.
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

        /// <summary>
        /// Development builds: logs an error for every Character or Fly in the loaded scenes that
        /// is not one of the pooled objects. Only pooled objects are saved and loaded, so one
        /// placed in a scene is invisible to a Rollback while the simulation can still touch it:
        /// a tongue could catch the fly placed in Finale, and the Peer that rolled back then moved
        /// the pooled fly instead (SMA-51). Call it once, after the pools are created.
        ///
        /// Not simulation: it runs once per scene, before the first Tick, and only logs, so the
        /// order in which Unity returns the objects does not matter.
        /// </summary>
        public static void ReportStrayObjects()
        {
            if (!Debug.isDebugBuild)
                return;
            ReportStrays(Characters);
            ReportStrays(Flies);
        }

        private static void ReportStrays<T>(SimPool<T> pool) where T : Component, ISimulated
        {
            // FindObjectsOfTypeAll includes inactive objects (the pool's own start inactive) and
            // prefab assets, which have no scene.
            foreach (var item in Resources.FindObjectsOfTypeAll<T>())
            {
                if (!item.gameObject.scene.IsValid() || (pool != null && pool.Contains(item)))
                    continue;
                Debug.LogError("[SIM] stray " + typeof(T).Name + " '" + item.name + "' in scene "
                    + item.gameObject.scene.name + ": it is not in the pool, so Rollbacks do not save it");
            }
        }

        public static void Clear()
        {
            Characters = null;
            Flies = null;
        }
    }
}
