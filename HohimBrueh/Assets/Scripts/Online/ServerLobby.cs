using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Hootenanny.Protocol;
using LiteNetLib;
using LiteNetLib.Utils;
using Debug = UnityEngine.Debug;

namespace FrogSmashers.Online
{
    /// <summary>
    /// Our lobby: Room Codes through the Hootenanny rendezvous and relay server.
    ///
    /// 1. Connect to the server, then create a Room (and wait for the friend) or join one by code.
    /// 2. When both are in, ask the server for a NAT introduction, which tells each game the
    ///    other's public and LAN address, and connect directly.
    /// 3. The Room Owner picks the path: direct if its direct connection is up within 3 seconds,
    ///    otherwise the relay, and tells the friend. Deciding on one side means the two can't end
    ///    up on different paths when a connection completes right at the deadline.
    ///
    /// One LiteNetLib socket does everything. The introduction gives the friend the address the
    /// server saw, which is this socket's, so the direct connection must use the same socket.
    /// Both games stay connected to the server for the whole Match: on the relay path the server
    /// carries the game, and on the direct path leaving early would tell the friend "left" before
    /// its own direct connection had completed.
    /// </summary>
    public sealed class ServerLobby : ILobby, INetEventListener, INatPunchListener
    {
        // The rendezvous server (an AWS Elastic IP). -rendezvous host:port overrides it.
        private const string DefaultServerHost = "98.80.29.2";

        private const long DirectWindowMs = 3000;
        private const long FriendConfirmMs = 2000;
        private const long ConnectToFriendMs = 12000;
        private const long ReachServerMs = 8000;

        // The Room Owner's path decision and the friend's answer, over the relay.
        private const byte PathDirect = 1;
        private const byte PathRelay = 2;
        private const byte AnswerDirect = 3;
        private const byte AnswerRelay = 4;

        private enum Phase { Idle, ReachingServer, InRoom, Punching, OwnerAwaitsAnswer, FriendAwaitsPath, FriendConfirmingDirect, Done }

        private readonly NetManager net;
        private readonly string serverHost;
        private readonly int serverPort;
        private readonly bool forceRelay;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly List<IPEndPoint> introduced = new List<IPEndPoint>();

        private Phase phase = Phase.Idle;
        private bool creating;
        private string codeToJoin;
        private NetPeer server;
        private NetPeer direct;
        private string punchToken;
        private bool isRoomOwner;
        private long phaseStarted;
        private long punchStarted;
        private LiteNetLibLink link;

        public bool UsesRoomCodes => true;
        public string RoomCodeAlphabet => Hootenanny.Protocol.RoomCode.Alphabet;
        public int RoomCodeLength => Hootenanny.Protocol.RoomCode.Length;
        public LobbyStatus Status { get; private set; } = LobbyStatus.Idle;
        public string RoomCode { get; private set; }
        public string FailureReason { get; private set; }
        public OnlineMatchStart Ready { get; private set; }

        /// <summary>
        /// A lobby for our server. Development options: -rendezvous host:port picks another server
        /// (a local one, for example), and -online-relay skips the direct connection.
        /// </summary>
        public static ServerLobby FromCommandLine(string[] args, NetworkConditions conditions)
        {
            string host = DefaultServerHost;
            int port = Wire.DefaultPort;
            string rendezvous = CommandLine.After(args, "-rendezvous");
            if (rendezvous != null)
            {
                string[] parts = rendezvous.Split(':');
                host = parts[0];
                if (parts.Length > 1)
                    port = CommandLine.ParseInt(parts[1], port);
            }
            bool forceRelay = UnityEngine.Debug.isDebugBuild && CommandLine.Has(args, "-online-relay");
            return new ServerLobby(host, port, forceRelay, conditions);
        }

        private ServerLobby(string serverHost, int serverPort, bool forceRelay, NetworkConditions conditions)
        {
            UnityNetLogger.Install();
            this.serverHost = serverHost;
            this.serverPort = serverPort;
            this.forceRelay = forceRelay;
            net = new NetManager(this)
            {
                ChannelsCount = 2,
                DisconnectTimeout = LiteNetLibLink.LiteNetLibTimeoutMs,
                AutoRecycle = true,
                NatPunchEnabled = true,
                // IPv4 only. With IPv6 on, LiteNetLib also opens a dual-mode IPv6 socket, and
                // Unity 2019's IL2CPP on Windows throws while setting that socket's options.
                IPv6Enabled = false,
            };
            net.NatPunchModule.Init(this);
            conditions.ApplyTo(net);
        }

