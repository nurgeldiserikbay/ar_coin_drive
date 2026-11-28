using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Checks that every race track and battle arena fits its virtual location: nothing solid
/// (furniture legs, machines, trees...) on the road, the run-off or inside the ring, and the whole
/// play area inside the room or yard. Run in batch mode with -executeMethod LocationLayoutCheck.Run.
/// </summary>
public static class LocationLayoutCheck
{
    [MenuItem("AR Coin Drive/Check Location Layouts")]
    public static void Run()
    {
        var ok = true;
        foreach (GameLocation location in Enum.GetValues(typeof(GameLocation)))
        {
            var root = new GameObject("Check " + location).transform;
            var room = VirtualHouse.Build(root, location);
            var area = room.RoomRect(room.RoomAt(Vector2.zero));

            foreach (TrackLayout layout in Enum.GetValues(typeof(TrackLayout)))
            {
                foreach (TrackSize size in Enum.GetValues(typeof(TrackSize)))
                {
                    if (size < RaceTrack.MinSize(layout)) continue;
                    var track = RaceTrack.Get(layout, size);
                    var margin = track.RailOffset + RaceTrack.TubeRadius;
                    var hits = 0;
                    var example = "";
                    foreach (var point in track.CenterLine)
                    {
                        foreach (var obstacle in room.Obstacles)
                        {
                            if (!obstacle.Contains(point, margin)) continue;
                            hits++;
                            if (example.Length == 0) example = " first at (" + point.x.ToString("0.00") + ", " + point.y.ToString("0.00") + ")";
                        }

                        if (!area.Contains(point + Vector2.one * margin) || !area.Contains(point - Vector2.one * margin))
                        {
                            hits++;
                            if (example.Length == 0) example = " leaves the area at (" + point.x.ToString("0.00") + ", " + point.y.ToString("0.00") + ")";
                        }
                    }

                    ok &= hits == 0;
                    if (hits > 0 || size == TrackSize.L)
                        Debug.Log($"LAYOUT_CHECK {location} track {layout} {size}: {(hits == 0 ? "OK" : hits + " blocked samples" + example)}");
                }
            }

            foreach (ArenaShape shape in Enum.GetValues(typeof(ArenaShape)))
            {
                for (var size = 0; size < BattleArena.SizeRadius.Length; size++)
                {
                    var radius = BattleArena.SizeRadius[size] + 0.06f;
                    var hits = 0;
                    var example = "";
                    for (var x = -radius; x <= radius; x += 0.04f)
                    {
                        for (var z = -radius; z <= radius; z += 0.04f)
                        {
                            var point = new Vector2(x, z);
                            if (BattleArena.Norm(point, shape) > radius) continue;
                            var blocked = !area.Contains(point);
                            foreach (var obstacle in room.Obstacles) blocked |= obstacle.Contains(point, 0.02f);
                            if (!blocked) continue;
                            hits++;
                            if (example.Length == 0) example = " first at (" + x.ToString("0.00") + ", " + z.ToString("0.00") + ")";
                        }
                    }

                    ok &= hits == 0;
                    Debug.Log($"LAYOUT_CHECK {location} arena {shape} {BattleArena.SizeNames[size]}: {(hits == 0 ? "OK" : hits + " blocked cells" + example)}");
                }
            }

            UnityEngine.Object.DestroyImmediate(root.gameObject);
            VirtualHouse.Release();
        }

        Debug.Log("LAYOUT_CHECK_RESULT " + (ok ? "OK" : "BLOCKED"));
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }
}
