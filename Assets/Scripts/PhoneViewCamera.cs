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
    private readonly List<(Vector3 position, Vector3 lookAt, int room)> _spots;
    private int _spot;
    private Vector3 _position;
    private Vector3 _moveFrom;
    private float _move = 1f;
    private bool _placed;
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
    public int Room => _spots[_spot].room;
    public string SpotName => (_spot % 2 == 0 ? "corner A" : "corner B");

    public PhoneViewCamera(List<(Vector3 position, Vector3 lookAt, int room)> spots)
    {
        _spots = spots;
        HasGyro = SystemInfo.supportsGyroscope;
        if (HasGyro) Input.gyro.enabled = true;
    }

    /// <summary>The other standing spot in the same room.</summary>
    public void NextSpot()
    {
        var room = Room;
        for (var i = 1; i <= _spots.Count; i++)
        {
            var candidate = (_spot + i) % _spots.Count;
            if (_spots[candidate].room != room) continue;
            MoveTo(candidate);
            return;
        }
    }

    /// <summary>
    /// The MOVE button: walk to the room the car is in, or to the other spot when already there.
    /// The player is never moved automatically, so the view only changes when they ask for it.
    /// </summary>
    public bool WalkTowards(int carRoom, Vector3 car)
    {
        if (Room != carRoom)
        {
            FollowRoom(carRoom, car);
            return true;
        }

        NextSpot();
        return false;
    }

    /// <summary>Walk to the room the car is in (the nearer of its spots), if not there already.</summary>
    public void FollowRoom(int room, Vector3 car)
    {
        if (Room == room) return;
        var best = -1;
        var bestDistance = float.MaxValue;
        for (var i = 0; i < _spots.Count; i++)
        {
            if (_spots[i].room != room) continue;
            var distance = (_spots[i].position - _position).sqrMagnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = i;
        }

        if (best >= 0) MoveTo(best);
    }

    private void MoveTo(int spot)
    {
        _spot = spot;
        _moveFrom = _position;
        _move = _placed ? 0f : 1f;
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
        var (target3, lookAt, _) = _spots[_spot];
        // Walk smoothly between spots (about 0.8 s) instead of teleporting.
        _move = Mathf.Min(1f, _move + dt / 0.8f);
        var eased = _move * _move * (3f - 2f * _move);
        _position = _placed ? Vector3.Lerp(_moveFrom, target3, eased) : target3;
        _placed = true;
        var position = _position;
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
                // Recentre so the phone's current heading looks at the car.
                _yawOffset = Yaw(target - target3) - Yaw(device * Vector3.forward);
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
