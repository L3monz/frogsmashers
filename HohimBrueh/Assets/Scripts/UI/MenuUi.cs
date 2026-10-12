using FreeLives;
using UnityEngine;
using UnityEngine.UI;

namespace FrogSmashers
{
    /// <summary>
    /// Menu input from any device: the current gamepad and both keyboard layouts together, so
    /// whoever reaches for a controller can use it. Escape counts as back. Edges only: a held
    /// button acts once.
    /// </summary>
    public sealed class MenuInput
    {
        private readonly InputState gamepad = new InputState();
        private readonly InputState keyboard1 = new InputState();
        private readonly InputState keyboard2 = new InputState();

        public bool Up, Down, Left, Right, Confirm, Back;

        /// <summary>
        /// While the player types letters (a Room Code), letter keys are text, not buttons: the
        /// WASD layout and the M confirm key are ignored. Arrows, Enter, Space, Comma, Escape and
        /// the gamepad still work.
        /// </summary>
        public bool Typing;

        /// <summary>Any menu button was pressed this frame.</summary>
        public bool Any => Up || Down || Left || Right || Confirm || Back;

        // The first read only learns what is already held, so a button held while a menu
        // opens doesn't count as a press in it.
        private bool primed;

        public void Read()
        {
            // The device-less GetInput reads the keyboard when no gamepad is connected, and it
            // doesn't keep the previous frame's buttons, so a held key would look new every
            // frame. Read the gamepad only when there is one, and keep its history here.
            KeepPrevious(gamepad);
            if (UnityEngine.InputSystem.Gamepad.current != null)
                InputReader.GetInput(gamepad);
            else
            {
                InputReader.ClearInputState(gamepad);
                gamepad.start = false;
            }
            InputReader.GetInput(InputReader.Device.Keyboard1, keyboard1);
            InputReader.GetInput(InputReader.Device.Keyboard2, keyboard2);
            if (!primed)
            {
                primed = true;
                Up = Down = Left = Right = Confirm = Back = false;
                return;
            }

            Up = Pressed(s => s.up, s => s.wasUp);
            Down = Pressed(s => s.down, s => s.wasDown);
            Left = Pressed(s => s.left, s => s.wasLeft);
            Right = Pressed(s => s.right, s => s.wasRight);
            Confirm = Pressed(s => s.start, s => s.wasStart)
                || (Typing ? Input.GetKeyDown(KeyCode.Space) || Edge(gamepad, s => s.aButton, s => s.wasAButton)
                           : Pressed(s => s.aButton, s => s.wasAButton));
            Back = Pressed(s => s.bButton, s => s.wasBButton) || Input.GetKeyDown(KeyCode.Escape);
        }

        private static void KeepPrevious(InputState s)
        {
            s.wasAButton = s.aButton;
            s.wasBButton = s.bButton;
            s.wasXButton = s.xButton;
            s.wasYButton = s.yButton;
            s.wasLeft = s.left;
            s.wasRight = s.right;
            s.wasUp = s.up;
            s.wasDown = s.down;
            s.wasStart = s.start;
        }

        // Keyboard 2 (WASD, T, Y, Space) is all letters except Space, so it sits out while typing.
        private bool Pressed(System.Func<InputState, bool> now, System.Func<InputState, bool> before) =>
            Edge(gamepad, now, before) || Edge(keyboard1, now, before) || (!Typing && Edge(keyboard2, now, before));

        private static bool Edge(InputState s, System.Func<InputState, bool> now, System.Func<InputState, bool> before) =>
            now(s) && !before(s);
    }

    /// <summary>
    /// Builds screen-space UI in code, in the join screen's style (its pixel font). Built in
    /// code rather than placed in scenes, so the original scene files stay untouched.
    /// </summary>
    public static class MenuUi
    {
        private static Font arcade, plain;

        /// <summary>The join screen's font. Capitals and digits only, so big text only.</summary>
        public static Font Arcade => arcade != null ? arcade : arcade = Load("Fonts/ArcadeClassic");

        /// <summary>A pixel font with punctuation, for messages.</summary>
        public static Font Plain => plain != null ? plain : plain = Load("Fonts/Kemco Pixel Bold");

        private static Font Load(string path)
        {
            var loaded = Resources.Load<Font>(path);
            return loaded != null ? loaded : Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        /// <summary>A screen-space canvas at 1280x720 reference size, drawn above the scene.</summary>
        public static Transform Canvas(string name, int sortingOrder)
        {
            var root = new GameObject(name);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 1f;
            return root.transform;
        }

        /// <summary>Darkens the whole screen, so text reads over the scene behind it.</summary>
        public static void Dim(Transform parent, float alpha)
        {
            var go = new GameObject("Dim");
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, alpha);
            image.raycastTarget = false;
            var rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// One centred line of text, <paramref name="y"/> pixels above the middle of the screen,
        /// <paramref name="x"/> to the right. In <see cref="Arcade"/>, or <see cref="Plain"/> for messages.
        /// </summary>
        public static Text Line(Transform parent, int size, float y, bool plainFont = false, float x = 0f)
        {
            var go = new GameObject("Line");
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<Text>();
            label.font = plainFont ? Plain : Arcade;
            label.fontSize = size;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;

            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            shadow.effectDistance = new Vector2(2f, -2f);

            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(1200f, size * 1.5f);
            return label;
        }

        /// <summary>The join screen's prompt colour: white fading to black and back.</summary>
        public static Color Pulse() => Color.Lerp(Color.white, Color.black, Mathf.PingPong(Time.time * 3f, 1f));

        /// <summary>The same pulse for a dark background, where black would vanish: white to yellow.</summary>
        public static Color PulseOnDark() => Color.Lerp(Color.white, new Color(1f, 0.8f, 0.1f), Mathf.PingPong(Time.time * 3f, 1f));
    }
}
