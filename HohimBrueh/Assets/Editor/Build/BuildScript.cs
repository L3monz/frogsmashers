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
    private const string LicenseSource = "../LICENSE.md";
    private const string BuildIdAsset = "Assets/Resources/" + FrogSmashers.BuildInfo.ResourceName + ".txt";

    /// <summary>
    /// A Windows development build. Development builds run the playtest diagnostics, start
    /// Rounds in Sync Test mode like the editor, and measure garbage without the editor's own
    /// allocations mixed in.
    /// </summary>
    public static void BuildDevelopmentWindows()
    {
        Build(DevPath, BuildOptions.Development);
    }

    /// <summary>The build players get: no development features, the same IL2CPP backend.</summary>
    public static void BuildReleaseWindows()
    {
        Build(ReleasePath, BuildOptions.None);
    }

    private static void Build(string path, BuildOptions buildOptions)
    {
        string buildId = ArgumentAfter("-buildId") ?? "unknown";
        bool succeeded = false;
        try
        {
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

            // The license must travel with every binary distribution, unchanged.
            if (succeeded)
                File.Copy(LicenseSource, Path.Combine(Path.GetDirectoryName(path), "LICENSE.md"), overwrite: true);

            Debug.Log("[BUILD] result=" + summary.result + " id=" + buildId + " errors=" + summary.totalErrors
                + " seconds=" + summary.totalTime.TotalSeconds.ToString("0") + " size=" + (summary.totalSize / (1024 * 1024)) + "MB");
        }
        finally
        {
            AssetDatabase.DeleteAsset(BuildIdAsset);
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
