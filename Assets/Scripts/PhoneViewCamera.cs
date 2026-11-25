using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Virtual-room camera that behaves like filming the car with the phone: the player stands
/// at a spot in the room and the phone's gyroscope turns the view. Without a gyroscope the
/// accelerometer tilts the view and the camera pans to keep the car in frame by itself
/// (in the Editor it simply follows the car). Dragging on the screen turns the view too.
/// </summary>
public sealed class PhoneViewCamera
{
    private readonly List<(Vector3 position, Vector3 lookAt)> _spots;
    private int _spot;
    private float _yawOffset;
    private float _dragYaw;
    private float _dragPitch;
    private bool _needsRecenter = true;
    private bool _useScreenCorrection;
    private Quaternion _smoothed = Quaternion.identity;
    private bool _hasSmoothed;
    private float _autoYaw;
    private float _autoPitch;
    private bool _hasAuto;

    public float FieldOfView { get; set; } = 42f;
    public bool HasGyro { get; }
    public string SpotName => _spot == _spots.Count - 1 ? "LOW" : "SPOT " + (_spot + 1);

    public PhoneViewCamera(List<(Vector3 position, Vector3 lookAt)> spots)
    {
        _spots = spots;
        HasGyro = SystemInfo.supportsGyroscope;
        if (HasGyro) Input.gyro.enabled = true;
    }

    public void NextSpot()
    {
        _spot = (_spot + 1) % _spots.Count;
        Recenter();
    }

    /// <summary>Points the current phone direction at the middle of the play area again.</summary>
    public void Recenter()
    {
        _needsRecenter = true;
        _dragYaw = 0f;
        _dragPitch = 0f;
        _hasAuto = false;
    }

    public void Drag(Vector2 deltaDegrees)
    {
        _dragYaw += deltaDegrees.x;
        _dragPitch = Mathf.Clamp(_dragPitch - deltaDegrees.y, -60f, 60f);
    }

    public void Update(Transform camera, Camera cameraComponent, Vector3 target, float dt)
    {
        var (position, lookAt) = _spots[_spot];
        camera.position = position;
        if (cameraComponent != null)
            cameraComponent.fieldOfView = Mathf.Lerp(cameraComponent.fieldOfView, FieldOfView, 1f - Mathf.Exp(-10f * dt));

        Quaternion rotation;
        if (HasGyro && Input.gyro.enabled)
        {
            var device = GyroRotation();
            if (_needsRecenter)
            {
                _needsRecenter = false;
                _yawOffset = Yaw(lookAt - position) - Yaw(device * Vector3.forward);
                _hasSmoothed = false;
            }

            rotation = Quaternion.Euler(0f, _yawOffset + _dragYaw, 0f) * device;
        }
        else
        {
            rotation = AutoAim(position, target, dt);
        }

        // Light smoothing removes sensor jitter without adding noticeable lag.
        if (!_hasSmoothed)
        {
            _smoothed = rotation;
            _hasSmoothed = true;
        }

        _smoothed = Quaternion.Slerp(_smoothed, rotation, 1f - Mathf.Exp(-25f * dt));
        camera.rotation = _smoothed;
    }

    /// <summary>
    /// Device attitude converted to Unity's world (y up). Phones may or may not already rotate
    /// the reading to the screen orientation, so both interpretations are tried and the one
    /// that keeps the picture upright wins (with hysteresis so it never flickers).
    /// </summary>
    private Quaternion GyroRotation()
    {
        var a = Input.gyro.attitude;
        var raw = Quaternion.Euler(90f, 0f, 0f) * new Quaternion(a.x, a.y, -a.z, -a.w);
        var correction = ScreenCorrection();
        if (Mathf.Approximately(correction, 0f)) return raw;

        var corrected = raw * Quaternion.Euler(0f, 0f, correction);
        var rawUp = (raw * Vector3.up).y;
        var correctedUp = (corrected * Vector3.up).y;
        if (_useScreenCorrection && rawUp > correctedUp + 0.5f) _useScreenCorrection = false;
        else if (!_useScreenCorrection && correctedUp > rawUp + 0.5f) _useScreenCorrection = true;
        return _useScreenCorrection ? corrected : raw;
    }

    private static float ScreenCorrection()
    {
        switch (Screen.orientation)
        {
            case ScreenOrientation.LandscapeLeft: return -90f;
            case ScreenOrientation.LandscapeRight: return 90f;
            case ScreenOrientation.PortraitUpsideDown: return 180f;
            default: return 0f;
        }
    }

    /// <summary>No gyroscope: pan smoothly after the car, tilt from the accelerometer if present.</summary>
    private Quaternion AutoAim(Vector3 position, Vector3 target, float dt)
    {
        var toTarget = target - position;
        var targetYaw = Yaw(toTarget);
        var targetPitch = -Mathf.Atan2(toTarget.y, new Vector2(toTarget.x, toTarget.z).magnitude) * Mathf.Rad2Deg;

        var acceleration = Input.acceleration;
        if (acceleration.sqrMagnitude > 0.25f)
        {
            // Screen-space gravity: upright phone reads (0,-1,0), flat on its back (0,0,-1).
            var tilt = Mathf.Atan2(-acceleration.z, -acceleration.y) * Mathf.Rad2Deg;
            targetPitch = Mathf.Clamp(tilt, -30f, 85f);
        }

        if (!_hasAuto)
        {
            _autoYaw = targetYaw;
            _autoPitch = targetPitch;
            _hasAuto = true;
        }

        var follow = 1f - Mathf.Exp(-3.5f * dt);
        _autoYaw = Mathf.LerpAngle(_autoYaw, targetYaw, follow);
        _autoPitch = Mathf.Lerp(_autoPitch, targetPitch, follow);
        return Quaternion.Euler(Mathf.Clamp(_autoPitch + _dragPitch, -60f, 89f), _autoYaw + _dragYaw, 0f);
    }

    private static float Yaw(Vector3 direction) => Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
}
