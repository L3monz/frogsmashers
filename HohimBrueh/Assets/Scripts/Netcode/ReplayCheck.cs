using System;
using System.IO;
using System.Linq;
using Hindsight.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrogSmashers.Netcode
{
    /// <summary>
    /// Proves that the frame rate does not change the simulation. Development builds only.
    ///
    /// Recording: every Round records its Round Setup, the checksum of its starting state, and
    /// for each Tick the six inputs and the checksum of the state after the Tick. When the
    /// scene ends, the recording is written to <c>persistentDataPath/replays/</c>; the newest
    /// <see cref="FilesKept"/> files are kept.
    ///
    /// Replay (F10): loads the newest recording, restores its Round Setup, loads its Level,
    /// and runs the recorded inputs in Direct mode at whatever frame-rate cap is set. After
    /// every Tick it compares the checksum with the recorded one. A Round recorded at one
    /// frame rate must match Tick for Tick at any other, because the frame rate only decides
    /// how many Ticks run per frame, never what a Tick does. The first mismatch is logged
    /// with its Tick. When the replay ends, pass or fail, the game loads the join screen: the
    /// normal round end would count the replay as a Round of the Match.
    ///
    /// Online Rounds are recorded too, differently: a first run there uses predicted inputs,
    /// and a Rollback runs past Ticks again with the real ones. So every run of a Tick is
    /// recorded by its number, overwriting the earlier run, and when the Round ends the
    /// recording keeps only the Ticks confirmed by both Peers. Both Peers' recordings of the
    /// same Round must then be identical, and each replays offline like any other.
    ///
    /// The driver calls <see cref="SeedForRound"/>, <see cref="BeginRound"/>,
    /// <see cref="TryGetReplayInputs"/>, <see cref="AfterTick"/> and <see cref="EndRound"/>.
    /// Not simulation code: it reads the state after each Tick and never changes it.
    /// </summary>
    public static class ReplayCheck
    {
        private const int FilesKept = 10;
        private const string Extension = ".frogreplay";

        private enum Mode { Idle, Recording, Replaying }

        private static Mode mode;
        private static readonly RoundRecording recording = new RoundRecording();
        private static RoundRecording replay;
        private static RoundRecording pendingReplay;
        private static int replayTick;
        private static bool recordingOnline;
        // The online Slot, taken when the Round starts: by the time a Match that ended early
        // saves its recording, the Match itself is already gone.
        private static int recordingSlot;
        private static readonly StateBuffer stateBuffer = new StateBuffer(4096);

        /// <summary>True while a recorded Round is being replayed. The driver runs it in Direct mode.</summary>
        public static bool IsReplaying
        {
            get { return mode == Mode.Replaying || pendingReplay != null; }
        }

        private static string Folder
        {
            get { return Path.Combine(Application.persistentDataPath, "replays"); }
        }

        // Development builds started with -replay-newest replay the newest recording straight
        // away and quit when it's done, so a script can check a recording without a key press.
        private static bool AutoReplay
        {
            get { return Debug.isDebugBuild && Array.IndexOf(Environment.GetCommandLineArgs(), "-replay-newest") >= 0; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartAutoReplay()
        {
            if (AutoReplay && !StartReplayOfNewest())
                Application.Quit();
        }

        /// <summary>F10: replays the newest recording. Returns false if there is none.</summary>
        public static bool StartReplayOfNewest()
        {
            string path = NewestRecordingPath();
            if (path == null)
            {
                Log("replay none: no recordings in " + Folder);
                return false;
            }

            RoundRecording loaded;
            try
            {
                using (var file = File.OpenRead(path))
                    loaded = RoundRecording.Read(file);
            }
            catch (Exception e)
            {
                Log("replay none: could not read " + path + ": " + e.Message);
                return false;
            }

            // Stop the current scene's Ticks before changing anything it reads.
            SimulationDriver.HaltCurrentScene();
            GameController.RestoreRoundSetup(loaded);
            pendingReplay = loaded;
            Log("replay start file=" + Path.GetFileName(path) + " level=" + loaded.Level
                + " ticks=" + loaded.TickCount + " finished=" + loaded.EndedWithRoundOver
                + " recordedCap=" + CapName(loaded.RecordedCap) + " cap=" + CapName(Application.targetFrameRate));
            SceneManager.LoadScene(loaded.Level);
            return true;
        }

        /// <summary>The seed for a Round that is starting: the recorded one when replaying, otherwise <paramref name="fresh"/>.</summary>
        public static ulong SeedForRound(ulong fresh)
        {
            return pendingReplay != null ? pendingReplay.Seed : fresh;
        }

        /// <summary>
        /// Called when a Round's scene has set up and seeded its simulation, before Tick 0.
        /// Starts a replay if one is pending, otherwise starts recording.
        /// </summary>
        public static void BeginRound(FrogSimulation simulation, ulong seed, bool online = false)
        {
            // If the new scene set up before the old one ended, finish the old Round first.
            if (mode != Mode.Idle)
                EndRound();

            uint start = Checksum(simulation);

            if (pendingReplay != null)
            {
                replay = pendingReplay;
                pendingReplay = null;
                replayTick = 0;
                mode = Mode.Replaying;
                if (start != replay.StartChecksum)
                    Finish(false, "the starting state differs, so the Round Setup was not fully restored");
                return;
            }

            if (!Debug.isDebugBuild)
                return;

            recording.Clear();
            GameController.CaptureRoundSetup(recording);
            recording.Seed = seed;
            recording.RecordedCap = Application.targetFrameRate;
            recording.StartChecksum = start;
            mode = Mode.Recording;
            recordingOnline = online;
            recordingSlot = FrogSmashers.Online.OnlineMatch.LocalSlot;
            if (online)
                simulation.TickRun = RecordOnlineTick;
        }

        // Every run of a Tick in an online Round, Rollbacks included; see SetTick.
        private static void RecordOnlineTick(FrogSimulation simulation, int tick, FrogInput[] inputs)
        {
            if (mode == Mode.Recording && recordingOnline)
                recording.SetTick(tick, inputs, Checksum(simulation));
        }

        /// <summary>
        /// Online: keeps the Ticks up to <paramref name="lastTick"/>, the last one both Peers
        /// confirmed; later ones may still have run on predictions.
        /// </summary>
        public static void KeepOnlineTicksThrough(int lastTick, bool roundOver)
        {
            if (mode != Mode.Recording || !recordingOnline)
                return;
            recording.Truncate(lastTick + 1);
            if (roundOver)
                recording.EndedWithRoundOver = true;
        }

        /// <summary>While replaying, fills <paramref name="inputs"/> with the next Tick's recorded inputs.</summary>
        public static bool TryGetReplayInputs(FrogInput[] inputs)
        {
            if (mode != Mode.Replaying)
                return false;
            replay.GetInputs(replayTick, inputs);
            return true;
        }

        /// <summary>
        /// Called after each first-run Tick. Records it, or checks it against the recording.
        /// Returns true when a replay has ended: the driver must run no more Ticks.
        /// </summary>
        public static bool AfterTick(FrogSimulation simulation, FrogInput[] inputs, bool roundOver)
        {
            if (mode == Mode.Recording)
            {
                recording.AddTick(inputs, Checksum(simulation));
                if (roundOver)
                    recording.EndedWithRoundOver = true;
                return false;
            }

            if (mode != Mode.Replaying)
                return false;

            int tick = replayTick++;
            uint actual = Checksum(simulation);
            if (actual != replay.ChecksumAt(tick))
            {
                Finish(false, "first mismatch at tick=" + tick);
                return true;
            }

            bool last = replayTick == replay.TickCount;
            if (roundOver && !last)
            {
                Finish(false, "the Round ended early, at tick=" + tick);
                return true;
            }
            if (last)
            {
                if (replay.EndedWithRoundOver && !roundOver)
                    Finish(false, "the Round did not end on the last recorded tick=" + tick);
                else
                    Finish(true, "all " + replay.TickCount + " ticks match");
                return true;
            }
            return false;
        }

        /// <summary>Called when a Round's scene ends. Writes the recording, if there is one.</summary>
        public static void EndRound()
        {
            if (mode == Mode.Recording && recording.TickCount > 0)
                Save();
            else if (mode == Mode.Replaying)
                Log("replay result=INTERRUPTED ticks=" + replayTick + " of " + replay.TickCount);
            mode = Mode.Idle;
            recordingOnline = false;
        }

        private static void Finish(bool passed, string reason)
        {
            Log("replay result=" + (passed ? "PASS" : "FAIL") + " level=" + replay.Level
                + " ticks=" + replayTick + " of " + replay.TickCount
                + " recordedCap=" + CapName(replay.RecordedCap) + " cap=" + CapName(Application.targetFrameRate)
                + " reason=" + reason);
            mode = Mode.Idle;
            replay = null;
            SimulationDriver.HaltCurrentScene();
            if (AutoReplay)
                Application.Quit();
            else
                SceneManager.LoadScene("JoinScreen");
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string online = recordingOnline ? "-online-slot" + recordingSlot : "";
                string name = "round-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + recording.Level + online + Extension;
                string path = Path.Combine(Folder, name);
                using (var file = File.Create(path))
                    recording.Write(file);
                Log("recording saved file=" + name + " ticks=" + recording.TickCount
                    + " finished=" + recording.EndedWithRoundOver + " bytes=" + new FileInfo(path).Length);

                foreach (var old in Directory.GetFiles(Folder, "*" + Extension).OrderByDescending(f => f).Skip(FilesKept))
                    File.Delete(old);
            }
            catch (Exception e)
            {
                Log("recording not saved: " + e.Message);
            }
        }

        private static string NewestRecordingPath()
        {
            if (!Directory.Exists(Folder))
                return null;
            return Directory.GetFiles(Folder, "*" + Extension).OrderByDescending(f => f).FirstOrDefault();
        }

        // FNV-1a over the Saved State. Saved into a reused buffer, so this allocates nothing.
        private static uint Checksum(FrogSimulation simulation)
        {
            stateBuffer.Clear();
            simulation.SaveState(stateBuffer);
            uint hash = 2166136261;
            byte[] data = stateBuffer.Data;
            for (int i = 0; i < stateBuffer.Length; i++)
            {
                hash ^= data[i];
                hash *= 16777619;
            }
            return hash;
        }

        private static string CapName(int targetFrameRate)
        {
            return targetFrameRate <= 0 ? "none" : targetFrameRate.ToString();
        }

        private static void Log(string message)
        {
            Debug.Log("[SIM] " + message);
        }
    }
}
