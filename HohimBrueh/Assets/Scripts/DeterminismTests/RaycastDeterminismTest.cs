using System;
using System.IO;
using System.Text;
using UnityEngine;

// Standalone test harness — NOT part of the game. Answers the question from
// SMA-5 / "Raycast/CircleCast Determinism Research": do Physics2D.Raycast and
// Physics2D.CircleCastAll return bit-identical results on different machines
// and build backends, when run against the same static terrain?
//
// HOW TO USE
// 1. Drop this on an empty GameObject in a scene that has your real terrain
//    colliders set up (or a purpose-built test scene — doesn't matter, as
//    long as both machines run the exact same scene).
// 2. Set `terrainLayer` in the Inspector to match what Character.cs uses.
// 3. Build standalone (once with Mono, once with IL2CPP — Player Settings ->
//    Other Settings -> Scripting Backend). Play-mode-in-editor does NOT
//    exercise IL2CPP, so it isn't a substitute for a real build here.
// 4. Run each build. It writes two files to Application.persistentDataPath
//    (path is printed to the console/log on run):
//      - raycast_determinism_summary.txt  (one line: platform/backend/checksum)
//      - raycast_determinism_log.txt      (every individual hit result)
// 5. Compare summaries first. Same checksum on both machines/backends means
//    every single test case matched bit-for-bit. Different checksum -> diff
//    the two full logs; the first line that differs is the exact case that
//    broke, which tells you whether it was a Raycast or a CircleCastAll case.
//
// WHY NO RANDOM/TRIG HERE: if the test's own input generation used
// UnityEngine.Random or Mathf.Sin/Cos/Atan2, a mismatch could be caused by
// *that* being non-deterministic instead of the thing we're actually testing.
// So every origin/direction/distance/radius below is a literal constant or
// built from plain integer multiplication — nothing that could itself be a
// source of cross-machine variance.
public class RaycastDeterminismTest : MonoBehaviour
{
    public LayerMask terrainLayer;

    // Grid of origins: pure integer arithmetic, no trig/RNG.
    const int GridHalfWidth = 10;   // -10..10 -> 21 columns
    const int GridHalfHeight = 5;   // -5..5 -> 11 rows
    const float GridSpacing = 3f;
    const float GridYOffset = 5f;

    // Hardcoded unit-length directions. The diagonals use a literal
    // precomputed constant (1/sqrt(2)) rather than calling Mathf.Sqrt/Sin/Cos
    // at runtime, so direction generation itself can't be a variable.
    const float Diag = 0.70710678f;
    static readonly Vector2[] Directions = new Vector2[]
    {
        new Vector2( 1f,  0f),
        new Vector2(-1f,  0f),
        new Vector2( 0f,  1f),
        new Vector2( 0f, -1f),
        new Vector2( Diag,  Diag),
        new Vector2(-Diag,  Diag),
        new Vector2( Diag, -Diag),
        new Vector2(-Diag, -Diag),
    };

    static readonly float[] Distances = new float[] { 1f, 5f, 15f, 40f };
    static readonly float[] Radii = new float[] { 0.5f, 1.25f, 2f };

    StringBuilder _log = new StringBuilder();
    uint _checksum = 2166136261; // FNV-1a offset basis
    int _caseIndex;

    void Start()
    {
        RunSweep();
        WriteResults();
    }

    void RunSweep()
    {
        for (int gx = -GridHalfWidth; gx <= GridHalfWidth; gx++)
        {
            for (int gy = -GridHalfHeight; gy <= GridHalfHeight; gy++)
            {
                Vector2 origin = new Vector2(gx * GridSpacing, gy * GridSpacing + GridYOffset);

                foreach (var dir in Directions)
                {
                    foreach (var distance in Distances)
                    {
                        RaycastHit2D hit = Physics2D.Raycast(origin, dir, distance, terrainLayer);
                        LogRayResult("Raycast", hit);

                        foreach (var radius in Radii)
                        {
                            RaycastHit2D[] hits = Physics2D.CircleCastAll(origin, radius, dir, distance, terrainLayer);
                            LogCircleResults(hits);
                        }
                    }
                }
            }
        }
    }

    void LogRayResult(string kind, RaycastHit2D hit)
    {
        string line = FormatHit(kind, hit);
        _log.AppendLine(line);
        UpdateChecksum(line);
        _caseIndex++;
    }

    void LogCircleResults(RaycastHit2D[] hits)
    {
        string header = $"{_caseIndex},CircleCastAll,count={hits.Length}";
        _log.AppendLine(header);
        UpdateChecksum(header);
        foreach (var hit in hits)
        {
            string line = FormatHit("CircleHit", hit);
            _log.AppendLine(line);
            UpdateChecksum(line);
        }
        _caseIndex++;
    }

    string FormatHit(string kind, RaycastHit2D hit)
    {
        // Raw hex of the underlying float bits, not the printed decimal —
        // two floats can print the same while differing by 1 ULP, and a
        // 1-ULP difference is exactly what would desync a rollback resim.
        return $"{_caseIndex},{kind},hit={hit.collider != null}," +
               $"px={FloatBits(hit.point.x)},py={FloatBits(hit.point.y)}," +
               $"nx={FloatBits(hit.normal.x)},ny={FloatBits(hit.normal.y)}," +
               $"frac={FloatBits(hit.fraction)},dist={FloatBits(hit.distance)}";
    }

    string FloatBits(float f)
    {
        // BitConverter.SingleToInt32Bits isn't available on Unity 2019.2's
        // runtime (added to .NET years later) — GetBytes/ToInt32 is the
        // equivalent that's been around since old .NET Framework.
        int bits = BitConverter.ToInt32(BitConverter.GetBytes(f), 0);
        return bits.ToString("X8");
    }

    void UpdateChecksum(string line)
    {
        foreach (char c in line)
        {
            _checksum ^= c;
            _checksum *= 16777619; // FNV-1a prime
        }
    }

    void WriteResults()
    {
#if ENABLE_IL2CPP
        const string backend = "IL2CPP";
#else
        const string backend = "Mono";
#endif
        string dir = Application.persistentDataPath;
        string logPath = Path.Combine(dir, "raycast_determinism_log.txt");
        string summaryPath = Path.Combine(dir, "raycast_determinism_summary.txt");

        File.WriteAllText(logPath, _log.ToString());
        string summary =
            $"machine={SystemInfo.deviceName}\n" +
            $"os={SystemInfo.operatingSystem}\n" +
            $"cpu={SystemInfo.processorType}\n" +
            $"platform={Application.platform}\n" +
            $"backend={backend}\n" +
            $"cases={_caseIndex}\n" +
            $"checksum={_checksum:X8}\n";
        File.WriteAllText(summaryPath, summary);

        Debug.Log($"[RaycastDeterminismTest] Done. checksum={_checksum:X8} backend={backend}\nLogs written to: {dir}");
    }
}
