using Rollback.Determinism;
using UnityEngine;

namespace FrogSmashers.Netcode
{
    /// <summary>
    /// The simulation's random number stream. Simulation code must use this, not
    /// <c>UnityEngine.Random</c> (SMA-22).
    ///
    /// <c>UnityEngine.Random</c> is a global stream. Two machines are not at the same point in
    /// it, and a rollback does not rewind it. This stream is a <see cref="SimRandom"/>, and
    /// its complete state is one value, <see cref="State"/>. Milestone 4 saves that value with
    /// the rest of the simulation state.
    ///
    /// There is one stream for the whole simulation. The draws happen in tick order, and the
    /// tick order is the registration order in <see cref="SimulationDriver"/>, which is the
    /// same on every machine.
    ///
    /// Cosmetic code (effects, camera shake, sound variation) continues to use
    /// <c>UnityEngine.Random</c>. Those can differ between machines, and a cosmetic draw from
    /// this stream would change the simulation.
    /// </summary>
    public static class SimRng
    {
        private static readonly SimRandom stream = new SimRandom(0);

        /// <summary>The complete state of the stream. Save and restore it with the simulation state.</summary>
        public static ulong State
        {
            get { return stream.State; }
            set { stream.State = value; }
        }

        /// <summary>
        /// Moves the stream to the start position for <paramref name="seed"/>. All peers in an
        /// online match must use the same seed. The session will supply it in Milestone 5.
        /// </summary>
        public static void Seed(ulong seed)
        {
            stream.Reseed(seed);
        }

        /// <summary>A float in [0, 1). Replaces <c>Random.value</c>.</summary>
        public static float Value
        {
            get { return stream.NextFloat(); }
        }

        /// <summary>A float in [min, max). Replaces <c>Random.Range(float, float)</c>.</summary>
        public static float Range(float min, float max)
        {
            return stream.Range(min, max);
        }

        /// <summary>A random direction with length 1. Replaces <c>Random.insideUnitCircle.normalized</c>.</summary>
        public static Vector2 Direction()
        {
            float x, y;
            stream.NextDirection(out x, out y);
            return new Vector2(x, y);
        }
    }
}
