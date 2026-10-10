namespace FrogSmashers.Netcode
{
    /// <summary>
    /// The simulation's clock. Nothing inside the simulation may ask Unity what time it is.
    ///
    /// Rollback works by replaying frames: when a remote player's real input turns out to
    /// differ from what we guessed, the game rewinds and re-runs every frame since. A replayed
    /// frame has to come out identical, and <c>Time.time</c> makes that impossible — it keeps
    /// climbing, so frame 100 replayed a moment later sees a different value than frame 100 did
    /// the first time. <c>Time.deltaTime</c> is the same problem: it is however long the last
    /// *rendered* frame happened to take, which no two machines ever agree on.
    ///
    /// So the simulation's only notion of time is <see cref="Tick"/>, and the only timestep is
    /// <see cref="FixedDeltaTime"/>. Tick 100 is always tick 100. GGPO's own sample works
    /// exactly this way: wall-clock time appears in its renderer and its frame-pacing loop, and
    /// never in <c>gamestate.cpp</c>, whose sole clock is a frame number saved and restored
    /// along with the rest of the state.
    /// </summary>
    public static class SimClock
    {
        public const int TicksPerSecond = 60;

        /// <summary>The one and only timestep simulation code ever advances by.</summary>
        public const float FixedDeltaTime = 1f / TicksPerSecond;

        /// <summary>
        /// Which tick the simulation is currently on. This is state, not a reading — a rollback
        /// moves it backwards via <see cref="SeekTo"/> just like it rewinds everything else.
        /// </summary>
        public static int Tick { get; private set; }

        /// <summary>
        /// Seconds of simulated time since the start, for code that wants a continuous
        /// quantity rather than a tick count. Derived from <see cref="Tick"/> rather than
        /// accumulated, so it cannot drift and it is the same on every machine at tick N.
        ///
        /// Long sessions lose phase precision here the way any float does — past a few hours
        /// the spacing between representable values grows. That costs accuracy, not
        /// determinism: tick N still gives the identical value everywhere.
        /// </summary>
        public static float Elapsed
        {
            get { return Tick * FixedDeltaTime; }
        }

        /// <summary>Advances one tick. Called only by <see cref="SimulationDriver"/>.</summary>
        internal static void Advance()
        {
            Tick++;
        }

        /// <summary>
        /// Puts the clock back to a given tick, for a rollback restoring a saved state. Kept
        /// public because it is the rollback session's job to call it, not the driver's.
        /// </summary>
        public static void SeekTo(int tick)
        {
            Tick = tick;
        }
    }
}
