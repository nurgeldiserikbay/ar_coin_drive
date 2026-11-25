using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Matches the virtual lighting to the real room using ARCore light estimation: main light
/// direction, colour and brightness drive the key light (and so the car's shadow), and the
/// ambient spherical harmonics light everything else. Values the device does not provide
/// keep sensible defaults.
/// </summary>
[RequireComponent(typeof(ARCameraManager))]
public sealed class ARLightEstimation : MonoBehaviour
{
    private ARCameraManager _cameraManager;
    private Light _light;
    private Quaternion _defaultRotation;
    private float _defaultIntensity;
    private Color _defaultColor;

    public bool HasEstimate { get; private set; }

    public void Configure(Light keyLight)
    {
        _light = keyLight;
        if (_light == null) return;
        _defaultRotation = _light.transform.rotation;
        _defaultIntensity = _light.intensity;
        _defaultColor = _light.color;
    }

    private void Awake()
    {
        _cameraManager = GetComponent<ARCameraManager>();
        // Environmental HDR: direction + intensity + ambient SH. Falls back silently when unsupported.
        _cameraManager.requestedLightEstimation = LightEstimation.MainLightDirection
            | LightEstimation.MainLightIntensity | LightEstimation.AmbientSphericalHarmonics;
    }

    private void OnEnable() => _cameraManager.frameReceived += OnFrame;

    private void OnDisable()
    {
        _cameraManager.frameReceived -= OnFrame;
        if (_light != null)
        {
            _light.transform.rotation = _defaultRotation;
            _light.intensity = _defaultIntensity;
            _light.color = _defaultColor;
        }
    }

    private void OnFrame(ARCameraFrameEventArgs args)
    {
        if (_light == null) return;
        var estimate = args.lightEstimation;

        if (estimate.mainLightDirection.HasValue)
        {
            var direction = estimate.mainLightDirection.Value;
            // Keep the light from skimming the floor so shadows stay short and readable.
            if (direction.y > -0.35f) direction.y = -0.35f;
            _light.transform.rotation = Quaternion.Slerp(_light.transform.rotation,
                Quaternion.LookRotation(direction.normalized), 0.15f);
            HasEstimate = true;
        }

        if (estimate.mainLightColor.HasValue)
        {
            // ARCore reports linear radiance; the built-in pipeline here works in gamma.
            var color = estimate.mainLightColor.Value / Mathf.PI;
            color = color.gamma;
            var peak = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            if (peak > 0.0001f)
            {
                _light.color = Color.Lerp(_light.color, color / peak, 0.2f);
                _light.intensity = Mathf.Lerp(_light.intensity, Mathf.Clamp(peak * 1.6f, 0.35f, 1.6f), 0.2f);
            }
        }
        else if (estimate.averageBrightness.HasValue)
        {
            _light.intensity = Mathf.Lerp(_light.intensity, Mathf.Clamp(estimate.averageBrightness.Value * 2f, 0.35f, 1.5f), 0.2f);
        }

        if (estimate.ambientSphericalHarmonics.HasValue)
        {
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientProbe = estimate.ambientSphericalHarmonics.Value;
        }
        else if (estimate.averageBrightness.HasValue)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            var level = Mathf.Clamp(estimate.averageBrightness.Value, 0.15f, 0.9f);
            RenderSettings.ambientLight = new Color(level, level, level);
        }
    }
}
