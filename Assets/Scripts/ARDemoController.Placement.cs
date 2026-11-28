using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// AR placement aid: while scanning, a ring follows the centre of the screen across the floor
/// and shows the chosen track (or battle ring) at real size, so the player sees where it will go
/// and whether it fits before tapping. Once any piece of floor is found, the ring also works on
/// the floor around it (the floor plane extended), so a small scanned patch is enough.
/// </summary>
public sealed partial class ARDemoController
{
    private static readonly List<ARRaycastHit> ReticleHits = new List<ARRaycastHit>();

    private Transform _reticle;
    private LineRenderer _reticleFootprint;
    private LineRenderer _reticleRing;
    private Material _reticleMaterial;
    private bool _reticleValid;
    private Pose _reticlePose;
    private ARPlane _reticlePlane;
    private string _footprintKey;

    /// <summary>True while the AR floor search is running (no play area yet).</summary>
    private bool ScanningAR => _arena == null && !_virtualMode && _screen == UiScreen.Game && _state == GameState.Placing;

    /// <summary>
    /// Finds the floor under a screen point. Prefers a scanned patch; otherwise the extension of
    /// the lowest horizontal plane found so far (the floor rather than a table top). The pose
    /// faces away from the camera so the start straight runs across the view.
    /// </summary>
    private bool FindFloor(Vector2 screenPosition, out Pose pose, out ARPlane plane)
    {
        pose = default;
        plane = null;
        if (_raycastManager == null || _arCamera == null) return false;
        if (!_raycastManager.Raycast(screenPosition, ReticleHits, TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinInfinity))
            return false;

        var cameraHeight = _arCamera.transform.position.y;
        var best = -1;
        var bestInside = false;
        for (var i = 0; i < ReticleHits.Count; i++)
        {
            var hitPlane = ReticleHits[i].trackable as ARPlane;
            if (hitPlane == null || hitPlane.alignment != PlaneAlignment.HorizontalUp) continue;
            var hit = ReticleHits[i];
            // A plane at eye height or above is not a floor we can drive on.
            if (cameraHeight - hit.pose.position.y < 0.12f) continue;
            if (Vector3.Distance(hit.pose.position, _arCamera.transform.position) > 6f) continue;
            var inside = (hit.hitType & TrackableType.PlaneWithinPolygon) != 0;
            if (best < 0
                || (inside && !bestInside)
                || (inside == bestInside && !inside && hit.pose.position.y < ReticleHits[best].pose.position.y - 0.05f))
            {
                best = i;
                bestInside = inside;
            }

            // Hits come nearest first: the first real patch under the ray wins.
            if (inside) break;
        }

        if (best < 0) return false;
        var chosen = ReticleHits[best];
        plane = chosen.trackable as ARPlane;
        var up = plane != null ? plane.transform.up : Vector3.up;
        var look = Vector3.ProjectOnPlane(_arCamera.transform.forward, up);
        if (look.sqrMagnitude < 0.0001f) look = Vector3.ProjectOnPlane(_arCamera.transform.up, up);
        pose = new Pose(chosen.pose.position, Quaternion.LookRotation(look.normalized, up));
        return true;
    }

    /// <summary>Runs every frame: moves the ring and footprint, or hides them.</summary>
    private void UpdateReticle()
    {
        if (!ScanningAR)
        {
            _reticleValid = false;
            if (_reticle != null && _reticle.gameObject.activeSelf) _reticle.gameObject.SetActive(false);
            return;
        }

        var centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        _reticleValid = FindFloor(centre, out var pose, out _reticlePlane);
        if (!_reticleValid)
        {
            if (_reticle != null) _reticle.gameObject.SetActive(false);
            return;
        }

        EnsureReticle();
        // Glide rather than jitter as the plane estimate shifts.
        var first = !_reticle.gameObject.activeSelf;
        _reticle.gameObject.SetActive(true);
        var t = first ? 1f : 1f - Mathf.Exp(-14f * Time.deltaTime);
        _reticle.SetPositionAndRotation(
            Vector3.Lerp(_reticle.position, pose.position + pose.up * 0.004f, t),
            Quaternion.Slerp(_reticle.rotation, pose.rotation, t));
        _reticlePose = pose;

        UpdateFootprint();
        var pulse = 1f + 0.08f * Mathf.Sin(Time.unscaledTime * 5f);
        _reticleRing.transform.localScale = new Vector3(pulse, pulse, 1f);
    }

