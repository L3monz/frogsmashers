using System;
using System.Collections.Generic;
using Hindsight.Core;
using UnityEngine;

namespace FrogSmashers.Netcode
{
    /// <summary>
    /// Runs the simulation on a fixed tick, decoupled from the frame rate, and connects it to
    /// Hindsight.
    ///
    /// This is the one place allowed to look at the wall clock. It reads Unity's frame time
    /// only to decide *how many* Ticks to run, never how far anything moves: the same division
    /// of labour as GGPO's sample, where <c>timeGetTime()</c> lives in the frame-pacing loop
    /// and never reaches the simulation.
    ///
    /// Each scene with a GameController hands the driver a <see cref="FrogSimulation"/>. The
    /// driver runs it in one of two modes:
    /// - <see cref="Mode.Direct"/>: one AdvanceFrame per Tick. No Saved States.
    /// - <see cref="Mode.SyncTest"/>: through Hindsight's SyncTestSession, which every 8 Ticks
    ///   rolls back 8 Ticks and runs them again, and compares checksums. A Desync is reported
    ///   field by field, and the Round continues in Direct mode.
    /// The mode is chosen when a Round starts. The join screen always runs Direct: it is a
    /// lobby before the Round, not part of it.
    /// </summary>
    public class SimulationDriver : MonoBehaviour
    {
        public enum Mode { Direct, SyncTest }

        /// <summary>
        /// Cap on Ticks per rendered frame. After a hitch (a shader compile, the editor
        /// pausing, a breakpoint) the accumulator holds seconds of debt, and paying it all off
        /// in one frame produces a long freeze followed by a lurch. Better to drop the backlog
        /// and carry on. In networked play, time synchronisation between peers takes over.
        /// </summary>
        private const int MaxTicksPerFrame = 8;

        // How far back a Sync Test rolls, and how often. Must not exceed the Saved States
        // Hindsight keeps (MaxPredictionFrames).
        private const int SyncTestDistance = 8;

        private static SimulationDriver instance;

        private float accumulator;
        private FrogSimulation simulation;
        private SyncTestSession<FrogInput> session;
        private bool roundEnded;

        // This Tick's input for all six Slots, reused every Tick.
        private readonly FrogInput[] tickInputs = new FrogInput[6];

        // sim-guard: allow measures how long a Tick takes, for diagnostics; never affects the simulation
        private readonly System.Diagnostics.Stopwatch tickTimer = new System.Diagnostics.Stopwatch();

        /// <summary>
        /// Raised after each first-run Tick, never for a Tick that a Rollback runs again. For
        /// read-only observers such as <see cref="SimDiagnostics"/>. A handler must not change
        /// simulation state.
        /// </summary>
        public static event Action TickCompleted;

        /// <summary>Raised when a scene hands over its simulation, after the mode and seed are set.</summary>
        public static event Action RoundStarted;

        /// <summary>Raised on a Desync, with one line per differing field (at most 10).</summary>
        public static event Action<int, IList<string>> DesyncReported;

        /// <summary>The mode the next Round starts in. Sync Test in the editor and development builds.</summary>
        public static Mode NextRoundMode { get; private set; }

        /// <summary>The mode of the current Round. Becomes Direct after a Desync.</summary>
        public static Mode CurrentMode { get; private set; }

        /// <summary>The random seed of the current Round.</summary>
        public static ulong RoundSeed { get; private set; }

        /// <summary>
        /// True only while a Rollback runs past Ticks again. Sounds, particles and popups check
        /// this and do nothing, so a Rollback does not play them a second time.
        /// </summary>
        public static bool InRollback
        {
            get { return instance != null && instance.session != null && instance.session.InRollback; }
        }

        /// <summary>True while a Sync Test session drives the Round.</summary>
        public static bool SessionActive
        {
            get { return instance != null && instance.session != null; }
        }

        /// <summary>
        /// The number of frames where the Tick backlog was more than
        /// <see cref="MaxTicksPerFrame"/> and the driver discarded it.
        /// </summary>
        public static int BacklogDrops { get; private set; }

        /// <summary>
        /// Running totals for diagnostics, updated together in one frame so a reader always
        /// sees a consistent set. The rule the first three must keep: first-run Ticks =
        /// (time received - time dropped) / FixedDeltaTime, less a fraction of one Tick still
        /// waiting in the accumulator.
        /// </summary>
        public static double TotalTimeReceived { get; private set; }
        public static double TotalTimeDropped { get; private set; }
        public static long TotalTicksRun { get; private set; }
        public static long TotalRollbacks { get; private set; }
        public static long TotalRollbackTicks { get; private set; }
        public static long TotalDesyncs { get; private set; }
        public static double TotalTickMilliseconds { get; private set; }

