namespace FrogSmashers.Online
{
    public enum LobbyStatus { Idle, Creating, WaitingForFriend, Joining, Connecting, Ready, Failed }

    /// <summary>
    /// How two games find each other before an Online Match: create a Room and wait, or join one.
    ///
    /// The seam that keeps the rest of the game independent of how players meet. Ours talks to
    /// our rendezvous server and uses Room Codes (<see cref="ServerLobby"/>); a Steam build would
    /// use Steam's lobbies and invites instead, and the online screens would hide code entry.
    /// Nothing outside an implementation knows a server exists or what a Room Code looks like.
    /// </summary>
    public interface ILobby
    {
        /// <summary>True when friends join by typing a code; false for invite-based lobbies.</summary>
        bool UsesRoomCodes { get; }

        /// <summary>The characters a Room Code can contain, for the code picker.</summary>
        string RoomCodeAlphabet { get; }

        int RoomCodeLength { get; }

        void CreateRoom();

        /// <summary>Only when <see cref="UsesRoomCodes"/>.</summary>
        void JoinRoom(string roomCode);

        /// <summary>Give up before the Match starts. After <see cref="LobbyStatus.Ready"/>, the link owns the connection.</summary>
        void Leave();

        /// <summary>Call once per Render Frame until the status is Ready or Failed.</summary>
        void Poll();

        LobbyStatus Status { get; }

        /// <summary>Set while waiting for the friend. Shown as text, never parsed.</summary>
        string RoomCode { get; }

        /// <summary>Why the status is Failed, for the player.</summary>
        string FailureReason { get; }

        /// <summary>Set when the status is Ready.</summary>
        OnlineMatchStart Ready { get; }
    }

    /// <summary>What a lobby hands over when both players are connected.</summary>
    public sealed class OnlineMatchStart
    {
        public IOnlineLink Link;

        /// <summary>The player who created the Room. Plays Slot 0 and sends the Match setup.</summary>
        public bool IsRoomOwner;

        /// <summary>True when the two games talk through a relay server, not directly. For the logs and testing.</summary>
        public bool Relayed;
    }
}
