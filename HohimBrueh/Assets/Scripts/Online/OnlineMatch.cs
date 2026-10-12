using System;
using FrogSmashers.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrogSmashers.Online
{
    /// <summary>
    /// One Online Match between two Peers, from connecting until someone leaves.
    ///
    /// For now the Peers connect by IP address from the command line:
    ///   FrogSmashers.exe -online-host 9100                 (the Room Owner, Slot 0)
    ///   FrogSmashers.exe -online-join 192.168.1.20:9100    (the friend, Slot 1)
    /// with optional -online-latency MS and -online-loss PERCENT to simulate a bad network.
    /// The room-code screens replace the command line later; they produce the same link.
    ///
    /// Once connected, the Room Owner sends the Match setup (the Match seed and the option
    /// toggles). Both games apply the same fixed Round Setup (Slots 0 and 1, default colours,
    /// free-for-all, no drop-in joins) and load the first Level together. From then on the
    /// normal Level, score screen and Showdown flow runs on both, and each Round's handshake
    /// lines the two up again.
    /// </summary>
    public sealed class OnlineMatch : MonoBehaviour
    {
        /// <summary>Input delay online, in Ticks: hides about 33 ms of latency without a Rollback.</summary>
        public const int FrameDelay = 2;

        private const byte SetupMessage = 1;
        private const byte SetupMessageVersion = 1;
        private const float MessageSeconds = 6f;

        private static OnlineMatch instance;
        private static string lastMessage;
        private static float lastMessageUntil;

        private IOnlineLink link;
        private bool isRoomOwner;
        private bool setupReceived;
        private bool started;
        private ulong matchSeed;

        // Development builds: leave the Match this many seconds after it starts, so an automatic
        // test ends cleanly and both Peers save their recordings. 0 = never.
        private float leaveAfterSeconds;
        private float leaveAt;

        /// <summary>True from the start of an Online Match until it ends.</summary>
        public static bool IsActive => instance != null && instance.started;

        public static bool IsRoomOwner => instance != null && instance.isRoomOwner;

        /// <summary>The Room Owner plays in Slot 0 and the friend in Slot 1.</summary>
        public static int LocalSlot => IsRoomOwner ? 0 : 1;
        public static int RemoteSlot => IsRoomOwner ? 1 : 0;

        public static IOnlineLink Link => instance != null ? instance.link : null;

        /// <summary>Development builds with -online-bot: the local Player plays scripted input.</summary>
        public static bool BotInput { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void StartFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            string host = ArgumentAfter(args, "-online-host");
            string join = ArgumentAfter(args, "-online-join");
            if (host == null && join == null)
                return;

            var conditions = new NetworkConditions
            {
                LatencyMs = ParseInt(ArgumentAfter(args, "-online-latency")),
                LossPercent = ParseInt(ArgumentAfter(args, "-online-loss")),
            };

            // A window without focus must keep running, or its Peer stops sending and the
            // other Peer sees it as gone. (Two instances on one PC: one is always unfocused.)
            Application.runInBackground = true;

            IOnlineLink link;
            try
            {
                if (host != null)
                {
                    link = LiteNetLibLink.Listen(ParseInt(host), conditions);
                }
                else
                {
                    string[] parts = join.Split(':');
                    link = LiteNetLibLink.Connect(parts[0], parts.Length > 1 ? ParseInt(parts[1]) : 9100, conditions);
                }
            }
            catch (Exception ex)
            {
                // No Match without a link. Say why on screen, and play on offline.
                Debug.LogError("[ONLINE] couldn't open the network connection: " + ex);
                ShowMessage("Couldn't start online play: " + ex.Message);
                return;
            }

            var go = new GameObject("OnlineMatch");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<OnlineMatch>();
            instance.isRoomOwner = host != null;
            instance.link = link;
            if (Debug.isDebugBuild)
            {
                instance.leaveAfterSeconds = ParseInt(ArgumentAfter(args, "-online-leave-after"));
                BotInput = Array.IndexOf(args, "-online-bot") >= 0;
            }
            Debug.Log("[ONLINE] " + (instance.isRoomOwner ? "Room Owner" : "friend") + ", " + instance.link.Description
                + ", " + conditions + ", build " + BuildInfo.Id);
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        // The Outro returns to the title screen when a Match is won: that ends it too.
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (started && instance == this && scene.name == "TitleScreen")
                End("The Match is over.");
        }

        private void Update()
        {
            link.Poll();

            if (link.State == LinkState.Closed)
            {
                End(link.CloseReason);
                return;
            }
            if (link.State != LinkState.Connected)
                return;

            while (link.TryReceiveMatchMessage(out byte[] message))
                OnMatchMessage(message);

            if (started && leaveAfterSeconds > 0 && Time.realtimeSinceStartup > leaveAt)
            {
                End("Test finished: left after " + leaveAfterSeconds + " s.");
                return;
            }

            if (!started && isRoomOwner && !setupReceived)
                SendSetup();

            if (!started && setupReceived)
                StartMatch();
        }

        private void SendSetup()
        {
            // A one-time read before the Match: both Peers get this seed from the message,
            // so it doesn't matter where it came from.
            matchSeed = ((ulong)(uint)Environment.TickCount << 32) ^ (ulong)DateTime.UtcNow.Ticks;
            var message = new byte[11];
            message[0] = SetupMessage;
            message[1] = SetupMessageVersion;
            for (int i = 0; i < 8; i++)
                message[2 + i] = (byte)(matchSeed >> (8 * i));
            message[10] = (byte)((GameController.charactersBounceEachOther ? 1 : 0)
                                 | (GameController.weirdBounceTrajectories ? 2 : 0)
                                 | (GameController.onlyBounceBeforeRecover ? 4 : 0));
            link.SendMatchMessage(message);
            ApplyOptions(message[10]);
            setupReceived = true;
        }

        private void OnMatchMessage(byte[] message)
        {
            if (message.Length == 11 && message[0] == SetupMessage && message[1] == SetupMessageVersion && !isRoomOwner)
            {
                matchSeed = 0;
                for (int i = 0; i < 8; i++)
                    matchSeed |= (ulong)message[2 + i] << (8 * i);
                ApplyOptions(message[10]);
                setupReceived = true;
            }
        }

        private static void ApplyOptions(byte flags)
        {
            GameController.charactersBounceEachOther = (flags & 1) != 0;
            GameController.weirdBounceTrajectories = (flags & 2) != 0;
            GameController.onlyBounceBeforeRecover = (flags & 4) != 0;
        }

        private void StartMatch()
        {
            started = true;
            leaveAt = Time.realtimeSinceStartup + leaveAfterSeconds;
            GameController.PrepareOnlineMatch(new[] { 0, 1 });
            Debug.Log("[ONLINE] Match starting: seed " + matchSeed.ToString("X16") + ", local Slot " + LocalSlot);
            SceneManager.LoadScene(GameController.levelNames[0]);
        }

        /// <summary>
        /// The random seed of a Round, the same on both Peers without another message: from the
        /// Match seed, the Level number and whether it's the Showdown.
        /// </summary>
        public static ulong SeedForRound(int levelNo, bool showdown)
        {
            ulong seed = instance.matchSeed ^ ((ulong)(uint)levelNo * 0x9E3779B97F4A7C15UL) ^ (showdown ? 0xD1B54A32D192ED03UL : 0UL);
            // SplitMix64's finalizer, so neighbouring Levels get unrelated seeds.
            seed = (seed ^ (seed >> 30)) * 0xBF58476D1CE4E5B9UL;
            seed = (seed ^ (seed >> 27)) * 0x94D049BB133111EBUL;
            return seed ^ (seed >> 31);
        }

        /// <summary>
        /// A hash of everything both Peers must agree on before Tick 0 of this Round: the Level,
        /// the seed, the option toggles, the roster, teams, Round wins and colours. The handshake
        /// refuses to start if the two hashes differ.
        /// </summary>
        public static uint HashRoundSetup(ulong roundSeed)
        {
            var setup = new RoundRecording();
            GameController.CaptureRoundSetup(setup);

            uint hash = 2166136261;
            void Mix(uint value)
            {
                for (int i = 0; i < 4; i++)
                {
                    hash ^= (byte)(value >> (8 * i));
                    hash *= 16777619;
                }
            }

            foreach (char c in setup.Level)
                Mix(c);
            Mix((uint)roundSeed);
            Mix((uint)(roundSeed >> 32));
            Mix((setup.TeamMode ? 1u : 0) | (setup.PlayersCanDropIn ? 2u : 0) | (setup.ShowDown ? 4u : 0)
                | (setup.CharactersBounceEachOther ? 8u : 0) | (setup.WeirdBounceTrajectories ? 16u : 0)
                | (setup.OnlyBounceBeforeRecover ? 32u : 0));
            foreach (int slot in setup.Roster)
                Mix((uint)slot);
            for (int slot = 0; slot < RoundRecording.Slots; slot++)
            {
                Mix((uint)setup.Teams[slot]);
                Mix((uint)setup.RoundWins[slot]);
                Color32 colour = setup.Colors[slot];
                Mix(colour.r | ((uint)colour.g << 8) | ((uint)colour.b << 16) | ((uint)colour.a << 24));
            }
            return hash;
        }

        /// <summary>
        /// Ends the Online Match: tells the other Peer, closes the link, and returns to the title
        /// screen with <paramref name="message"/> shown for a few seconds. The other Peer shows
        /// <paramref name="messageForFriend"/>, or "Your friend left." when it's null.
        /// </summary>
        public static void End(string message, string messageForFriend = null)
        {
            if (instance == null)
                return;

            Debug.Log("[ONLINE] Match ended: " + message);
            var ending = instance;
            instance = null;
            SimulationDriver.EndOnlineSession();
            ending.link.Close(message, messageForFriend);
            Destroy(ending.gameObject);
            ShowMessage(message);

            if (SceneManager.GetActiveScene().name != "TitleScreen")
                SceneManager.LoadScene("TitleScreen");
        }

        private void OnGUI()
        {
            if (started)
                return;
            GUI.Label(new Rect(10, 10, 800, 24), "ONLINE: " + link.Description);
        }

        /// <summary>Shows a message about online play for a few seconds, on any screen.</summary>
        private static void ShowMessage(string message)
        {
            lastMessage = message;
            lastMessageUntil = Time.realtimeSinceStartup + MessageSeconds;
            var go = new GameObject("OnlineMessage");
            DontDestroyOnLoad(go);
            go.AddComponent<EndMessage>();
        }

        /// <summary>Shows the last online message, then removes itself.</summary>
        private sealed class EndMessage : MonoBehaviour
        {
            private void OnGUI()
            {
                if (Time.realtimeSinceStartup > lastMessageUntil)
                {
                    Destroy(gameObject);
                    return;
                }
                GUI.Label(new Rect(10, 10, 800, 24), "ONLINE: " + lastMessage);
            }
        }

        private static string ArgumentAfter(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        private static int ParseInt(string text)
        {
            return int.TryParse(text, out int value) ? value : 0;
        }
    }
}