        // ---- ILobby -----------------------------------------------------------------------

        public void CreateRoom()
        {
            creating = true;
            Status = LobbyStatus.Creating;
            ReachServer();
        }

        public void JoinRoom(string roomCode)
        {
            creating = false;
            codeToJoin = Hootenanny.Protocol.RoomCode.Normalize(roomCode);
            Status = LobbyStatus.Joining;
            if (!Hootenanny.Protocol.RoomCode.IsValid(codeToJoin))
            {
                Fail("That isn't a Room Code.");
                return;
            }
            ReachServer();
        }

        public void Leave()
        {
            if (phase == Phase.Done && Status == LobbyStatus.Ready)
                return;
            if (server != null && server.ConnectionState == ConnectionState.Connected)
                server.Send(Messages.LeaveRoom(), DeliveryMethod.ReliableOrdered);
            net.Stop(sendDisconnectMessages: true);
            phase = Phase.Idle;
            Status = LobbyStatus.Idle;
        }

        public void Poll()
        {
            PollEvents();
            if (phase == Phase.Idle || phase == Phase.Done)
                return;

            long now = clock.ElapsedMilliseconds;
            switch (phase)
            {
                case Phase.ReachingServer:
                    if (now - phaseStarted > ReachServerMs)
                        Fail("Couldn't reach the server.");
                    break;

                case Phase.Punching:
                    // Only the Room Owner decides; the friend waits for its decision.
                    if (isRoomOwner)
                    {
                        if (direct != null)
                        {
                            SendLobby(PathDirect);
                            Enter(Phase.OwnerAwaitsAnswer);
                        }
                        else if (forceRelay || now - punchStarted > DirectWindowMs)
                        {
                            SendLobby(PathRelay);
                            Finish(relayed: true);
                        }
                    }
                    break;

                case Phase.FriendConfirmingDirect:
                    if (direct != null)
                    {
                        SendLobby(AnswerDirect);
                        Finish(relayed: false);
                    }
                    else if (now - phaseStarted > FriendConfirmMs)
                    {
                        SendLobby(AnswerRelay);
                        Finish(relayed: true);
                    }
                    break;
            }

            if (phase != Phase.Done && phase != Phase.Idle && punchStarted > 0 && now - punchStarted > ConnectToFriendMs)
                Fail("Couldn't connect to your friend.");
        }

        // ---- Steps ------------------------------------------------------------------------

        private void PollEvents()
        {
            net.PollEvents();
            net.NatPunchModule.PollEvents();
        }

        private void ReachServer()
        {
            UnityEngine.Application.runInBackground = true;
            if (!net.IsRunning)
                net.Start();
            var writer = new NetDataWriter();
            writer.Put(Wire.ConnectionData());
            server = net.Connect(serverHost, serverPort, writer);
            Enter(Phase.ReachingServer);
            Debug.Log("[ONLINE] lobby: reaching the server" + (forceRelay ? ", relay forced" : "") + ", build " + BuildInfo.Id);
        }

        private void OnServerConnected()
        {
            Enter(Phase.InRoom);
            server.Send(creating ? Messages.CreateRoom() : Messages.JoinRoom(codeToJoin), DeliveryMethod.ReliableOrdered);
        }

        private void OnFriendArrived(string token, bool owner)
        {
            punchToken = token;
            isRoomOwner = owner;
            Status = LobbyStatus.Connecting;
            punchStarted = clock.ElapsedMilliseconds;
            Debug.Log("[ONLINE] lobby: friend is here, " + (owner ? "Room Owner" : "friend"));
            if (!forceRelay)
                net.NatPunchModule.SendNatIntroduceRequest(new IPEndPoint(server.Address, server.Port), punchToken);
            Enter(isRoomOwner ? Phase.Punching : Phase.FriendAwaitsPath);
        }

