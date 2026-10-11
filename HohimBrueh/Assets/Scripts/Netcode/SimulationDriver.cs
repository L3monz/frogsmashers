using System;
using System.Collections.Generic;
using UnityEngine;

namespace FrogSmashers.Netcode
{
    /// <summary>
    /// Drives the simulation on a fixed tick, decoupled from the frame rate.
    ///
    /// This is the one place allowed to look at the wall clock. It reads Unity's frame time
    /// purely to decide *how many* fixed ticks to run, never to decide how far anything moves —
    /// the same division of labour as GGPO's sample, where <c>timeGetTime()</c> lives in the
    /// frame-pacing loop and never reaches the simulation. Everything downstream of here sees
    /// only <see cref="SimClock.FixedDeltaTime"/>.
    ///
    /// Having a single call site for "advance one tick" is the point. While every MonoBehaviour
    /// ticked itself from its own <c>Update()</c>, there was no moment in the code where the
    /// whole simulation moved forward, and so nothing a rollback session could ever call.
    /// </summary>
    public class SimulationDriver : MonoBehaviour
    {
        /// <summary>
        /// Cap on ticks per rendered frame. After a hitch — a shader compile, the editor
        /// pausing, a breakpoint — the accumulator holds seconds of debt, and paying it all
        /// off in one frame produces a long freeze followed by a lurch. Better to drop the
        /// backlog and carry on; in a networked session this is the throttling TimeSync
        /// handles properly.
        /// </summary>
        private const int MaxTicksPerFrame = 8;

        private static SimulationDriver instance;

        // Registration order, not FindObjectsOfType. Unordered iteration where the order
        // affects state is one of the things tools/sim-guard.py catches: if two characters
        // resolve a collision against each other, whichever ticks first sees the other's old
        // position, and two machines disagreeing on that order is a desync.
        private readonly List<ISimulated> simulated = new List<ISimulated>();

        private float accumulator;

        /// <summary>
        /// Raised after each tick. For read-only observers such as <see cref="SimDiagnostics"/>.
        /// A handler must not change simulation state.
        /// </summary>
        public static event Action TickCompleted;

        /// <summary>
        /// The number of frames where the tick backlog was more than
        /// <see cref="MaxTicksPerFrame"/> and the driver discarded it. For diagnostics.
        /// </summary>
        public static int BacklogDrops { get; private set; }

        /// <summary>
        /// Running totals for diagnostics: all frame time the driver received, the part it
        /// discarded as backlog, and the ticks it ran. They are updated together in one
        /// frame, so a reader always sees a consistent set. The rule they must keep:
        /// ticks run = (time received - time dropped) / FixedDeltaTime, less a fraction of
        /// one tick still waiting in the accumulator.
        /// </summary>
        public static double TotalTimeReceived { get; private set; }
        public static double TotalTimeDropped { get; private set; }
        public static long TotalTicksRun { get; private set; }

        /// <summary>
        /// Creates the driver before any scene loads, so it is already there when the first
        /// character's OnEnable tries to register.
        ///
        /// Done in code rather than by dropping a component in a scene because every scene
        /// with gameplay in it needs one, and a missing component would show up as "the game
        /// is frozen" rather than as an error.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null)
                return;

            var host = new GameObject("SimulationDriver");
            DontDestroyOnLoad(host);
            host.AddComponent<SimulationDriver>();
        }

        private void Awake()
        {
            instance = this;

            // Offline, any seed is correct, so use the clock: then each session is different.
            // This is a one-time read before the first tick, not a read inside the
            // simulation. In online play, the session replaces this with a seed that all peers
            // agree on.
            // sim-guard: allow offline seed, read once before the first tick; online, the session supplies it
            SimRng.Seed((ulong)System.DateTime.UtcNow.Ticks);

            // Set explicitly. The project's Physics2DSettings.asset is in an old format with
            // no entry for this, so its effective value is not confirmed. With auto sync on, a
            // query always sees the current transforms. With it off, a query sees the
            // positions from the last sync, and Unity's physics step does that sync on the
            // wall clock. Then the result of a hit check depends on frame timing.
            Physics2D.autoSyncTransforms = true;
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        /// <summary>
        /// Adds an object to the tick. Registration order
        /// is the tick order, so anything spawned as part of the simulation lands in the same
        /// place in the order on every machine.
        /// </summary>
        public static void Register(object participant)
        {
            if (instance == null)
                return;

            var sim = participant as ISimulated;
            if (sim != null && !instance.simulated.Contains(sim))
                instance.simulated.Add(sim);

        }

        public static void Unregister(object participant)
        {
            if (instance == null)
                return;

            var sim = participant as ISimulated;
            if (sim != null)
                instance.simulated.Remove(sim);

        }

        private void Update()
        {
            // sim-guard: allow the driver reads frame time only to decide how many ticks to run
            float frameTime = Time.deltaTime;
            accumulator += frameTime;
            TotalTimeReceived += frameTime;

            int ticksRun = 0;
            while (accumulator >= SimClock.FixedDeltaTime && ticksRun < MaxTicksPerFrame)
            {
                accumulator -= SimClock.FixedDeltaTime;
                AdvanceOneTick();
                ticksRun++;
            }

            if (ticksRun == MaxTicksPerFrame && accumulator >= SimClock.FixedDeltaTime)
            {
                TotalTimeDropped += accumulator;
                accumulator = 0f;
                BacklogDrops++;
            }
        }

        /// <summary>
        /// Pooled objects stay registered while dead, so the tick skips any that are
        /// inactive. One that Despawns during a Tick does not tick again that Tick. (Unity's
        /// Destroy only takes effect at the end of the Render Frame, so a destroyed object
        /// used to keep ticking for the rest of that frame.) Also false for an object Unity
        /// destroyed.
        /// </summary>
        private static bool IsLive(object participant)
        {
            var behaviour = participant as MonoBehaviour;
            return behaviour != null && behaviour.isActiveAndEnabled;
        }

        // This Tick's input for all six Slots, reused every Tick.
        private readonly FrogInput[] tickInputs = new FrogInput[6];

        /// <summary>
        /// Runs exactly one Tick, in a fixed order: read the six devices and apply the input,
        /// sync transforms into Box2D, tick everything, advance the clock.
        ///
        /// Public because this is the seam a rollback session drives. Running a past Tick
        /// again means restoring state, pointing <see cref="SimClock.SeekTo"/> at that Tick,
        /// applying the inputs kept for it, and ticking, without reading any device.
        /// </summary>
        public void AdvanceOneTick(bool sampleInput = true)
        {
            if (sampleInput)
                InputSampler.Sample(tickInputs);
            TickInput.Apply(tickInputs);

            // Push every transform into Box2D before any hit check runs. Auto sync already
            // does this on the first query, but an explicit sync makes the start of each tick
            // a known point. This matters after a rollback restores state: the restored
            // positions must reach the colliders before the resimulated frame queries them.
            Physics2D.SyncTransforms();

            for (int i = 0; i < simulated.Count; i++)
            {
                if (IsLive(simulated[i]))
                    simulated[i].SimulateTick(SimClock.FixedDeltaTime);
            }

            SimClock.Advance();
            TotalTicksRun++;

            if (TickCompleted != null)
                TickCompleted();
        }
    }
}
