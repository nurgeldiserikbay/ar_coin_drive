using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Checks every track layout and size: two parts of the track that are not neighbours must be far
/// enough apart that their barriers (road + curb + turf + foam tube on each side) do not overlap.
/// Run from the menu or in batch mode with -executeMethod TrackLayoutCheck.Run.
/// </summary>
public static class TrackLayoutCheck
{
    [MenuItem("AR Coin Drive/Check Track Layouts")]
    public static void Run()
    {
        var ok = true;
        foreach (TrackLayout layout in Enum.GetValues(typeof(TrackLayout)))
        {
            foreach (TrackSize size in Enum.GetValues(typeof(TrackSize)))
            {
                if (size < RaceTrack.MinSize(layout)) continue;
                var track = RaceTrack.Get(layout, size);
                // Each side needs RailOffset plus the tube; keep 2 cm of floor between the two tubes.
                var needed = 2f * (track.RailOffset + RaceTrack.TubeRadius) + RaceTrack.MinBarrierGap;
                var gap = track.ClosestApproach();
                var fits = gap >= needed;
                ok &= fits;
                Debug.Log($"TRACK_CHECK {layout} {size} (min {RaceTrack.MinSize(layout)}): road {track.RoadHalfWidth * 200f:0} cm, lap {track.Length:0.00} m, "
                          + $"closest {gap * 100f:0} cm, needs {needed * 100f:0} cm {(fits ? "OK" : "OVERLAP")}");
            }
        }

        Debug.Log("TRACK_CHECK_RESULT " + (ok ? "OK" : "OVERLAP"));
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }
}
