using FrogSmashers.Online;
using UnityEngine;
using UnityEngine.UI;

namespace FrogSmashers
{
    /// <summary>
    /// The Online screen, over the title screen: create a Room and wait for a friend, or join one
    /// by its code. It talks only to <see cref="ILobby"/>, so it doesn't know there's a server or
    /// what a Room Code means; the lobby says which characters a code can contain.
    ///
    /// Works the same on a gamepad and a keyboard: up and down choose or change a letter, left
    /// and right move between letters, confirm (A, M, T, Enter or Space) accepts, back (B, Comma,
    /// Y or Escape) goes back.
    /// </summary>
    public sealed class OnlineScreen : MonoBehaviour
    {
        private enum Page { Menu, Hosting, Picking, Joining, Failed }

        private static readonly string[] MenuItems = { "CREATE ROOM", "JOIN ROOM", "BACK" };

        private static OnlineScreen open;

        private readonly MenuInput input = new MenuInput();
        private Text title, big, line1, line2, hint;
        private Text[] menuLines;
        private Text[] letters;
        private Page page;
        private int selected;
        private ILobby lobby;

        // The code being picked, as indexes into the lobby's alphabet.
        private int[] code;
        private int cursor;
        private string alphabet;

        public static bool IsOpen => open != null;

        public static void Open()
        {
            if (open != null)
                return;
            Transform root = MenuUi.Canvas("OnlineScreen", 200);
            MenuUi.Background(root);
            open = root.gameObject.AddComponent<OnlineScreen>();
            open.Build(root);
        }

        private void Build(Transform root)
        {
            title = MenuUi.Line(root, 64, 230f);
            title.text = "ONLINE";
            big = MenuUi.Line(root, 120, 60f);
            line1 = MenuUi.Line(root, 28, -60f, plainFont: true);
            line2 = MenuUi.Line(root, 22, -110f, plainFont: true);
            hint = MenuUi.Line(root, 18, -300f, plainFont: true);
            hint.color = new Color(1f, 1f, 1f, 0.75f);
            menuLines = new Text[MenuItems.Length];
            for (int i = 0; i < MenuItems.Length; i++)
                menuLines[i] = MenuUi.Line(root, 44, 40f - i * 60f);
            ShowPage(Page.Menu);
        }

        private void ShowPage(Page next)
        {
            page = next;
            big.text = line1.text = line2.text = "";
            foreach (var line in menuLines)
                line.gameObject.SetActive(next == Page.Menu);
            if (letters != null)
            {
                foreach (var letter in letters)
                    Destroy(letter.gameObject);
                letters = null;
            }
            switch (next)
            {
                case Page.Menu:
                    hint.text = "Up/Down: choose    Confirm: select    Back: return";
                    break;
                case Page.Hosting:
                case Page.Joining:
                    hint.text = "Back: cancel";
                    break;
                case Page.Picking:
                    hint.text = "Up/Down: change letter    Left/Right: move    Confirm: join    Back: return";
                    letters = new Text[code.Length];
                    for (int i = 0; i < letters.Length; i++)
                        letters[i] = MenuUi.Line(transform, 120, 60f, x: (i - (letters.Length - 1) / 2f) * 110f);
                    break;
                case Page.Failed:
                    hint.text = "Confirm: OK";
                    break;
            }
        }

        private void Update()
        {
            input.Read();
            if (lobby != null)
                lobby.Poll();

            switch (page)
            {
                case Page.Menu: UpdateMenu(); break;
                case Page.Hosting:
                case Page.Joining: UpdateConnecting(); break;
                case Page.Picking: UpdatePicking(); break;
                case Page.Failed:
                    if (input.Confirm || input.Back)
                        ShowPage(Page.Menu);
                    break;
            }
        }

        private void UpdateMenu()
        {
            if (input.Up) selected = (selected + MenuItems.Length - 1) % MenuItems.Length;
            if (input.Down) selected = (selected + 1) % MenuItems.Length;
            for (int i = 0; i < menuLines.Length; i++)
            {
                menuLines[i].text = MenuItems[i];
                menuLines[i].color = i == selected ? MenuUi.Pulse() : new Color(1f, 1f, 1f, 0.7f);
            }

            if (input.Back || (input.Confirm && selected == 2))
            {
                Close();
            }
            else if (input.Confirm && selected == 0)
            {
                lobby = OnlineLauncher.CreateLobby();
                lobby.CreateRoom();
                ShowPage(Page.Hosting);
            }
            else if (input.Confirm && selected == 1)
            {
                lobby = OnlineLauncher.CreateLobby();
                alphabet = lobby.RoomCodeAlphabet;
                if (!lobby.UsesRoomCodes || alphabet.Length == 0)
                {
                    // An invite-based lobby has nothing to type; it joins through its own overlay.
                    lobby.JoinRoom(null);
                    ShowPage(Page.Joining);
                    return;
                }
                code = new int[lobby.RoomCodeLength];
                cursor = 0;
                ShowPage(Page.Picking);
            }
        }

        private void UpdatePicking()
        {
            if (input.Back)
            {
                lobby = null;
                ShowPage(Page.Menu);
                return;
            }
            if (input.Up) code[cursor] = (code[cursor] + 1) % alphabet.Length;
            if (input.Down) code[cursor] = (code[cursor] + alphabet.Length - 1) % alphabet.Length;
            if (input.Left) cursor = (cursor + code.Length - 1) % code.Length;
            if (input.Right) cursor = (cursor + 1) % code.Length;

            if (input.Confirm)
            {
                var text = new System.Text.StringBuilder();
                foreach (int index in code)
                    text.Append(alphabet[index]);
                lobby.JoinRoom(text.ToString());
                ShowPage(Page.Joining);
                return;
            }

            // The letter under the cursor pulses, like the join screen's prompts.
            for (int i = 0; i < letters.Length; i++)
            {
                letters[i].text = alphabet[code[i]].ToString();
                letters[i].color = i == cursor ? MenuUi.Pulse() : Color.white;
            }
            line1.text = "Enter your friend's Room Code";
        }

        private void UpdateConnecting()
        {
            if (input.Back)
            {
                lobby.Leave();
                lobby = null;
                ShowPage(Page.Menu);
                return;
            }

            switch (lobby.Status)
            {
                case LobbyStatus.Creating:
                case LobbyStatus.Joining:
                    line1.text = "Contacting the server...";
                    break;
                case LobbyStatus.WaitingForFriend:
                    big.text = lobby.RoomCode;
                    line1.text = "Waiting for your friend...";
                    line2.text = "Give them this Room Code";
                    break;
                case LobbyStatus.Connecting:
                    line1.text = "Connecting to your friend...";
                    break;
                case LobbyStatus.Failed:
                    string reason = lobby.FailureReason;
                    lobby = null;
                    ShowPage(Page.Failed);
                    line1.text = reason;
                    line1.color = Color.white;
                    return;
                case LobbyStatus.Ready:
                    var start = lobby.Ready;
                    lobby = null;
                    Close();
                    OnlineMatch.Begin(start);
                    return;
            }
            line1.color = MenuUi.Pulse();
        }

        private void Close()
        {
            if (lobby != null)
                lobby.Leave();
            lobby = null;
            open = null;
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            // Leaving the title scene with a Room open (it shouldn't happen) still leaves the Room.
            if (lobby != null)
                lobby.Leave();
            if (open == this)
                open = null;
        }
    }
}
