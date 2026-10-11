using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Command-line builds. tools/build.sh runs these in batch mode, so a build does not need the
/// editor open. Both write the build ID into the build and copy the license next to the exe.
/// </summary>
public static class BuildScript
{
    // Relative to the Unity project folder (HohimBrueh), so builds land in builds/ at the
    // repository root, which is gitignored.
    private const string DevPath = "../builds/dev/FrogSmashers.exe";
    private const string ReleasePath = "../builds/release/FrogSmashers.exe";
    // Copied next to the exe in every build: the Frog Smashers license requires its notice in
    // every binary distribution, and LiteNetLib's MIT license requires its notice in every copy.
    private static readonly string[] Notices = { "../LICENSE.md", "../THIRD-PARTY-NOTICES.md" };
    private const string BuildIdAsset = "Assets/Resources/" + FrogSmashers.BuildInfo.ResourceName + ".txt";

    /// <summary>
    /// A Windows development build. Development builds run the playtest diagnostics, start
    /// Rounds in Sync Test mode like the editor, and measure garbage without the editor's own
    /// allocations mixed in.
    /// </summary>
    public static void BuildDevelopmentWindows()
    {
        // Several copies may run at once, so two instances on one PC can play each other.
        Build(DevPath, BuildOptions.Development, singleInstance: false);
    }

    /// <summary>
    /// The build players get: no development features, the same IL2CPP backend, and one copy
    /// at a time (starting it again brings the running game to the front).
    /// </summary>
    public static void BuildReleaseWindows()
    {
        Build(ReleasePath, BuildOptions.None, singleInstance: true);
    }

    private static void Build(string path, BuildOptions buildOptions, bool singleInstance)
    {
        string buildId = ArgumentAfter("-buildId") ?? "unknown";
        bool succeeded = false;
        bool savedSingleInstance = PlayerSettings.forceSingleInstance;
        try
        {
            // Set for this build only and restored afterwards, so ProjectSettings doesn't change.
            PlayerSettings.forceSingleInstance = singleInstance;

            // Written for this build only and removed afterwards, so the editor keeps reporting
            // "editor" and the file never gets committed.
            File.WriteAllText(BuildIdAsset, buildId);
            AssetDatabase.ImportAsset(BuildIdAsset);

            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = path,
                target = BuildTarget.StandaloneWindows64,
                options = buildOptions,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            succeeded = summary.result == BuildResult.Succeeded;

            if (succeeded)
            {
                foreach (string notice in Notices)
                    File.Copy(notice, Path.Combine(Path.GetDirectoryName(path), Path.GetFileName(notice)), overwrite: true);
            }

            Debug.Log("[BUILD] result=" + summary.result + " id=" + buildId + " errors=" + summary.totalErrors
                + " seconds=" + summary.totalTime.TotalSeconds.ToString("0") + " size=" + (summary.totalSize / (1024 * 1024)) + "MB");
        }
        finally
        {
            AssetDatabase.DeleteAsset(BuildIdAsset);
            PlayerSettings.forceSingleInstance = savedSingleInstance;
        }
        EditorApplication.Exit(succeeded ? 0 : 1);
    }

    private static string ArgumentAfter(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
