using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Hindsight.Core;
using LiteNetLib;
using UnityEngine;

namespace FrogSmashers.Online
{
    /// <summary>
    /// The connection to the other Peer, over LiteNetLib. Two channels share it:
    /// - gameplay datagrams for Hindsight's session (<see cref="IPeerTransport"/>), unreliable,
    ///   because the session repeats every unacknowledged input itself and a resend would only
    ///   add latency;
    /// - match messages between the two games (the Match setup, leaving), reliable and in order.
    ///
    /// Nothing outside this class knows about addresses or LiteNetLib: the rest of the game sees
    /// <see cref="IOnlineLink"/>.
    ///
    /// Single-threaded: call <see cref="Poll"/> on the main thread, every frame and before the
    /// session polls.
    /// </summary>
    public sealed class LiteNetLibLink : IOnlineLink, INetEventListener, IDisposable
    {
        private const string ConnectionKey = "frog-smashers-online";
        private const byte GameplayChannel = 0;
        private const byte MatchChannel = 1;

        // Hindsight decides when a silent Peer is gone (Interrupted after 750 ms, Disconnected
        // after 5 s). LiteNetLib's own timeout is longer, so it never closes the connection first.
        private const int LiteNetLibTimeoutMs = 10000;

        // Received gameplay datagrams wait here until the session reads them. Buffers are reused,
        // so receiving allocates nothing once the pool has grown.
        private const int MaxDatagram = 1500;
        private readonly Queue<byte[]> received = new Queue<byte[]>();
        private readonly Queue<int> receivedLengths = new Queue<int>();
        private readonly Stack<byte[]> pool = new Stack<byte[]>();

        private readonly Queue<byte[]> matchMessages = new Queue<byte[]>();
        private readonly NetManager net;
        private readonly bool accepting;
        private NetPeer peer;

        // The other Peer, as Hindsight names it. There's only ever one.
        private static readonly PeerId RemotePeer = new PeerId(1);

        public PeerId Remote => RemotePeer;

        public LinkState State { get; private set; } = LinkState.Connecting;
        public string Description { get; private set; }
        public string CloseReason { get; private set; }
        public int RoundTripMs => peer != null ? peer.RoundTripTime : 0;

        // LiteNetLib's own warnings and errors, into Player.log with the rest.
        private sealed class UnityNetLogger : INetLogger
        {
            public void WriteNet(NetLogLevel level, string str, params object[] args)
            {
                string line = "[LITENETLIB] " + (args != null && args.Length > 0 ? string.Format(str, args) : str);
                if (level == NetLogLevel.Error) Debug.LogWarning(line);
                else Debug.Log(line);
            }
        }

        private LiteNetLibLink(bool accepting, NetworkConditions conditions)
        {
            if (NetDebug.Logger == null)
                NetDebug.Logger = new UnityNetLogger();
            this.accepting = accepting;
            net = new NetManager(this)
            {
                ChannelsCount = 2,
                DisconnectTimeout = LiteNetLibTimeoutMs,
                AutoRecycle = true,
                // IPv4 only. With IPv6 on, LiteNetLib also opens a dual-mode IPv6 socket, and
                // Unity 2019's IL2CPP on Windows throws while setting that socket's options.
                IPv6Enabled = false,
            };
            conditions.ApplyTo(net);
        }

        /// <summary>Wait for the other Peer to connect on <paramref name="port"/>.</summary>
        public static LiteNetLibLink Listen(int port, NetworkConditions conditions)
        {
            var link = new LiteNetLibLink(accepting: true, conditions);
            if (!link.net.Start(port))
                link.Close("Couldn't listen on UDP port " + port + ".");
            else
                link.Description = "waiting for a friend on port " + port;
            return link;
        }

        /// <summary>Connect to a Peer that is listening at <paramref name="host"/>:<paramref name="port"/>.</summary>
        public static LiteNetLibLink Connect(string host, int port, NetworkConditions conditions)
        {
            var link = new LiteNetLibLink(accepting: false, conditions);
            link.net.Start();
            link.peer = link.net.Connect(host, port, ConnectionKey);
            link.Description = "connecting to " + host + ":" + port;
            return link;
        }

        /// <summary>Handle everything that arrived. Call every frame, before the session polls.</summary>
        public void Poll()
        {
            if (State != LinkState.Closed)
                net.PollEvents();
        }

        // ---- Gameplay datagrams (IPeerTransport) -----------------------------------------

        public void Send(PeerId to, byte[] data, int offset, int length)
        {
            if (State == LinkState.Connected && to == RemotePeer)
                peer.Send(data, offset, length, GameplayChannel, DeliveryMethod.Unreliable);
        }