        /// <summary>
        /// Creates the driver before any scene loads, so it is there when the first
        /// GameController hands over its simulation. Done in code rather than as a component
        /// in each scene, because a missing component would show up as "the game is frozen"
        /// rather than as an error.
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
            NextRoundMode = Debug.isDebugBuild ? Mode.SyncTest : Mode.Direct;

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
        /// A scene's GameController hands over its simulation when the scene loads. Seeds the
        /// random stream for the Round, and starts a Sync Test session if this is a Round and
        /// the next Round's mode is Sync Test.
        /// </summary>
        public static void BeginScene(FrogSimulation simulation, bool isRound)
        {
            if (instance == null)
                return;

            instance.simulation = simulation;
            instance.roundEnded = false;
            instance.accumulator = 0f;

            // Offline, any seed is correct, so use the clock: each Round is different. This is
            // a one-time read before the first Tick, not a read inside the simulation. In
            // online play, the peers agree on the seed before the Round starts.
            // sim-guard: allow offline seed, read once before the first Tick of the Round
            RoundSeed = (ulong)DateTime.UtcNow.Ticks;
            SimRng.Seed(RoundSeed);

            if (isRound && NextRoundMode == Mode.SyncTest)
            {
                var config = new RollbackConfig(playerCount: 6) { MaxPredictionFrames = SyncTestDistance };
                instance.session = new SyncTestSession<FrogInput>(config, simulation, SyncTestDistance);
                CurrentMode = Mode.SyncTest;
            }
            else
            {
                instance.session = null;
                CurrentMode = Mode.Direct;
            }

            if (RoundStarted != null)
                RoundStarted();
        }

        /// <summary>The scene's GameController is going away. Ignored if a newer scene already took over.</summary>
        public static void EndScene(FrogSimulation simulation)
        {
            if (instance == null || instance.simulation != simulation)
                return;
            instance.simulation = null;
            instance.session = null;
        }

        /// <summary>Switches the mode for the next Round (F4 in development builds).</summary>
        public static void ToggleNextRoundMode()
        {
            NextRoundMode = NextRoundMode == Mode.SyncTest ? Mode.Direct : Mode.SyncTest;
        }

        private void Update()
        {
            if (simulation == null)
            {
                accumulator = 0f;
                return;
            }

            // sim-guard: allow the driver reads frame time only to decide how many Ticks to run
            float frameTime = Time.deltaTime;
            accumulator += frameTime;
            TotalTimeReceived += frameTime;

            int ticksRun = 0;
            while (accumulator >= SimClock.FixedDeltaTime && ticksRun < MaxTicksPerFrame && !roundEnded)
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
        /// Runs one first-run Tick: read the six devices, then advance the simulation directly
        /// or through the Sync Test session. When a Tick ends the Round, the driver changes
        /// scene here, outside the simulation, and runs no more Ticks for this scene.
        /// </summary>
        private void AdvanceOneTick()
        {
            InputSampler.Sample(tickInputs);

            tickTimer.Reset();
            tickTimer.Start();
            if (session != null)
            {
                try
                {
                    var result = session.AdvanceFrame(tickInputs);
                    if (result.RolledBack)
                    {
                        TotalRollbacks++;
                        TotalRollbackTicks += result.ResimulatedFrames;
                    }
                }
                catch (DesyncException desync)
                {
                    ReportDesync(desync);
                    session = null;
                    CurrentMode = Mode.Direct;
                }
            }
            else
            {
                simulation.AdvanceFrame(tickInputs);
            }
            tickTimer.Stop();
            TotalTickMilliseconds += tickTimer.Elapsed.TotalMilliseconds;
            TotalTicksRun++;

            if (TickCompleted != null)
                TickCompleted();

            if (simulation.RoundOver && !roundEnded)
            {
                roundEnded = true;
                session = null;
                GameController.FinishRoundAfterSimulation();
            }
        }

        /// <summary>
        /// Decodes both Saved States of the Tick that differed, and reports the fields that
        /// differ, as named paths such as <c>Character[2].velocity.x</c>.
        /// </summary>
        private void ReportDesync(DesyncException desync)
        {
            TotalDesyncs++;
            var expected = simulation.Describe(desync.ExpectedState);
            var actual = simulation.Describe(desync.ActualState);

            var lines = new List<string>();
            int count = Math.Min(expected.Count, actual.Count);
            for (int i = 0; i < count && lines.Count < 10; i++)
            {
                if (expected[i].Value != actual[i].Value)
                    lines.Add(expected[i].Key + ": " + expected[i].Value + " -> " + actual[i].Value);
            }

            if (DesyncReported != null)
                DesyncReported(desync.Frame, lines);
        }
    }
}
