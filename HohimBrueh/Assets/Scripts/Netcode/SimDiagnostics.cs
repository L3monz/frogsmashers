using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace FrogSmashers.Netcode
{
    /// <summary>
    /// Playtest instrumentation for the fixed tick (SMA-19). Writes lines that start with
    /// "[SIM]" to the Unity log, so a script can read them from Editor.log after a playtest.
    ///
    /// It reports three things:
    /// - Once per second: the frame rate, the ticks run in that second, and backlog drops.
    ///   The tick count must be 60 at every frame rate.
    /// - Each ground jump: the apex height, the ticks to the apex, and how many ticks the jump
    ///   button was held. In this game, holding jump keeps the frog rising, so the apex is a
    ///   function of the hold length. With a fixed tick, the same hold length must give the
    ///   same apex at every frame rate. A jump is "clean" only if nothing else changed its
    ///   arc: no ceiling, no attack, no hit.
    /// - Each hit: the knockback velocity on the tick of the hit.
    /// - Each spawn, and once per second the position and state of each frog.
    /// - The gamepads Unity sees, and each face-button press, to debug controller support.
    ///
    /// F3 cycles a frame-rate cap, so a tester can compare frame rates in one session.
    ///
    /// This class only reads simulation state. It never changes it. It runs only in the
    /// editor and in development builds. It uses a Dictionary and the wall clock, which
    /// simulation code must not use (SMA-18). That is acceptable here because nothing in
    /// the simulation reads its output.
    /// </summary>
    public class SimDiagnostics : MonoBehaviour
    {
        private const string Prefix = "[SIM] ";

        // 0 means no cap. The order goes from fast to slow, so one cycle tests the full range.
        private static readonly int[] FrameCaps = { 0, 144, 60, 30, 20 };

        private class JumpTracker
        {
            public bool WasOnGround;
            public bool Tracking;
            public int HoldTicks;
            public bool StillHolding;
            public bool Clean;
            public float PreviousVelocityY;
            public float StartY;
            public float MaxY;
            public int StartTick;
            public int HitsTaken;
        }

        private readonly Dictionary<Character, JumpTracker> trackers = new Dictionary<Character, JumpTracker>();

        private int capIndex;
        private float secondTimer;
        private float simTimeThisSecond;
        private int framesThisSecond;
        private int ticksThisSecond;
        private int dropsAtLastReport;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (!Debug.isDebugBuild)
                return;

            var host = new GameObject("SimDiagnostics");
            DontDestroyOnLoad(host);
            host.AddComponent<SimDiagnostics>();
        }

        private void OnEnable()
        {
            SimulationDriver.TickCompleted += OnTick;
            Log("session start, unityVersion=" + Application.unityVersion
                + " vSyncCount=" + QualitySettings.vSyncCount
                + " targetFrameRate=" + Application.targetFrameRate);
            LogGamepads("at start");
            UnityEngine.InputSystem.InputSystem.onDeviceChange += OnDeviceChange;
        }

        private void OnDeviceChange(UnityEngine.InputSystem.InputDevice device, UnityEngine.InputSystem.InputDeviceChange change)
        {
            Log("device " + change + ": " + device.layout + " \"" + device.displayName + "\"");
            LogGamepads("after change");
        }

        private static void LogGamepads(string when)
        {
            var pads = UnityEngine.InputSystem.Gamepad.all;
            var names = new List<string>();
            for (int i = 0; i < pads.Count; i++)
                names.Add("[" + i + "] " + pads[i].layout + " \"" + pads[i].displayName + "\"");
            Log("gamepads " + when + ": " + (names.Count == 0 ? "none" : string.Join(", ", names.ToArray())));
        }

        private static void LogGamepadPresses()
        {
            var pads = UnityEngine.InputSystem.Gamepad.all;
            for (int i = 0; i < pads.Count; i++)
            {
                var pad = pads[i];
                if (pad.buttonSouth.wasPressedThisFrame) Log("pad[" + i + "] south pressed");
                if (pad.buttonWest.wasPressedThisFrame) Log("pad[" + i + "] west pressed");
                if (pad.buttonEast.wasPressedThisFrame) Log("pad[" + i + "] east pressed");
                if (pad.buttonNorth.wasPressedThisFrame) Log("pad[" + i + "] north pressed");
            }
        }

        private void OnDisable()
        {
            SimulationDriver.TickCompleted -= OnTick;
            UnityEngine.InputSystem.InputSystem.onDeviceChange -= OnDeviceChange;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F3))
                CycleFrameCap();

            LogGamepadPresses();

            framesThisSecond++;
            secondTimer += Time.unscaledDeltaTime;

            // Time.deltaTime is what the driver accumulates. It is capped per frame by
            // Time.maximumDeltaTime and scaled by Time.timeScale, so after a stall it is less
            // than the wall-clock time. The driver must run exactly this much time as ticks.
            simTimeThisSecond += Time.deltaTime;
            if (secondTimer >= 1f)
            {
                int drops = SimulationDriver.BacklogDrops - dropsAtLastReport;
                dropsAtLastReport = SimulationDriver.BacklogDrops;

                Log(string.Format(CultureInfo.InvariantCulture,
                    "second fps={0:0.0} ticks={1} tick={2} drops={3} cap={4} timeScale={5} wall={6:0.000} sim={7:0.000}",
                    framesThisSecond / secondTimer, ticksThisSecond, SimClock.Tick, drops,
                    CapLabel(), Time.timeScale, secondTimer, simTimeThisSecond));

                foreach (var player in GameController.activePlayers)
                {
                    var c = player.character;
                    if (c == null)
                    {
                        Log("frog P" + Slot(player) + " none (dead or not spawned)");
                        continue;
                    }
                    Vector3 p = c.transform.position;
                    Log(string.Format(CultureInfo.InvariantCulture,
                        "frog P{0} pos=({1:0.00}, {2:0.00}) vel=({3:0.00}, {4:0.00}) onGround={5} state={6} active={7}",
                        Slot(player), p.x, p.y, c.velocity.x, c.velocity.y, c.OnGround, c.state,
                        c.gameObject.activeInHierarchy));
                }

                secondTimer = 0f;
                simTimeThisSecond = 0f;
                framesThisSecond = 0;
                ticksThisSecond = 0;
            }
        }

        private void CycleFrameCap()
        {
            capIndex = (capIndex + 1) % FrameCaps.Length;

            // Application.targetFrameRate has no effect while vSync is on.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = FrameCaps[capIndex] == 0 ? -1 : FrameCaps[capIndex];

            Log("frame cap set to " + CapLabel());
        }

        private string CapLabel()
        {
            return FrameCaps[capIndex] == 0 ? "none" : FrameCaps[capIndex].ToString(CultureInfo.InvariantCulture);
        }

        private void OnTick()
        {
            ticksThisSecond++;

            foreach (var player in GameController.activePlayers)
            {
                var character = player.character;
                if (character == null)
                    continue;

                JumpTracker tracker;
                if (!trackers.TryGetValue(character, out tracker))
                {
                    tracker = new JumpTracker { WasOnGround = character.OnGround, HitsTaken = character.hitsTaken };
                    trackers[character] = tracker;
                    Vector3 p = character.transform.position;
                    Log(string.Format(CultureInfo.InvariantCulture, "spawn P{0} at ({1:0.00}, {2:0.00}) tick={3}",
                        Slot(player), p.x, p.y, SimClock.Tick));
                }

                ObserveHit(character, tracker);
                ObserveJump(character, tracker);
                tracker.WasOnGround = character.OnGround;
            }

            // Characters that were destroyed compare equal to null.
            if (trackers.Count > 12)
            {
                var dead = new List<Character>();
                foreach (var key in trackers.Keys)
                {
                    if (key == null)
                        dead.Add(key);
                }
                foreach (var key in dead)
                    trackers.Remove(key);
            }
        }

        private void ObserveHit(Character character, JumpTracker tracker)
        {
            if (character.hitsTaken == tracker.HitsTaken)
                return;

            if (character.hitsTaken > tracker.HitsTaken)
            {
                Log(string.Format(CultureInfo.InvariantCulture,
                    "hit P{0} hitsTaken={1} by=P{2} velocity=({3:R}, {4:R}) tick={5}",
                    Slot(character.player), character.hitsTaken, Slot(character.lastHitByPlayer),
                    character.velocity.x, character.velocity.y, SimClock.Tick));
            }

            tracker.HitsTaken = character.hitsTaken;

            // A hit changes the arc, so a jump in progress is no longer a clean sample.
            tracker.Tracking = false;
        }

        private void ObserveJump(Character character, JumpTracker tracker)
        {
            float y = character.transform.position.y;

            if (!tracker.Tracking)
            {
                if (tracker.WasOnGround && !character.OnGround && character.velocity.y > 0f)
                {
                    tracker.Tracking = true;
                    tracker.StillHolding = character.input.aButton;
                    tracker.HoldTicks = tracker.StillHolding ? 1 : 0;
                    tracker.Clean = character.state == CharacterState.Normal;
                    tracker.PreviousVelocityY = character.velocity.y;
                    tracker.StartY = tracker.MaxY = y;
                    tracker.StartTick = SimClock.Tick;
                }
                return;
            }

            if (character.OnGround)
            {
                tracker.Tracking = false;
                return;
            }

            if (tracker.StillHolding && character.input.aButton)
                tracker.HoldTicks++;
            else
                tracker.StillHolding = false;

            if (character.state != CharacterState.Normal)
                tracker.Clean = false;

            // Gravity removes at most gravity * dt per tick. A larger drop means something else
            // stopped the frog, for example a ceiling.
            if (tracker.PreviousVelocityY - character.velocity.y > character.gravity * SimClock.FixedDeltaTime * 1.01f)
                tracker.Clean = false;
            tracker.PreviousVelocityY = character.velocity.y;

            if (y > tracker.MaxY)
                tracker.MaxY = y;

            if (character.velocity.y <= 0f)
            {
                // R format prints the shortest string that gives back the identical float, so
                // two equal lines prove bit-identical results.
                Log(string.Format(CultureInfo.InvariantCulture,
                    "jump P{0} apex={1:R} ticks={2} hold={3} clean={4} cap={5}",
                    Slot(character.player), tracker.MaxY - tracker.StartY,
                    SimClock.Tick - tracker.StartTick, tracker.HoldTicks,
                    tracker.Clean ? "yes" : "no", CapLabel()));
                tracker.Tracking = false;
            }
        }

        private static string Slot(Player player)
        {
            return player == null ? "?" : player.sortPriority.ToString(CultureInfo.InvariantCulture);
        }

        private static void Log(string message)
        {
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", Prefix + message);
        }
    }
}
