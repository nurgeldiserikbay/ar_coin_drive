using UnityEngine;

/// <summary>
/// Purely visual car motion: wheels roll with travel, front wheels steer, and the sprung shell
/// rolls into corners, squats and dives with throttle and brake, and rocks on its suspension
/// after a bump. Gameplay position and collisions are unaffected.
/// </summary>
public sealed class ProceduralCarRig : MonoBehaviour
{
    private const float MaxSteerAngle = 26f;
    private const float MaxRoll = 4.5f;
    private const float MaxPitch = 3.2f;

    private Transform _body;
    private Transform[] _steerPivots;
    private Transform[] _spinners;
    private float _wheelRadius;
    private float _spin;
    private float _steer;
    private float _roll;
    private float _pitch;
    private float _lastSpeed;
    private float _bumpPitch;
    private float _bumpVelocity;

    public float ReferenceSpeed { get; set; } = 0.5f;

    /// <summary>Uniform scale of the car in the scene; wheels spin to match the ground at that size.</summary>
    public float SizeScale { get; set; } = 1f;

    /// <summary>The sprung shell (driver camera mount).</summary>
    public Transform Body => _body;

    public void Configure(Transform body, Transform[] steerPivots, Transform[] spinners, float wheelRadius)
    {
        _body = body;
        _steerPivots = steerPivots;
        _spinners = spinners;
        _wheelRadius = wheelRadius;
    }

    /// <summary>Kicks the suspension after an impact; positive = hit from the front.</summary>
    public void Bump(float strength)
    {
        _bumpVelocity += Mathf.Clamp(strength, -1.5f, 1.5f) * 160f;
    }

    public void UpdateVisual(float speed, float steerInput, float deltaTime)
    {
        if (_body == null || deltaTime <= 0f) return;

        _spin = Mathf.Repeat(_spin + speed * deltaTime / (_wheelRadius * SizeScale) * Mathf.Rad2Deg, 360f);
        for (var i = 0; i < _spinners.Length; i++)
            _spinners[i].localRotation = Quaternion.Euler(_spin, 0f, 0f);

        _steer = Mathf.MoveTowards(_steer, steerInput * MaxSteerAngle, 220f * deltaTime);
        for (var i = 0; i < _steerPivots.Length; i++)
            _steerPivots[i].localRotation = Quaternion.Euler(0f, _steer, 0f);

        var acceleration = (speed - _lastSpeed) / deltaTime;
        _lastSpeed = speed;
        var speedRatio = Mathf.Clamp01(Mathf.Abs(speed) / ReferenceSpeed);
        var targetRoll = steerInput * speedRatio * MaxRoll * Mathf.Sign(speed == 0f ? 1f : speed);
        var targetPitch = Mathf.Clamp(-acceleration * 1.4f, -MaxPitch, MaxPitch);
        var blend = 1f - Mathf.Exp(-8f * deltaTime);
        _roll = Mathf.Lerp(_roll, targetRoll, blend);
        _pitch = Mathf.Lerp(_pitch, targetPitch, blend);

        // Damped spring for the post-impact rocking.
        _bumpVelocity += (-_bumpPitch * 260f - _bumpVelocity * 14f) * deltaTime;
        _bumpPitch += _bumpVelocity * deltaTime;
        _bumpPitch = Mathf.Clamp(_bumpPitch, -8f, 8f);

        _body.localRotation = Quaternion.Euler(_pitch + _bumpPitch, 0f, _roll);
    }
}
