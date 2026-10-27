using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Samples supported AR environment-depth frames and keeps nearby real-world points
/// above the play surface. This is a conservative proximity guard, not full scene
/// reconstruction or semantic object recognition.
/// </summary>
public sealed class ARDepthObstacleDetector : MonoBehaviour
{
    private const int PixelStep = 8;
    private const int MaximumPoints = 2500;
    private const float SampleInterval = 0.25f;
    private const float MinimumHeight = 0.045f;
    private const float MaximumHeight = 0.32f;
    private const float MaximumDepth = 4f;

    private AROcclusionManager _occlusionManager;
    private ARCameraManager _cameraManager;
    private Camera _camera;
    private Transform _arena;
    private readonly List<Vector3> _obstaclePoints = new List<Vector3>();
    private float _nextSampleTime;

    public bool DepthSupported { get; private set; }
    public bool HasDepthFrame { get; private set; }

    private void Awake()
    {
        _occlusionManager = GetComponent<AROcclusionManager>();
        _cameraManager = GetComponent<ARCameraManager>();
        _camera = GetComponent<Camera>();
    }

    public void SetArena(Transform arena)
    {
        _arena = arena;
        _obstaclePoints.Clear();
        HasDepthFrame = false;
    }

    private void Update()
    {
        if (_arena == null || Time.unscaledTime < _nextSampleTime)
            return;

        _nextSampleTime = Time.unscaledTime + SampleInterval;
        SampleEnvironmentDepth();
    }

    public bool IsPathBlocked(Vector3 carWorldPosition, Vector3 worldForward, float lookAhead, float radius)
    {
        if (!HasDepthFrame || _arena == null || _obstaclePoints.Count == 0)
            return false;

        var forward = Vector3.ProjectOnPlane(worldForward, _arena.up).normalized;
        if (forward.sqrMagnitude < 0.001f)
            return false;

        var probe = carWorldPosition + _arena.up * 0.075f + forward * lookAhead;
        var radiusSquared = radius * radius;

        for (var i = 0; i < _obstaclePoints.Count; i++)
        {
            var delta = _obstaclePoints[i] - probe;
            var verticalDistance = Mathf.Abs(Vector3.Dot(delta, _arena.up));
            if (verticalDistance > 0.16f)
                continue;

            var horizontalDelta = Vector3.ProjectOnPlane(delta, _arena.up);
            if (horizontalDelta.sqrMagnitude <= radiusSquared)
                return true;
        }

        return false;
    }

    private void SampleEnvironmentDepth()
    {
        var descriptor = _occlusionManager != null ? _occlusionManager.descriptor : null;
        DepthSupported = descriptor != null && descriptor.supportsEnvironmentDepthImage;
        if (!DepthSupported || _cameraManager == null || _camera == null
            || !_cameraManager.TryGetIntrinsics(out var intrinsics)
            || !_occlusionManager.TryAcquireEnvironmentDepthCpuImage(out var image))
        {
            HasDepthFrame = false;
            _obstaclePoints.Clear();
            return;
        }

        using (image)
        {
            if (image.planeCount < 1 || intrinsics.resolution.x <= 0 || intrinsics.resolution.y <= 0)
            {
                HasDepthFrame = false;
                _obstaclePoints.Clear();
                return;
            }

            var plane = image.GetPlane(0);
            var bytes = plane.data.ToArray();
            var scaleX = image.width / (float)intrinsics.resolution.x;
            var scaleY = image.height / (float)intrinsics.resolution.y;
            var focalX = intrinsics.focalLength.x * scaleX;
            var focalY = intrinsics.focalLength.y * scaleY;
            var principalX = intrinsics.principalPoint.x * scaleX;
            var principalY = intrinsics.principalPoint.y * scaleY;

            if (focalX <= 0f || focalY <= 0f)
            {
                HasDepthFrame = false;
                _obstaclePoints.Clear();
                return;
            }

            _obstaclePoints.Clear();
            for (var y = PixelStep / 2; y < image.height; y += PixelStep)
            {
                for (var x = PixelStep / 2; x < image.width; x += PixelStep)
                {
                    var depth = ReadDepthMeters(image.format, bytes, plane.rowStride, plane.pixelStride, x, y);
                    if (depth < 0.12f || depth > MaximumDepth)
                        continue;

                    // Depth values are camera-space Z; intrinsics convert pixels to a camera ray.
                    var cameraPoint = new Vector3(
                        (x - principalX) * depth / focalX,
                        -(y - principalY) * depth / focalY,
                        depth);
                    var worldPoint = _camera.transform.TransformPoint(cameraPoint);
                    var arenaPoint = _arena.InverseTransformPoint(worldPoint);

                    if (arenaPoint.x < -0.78f || arenaPoint.x > 0.78f
                        || arenaPoint.z < -0.78f || arenaPoint.z > 0.78f
                        || arenaPoint.y < MinimumHeight || arenaPoint.y > MaximumHeight)
                        continue;

                    _obstaclePoints.Add(worldPoint);
                    if (_obstaclePoints.Count >= MaximumPoints)
                        break;
                }

                if (_obstaclePoints.Count >= MaximumPoints)
                    break;
            }

            HasDepthFrame = true;
        }
    }

    private static float ReadDepthMeters(XRCpuImage.Format format, byte[] bytes,
        int rowStride, int pixelStride, int x, int y)
    {
        var index = y * rowStride + x * pixelStride;
        if (index < 0 || index >= bytes.Length)
            return 0f;

        if (format == XRCpuImage.Format.DepthUint16)
        {
            if (index + 1 >= bytes.Length) return 0f;
            var millimeters = bytes[index] | (bytes[index + 1] << 8);
            return millimeters * 0.001f;
        }

        if (format == XRCpuImage.Format.DepthFloat32)
        {
            if (index + 3 >= bytes.Length) return 0f;
            return BitConverter.ToSingle(bytes, index);
        }

        return 0f;
    }
}