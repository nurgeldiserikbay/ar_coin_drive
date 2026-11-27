using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Builds a floor map of real obstacles from AR environment depth, so the virtual car bumps
/// into shoes, furniture and walls like a real RC car.
///
/// The play surface is split into 4 cm cells. Each depth frame votes per cell: points between
/// 3.5 cm and 35 cm above the floor (car height) mark the cell solid, points lying on the
/// floor clear it again, and a drop of more than 8 cm (a table edge) also counts as solid.
/// A cell needs votes from two frames before it blocks, which filters single noisy readings.
/// The map persists while the phone looks elsewhere, so obstacles stay solid when out of view.
/// Depth is geometric only (no object recognition) and depends on the device.
///
/// Phones without the Depth API still get a map from ARCore's feature points (the same points AR
/// tracking uses): sparser, and only on textured surfaces, but available on every ARCore phone.
/// Each point at car height marks its cell and the cells around it; points on the floor clear them.
/// </summary>
public sealed class ARDepthObstacleDetector : MonoBehaviour
{
    public const float CellSize = 0.04f;
    public const float HalfExtent = 3f;
    private const int Cells = (int)(HalfExtent * 2f / CellSize);

    private const float SampleInterval = 0.12f;
    private const int PixelStep = 2;
    private const float MaximumDepth = 3.6f;
    private const float ObstacleMinHeight = 0.035f;
    private const float ObstacleMaxHeight = 0.35f;
    // Feature points are noisier than depth: a textured rug must not turn into obstacles.
    private const float PointMinHeight = 0.05f;
    private const float FloorBand = 0.015f;
    private const float CliffDepth = -0.08f;
    private const float SolidThreshold = 2f;
    private const float MaxEvidence = 6f;
    private const byte MinimumConfidence = 90;

    private AROcclusionManager _occlusionManager;
    private ARPointCloudManager _pointClouds;
    private ARCameraManager _cameraManager;
    private Camera _camera;
    private Transform _arena;
    private float _nextSampleTime;

    private readonly float[] _evidence = new float[Cells * Cells];
    private readonly ushort[] _frameHits = new ushort[Cells * Cells];
    private readonly ushort[] _frameFloor = new ushort[Cells * Cells];
    private readonly List<int> _touched = new List<int>(4096);
    private int _solidCount;

    private bool _showOverlay = true;
    private GameObject _overlayObject;
    private Mesh _overlayMesh;
    private Material _overlayMaterial;
    private bool _overlayDirty;
    private readonly List<Vector3> _overlayVertices = new List<Vector3>();
    private readonly List<Color32> _overlayColors = new List<Color32>();
    private readonly List<int> _overlayTriangles = new List<int>();

    public bool DepthSupported { get; private set; }
    public bool HasDepthFrame { get; private set; }
    /// <summary>Feature points are feeding the map (works without the Depth API).</summary>
    public bool HasPointFrame { get; private set; }
    /// <summary>Some source is mapping obstacles: depth or feature points.</summary>
    public bool Mapping => DepthSupported || HasPointFrame;
    public int SolidCellCount => _solidCount;

    public bool ShowOverlay
    {
        get => _showOverlay;
        set
        {
            _showOverlay = value;
            _overlayDirty = true;
            if (_overlayObject != null) _overlayObject.SetActive(value);
        }
    }

    private void Awake()
    {
        _occlusionManager = GetComponent<AROcclusionManager>();
        _cameraManager = GetComponent<ARCameraManager>();
        _camera = GetComponent<Camera>();
    }

    public void SetArena(Transform arena)
    {
        _arena = arena;
        Array.Clear(_evidence, 0, _evidence.Length);
        _solidCount = 0;
        HasDepthFrame = false;
        if (_overlayObject != null) Destroy(_overlayObject);
        _overlayObject = null;
        _overlayDirty = true;
    }

    private void Update()
    {
        RefreshDepthSupport();
        if (_arena == null) return;

        if (Time.unscaledTime >= _nextSampleTime)
        {
            _nextSampleTime = Time.unscaledTime + SampleInterval;
            if (DepthSupported) SampleEnvironmentDepth();
            SamplePointClouds();
        }

        if (_overlayDirty && _showOverlay) RebuildOverlay();
    }

    private void RefreshDepthSupport()
    {
        var descriptor = _occlusionManager != null ? _occlusionManager.descriptor : null;
        DepthSupported = _occlusionManager != null && _occlusionManager.enabled && descriptor != null
            && descriptor.environmentDepthImageSupported == Supported.Supported;
        if (!DepthSupported) HasDepthFrame = false;
    }

    // ------------------------------------------------------------------ queries

    public bool IsSolid(Vector2 local)
    {
        var index = CellIndex(local.x, local.y);
        return index >= 0 && _evidence[index] >= SolidThreshold;
    }

