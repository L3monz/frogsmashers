using UnityEngine;

namespace FrogSmashers
{
    /// <summary>
    /// Which build of the game this is: the short git commit it was built from, with "-dirty"
    /// when the working tree had uncommitted changes. The build script writes it into
    /// Resources/BuildId.txt; the editor, which has no such file, reports "editor".
    ///
    /// Two machines can only play each other on the identical build, because a different
    /// build may simulate differently. Online play compares this in its handshake. The
    /// platform is part of it: a Windows and a Mac build of the same commit use different
    /// compilers (and on Apple Silicon a different CPU), so they aren't known to simulate
    /// identically, and must not play together until that's been checked.
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
                    string commit = asset != null && asset.text.Trim().Length > 0 ? asset.text.Trim() : "editor";
                    id = commit + "-" + PlatformTag;
                }
                return id;
            }
        }

        // Short and stable: "win64", "mac", "editor-win" and so on.
        private static string PlatformTag
        {
            get
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsPlayer: return System.IntPtr.Size == 8 ? "win64" : "win32";
                    case RuntimePlatform.OSXPlayer: return "mac";
                    case RuntimePlatform.LinuxPlayer: return "linux";
                    case RuntimePlatform.WindowsEditor: return "win-editor";
                    case RuntimePlatform.OSXEditor: return "mac-editor";
                    default: return Application.platform.ToString();
                }
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
