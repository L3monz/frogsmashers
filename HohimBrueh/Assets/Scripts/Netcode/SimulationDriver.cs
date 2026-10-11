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
    /// - <see cref="Mode.Online"/>: through Hindsight's PeerToPeerSession, against the other
    ///   Peer of an Online Match. Each Tick: poll, add this machine's input, advance. A Tick is
    ///   skipped when the session refuses the input (the prediction limit) or recommends a wait.
    /// The mode is chosen when a Round starts. The join screen always runs Direct: it is a
    /// lobby before the Round, not part of it.
    /// </summary>
    public class SimulationDriver : MonoBehaviour
    {
        public enum Mode { Direct, SyncTest, Online }

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
        private int injectSlot = -1;

        // This Tick's input for all six Slots, reused every Tick.
        private readonly FrogInput[] tickInputs = new FrogInput[6];

        // Online play. The session of the current Round, and the one of the Round that just
        // ended, which keeps answering for a moment so the other Peer receives our last inputs
        // (without them it can't confirm the end of the Round, and would time out).
        private const float DrainSeconds = 2f;
        private PeerToPeerSession<FrogInput> online;
        private PeerToPeerSession<FrogInput> draining;
        private float drainUntil;
        private int waitTicks;
        private int onlineRollbacksSeen;
        private int onlineRolledBackTicksSeen;
        private float nextOnlineStats;
        private static readonly FrogSmashers.Online.FrogInputCodec Codec = new FrogSmashers.Online.FrogInputCodec();
        private static readonly IClock Clock = new StopwatchClock();

        /// <summary>Online Ticks skipped: refused at the prediction limit, or waited for the other Peer.</summary>
        public static long TotalOnlineRefusedTicks { get; private set; }
        public static long TotalOnlineWaitedTicks { get; private set; }

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
            get
            {
                return instance != null
                    && ((instance.session != null && instance.session.InRollback)
                        || (instance.online != null && instance.online.InRollback));
            }
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
        /// Bytes allocated during Ticks (including Rollbacks and Saved States), for diagnostics.
        /// Measured as growth of the managed heap; a collection in between makes it read low.
        /// </summary>
        public static long TotalTickAllocatedBytes { get; private set; }

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

            bool online = isRound && FrogSmashers.Online.OnlineMatch.IsActive && !ReplayCheck.IsReplaying;

            // Offline, any seed is correct, so use the clock: each Round is different. This is
            // a one-time read before the first Tick, not a read inside the simulation. Online,
            // both Peers derive the same seed from the Match seed.
            // sim-guard: allow offline seed, read once before the first Tick of the Round
            RoundSeed = (ulong)DateTime.UtcNow.Ticks;
            if (online)
                RoundSeed = FrogSmashers.Online.OnlineMatch.SeedForRound(GameController.levelNo, GameController.isShowDown);
            else if (isRound)
                RoundSeed = ReplayCheck.SeedForRound(RoundSeed);
            SimRng.Seed(RoundSeed);
            // Online Rounds aren't recorded yet: a first run uses predicted inputs, so a
            // recording would need each Tick's final inputs, after any Rollback.
            if (isRound && !online)
                ReplayCheck.BeginRound(simulation, RoundSeed);

            instance.online = null;
            instance.waitTicks = 0;
            if (online)
            {
                instance.StartOnlineSession(simulation);
                if (RoundStarted != null)
                    RoundStarted();
                return;
            }

            // A replay runs Direct: it checks the frame rate, and Sync Test is a separate check.
            if (isRound && NextRoundMode == Mode.SyncTest && !ReplayCheck.IsReplaying)
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
            ReplayCheck.EndRound();
            instance.simulation = null;
            instance.session = null;
            if (instance.online != null)
                instance.StartDraining();
        }

        /// <summary>
        /// The Online Match ended (a Peer left, or the handshake failed). Tells the other Peer
        /// and drops the sessions.
        /// </summary>
        public static void EndOnlineSession()
        {
            if (instance == null)
                return;
            if (instance.online != null)
                instance.online.Disconnect();
            if (instance.draining != null)
                instance.draining.Disconnect();
            instance.online = null;
            instance.draining = null;
            instance.roundEnded = true;
        }

        private void StartOnlineSession(FrogSimulation simulation)
        {
            var link = FrogSmashers.Online.OnlineMatch.Link;
            int local = FrogSmashers.Online.OnlineMatch.LocalSlot;
            int remote = FrogSmashers.Online.OnlineMatch.RemoteSlot;

            // Six Players, as offline, so the Slot layout and the Saved State don't change.
            // Slots 2 to 5 are unused: every Peer gives them a blank input.
            var config = new RollbackConfig(playerCount: 6)
            {
                MaxPredictionFrames = 8,
                FrameDelay = FrogSmashers.Online.OnlineMatch.FrameDelay,
            };
            var setup = new PeerSetup
            {
                BuildId = FrogSmashers.BuildInfo.Id,
                SetupHash = FrogSmashers.Online.OnlineMatch.HashRoundSetup(RoundSeed),
                FramesPerSecond = SimClock.TicksPerSecond,
            };
            setup.AddLocalPlayer(local).AddRemotePlayer(remote, FrogSmashers.Online.OnlineMatch.Link.Remote);

            online = new PeerToPeerSession<FrogInput>(config, simulation, link, Codec, setup, Clock);
            onlineRollbacksSeen = 0;
            onlineRolledBackTicksSeen = 0;
            CurrentMode = Mode.Online;
            Debug.Log("[ONLINE] Round " + GameController.levelNo + (GameController.isShowDown ? " (Showdown)" : "")
                + ": session started, local Slot " + local + ", seed " + RoundSeed.ToString("X16")
                + ", setup " + setup.SetupHash.ToString("X8") + ", build " + setup.BuildId);
        }

        private void StartDraining()
        {
            draining = online;
            online = null;
            // sim-guard: allow a network timeout for the ended Round's session; not simulation
            drainUntil = Time.realtimeSinceStartup + DrainSeconds;
        }

        // Keeps the last Round's session answering until the other Peer has every input it
        // needs, or briefly at most. Its Ticks are all confirmed, so it never rolls back.
        private void DrainPreviousSession()
        {
            if (draining == null)
                return;
            FrogSmashers.Online.OnlineMatch.Link.Poll();
            draining.Poll();
            while (draining.TryGetEvent(out SessionEvent ignored)) { }
            // sim-guard: allow a network timeout for the ended Round's session; not simulation
            if (Time.realtimeSinceStartup > drainUntil
                || draining.GetStats(FrogSmashers.Online.OnlineMatch.Link.Remote).UnacknowledgedFrames == 0)
            {
                draining = null;
            }
        }

        /// <summary>
        /// Runs no more Ticks in the current scene, before a development tool changes scene
        /// (the replay check). Changing Round Setup under a scene that still ticks would change
        /// its last Ticks.
        /// </summary>
        public static void HaltCurrentScene()
        {
            if (instance == null)
                return;
            instance.roundEnded = true;
            instance.session = null;
        }

        /// <summary>
        /// For a development check only (F9): makes the next suitable first-run Frame nudge the
        /// Character in <paramref name="slot"/>, so the Sync Test reports a Desync. Returns false
        /// when no Sync Test is running.
        /// </summary>
        public static bool RequestInjectedDesync(int slot)
        {
            if (instance == null || instance.session == null)
                return false;
            instance.injectSlot = slot;
            return true;
        }

        /// <summary>Switches the mode for the next Round (F4 in development builds).</summary>
        public static void ToggleNextRoundMode()
        {
            NextRoundMode = NextRoundMode == Mode.SyncTest ? Mode.Direct : Mode.SyncTest;
        }

        private void Update()
        {
            DrainPreviousSession();

            if (simulation == null)
            {
                accumulator = 0f;
                return;
            }

            // Online, nothing runs until both Peers finish the handshake.
            if (online != null && !online.IsSynchronized)
            {
                accumulator = 0f;
                FrogSmashers.Online.OnlineMatch.Link.Poll();
                online.Poll();
                HandleOnlineEvents();
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
                if (online != null)
                    AdvanceOneOnlineTick();
                else
                    AdvanceOneTick();
                ticksRun++;
            }

            if (online != null)
                LogOnlineStats();

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
            if (!ReplayCheck.TryGetReplayInputs(tickInputs))
                InputSampler.Sample(tickInputs);

            long heapBefore = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            tickTimer.Reset();
            tickTimer.Start();
            if (session != null)
            {
                // A nudge in the last Frame of a check window would only change the state after
                // the window, which this check never compares, and the next window would start
                // from the nudged state. So wait for any other Frame.
                if (injectSlot >= 0 && session.FrameCount % SyncTestDistance != SyncTestDistance - 1)
                {
                    simulation.InjectDesyncAfterThisFrame(injectSlot);
                    injectSlot = -1;
                }

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
                    injectSlot = -1;
                    CurrentMode = Mode.Direct;
                }
            }
            else
            {
                simulation.AdvanceFrame(tickInputs);
            }
            tickTimer.Stop();
            TotalTickMilliseconds += tickTimer.Elapsed.TotalMilliseconds;
            TotalTickAllocatedBytes += Math.Max(0, UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() - heapBefore);
            TotalTicksRun++;

            if (TickCompleted != null)
                TickCompleted();

            if (ReplayCheck.AfterTick(simulation, tickInputs, simulation.RoundOver))
            {
                roundEnded = true;
                session = null;
                return;
            }

            if (simulation.RoundOver && !roundEnded)
            {
                roundEnded = true;
                session = null;
                GameController.FinishRoundAfterSimulation();
            }
        }

        /// <summary>
        /// One Tick of an Online Round, in Hindsight's order: poll (which rolls back if a remote
        /// input proved a prediction wrong), add this machine's input, advance. A refused input
        /// or a recommended wait skips the Tick; the frame pacing carries on.
        /// </summary>
        private void AdvanceOneOnlineTick()
        {
            FrogSmashers.Online.OnlineMatch.Link.Poll();
            online.Poll();
            if (!HandleOnlineEvents() || CheckOnlineRoundEnd())
                return;

            if (waitTicks > 0)
            {
                // Spread out, one Tick at a time, so the game doesn't visibly freeze.
                waitTicks--;
                TotalOnlineWaitedTicks++;
                return;
            }

            if (!online.AddLocalInput(FrogSmashers.Online.OnlineMatch.LocalSlot, InputSampler.SampleLocal()))
            {
                TotalOnlineRefusedTicks++;
                return;
            }

            long heapBefore = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            tickTimer.Reset();
            tickTimer.Start();
            online.AdvanceFrame();
            tickTimer.Stop();
            TotalTickMilliseconds += tickTimer.Elapsed.TotalMilliseconds;
            TotalTickAllocatedBytes += Math.Max(0, UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() - heapBefore);
            TotalTicksRun++;

            // Rollbacks happen inside Poll; count them here, once per Tick.
            TotalRollbacks += online.RollbackCount - onlineRollbacksSeen;
            TotalRollbackTicks += online.RolledBackFrames - onlineRolledBackTicksSeen;
            onlineRollbacksSeen = online.RollbackCount;
            onlineRolledBackTicksSeen = online.RolledBackFrames;

            if (TickCompleted != null)
                TickCompleted();

            CheckOnlineRoundEnd();
        }

        /// <summary>
        /// The Round ends once the Tick that ended it is confirmed by both Peers. Checking the
        /// latest Tick instead would be wrong: a Peer that ran a few predicted Ticks ahead would
        /// wait for inputs the other Peer never sends, because that Peer already stopped.
        /// </summary>
        private bool CheckOnlineRoundEnd()
        {
            if (online == null || roundEnded || !simulation.RoundOver || online.LastConfirmedFrame < simulation.RoundOverTick)
                return false;

            Debug.Log("[ONLINE] Round over at Tick " + simulation.RoundOverTick + " (confirmed), "
                + online.RollbackCount + " Rollbacks, " + online.RolledBackFrames + " Ticks run again");
            roundEnded = true;
            StartDraining();
            GameController.FinishRoundAfterSimulation();
            return true;
        }

        /// <summary>Handles the session's events. Returns false when the Match has ended.</summary>
        private bool HandleOnlineEvents()
        {
            while (online != null && online.TryGetEvent(out SessionEvent e))
            {
                switch (e.Kind)
                {
                    case SessionEventKind.Synchronized:
                        Debug.Log("[ONLINE] synchronized, Tick 0 starts");
                        break;
                    case SessionEventKind.WaitRecommended:
                        waitTicks += e.Frames;
                        Debug.Log("[ONLINE] ahead of the other Peer: waiting " + e.Frames + " Ticks");
                        break;
                    case SessionEventKind.Interrupted:
                    case SessionEventKind.Resumed:
                        Debug.Log("[ONLINE] " + e);
                        break;
                    case SessionEventKind.DesyncDetected:
                        ReportOnlineDesync(e);
                        break;
                    case SessionEventKind.SyncFailed:
                        FrogSmashers.Online.OnlineMatch.End(e.Failure == SyncFailure.BuildId || e.Failure == SyncFailure.ProtocolVersion
                            ? "Your friend is on a different version of the game."
                            : "The two games disagree on the Round (" + e.Failure + ").");
                        return false;
                    case SessionEventKind.Disconnected:
                        FrogSmashers.Online.OnlineMatch.End("Your friend left or the connection was lost.");
                        return false;
                }
            }
            return online != null;
        }

        /// <summary>
        /// Online Desync: logs both checksums and writes this Peer's Saved State of that Tick, with
        /// its field-by-field description, to persistentDataPath/desyncs/. The other Peer writes
        /// its own; comparing the two descriptions names the field. Play continues.
        /// </summary>
        private void ReportOnlineDesync(SessionEvent e)
        {
            TotalDesyncs++;
            Debug.LogWarning("[SIM] desync online tick=" + e.Frame + " local=" + e.LocalChecksum.ToString("X8")
                + " remote=" + e.RemoteChecksum.ToString("X8"));
            if (!online.TryGetCheckpointState(e.Frame, out StateBuffer state))
                return;

            try
            {
                string folder = System.IO.Path.Combine(Application.persistentDataPath, "desyncs");
                System.IO.Directory.CreateDirectory(folder);
                string name = "desync-tick" + e.Frame + "-slot" + FrogSmashers.Online.OnlineMatch.LocalSlot + "-" + FrogSmashers.BuildInfo.Id;
                byte[] bytes = state.ToArray();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, name + ".bin"), bytes);
                var lines = new List<string>();
                foreach (var field in simulation.Describe(bytes))
                    lines.Add(field.Key + " = " + field.Value);
                System.IO.File.WriteAllLines(System.IO.Path.Combine(folder, name + ".txt"), lines);
                Debug.LogWarning("[SIM] desync files written to " + folder + "/" + name + ".*");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SIM] couldn't write the desync files: " + ex.Message);
            }
        }

        private void LogOnlineStats()
        {
            // sim-guard: allow paces a diagnostics log line; not simulation
            if (Time.realtimeSinceStartup < nextOnlineStats)
                return;
            // sim-guard: allow paces a diagnostics log line; not simulation
            nextOnlineStats = Time.realtimeSinceStartup + 1f;
            var stats = online.GetStats(FrogSmashers.Online.OnlineMatch.Link.Remote);
            Debug.Log("[ONLINE] tick=" + online.FrameCount + " confirmed=" + online.LastConfirmedFrame
                + " rtt=" + stats.RoundTripMs + "ms loss=" + stats.LossPercent.ToString("0.0") + "%"
                + " advantage=" + stats.LocalFrameAdvantage + "/" + stats.RemoteFrameAdvantage
                + " rollbacks=" + online.RollbackCount + " rolledBackTicks=" + online.RolledBackFrames
                + " refused=" + TotalOnlineRefusedTicks + " waited=" + TotalOnlineWaitedTicks
                + " sent=" + stats.BytesSentPerSecond.ToString("0") + "B/s");
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
