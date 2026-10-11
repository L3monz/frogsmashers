using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FrogSmashers
{
    /// <summary>
    /// Adds two lines to the title screen: the acknowledgement the Frog Smashers Source License
    /// requires "in the product", and the build ID in small type.
    ///
    /// Built in code rather than placed in the TitleScreen scene, so the original scene stays
    /// untouched and the text can't be lost in a merge of the scene file.
    /// </summary>
    public static class TitleCredits
    {
        private const string SceneName = "TitleScreen";

        // The license asks for the copyright holder and the origin, and for altered versions
        // to be marked as such.
        public const string Acknowledgement =
            "Based on the Frog Smashers source code, copyright Ruan Rothmann. Modified version.";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                if (scene.name == SceneName)
                    Create();
            };
        }

        private static void Create()
        {
            var font = Resources.Load<Font>("Fonts/Kemco Pixel Bold");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            // Part of the title scene, so it goes away with it.
            var root = new GameObject("TitleCredits");
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 1f;

            AddLine(root.transform, font, Acknowledgement, 18, new Color(1f, 1f, 1f, 0.85f),
                TextAnchor.LowerCenter, new Vector2(0.5f, 0f), new Vector2(0f, 14f));
            AddLine(root.transform, font, "build " + BuildInfo.Id, 12, new Color(1f, 1f, 1f, 0.45f),
                TextAnchor.LowerRight, new Vector2(1f, 0f), new Vector2(-10f, 4f));
        }

        private static void AddLine(Transform parent, Font font, string text, int size, Color color,
            TextAnchor alignment, Vector2 anchor, Vector2 offset)
        {
            var go = new GameObject("Line");
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<Text>();
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.raycastTarget = false;

            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            shadow.effectDistance = new Vector2(1f, -1f);

            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = offset;
            rect.sizeDelta = new Vector2(1200f, size * 2f);
        }
    }
}