    /// <summary>
    /// Solid cells under the car's rectangle. The normal points from the touched cells toward
    /// the car centre; depth is a conservative push-out distance.
    /// </summary>
    public bool Overlap(Vector2 carCenter, float heading, float halfWidth, float halfLength,
        out Vector2 normal, out float depth)
    {
        normal = Vector2.zero;
        depth = 0f;
        if (_arena == null || _solidCount == 0) return false;

        var forward = CarCollision.Forward(heading);
        var right = CarCollision.Right(heading);
        var reach = Mathf.Sqrt(halfWidth * halfWidth + halfLength * halfLength) + CellSize;
        var minX = ToCell(carCenter.x - reach);
        var maxX = ToCell(carCenter.x + reach);
        var minZ = ToCell(carCenter.y - reach);
        var maxZ = ToCell(carCenter.y + reach);
        var inflate = CellSize * 0.5f;
        var hits = 0;
        var push = Vector2.zero;
        var deepest = 0f;

        for (var z = Mathf.Max(0, minZ); z <= Mathf.Min(Cells - 1, maxZ); z++)
        {
            for (var x = Mathf.Max(0, minX); x <= Mathf.Min(Cells - 1, maxX); x++)
            {
                if (_evidence[z * Cells + x] < SolidThreshold) continue;
                var cell = CellCenter(x, z);
                var delta = cell - carCenter;
                var alongRight = Vector2.Dot(delta, right);
                var alongForward = Vector2.Dot(delta, forward);
                var penetrationSide = halfWidth + inflate - Mathf.Abs(alongRight);
                var penetrationEnd = halfLength + inflate - Mathf.Abs(alongForward);
                if (penetrationSide <= 0f || penetrationEnd <= 0f) continue;

                hits++;
                var away = -delta;
                push += away.sqrMagnitude > 0.000001f ? away.normalized : -forward;
                deepest = Mathf.Max(deepest, Mathf.Min(penetrationSide, penetrationEnd));
            }
        }

        if (hits == 0) return false;
        normal = push.sqrMagnitude > 0.000001f ? push.normalized : -forward;
        depth = Mathf.Min(deepest, CellSize * 1.5f);
        return true;
    }

    private static int ToCell(float coordinate) => Mathf.FloorToInt((coordinate + HalfExtent) / CellSize);

    private static int CellIndex(float x, float z)
    {
        var cx = ToCell(x);
        var cz = ToCell(z);
        if (cx < 0 || cz < 0 || cx >= Cells || cz >= Cells) return -1;
        return cz * Cells + cx;
    }

    private static Vector2 CellCenter(int x, int z) =>
        new Vector2((x + 0.5f) * CellSize - HalfExtent, (z + 0.5f) * CellSize - HalfExtent);

    // ------------------------------------------------------------------ sampling

    private void SampleEnvironmentDepth()
    {
        if (_cameraManager == null || _camera == null
            || !_cameraManager.TryGetIntrinsics(out var intrinsics)
            || !_occlusionManager.TryAcquireEnvironmentDepthCpuImage(out var image))
        {
            HasDepthFrame = false;
            return;
        }

        XRCpuImage confidence = default;
        var hasConfidence = false;
        try
        {
            hasConfidence = _occlusionManager.TryAcquireEnvironmentDepthConfidenceCpuImage(out confidence);
        }
        catch (Exception)
        {
            hasConfidence = false;
        }

        using (image)
        {
            try
            {
                Integrate(image, intrinsics, hasConfidence ? confidence : default, hasConfidence);
            }
            finally
            {
                if (hasConfidence) confidence.Dispose();
            }
        }
    }

