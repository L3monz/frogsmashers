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
        /// (spec D2, ADR 0003). One method for both directions, so the two cannot drift
        /// apart. See <see cref="StateStream"/> for the rules; tools/state-coverage.py checks
        /// that no assigned field is missing.
        /// </summary>
        void SyncState(StateStream s);
    }

    /// <summary>
    /// Something that produces input for a tick, sampled before anything simulates.
    ///
    /// Separate from <see cref="ISimulated"/> because reading a gamepad is emphatically *not*
    /// replayable — during a rollback the inputs for a past tick come from the session's
    /// history instead, and the gamepad must not be touched at all. Keeping the two apart is
    /// what lets the driver do "gather this tick's inputs, then advance the tick," which is
    /// the same shape a rollback session uses.
    /// </summary>
    public interface ISimulationInput
    {
        void SampleInput();
    }
}
