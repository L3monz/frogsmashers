using System;
using UnityEngine;

namespace FrogSmashers.Netcode
{
    /// <summary>
    /// Physics2D queries for simulation code: results in a deterministic order, and no garbage.
    ///
    /// Order: Box2D returns hits in broadphase order. That order comes from the history of
    /// collider movement, and a rollback changes that history. Thus the same frame can give the
    /// same hits in a different order. Simulation code applies hit effects one at a time, so a
    /// different order can give a different result. These methods sort the hits by player slot
    /// (0..5), which is the same on every machine. Colliders without a <see cref="Character"/>
    /// go last. Two colliders on the same character have the same key, and their relative order
    /// is not fixed. That is safe because the loops apply the same effect for each collider of
    /// one character.
    ///
    /// Garbage: the <c>*All</c> queries return a new array on every call, and a rollback runs
    /// every query again. These use the NonAlloc queries with a buffer the caller owns, and sort
    /// with a reused key array. Give each call site its own buffer, so a hit effect that runs
    /// another query cannot overwrite the results being looped over.
    /// </summary>
    public static class SimQuery
    {
        // Six frogs with a few colliders each fit easily. A full buffer could mean dropped hits,
        // and which hits get dropped depends on broadphase order, so a full buffer is reported.
        public const int BufferSize = 32;

        private static readonly int[] keys = new int[BufferSize];

        /// <summary>Returns the number of hits written to <paramref name="results"/>, sorted by slot.</summary>
        public static int OverlapCircle(Vector2 point, float radius, int layerMask, Collider2D[] results)
        {
            int count = Physics2D.OverlapCircleNonAlloc(point, radius, results, layerMask);
            CheckNotFull(count, results.Length);
            for (int i = 0; i < count; i++)
                keys[i] = SlotOf(results[i]);
            Array.Sort(keys, results, 0, count);
            return count;
        }

        /// <summary>Returns the number of hits written to <paramref name="results"/>, sorted by slot.</summary>
        public static int CircleCast(Vector2 origin, float radius, Vector2 direction, float distance, int layerMask, RaycastHit2D[] results)
        {
            int count = Physics2D.CircleCastNonAlloc(origin, radius, direction, results, distance, layerMask);
            CheckNotFull(count, results.Length);
            for (int i = 0; i < count; i++)
                keys[i] = SlotOf(results[i].collider);
            Array.Sort(keys, results, 0, count);
            return count;
        }

        private static void CheckNotFull(int count, int capacity)
        {
            if (capacity > keys.Length)
                throw new ArgumentException("A query buffer must hold at most " + keys.Length + " results.");
            if (count == capacity)
                Debug.LogError("Physics query filled its " + capacity + "-result buffer; hits may be dropped in a non-deterministic order.");
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
