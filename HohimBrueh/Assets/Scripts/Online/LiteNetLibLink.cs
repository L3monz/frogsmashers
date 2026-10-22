using System;
using System.Collections.Generic;
using Hindsight.Core;
using Hootenanny.Protocol;
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
    /// Both travel either directly to the other Peer or through the relay server, and nothing
    /// above this class can tell which. The lobby that made the connection owns the socket and
    /// its events, and hands what arrives to this link.
    ///
    /// Single-threaded: call <see cref="Poll"/> on the main thread, every frame and before the
    /// session polls.
    /// </summary>
    public sealed class LiteNetLibLink : IOnlineLink
    {
        /// <summary>The key one game uses to connect to the other, directly.</summary>
        internal const string PeerKey = "frog-smashers-online";

        // Hindsight decides when a silent Peer is gone (Interrupted after 750 ms, Disconnected
        // after 5 s). LiteNetLib's own timeout is longer, so it never closes the connection first.
        internal const int LiteNetLibTimeoutMs = 10000;

        private const byte GameplayChannel = 0;
        private const byte MatchChannel = 1;

        // On the relay path, reliable data starts with one of these after the relay header, to
        // tell match messages from the lobby's own and from a goodbye.
        internal const byte RelayKindMatch = 1;
        internal const byte RelayKindLobby = 2;
        internal const byte RelayKindGoodbye = 3;

        // Long enough for the last reliable messages (the relay goodbye, the Match summary to the
        // server) to leave the socket before it closes.
        private const int CloseDelayMs = 300;

        // Received gameplay datagrams wait here until the session reads them. Buffers are reused,
        // so receiving allocates nothing once the pool has grown.
        private const int MaxDatagram = 1500;
        private readonly Queue<byte[]> received = new Queue<byte[]>();
        private readonly Queue<int> receivedLengths = new Queue<int>();
        private readonly Stack<byte[]> pool = new Stack<byte[]>();
        private readonly byte[] relayBuffer = new byte[MaxDatagram + Relay.HeaderSize];

        private readonly Queue<byte[]> matchMessages = new Queue<byte[]>();
        private readonly NetManager net;
        private readonly Action pollEvents;
        private readonly NetPeer direct;
        private readonly NetPeer server;

        // The other Peer, as Hindsight names it. There's only ever one.
        private static readonly PeerId RemotePeer = new PeerId(1);

        public PeerId Remote => RemotePeer;

        public LinkState State { get; private set; } = LinkState.Connected;
        public string Description { get; }
        public string CloseReason { get; private set; }
        public bool Relayed => direct == null;

        /// <summary>A link straight to the other Peer.</summary>
        internal static LiteNetLibLink Direct(NetManager net, Action pollEvents, NetPeer peer) =>
            new LiteNetLibLink(net, pollEvents, peer, null, "direct to " + peer.Address + ":" + peer.Port);

        /// <summary>A link through the relay server, which forwards to the other member of the Room.</summary>
        internal static LiteNetLibLink ThroughRelay(NetManager net, Action pollEvents, NetPeer server) =>
            new LiteNetLibLink(net, pollEvents, null, server, "relayed through " + server.Address + ":" + server.Port);

        private LiteNetLibLink(NetManager net, Action pollEvents, NetPeer direct, NetPeer server, string description)
        {
            this.net = net;
            this.pollEvents = pollEvents;
            this.direct = direct;
            this.server = server;
            Description = description;
            Relay.WriteHeader(relayBuffer);
        }

        /// <summary>Handle everything that arrived. Call every frame, before the session polls.</summary>
        public void Poll()
        {
            if (State != LinkState.Closed)
                pollEvents();
        }

        // ---- Gameplay datagrams (IPeerTransport) -----------------------------------------

        public void Send(PeerId to, byte[] data, int offset, int length)
        {
            if (State != LinkState.Connected || to != RemotePeer)
                return;
            if (direct != null)
            {
                direct.Send(data, offset, length, GameplayChannel, DeliveryMethod.Unreliable);
            }
            else if (length <= MaxDatagram)
            {
                Buffer.BlockCopy(data, offset, relayBuffer, Relay.HeaderSize, length);
                server.Send(relayBuffer, 0, Relay.HeaderSize + length, DeliveryMethod.Unreliable);
            }
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
            if (State != LinkState.Connected)
                return;
            if (direct != null)
                direct.Send(message, MatchChannel, DeliveryMethod.ReliableOrdered);
            else
                SendRelayedReliable(server, RelayKindMatch, message);
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

            if (direct != null)
            {
                // The reason rides in the disconnect packet itself. Sent as a separate message, it
                // could lose the race with the disconnect, and the friend would only hear "left".
                if (direct.ConnectionState == ConnectionState.Connected)
                {
                    if (reasonForFriend != null)
                        net.DisconnectPeer(direct, System.Text.Encoding.UTF8.GetBytes(reasonForFriend));
                    else
                        net.DisconnectPeer(direct);
                }
            }
            else
            {
                // Through the relay there is no disconnect packet between the two games, so say
                // goodbye as a reliable message. Closing also leaves the Room, which tells the
                // friend in any case.
                SendRelayedReliable(server, RelayKindGoodbye,
                    System.Text.Encoding.UTF8.GetBytes(reasonForFriend ?? ""));
            }

            // The socket is shared with the server connection, which may still have reliable
            // messages to send, so give them a moment before it closes.
            var closing = net;
            System.Threading.Tasks.Task.Delay(CloseDelayMs).ContinueWith(_ => closing.Stop(sendDisconnectMessages: true));
        }

        // ---- From the lobby that owns the socket -----------------------------------------

        internal bool IsDirectPeer(NetPeer peer) => direct != null && peer == direct;

        /// <summary>Data from the direct peer: a gameplay datagram or, on the match channel, a match message.</summary>
        internal void OnDirectReceive(NetPacketReader reader, byte channel)
        {
            if (channel == MatchChannel)
                matchMessages.Enqueue(reader.GetRemainingBytes());
            else
                Enqueue(reader.RawData, reader.Position, reader.AvailableBytes);
        }

        /// <summary>A relayed gameplay datagram: the payload after the relay header.</summary>
        internal void OnRelayedDatagram(byte[] data, int offset, int length)
        {
            Enqueue(data, offset, length);
        }

        internal void OnRelayedMatchMessage(byte[] message)
        {
            matchMessages.Enqueue(message);
        }

        /// <summary>The other Peer is gone, or the path to it is: close, with a reason for the player.</summary>
        internal void OnLost(string reason)
        {
            Close(reason);
        }

        /// <summary>Reliable data for the other Peer through the relay: relay header, kind, payload.</summary>
        internal static void SendRelayedReliable(NetPeer server, byte kind, byte[] payload)
        {
            var packet = new byte[Relay.HeaderSize + 1 + payload.Length];
            Relay.WriteHeader(packet);
            packet[Relay.HeaderSize] = kind;
            Buffer.BlockCopy(payload, 0, packet, Relay.HeaderSize + 1, payload.Length);
            server.Send(packet, DeliveryMethod.ReliableOrdered);
        }

        private void Enqueue(byte[] data, int offset, int length)
        {
            if (State == LinkState.Closed || length > MaxDatagram)
                return;
            byte[] datagram = pool.Count > 0 ? pool.Pop() : new byte[MaxDatagram];
            Buffer.BlockCopy(data, offset, datagram, 0, length);
            received.Enqueue(datagram);
            receivedLengths.Enqueue(length);
        }
    }

    /// <summary>
    /// Simulated bad network, for testing: LiteNetLib delays and drops packets on purpose. Off
    /// unless set from the command line.
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

    /// <summary>LiteNetLib's own warnings and errors, into Player.log with the rest.</summary>
    internal sealed class UnityNetLogger : INetLogger
    {
        public static void Install()
        {
            if (NetDebug.Logger == null)
                NetDebug.Logger = new UnityNetLogger();
        }

        public void WriteNet(NetLogLevel level, string str, params object[] args)
        {
            string line = "[LITENETLIB] " + (args != null && args.Length > 0 ? string.Format(str, args) : str);
            if (level == NetLogLevel.Error) Debug.LogWarning(line);
            else Debug.Log(line);
        }
    }
}
