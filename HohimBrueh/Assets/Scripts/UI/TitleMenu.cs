using UnityEngine;
using UnityEngine.UI;

namespace FrogSmashers
{
    /// <summary>
    /// The title screen's choice between local play (the join screen) and online play (the
    /// Online screen), shown once the title animation has finished.
    /// </summary>
    public sealed class TitleMenu : MonoBehaviour
    {
        private static readonly string[] Items = { "LOCAL PLAY", "ONLINE" };

        private readonly MenuInput input = new MenuInput();
        private Canvas canvas;
        private Text[] lines;
        private int selected;
        private bool ready;
        private int shownOnFrame;

        // The title scene's own "START" prompt. The menu replaces it, so it never shows.
        private Renderer[] startPrompt;

        /// <summary>A menu button was pressed this frame, shown or not. The title uses it to skip its animation.</summary>
        public bool AnyPress => input.Any;

        /// <summary>Adds the menu to the title scene, hidden until <see cref="Show"/>.</summary>
        public static TitleMenu Create()
        {
            Transform root = MenuUi.Canvas("TitleMenu", 90);
            var menu = root.gameObject.AddComponent<TitleMenu>();
            menu.canvas = root.GetComponent<Canvas>();
            menu.canvas.enabled = false;
            menu.lines = new Text[Items.Length];
            for (int i = 0; i < Items.Length; i++)
                menu.lines[i] = MenuUi.Line(root, 40, -170f - i * 50f);
            var start = GameObject.Find("Start");
            menu.startPrompt = start != null ? start.GetComponentsInChildren<Renderer>() : new Renderer[0];
            return menu;
        }

        /// <summary>The title animation has finished: show the menu from now on.</summary>
        public void Show()
        {
            ready = true;
        }

        // After the title's animations have run, which may switch the prompt back on.
        private void LateUpdate()
        {
            foreach (var r in startPrompt)
                if (r != null) r.enabled = false;
        }

        private void Update()
        {
            // Reads every frame, hidden or not, so a held button is never a new press.
            input.Read();
            bool visible = ready && !OnlineScreen.IsOpen;
            if (visible && !canvas.enabled)
                shownOnFrame = Time.frameCount;
            canvas.enabled = visible;
            // The press that finished the animation, or closed the Online screen, doesn't also choose.
            if (!visible || Time.frameCount == shownOnFrame)
                return;

            if (input.Up || input.Down)
                selected = 1 - selected;
            if (input.Confirm)
            {
                if (selected == 1)
                    OnlineScreen.Open();
                else
                    UnityEngine.SceneManagement.SceneManager.LoadScene("JoinScreen");
                return;
            }

            for (int i = 0; i < lines.Length; i++)
            {
                lines[i].text = Items[i];
                lines[i].color = i == selected ? MenuUi.Pulse() : new Color(1f, 1f, 1f, 0.7f);
            }
        }
    }
}