    private void Integrate(XRCpuImage image, XRCameraIntrinsics intrinsics, XRCpuImage confidence, bool hasConfidence)
    {
        if (image.planeCount < 1 || intrinsics.resolution.x <= 0 || intrinsics.resolution.y <= 0) return;

        var plane = image.GetPlane(0);
        var bytes = plane.data;
        var scaleX = image.width / (float)intrinsics.resolution.x;
        var scaleY = image.height / (float)intrinsics.resolution.y;
        var focalX = intrinsics.focalLength.x * scaleX;
        var focalY = intrinsics.focalLength.y * scaleY;
        var principalX = intrinsics.principalPoint.x * scaleX;
        var principalY = intrinsics.principalPoint.y * scaleY;
        if (focalX <= 0f || focalY <= 0f) return;

        var confidenceBytes = default(NativeArray<byte>);
        var confidenceStride = 0;
        var confidenceScaleX = 1f;
        var confidenceScaleY = 1f;
        if (hasConfidence && confidence.planeCount > 0 && confidence.format == XRCpuImage.Format.OneComponent8)
        {
            var confidencePlane = confidence.GetPlane(0);
            confidenceBytes = confidencePlane.data;
            confidenceStride = confidencePlane.rowStride;
            confidenceScaleX = confidence.width / (float)image.width;
            confidenceScaleY = confidence.height / (float)image.height;
        }
        else
        {
            hasConfidence = false;
        }

        // Camera space (as the display sees it) straight into play-area space.
        var cameraToArena = _arena.worldToLocalMatrix * _camera.transform.localToWorldMatrix;
        var orientation = Screen.orientation;
        _touched.Clear();

        for (var y = PixelStep / 2; y < image.height; y += PixelStep)
        {
            for (var x = PixelStep / 2; x < image.width; x += PixelStep)
            {
                if (hasConfidence)
                {
                    var ci = Mathf.FloorToInt(y * confidenceScaleY) * confidenceStride + Mathf.FloorToInt(x * confidenceScaleX);
                    if (ci < 0 || ci >= confidenceBytes.Length || confidenceBytes[ci] < MinimumConfidence) continue;
                }

                var depth = ReadDepthMeters(image.format, bytes, plane.rowStride, plane.pixelStride, x, y);
                if (depth < 0.15f || depth > MaximumDepth) continue;

                var u = (x - principalX) * depth / focalX;
                var v = (y - principalY) * depth / focalY;
                var point = cameraToArena.MultiplyPoint3x4(SensorToDisplayCamera(u, v, depth, orientation));
                var index = CellIndex(point.x, point.z);
                if (index < 0) continue;

                var height = point.y;
                if (height >= ObstacleMinHeight && height <= ObstacleMaxHeight)
                    Vote(_frameHits, index);
                else if (height < CliffDepth && height > -1.5f)
                    Vote(_frameHits, index);
                else if (Mathf.Abs(height) <= FloorBand)
                    Vote(_frameFloor, index);
            }
        }

        Commit(2, 3, 1.5f);
        HasDepthFrame = true;
    }

    /// <summary>
    /// Turns this sample's votes into evidence: enough obstacle votes raise a cell, enough floor votes
    /// (and none for an obstacle) lower it. A cell blocks once its evidence reaches SolidThreshold.
    /// </summary>
    private void Commit(int minHits, int minFloor, float clearRate)
    {
        var changed = false;
        for (var i = 0; i < _touched.Count; i++)
        {
            var index = _touched[i];
            var before = _evidence[index];
            var after = before;
            if (_frameHits[index] >= minHits)
                after = Mathf.Min(MaxEvidence, before + 1f);
            else if (_frameFloor[index] >= minFloor && _frameHits[index] == 0)
                after = Mathf.Max(0f, before - clearRate);

            if (!Mathf.Approximately(before, after))
            {
                var wasSolid = before >= SolidThreshold;
                var isSolid = after >= SolidThreshold;
                if (wasSolid != isSolid)
                {
                    _solidCount += isSolid ? 1 : -1;
                    changed = true;
                }

                _evidence[index] = after;
            }

            _frameHits[index] = 0;
            _frameFloor[index] = 0;
        }

        _touched.Clear();
        if (changed) _overlayDirty = true;
    }

    /// <summary>
    /// Feature points: each point at car height votes for its cell and the four around it (points are
    /// sparse), points on the floor vote to clear. One vote is enough, since every point is already
    /// tracked by ARCore over several frames.
    /// </summary>
    private void SamplePointClouds()
    {
        if (_pointClouds == null) _pointClouds = FindAnyObjectByType<ARPointCloudManager>();
        if (_pointClouds == null || !_pointClouds.enabled) { HasPointFrame = false; return; }

        var arenaFromWorld = _arena.worldToLocalMatrix;
        _touched.Clear();
        var any = false;
        foreach (var cloud in _pointClouds.trackables)
        {
            if (!cloud.positions.HasValue) continue;
            var positions = cloud.positions.Value;
            // Positions are in session space: relative to the XR Origin's trackables parent.
            var space = cloud.transform.parent != null ? cloud.transform.parent.localToWorldMatrix : Matrix4x4.identity;
            var toArena = arenaFromWorld * space;
            for (var i = 0; i < positions.Length; i++)
            {
                var point = toArena.MultiplyPoint3x4(positions[i]);
                var height = point.y;
                if (height >= PointMinHeight && height <= ObstacleMaxHeight)
                {
                    var cx = Mathf.FloorToInt((point.x + HalfExtent) / CellSize);
                    var cz = Mathf.FloorToInt((point.z + HalfExtent) / CellSize);
                    VoteCell(_frameHits, cx, cz);
                    VoteCell(_frameHits, cx + 1, cz);
                    VoteCell(_frameHits, cx - 1, cz);
                    VoteCell(_frameHits, cx, cz + 1);
                    VoteCell(_frameHits, cx, cz - 1);
                    any = true;
                }
                else if (Mathf.Abs(height) <= FloorBand * 1.5f)
                {
                    var index = CellIndex(point.x, point.z);
                    if (index >= 0) Vote(_frameFloor, index);
                    any = true;
                }
            }
        }

        Commit(1, 2, 0.75f);
        if (any) HasPointFrame = true;
    }