        public bool TryReceive(byte[] buffer, out PeerId from, out int length)
        {
            from = RemotePeer;
            length = 0;
            while (received.Count > 0)
            {
                byte[] datagram = received.Dequeue();
                int size = receivedLengths.Dequeue();
                pool.Push(datagram);
                if (size > buffer.Length)
                    continue;
                Buffer.BlockCopy(datagram, 0, buffer, 0, size);
                length = size;
                return true;
            }
            return false;
        }

        // ---- Match messages ---------------------------------------------------------------

        /// <summary>Send a match message: reliable and in order. Rare, so it may allocate.</summary>
        public void SendMatchMessage(byte[] message)
        {
            if (State == LinkState.Connected)
                peer.Send(message, MatchChannel, DeliveryMethod.ReliableOrdered);
        }

        public bool TryReceiveMatchMessage(out byte[] message)
        {
            if (matchMessages.Count == 0)
            {
                message = null;
                return false;
            }
            message = matchMessages.Dequeue();
            return true;
        }

        // ---- Lifetime ---------------------------------------------------------------------

        public void Close(string reason, string reasonForFriend = null)
        {
            if (State == LinkState.Closed)
                return;
            State = LinkState.Closed;
            CloseReason = reason;
            Debug.Log("[ONLINE] link closed: " + reason);
            // The reason rides in the disconnect packet itself. Sent as a separate message, it
            // could lose the race with the disconnect, and the friend would only hear "left".
            if (reasonForFriend != null && peer != null && peer.ConnectionState == ConnectionState.Connected)
                net.DisconnectPeer(peer, System.Text.Encoding.UTF8.GetBytes(reasonForFriend));
            net.Stop(sendDisconnectMessages: true);
        }

        public void Dispose()
        {
            Close("closed");
        }

        // ---- LiteNetLib events ------------------------------------------------------------

        public void OnConnectionRequest(ConnectionRequest request)
        {
            // One friend only. A second caller, or one with the wrong key, is refused.
            if (accepting && peer == null && State == LinkState.Connecting)
                request.AcceptIfKey(ConnectionKey);
            else
                request.Reject();
        }

        public void OnPeerConnected(NetPeer connected)
        {
            peer = connected;
            State = LinkState.Connected;
            Description = "connected to " + connected.Address + ":" + connected.Port;
            Debug.Log("[ONLINE] " + Description);
        }

        public void OnPeerDisconnected(NetPeer disconnected, DisconnectInfo info)
        {
            if (disconnected != peer)
                return;
            if (info.Reason != DisconnectReason.RemoteConnectionClose)
                Close("Connection lost (" + info.Reason + ").");
            else if (info.AdditionalData != null && info.AdditionalData.AvailableBytes > 0)
                Close(System.Text.Encoding.UTF8.GetString(info.AdditionalData.GetRemainingBytes()));
            else
                Close("Your friend left.");
        }

        public void OnNetworkReceive(NetPeer from, NetPacketReader reader, byte channel, DeliveryMethod method)
        {
            int length = reader.AvailableBytes;
            if (channel == MatchChannel)
            {
                matchMessages.Enqueue(reader.GetRemainingBytes());
                return;
            }
            if (length > MaxDatagram)
                return;

            byte[] datagram = pool.Count > 0 ? pool.Pop() : new byte[MaxDatagram];
            Buffer.BlockCopy(reader.RawData, reader.Position, datagram, 0, length);
            received.Enqueue(datagram);
            receivedLengths.Enqueue(length);
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
    }

    /// <summary>
    /// Simulated bad network, for testing on a LAN: LiteNetLib delays and drops packets on
    /// purpose. Off unless set from the command line.
    /// </summary>
    public struct NetworkConditions
    {
        public int LatencyMs;
        public int LossPercent;

        public void ApplyTo(NetManager net)
        {
            if (LatencyMs > 0)
            {
                net.SimulateLatency = true;
                // LiteNetLib applies latency to sent packets; each side delays its own sends.
                net.SimulationMinLatency = LatencyMs;
                net.SimulationMaxLatency = LatencyMs + LatencyMs / 5;
            }
            if (LossPercent > 0)
            {
                net.SimulatePacketLoss = true;
                net.SimulationPacketLossChance = LossPercent;
            }
        }

        public override string ToString() => LatencyMs > 0 || LossPercent > 0
            ? "simulated " + LatencyMs + " ms, " + LossPercent + "% loss"
            : "no simulated latency";
    }
}