        private void OnLobbyMessage(byte message)
        {
            if (isRoomOwner && phase == Phase.OwnerAwaitsAnswer)
            {
                if (message == AnswerDirect)
                    Finish(relayed: false);
                else if (message == AnswerRelay)
                    Finish(relayed: true);
            }
            else if (!isRoomOwner && phase == Phase.FriendAwaitsPath)
            {
                if (message == PathRelay)
                {
                    Finish(relayed: true);
                }
                else if (message == PathDirect)
                {
                    if (direct != null && !forceRelay)
                    {
                        SendLobby(AnswerDirect);
                        Finish(relayed: false);
                    }
                    else if (forceRelay)
                    {
                        SendLobby(AnswerRelay);
                        Finish(relayed: true);
                    }
                    else
                    {
                        Enter(Phase.FriendConfirmingDirect);
                    }
                }
            }
        }

        private void Finish(bool relayed)
        {
            if (relayed && direct != null)
            {
                net.DisconnectPeer(direct);
                direct = null;
            }
            link = relayed ? LiteNetLibLink.ThroughRelay(net, PollEvents, server) : LiteNetLibLink.Direct(net, PollEvents, direct);
            Ready = new OnlineMatchStart { Link = link, IsRoomOwner = isRoomOwner, Relayed = relayed };
            phase = Phase.Done;
            Status = LobbyStatus.Ready;
            Debug.Log("[ONLINE] lobby: ready, " + link.Description + " after " + (clock.ElapsedMilliseconds - punchStarted) + " ms");
        }

        private void Fail(string reason)
        {
            if (phase == Phase.Done && Status == LobbyStatus.Failed)
                return;
            Debug.Log("[ONLINE] lobby failed: " + reason);
            FailureReason = reason;
            Status = LobbyStatus.Failed;
            phase = Phase.Done;
            net.Stop(sendDisconnectMessages: true);
        }

        private void Enter(Phase next)
        {
            phase = next;
            phaseStarted = clock.ElapsedMilliseconds;
        }

        private void SendLobby(byte message)
        {
            LiteNetLibLink.SendRelayedReliable(server, LiteNetLibLink.RelayKindLobby, new[] { message });
        }

        private bool IsIntroduced(IPEndPoint endPoint)
        {
            foreach (var known in introduced)
            {
                if (known.Address.Equals(endPoint.Address) && known.Port == endPoint.Port)
                    return true;
            }
            return false;
        }

        private bool LookingForFriend => phase == Phase.Punching || phase == Phase.OwnerAwaitsAnswer
            || phase == Phase.FriendAwaitsPath || phase == Phase.FriendConfirmingDirect;

        private static string Explain(ErrorReason reason)
        {
            switch (reason)
            {
                case ErrorReason.UnsupportedVersion: return "This version of the game is too old for the server. Please update.";
                case ErrorReason.RoomNotFound: return "Room not found.";
                case ErrorReason.RoomFull: return "That Room is full.";
                case ErrorReason.RoomExpired: return "The Room expired.";
                case ErrorReason.TooManyRooms: return "Too many Rooms created. Wait a minute and try again.";
                case ErrorReason.ServerFull: return "The server is full. Try again later.";
                default: return "The server refused (" + reason + ").";
            }
        }

        // ---- Messages from the server -----------------------------------------------------

        private void OnServerMessage(NetPacketReader reader, DeliveryMethod method)
        {
            byte[] data = reader.RawData;
            int offset = reader.Position;
            int length = reader.AvailableBytes;
            if (length < 1)
                return;

            switch ((MessageType)data[offset])
            {
                case MessageType.RelayData:
                    OnRelayData(data, offset + Relay.HeaderSize, length - Relay.HeaderSize, method);
                    break;

                case MessageType.RoomCreated:
                    if (Messages.TryReadCode(data, offset, length, out string created))
                    {
                        RoomCode = created;
                        Status = LobbyStatus.WaitingForFriend;
                        Debug.Log("[ONLINE] lobby: Room Code " + created);
                    }
                    break;

                case MessageType.RoomJoined:
                    Debug.Log("[ONLINE] lobby: joined the Room");
                    break;

                case MessageType.PeerArrived:
                    if (Messages.TryReadPeerArrived(data, offset, length, out string token, out bool owner))
                        OnFriendArrived(token, owner);
                    break;

                case MessageType.PeerLeft:
                    // On the direct path the server isn't needed any more; the session's own
                    // timeouts decide whether the friend is gone.
                    if (link == null)
                        Fail("Your friend left.");
                    else if (link.Relayed)
                        link.OnLost("Your friend left.");
                    break;

                case MessageType.Error:
                    if (Messages.TryReadError(data, offset, length, out ErrorReason reason))
                    {
                        if (link == null)
                            Fail(Explain(reason));
                        else
                            Debug.LogWarning("[ONLINE] server error during the Match: " + reason);
                    }
                    break;
            }
        }

