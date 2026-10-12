using System;
using UnityEngine;

namespace FrogSmashers.Online
{
    /// <summary>
    /// Starts online play from the command line, for development and automatic tests, and runs
    /// a lobby until it hands over a Match. The online screens use the same lobbies.
    ///
    /// Connect by IP address, no server (see <see cref="DirectLobby"/>):
    ///   -online-host PORT | -online-join HOST:PORT
    /// Through the rendezvous server (see <see cref="ServerLobby"/>):
    ///   -online-create | -online-code CODE      [-online-relay] [-rendezvous HOST:PORT]
    /// Either way, optionally:
    ///   -online-latency MS -online-loss PERCENT  a simulated bad network
    ///   -online-bot -online-leave-after SECONDS   an automatic test nobody plays
    /// </summary>
    public sealed class OnlineLauncher : MonoBehaviour
    {
        private ILobby lobby;

        /// <summary>The simulated network conditions from the command line; none by default.</summary>
        public static NetworkConditions Conditions()
        {
            string[] args = Environment.GetCommandLineArgs();
            return new NetworkConditions
            {
                LatencyMs = CommandLine.ParseInt(CommandLine.After(args, "-online-latency")),
                LossPercent = CommandLine.ParseInt(CommandLine.After(args, "-online-loss")),
            };
        }

        /// <summary>The lobby the online screens use.</summary>
        public static ILobby CreateLobby() => ServerLobby.FromCommandLine(Environment.GetCommandLineArgs(), Conditions());

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void StartFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            ILobby lobby = DirectLobby.FromCommandLine(args, Conditions());
            if (lobby == null)
            {
                string code = CommandLine.After(args, "-online-code");
                if (CommandLine.Has(args, "-online-create"))
                {
                    lobby = CreateLobby();
                    lobby.CreateRoom();
                }
                else if (code != null)
                {
                    lobby = CreateLobby();
                    lobby.JoinRoom(code);
                }
            }
            if (lobby != null)
                Run(lobby);
        }

        /// <summary>Polls <paramref name="lobby"/> every frame and starts the Match when it's ready.</summary>
        public static void Run(ILobby lobby)
        {
            Application.runInBackground = true;
            var go = new GameObject("OnlineLauncher");
            DontDestroyOnLoad(go);
            go.AddComponent<OnlineLauncher>().lobby = lobby;
        }

        private void Update()
        {
            lobby.Poll();
            if (lobby.Status == LobbyStatus.Ready)
            {
                OnlineMatch.Begin(lobby.Ready);
                Destroy(gameObject);
            }
            else if (lobby.Status == LobbyStatus.Failed)
            {
                OnlineMatch.ShowMessage(lobby.FailureReason);
                Destroy(gameObject);
            }
        }

        private void OnGUI()
        {
            string status = lobby.Status == LobbyStatus.WaitingForFriend ? "Room " + lobby.RoomCode + ", waiting for a friend" : lobby.Status.ToString();
            GUI.Label(new Rect(10, 10, 800, 24), "ONLINE: " + status);
        }
    }
}
