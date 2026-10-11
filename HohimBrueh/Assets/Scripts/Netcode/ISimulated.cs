namespace FrogSmashers.Netcode
{
    /// <summary>
    /// Something the simulation advances one tick at a time.
    ///
    /// Implementing this is what makes an object replayable. The rule is that
    /// <see cref="SimulateTick"/> must depend on nothing but the object's own state, the
    /// timestep it is handed, and inputs pushed in from outside — no <c>Time.deltaTime</c>,
    /// no <c>Time.time</c>, no unsynced random. Given the same state and the same inputs it
    /// has to produce the same result every time it runs, because a rollback will run it again.
    /// </summary>
    public interface ISimulated
    {
        /// <param name="deltaTime">Always <see cref="SimClock.FixedDeltaTime"/>. Passed in
        /// rather than read from the clock so the method stays a pure function of its
        /// arguments, which is what makes it testable outside a running game.</param>
        void SimulateTick(float deltaTime);

        /// <summary>
        /// Saves or loads this object's Simulation State through <paramref name="s"/>
        ///. One method for both directions, so the two cannot drift
        /// apart. See <see cref="StateStream"/> for the rules; tools/state-coverage.py checks
        /// that no assigned field is missing.
        /// </summary>
        void SyncState(StateStream s);
    }

}