    private void VoteCell(ushort[] counts, int cx, int cz)
    {
        if (cx < 0 || cz < 0 || cx >= Cells || cz >= Cells) return;
        Vote(counts, cz * Cells + cx);
    }

    private void Vote(ushort[] counts, int index)
    {
        if (_frameHits[index] == 0 && _frameFloor[index] == 0) _touched.Add(index);
        if (counts[index] < ushort.MaxValue) counts[index]++;
    }

    /// <summary>
    /// CPU depth images and camera intrinsics are in the sensor's native landscape-left
    /// orientation (x right, y down), while the AR camera transform follows the display.
    /// </summary>
    private static Vector3 SensorToDisplayCamera(float u, float v, float depth, ScreenOrientation orientation)
    {
        switch (orientation)
        {
            case ScreenOrientation.Portrait:
                return new Vector3(-v, -u, depth);
            case ScreenOrientation.PortraitUpsideDown:
                return new Vector3(v, u, depth);
            case ScreenOrientation.LandscapeRight:
                return new Vector3(-u, v, depth);
            default:
                return new Vector3(u, -v, depth);
        }
    }

    private static float ReadDepthMeters(XRCpuImage.Format format, NativeArray<byte> bytes,
        int rowStride, int pixelStride, int x, int y)
    {
        var index = y * rowStride + x * pixelStride;
        if (index < 0 || index >= bytes.Length) return 0f;

        if (format == XRCpuImage.Format.DepthUint16)
        {
            if (index + 1 >= bytes.Length) return 0f;
            return (bytes[index] | (bytes[index + 1] << 8)) * 0.001f;
        }

        if (format == XRCpuImage.Format.DepthFloat32)
        {
            if (index + 3 >= bytes.Length) return 0f;
            var bits = bytes[index] | (bytes[index + 1] << 8) | (bytes[index + 2] << 16) | (bytes[index + 3] << 24);
            return BitConverter.Int32BitsToSingle(bits);
        }

        return 0f;
    }

    // ------------------------------------------------------------------ overlay

    /// <summary>Faint orange tiles on the floor where the car will bump: shows what depth sees.</summary>
    private void RebuildOverlay()
    {
        _overlayDirty = false;
        if (_arena == null) return;

        if (_overlayObject == null)
        {
            var shader = Shader.Find("AR Coin Drive/Overlay");
            if (shader == null) return;
            if (_overlayMaterial == null)
                _overlayMaterial = new Material(shader) { name = "Depth Scan Overlay" };
            _overlayObject = new GameObject("Depth Scan Overlay");
            _overlayObject.transform.SetParent(_arena, false);
            _overlayMesh = new Mesh { name = "Depth Scan Overlay" };
            _overlayMesh.MarkDynamic();
            _overlayObject.AddComponent<MeshFilter>().sharedMesh = _overlayMesh;
            var meshRenderer = _overlayObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = _overlayMaterial;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            _overlayObject.SetActive(_showOverlay);
        }

        _overlayVertices.Clear();
        _overlayColors.Clear();
        _overlayTriangles.Clear();
        var half = CellSize * 0.42f;
        var tint = new Color32(255, 120, 40, 90);
        for (var z = 0; z < Cells && _overlayVertices.Count < 60000; z++)
        {
            for (var x = 0; x < Cells; x++)
            {
                if (_evidence[z * Cells + x] < SolidThreshold) continue;
                var c = CellCenter(x, z);
                var start = _overlayVertices.Count;
                _overlayVertices.Add(new Vector3(c.x - half, 0.004f, c.y - half));
                _overlayVertices.Add(new Vector3(c.x - half, 0.004f, c.y + half));
                _overlayVertices.Add(new Vector3(c.x + half, 0.004f, c.y + half));
                _overlayVertices.Add(new Vector3(c.x + half, 0.004f, c.y - half));
                for (var k = 0; k < 4; k++) _overlayColors.Add(tint);
                _overlayTriangles.Add(start);
                _overlayTriangles.Add(start + 1);
                _overlayTriangles.Add(start + 2);
                _overlayTriangles.Add(start);
                _overlayTriangles.Add(start + 2);
                _overlayTriangles.Add(start + 3);
            }
        }

        _overlayMesh.Clear();
        _overlayMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        _overlayMesh.SetVertices(_overlayVertices);
        _overlayMesh.SetColors(_overlayColors);
        _overlayMesh.SetTriangles(_overlayTriangles, 0);
        _overlayMesh.RecalculateBounds();
    }
}
