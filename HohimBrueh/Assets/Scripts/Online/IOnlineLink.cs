using Hindsight.Core;

namespace FrogSmashers.Online
{
    public enum LinkState { Connecting, Connected, Closed }

    /// <summary>
    /// The connection to the other Peer, as the rest of the game sees it: Hindsight's datagram
    /// channel (<see cref="IPeerTransport"/>, unreliable) plus a reliable channel for match
    /// messages between the two games.
    ///
    /// The seam that keeps online play independent of how Peers connect. Ours runs over
    /// LiteNetLib (<see cref="LiteNetLibLink"/>), directly or through our relay server; a Steam
    /// build would implement this over Steam's networking, and nothing else would change.
    /// </summary>
    public interface IOnlineLink : IPeerTransport
    {
        /// <summary>The other Peer, as the session names it.</summary>
        PeerId Remote { get; }

        LinkState State { get; }

        /// <summary>For the status line, such as "waiting for a friend". Never parsed.</summary>
        string Description { get; }

        /// <summary>Why the link closed, for the player.</summary>
        string CloseReason { get; }

        /// <summary>Handle everything that arrived. Call every frame, before the session polls.</summary>
        void Poll();

        /// <summary>Send a match message, reliably and in order.</summary>
        void SendMatchMessage(byte[] message);

        bool TryReceiveMatchMessage(out byte[] message);

        /// <summary>
        /// Tell the other Peer this one is leaving, and close. <paramref name="reasonForFriend"/>
        /// travels with the goodbye and becomes the other Peer's <see cref="CloseReason"/>; when
        /// it's null, the other Peer just hears that its friend left.
        /// </summary>
        void Close(string reason, string reasonForFriend = null);
    }
}
