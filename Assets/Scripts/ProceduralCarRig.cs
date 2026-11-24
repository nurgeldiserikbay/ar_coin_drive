using UnityEngine;

/// <summary>
/// Purely visual car motion: wheels roll with travel, front wheels steer, and the body
/// leans into acceleration and corners. Gameplay position and collisions are unaffected.
/// </summary>
public sealed class ProceduralCarRig : MonoBehaviour
{
    private const float MaxSteerAngle = 24f;
    private const float MaxRoll = 3.2f;
    private const float MaxPitch = 2.4f;
    private const float ReferenceSpeed = 0.48f;

    private Transform _body;
    private Transform[] _steerPivots;
    private Transform[] _spinners;
    private float _wheelRadius;
    private float _spin;
    private float _steer;
    private float _roll;
    private float _pitch;
    private float _lastSpeed;

    public void Configure(Transform body, Transform[] steerPivots, Transform[] spinners, float wheelRadius)
    {
        _body = body;
        _steerPivots = steerPivots;
        _spinners = spinners;
        _wheelRadius = wheelRadius;
    }

    public void UpdateVisual(float speed, float steerInput, float deltaTime)
    {
        if (_body == null || deltaTime <= 0f) return;

        _spin = Mathf.Repeat(_spin + speed * deltaTime / _wheelRadius * Mathf.Rad2Deg, 360f);
        for (var i = 0; i < _spinners.Length; i++)
            _spinners[i].localRotation = Quaternion.Euler(_spin, 0f, 0f);

        _steer = Mathf.MoveTowards(_steer, steerInput * MaxSteerAngle, 170f * deltaTime);
        for (var i = 0; i < _steerPivots.Length; i++)
            _steerPivots[i].localRotation = Quaternion.Euler(0f, _steer, 0f);

        var acceleration = (speed - _lastSpeed) / deltaTime;
        _lastSpeed = speed;
        var speedRatio = Mathf.Clamp01(Mathf.Abs(speed) / ReferenceSpeed);
        var targetRoll = steerInput * speedRatio * MaxRoll;
        var targetPitch = Mathf.Clamp(-acceleration * 1.6f, -MaxPitch, MaxPitch);
        var blend = 1f - Mathf.Exp(-9f * deltaTime);
        _roll = Mathf.Lerp(_roll, targetRoll, blend);
        _pitch = Mathf.Lerp(_pitch, targetPitch, blend);
        _body.localRotation = Quaternion.Euler(_pitch, 0f, _roll);
    }
}
