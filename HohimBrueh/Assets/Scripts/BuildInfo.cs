using UnityEngine;

namespace FrogSmashers
{
    /// <summary>
    /// Which build of the game this is: the short git commit it was built from, with "-dirty"
    /// when the working tree had uncommitted changes. The build script writes it into
    /// Resources/BuildId.txt; the editor, which has no such file, reports "editor".
    ///
    /// Two machines can only play each other on the identical build, because a different
    /// build may simulate differently. Online play compares this in its handshake.
    /// </summary>
    public static class BuildInfo
    {
        public const string ResourceName = "BuildId";

        private static string id;

        public static string Id
        {
            get
            {
                if (id == null)
                {
                    var asset = Resources.Load<TextAsset>(ResourceName);
                    id = asset != null && asset.text.Trim().Length > 0 ? asset.text.Trim() : "editor";
                }
                return id;
            }
        }

        // In Player.log, so a bug report says exactly which build it came from.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void LogId()
        {
            Debug.Log("[BUILD] id=" + Id + " unity=" + Application.unityVersion);
        }
    }
}
