using System;
using UnityEngine;

namespace FrogSmashers.Netcode
{
    /// <summary>
    /// Puts the results of Physics2D queries in a deterministic order.
    ///
    /// The <c>*All</c> queries return hits in Box2D broadphase order. That order comes from
    /// the history of collider movement, and a rollback changes that history. Thus the same
    /// frame can give the same hits in a different order. Simulation code applies hit effects
    /// one at a time, so a different order can give a different result. Sort the results with
    /// these methods before the loop.
    ///
    /// The sort key is the player slot (0..5), which is the same on every machine. Colliders
    /// without a <see cref="Character"/> go last. Two colliders on the same character have
    /// the same key, and their relative order is not fixed. That is safe because the loops
    /// apply the same effect for each collider of one character.
    /// </summary>
    public static class SimQuery
    {
        public static void SortBySlot(Collider2D[] colliders)
        {
            if (colliders.Length < 2)
                return;

            var keys = new int[colliders.Length];
            for (int i = 0; i < colliders.Length; i++)
                keys[i] = SlotOf(colliders[i]);

            Array.Sort(keys, colliders);
        }

        public static void SortBySlot(RaycastHit2D[] hits)
        {
            if (hits.Length < 2)
                return;

            var keys = new int[hits.Length];
            for (int i = 0; i < hits.Length; i++)
                keys[i] = SlotOf(hits[i].collider);

            Array.Sort(keys, hits);
        }

        // GetComponentInParent, not GetComponent: tongue colliders are on a child object of
        // the character.
        private static int SlotOf(Collider2D collider)
        {
            if (collider == null)
                return int.MaxValue;

            var character = collider.GetComponentInParent<Character>();
            if (character == null || character.player == null)
                return int.MaxValue;

            return character.player.sortPriority;
        }
    }
}
