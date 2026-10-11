using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Command-line builds. tools/build-dev.sh runs these in batch mode, so a build does not need
/// the editor open.
/// </summary>
public static class BuildScript
{
    /// <summary>
    /// A Windows development build: the scenes enabled in Build Settings, written to
    /// builds/dev/ at the repository root (gitignored). Development builds run the playtest
    /// diagnostics and start Rounds in Sync Test mode, like the editor, and measure garbage
    /// without the editor's own allocations mixed in.
    /// </summary>
    public static void BuildDevelopmentWindows()
    {
        var options = new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = "../builds/dev/FrogSmashers.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;
        Debug.Log("[BUILD] result=" + summary.result + " errors=" + summary.totalErrors
            + " seconds=" + summary.totalTime.TotalSeconds.ToString("0") + " size=" + (summary.totalSize / (1024 * 1024)) + "MB");
        EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