    private void EnsureReticle()
    {
        if (_reticle != null) return;
        _reticle = new GameObject("Placement Reticle").transform;
        var shader = Shader.Find("Sprites/Default");
        _reticleMaterial = new Material(shader) { name = "Reticle" };

        _reticleRing = NewLine("Ring", 0.012f, new Color(1f, 0.56f, 0.10f, 0.95f));
        const int segments = 40;
        _reticleRing.positionCount = segments;
        for (var i = 0; i < segments; i++)
        {
            var a = i * Mathf.PI * 2f / segments;
            _reticleRing.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.07f);
        }

        _reticleFootprint = NewLine("Footprint", 0.02f, new Color(1f, 1f, 1f, 0.45f));
    }

    private LineRenderer NewLine(string name, float width, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_reticle, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.widthMultiplier = width;
        line.numCornerVertices = 3;
        line.alignment = LineAlignment.TransformZ;
        // TransformZ alignment faces the line along local Z: lay it flat on the floor.
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        line.sharedMaterial = _reticleMaterial;
        line.startColor = line.endColor = color;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    /// <summary>The road of the chosen track, the battle ring, or the free-drive square, at real size.</summary>
    private void UpdateFootprint()
    {
        var track = IsClient ? RaceTrack.Shared : RaceTrack.Get(GameSettings.Track, GameSettings.TrackSize);
        var ring = BattleArena.SizeRadius[Mathf.Clamp(IsClient ? _arenaSize : GameSettings.ArenaSize, 0, BattleArena.SizeRadius.Length - 1)];
        var key = _mode + "/" + track.Layout + "/" + track.Size + "/" + ring;
        if (key == _footprintKey) return;
        _footprintKey = key;

        // Lines lie flat (tilted 90 degrees), so a floor point (x, z) is written as (x, y).
        var points = new List<Vector3>();
        if (_mode == GameMode.Race)
        {
            _reticleFootprint.widthMultiplier = track.RoadHalfWidth * 2f;
            var line = track.CenterLine;
            var step = Mathf.Max(1, line.Count / 120);
            for (var i = 0; i < line.Count; i += step) points.Add(new Vector3(line[i].x, line[i].y, 0f));
        }
        else
        {
            var half = _mode == GameMode.Battle ? ring : FreeARHalfExtent;
            _reticleFootprint.widthMultiplier = 0.02f;
            if (_mode == GameMode.Battle)
            {
                for (var i = 0; i < 64; i++)
                {
                    var a = i * Mathf.PI * 2f / 64f;
                    points.Add(new Vector3(Mathf.Cos(a) * half, Mathf.Sin(a) * half, 0f));
                }
            }
            else
            {
                points.Add(new Vector3(-half, -half, 0f));
                points.Add(new Vector3(-half, half, 0f));
                points.Add(new Vector3(half, half, 0f));
                points.Add(new Vector3(half, -half, 0f));
            }
        }

        _reticleFootprint.positionCount = points.Count;
        _reticleFootprint.SetPositions(points.ToArray());
    }

    /// <summary>Hint while scanning, most urgent problem first.</summary>
    private string ScanAdvice()
    {
        if (ARSession.state == ARSessionState.SessionInitializing || ARSession.notTrackingReason != NotTrackingReason.None)
        {
            switch (ARSession.notTrackingReason)
            {
                case NotTrackingReason.InsufficientLight: return "Too dark for AR. Turn on a light.";
                case NotTrackingReason.ExcessiveMotion: return "Move the phone more slowly.";
                case NotTrackingReason.InsufficientFeatures: return "Point at a floor with some pattern: a rug, tiles or wood. Plain shiny floors are hard to see.";
                case NotTrackingReason.Relocalizing: return "Finding your spot again... look at the floor you scanned.";
            }
        }

        var planes = _planeManager != null ? _planeManager.trackables.count : 0;
        if (planes == 0 || (_planeManager != null && !HasFloorPlane()))
            return "Point the phone at the floor about a metre away and slowly sweep it left and right.";
        return _reticleValid
            ? "Aim the ring at a free spot. The outline shows the " + (_mode == GameMode.Race ? "track" : _mode == GameMode.Battle ? "ring" : "play area") + " size. Tap PLACE."
            : "Aim lower, at the floor.";
    }

    private bool HasFloorPlane()
    {
        foreach (var plane in _planeManager.trackables)
            if (plane != null && plane.alignment == PlaneAlignment.HorizontalUp) return true;
        return false;
    }
}
