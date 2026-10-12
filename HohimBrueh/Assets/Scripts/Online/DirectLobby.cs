using System;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using UnityEngine;

namespace FrogSmashers.Online
{
    /// <summary>
    /// Development only: two games connect straight to each other by IP address, with no
    /// server. One listens on a port, the other connects to it:
    ///   FrogSmashers.exe -online-host 9100                 (the Room Owner, Slot 0)
    ///   FrogSmashers.exe -online-join 192.168.1.20:9100    (the friend, Slot 1)
    /// Useful for testing on one PC or a LAN without the rendezvous server.
    /// </summary>
    public sealed class DirectLobby : ILobby, INetEventListener
    {
        private const int DefaultPort = 9100;

        private readonly NetManager net;
        private readonly bool listening;
        private NetPeer peer;
        private LiteNetLibLink link;

        public bool UsesRoomCodes => false;
        public string RoomCodeAlphabet => "";
        public int RoomCodeLength => 0;
        public LobbyStatus Status { get; private set; } = LobbyStatus.Connecting;
        public string RoomCode => null;
        public string FailureReason { get; private set; }
        public OnlineMatchStart Ready { get; private set; }

        /// <summary>A lobby from -online-host or -online-join, or null when neither is given.</summary>
        public static DirectLobby FromCommandLine(string[] args, NetworkConditions conditions)
        {
            string host = CommandLine.After(args, "-online-host");
            string join = CommandLine.After(args, "-online-join");
            if (host != null)
                return new DirectLobby(listening: true, null, CommandLine.ParseInt(host, DefaultPort), conditions);
            if (join != null)
            {
                string[] parts = join.Split(':');
                return new DirectLobby(listening: false, parts[0], parts.Length > 1 ? CommandLine.ParseInt(parts[1], DefaultPort) : DefaultPort, conditions);
            }
            return null;
        }

        private DirectLobby(bool listening, string host, int port, NetworkConditions conditions)
        {
            UnityNetLogger.Install();
            this.listening = listening;
            net = new NetManager(this)
            {
                ChannelsCount = 2,
                DisconnectTimeout = LiteNetLibLink.LiteNetLibTimeoutMs,
                AutoRecycle = true,
                // IPv4 only. With IPv6 on, LiteNetLib also opens a dual-mode IPv6 socket, and
                // Unity 2019's IL2CPP on Windows throws while setting that socket's options.
                IPv6Enabled = false,
            };
            conditions.ApplyTo(net);

            if (listening)
            {
                if (net.Start(port))
                    Debug.Log("[ONLINE] Room Owner, waiting for a friend on port " + port + ", " + conditions);
                else
                    Fail("Couldn't listen on UDP port " + port + ".");
            }
            else
            {
                net.Start();
                peer = net.Connect(host, port, LiteNetLibLink.PeerKey);
                Debug.Log("[ONLINE] friend, connecting to " + host + ":" + port + ", " + conditions);
            }
        }

        public void CreateRoom() { }

        public void JoinRoom(string roomCode) { }

        public void Leave()
        {
            if (Status != LobbyStatus.Ready)
                net.Stop(sendDisconnectMessages: true);
            Status = LobbyStatus.Idle;
        }

        public void Poll()
        {
            net.PollEvents();
        }

        private void Fail(string reason)
        {
            Debug.Log("[ONLINE] lobby failed: " + reason);
            FailureReason = reason;
            Status = LobbyStatus.Failed;
            net.Stop(sendDisconnectMessages: true);
        }

        // ---- LiteNetLib events ------------------------------------------------------------

        public void OnConnectionRequest(ConnectionRequest request)
        {
            // One friend only. A second caller, or one with the wrong key, is refused.
            if (listening && link == null && Status == LobbyStatus.Connecting)
                request.AcceptIfKey(LiteNetLibLink.PeerKey);
            else
                request.Reject();
        }

        public void OnPeerConnected(NetPeer connected)
        {
            if (link != null)
            {
                net.DisconnectPeer(connected);
                return;
            }
            peer = connected;
            link = LiteNetLibLink.Direct(net, Poll, connected);
            Debug.Log("[ONLINE] connected, " + link.Description);
            Ready = new OnlineMatchStart { Link = link, IsRoomOwner = listening, Relayed = false };
            Status = LobbyStatus.Ready;
        }

        public void OnPeerDisconnected(NetPeer disconnected, DisconnectInfo info)
        {
            if (link == null)
            {
                if (Status == LobbyStatus.Connecting)
                    Fail("Couldn't connect (" + info.Reason + ").");
                return;
            }
            if (link.IsDirectPeer(disconnected))
                link.OnLost(LostReason(info));
        }

        public void OnNetworkReceive(NetPeer from, NetPacketReader reader, byte channel, DeliveryMethod method)
        {
            if (link != null && link.IsDirectPeer(from))
                link.OnDirectReceive(reader, channel);
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            Debug.LogWarning("[ONLINE] socket error " + socketError);
        }

        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
        }

        public void OnNetworkLatencyUpdate(NetPeer from, int latency)
        {
        }

        /// <summary>Why a direct connection to the other Peer ended, for the player.</summary>
        internal static string LostReason(DisconnectInfo info)
        {
            if (info.Reason != DisconnectReason.RemoteConnectionClose)
                return "Connection lost (" + info.Reason + ").";
            if (info.AdditionalData != null && info.AdditionalData.AvailableBytes > 0)
                return System.Text.Encoding.UTF8.GetString(info.AdditionalData.GetRemainingBytes());
            return "Your friend left.";
        }
    }

    /// <summary>Small helpers for development command-line options.</summary>
    internal static class CommandLine
    {
        public static string After(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        public static bool Has(string[] args, string name) => Array.IndexOf(args, name) >= 0;

        public static int ParseInt(string text, int fallback = 0) =>
            int.TryParse(text, out int value) ? value : fallback;
    }
}