        private void OnRelayData(byte[] data, int offset, int length, DeliveryMethod method)
        {
            if (method != DeliveryMethod.ReliableOrdered)
            {
                if (link != null && link.Relayed)
                    link.OnRelayedDatagram(data, offset, length);
                return;
            }
            if (length < 1)
                return;

            byte kind = data[offset];
            if (kind == LiteNetLibLink.RelayKindLobby && length >= 2)
            {
                OnLobbyMessage(data[offset + 1]);
            }
            else if (kind == LiteNetLibLink.RelayKindMatch && link != null && link.Relayed)
            {
                var message = new byte[length - 1];
                System.Buffer.BlockCopy(data, offset + 1, message, 0, message.Length);
                link.OnRelayedMatchMessage(message);
            }
            else if (kind == LiteNetLibLink.RelayKindGoodbye && link != null && link.Relayed)
            {
                string reason = length > 1 ? System.Text.Encoding.UTF8.GetString(data, offset + 1, length - 1) : "";
                link.OnLost(reason.Length > 0 ? reason : "Your friend left.");
            }
        }

        // ---- LiteNetLib events ------------------------------------------------------------

        public void OnConnectionRequest(ConnectionRequest request)
        {
            // Only the friend the server introduced may connect. The key alone is no proof: it's
            // a constant in the game's code.
            if (LookingForFriend && direct == null && !forceRelay && IsIntroduced(request.RemoteEndPoint))
                request.AcceptIfKey(LiteNetLibLink.PeerKey);
            else
                request.Reject();
        }

        public void OnPeerConnected(NetPeer peer)
        {
            if (peer == server)
            {
                OnServerConnected();
                return;
            }
            // Introductions can succeed on both the LAN and the public address; the first
            // connection wins and any other is dropped.
            if (direct == null && LookingForFriend && IsIntroduced(peer))
            {
                direct = peer;
                Debug.Log("[ONLINE] lobby: direct connection to " + peer.Address + ":" + peer.Port);
            }
            else
            {
                net.DisconnectPeer(peer);
            }
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            if (peer == server)
            {
                if (link == null)
                {
                    if (info.AdditionalData != null && info.AdditionalData.AvailableBytes > 0)
                    {
                        byte[] data = info.AdditionalData.GetRemainingBytes();
                        if (Messages.TryReadError(data, 0, data.Length, out ErrorReason reason))
                        {
                            Fail(Explain(reason));
                            return;
                        }
                    }
                    Fail(phase == Phase.ReachingServer ? "Couldn't reach the server." : "Lost the connection to the server.");
                }
                else if (link.Relayed)
                {
                    link.OnLost("Lost the connection to the server.");
                }
                else
                {
                    Debug.Log("[ONLINE] lost the server; the Match continues on the direct path");
                }
                return;
            }

            if (link != null && link.IsDirectPeer(peer))
                link.OnLost(DirectLobby.LostReason(info));
            else if (peer == direct)
                direct = null;
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
        {
            if (peer == server)
                OnServerMessage(reader, method);
            else if (link != null && link.IsDirectPeer(peer))
                link.OnDirectReceive(reader, channel);
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            Debug.LogWarning("[ONLINE] socket error " + socketError);
        }

        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
        }

        public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
        }

        // ---- NAT introduction -------------------------------------------------------------

        public void OnNatIntroductionRequest(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, string token)
        {
        }

        public void OnNatIntroductionSuccess(IPEndPoint targetEndPoint, NatAddressType type, string token)
        {
            if (!LookingForFriend || token != punchToken)
                return;
            // A plain copy: LiteNetLib's own endpoint type doesn't compare by address.
            var endPoint = new IPEndPoint(targetEndPoint.Address, targetEndPoint.Port);
            introduced.Add(endPoint);
            Debug.Log("[ONLINE] lobby: introduced to " + endPoint + " (" + type + ")");
            if (direct == null)
                net.Connect(endPoint, LiteNetLibLink.PeerKey);
        }
    }
}
