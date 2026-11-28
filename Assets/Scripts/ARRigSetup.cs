using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;

/// <summary>
/// Repairs AR rigs created by older versions of the scene builder at runtime, so an
/// existing scene works without being regenerated:
/// - the AR camera needs a TrackedPoseDriver, otherwise it never follows the phone and
///   virtual content stays glued to the screen;
/// - anchors need an ARAnchorManager on the XR Origin;
/// - detected planes need a visual so the player can see where to tap.
/// </summary>
public static class ARRigSetup
{
    private static Material _planeMaterial;

    public static void EnsureRig(Camera arCamera)
    {
        if (arCamera != null)
            EnsureTrackedPoseDriver(arCamera.gameObject);

        var origin = Object.FindAnyObjectByType<XROrigin>();
        if (origin != null && origin.GetComponent<ARAnchorManager>() == null)
            origin.gameObject.AddComponent<ARAnchorManager>();
        // Feature points: the obstacle map's fallback on phones without the ARCore Depth API.
        if (origin != null && origin.GetComponent<ARPointCloudManager>() == null)
            origin.gameObject.AddComponent<ARPointCloudManager>();
    }

    private static void EnsureTrackedPoseDriver(GameObject cameraObject)
    {
        if (cameraObject.GetComponent<TrackedPoseDriver>() != null) return;

        var position = new InputAction("AR Camera Position", InputActionType.Value, expectedControlType: "Vector3");
        position.AddBinding("<HandheldARInputDevice>/devicePosition");
        position.AddBinding("<XRHMD>/centerEyePosition");

        var rotation = new InputAction("AR Camera Rotation", InputActionType.Value, expectedControlType: "Quaternion");
        rotation.AddBinding("<HandheldARInputDevice>/deviceRotation");
        rotation.AddBinding("<XRHMD>/centerEyeRotation");

        var driver = cameraObject.AddComponent<TrackedPoseDriver>();
        driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        driver.ignoreTrackingState = true;
        // Direct (non-reference) actions are enabled by the driver itself.
        driver.positionInput = new InputActionProperty(position);
        driver.rotationInput = new InputActionProperty(rotation);
    }

    public static void SetPoseDriverEnabled(Camera arCamera, bool enabled)
    {
        if (arCamera != null && arCamera.TryGetComponent<TrackedPoseDriver>(out var driver))
            driver.enabled = enabled;
    }

    private static Material _planeEdgeMaterial;

    /// <summary>
    /// Adds a visual to planes when the plane manager has no prefab: a faint fill with a dot
    /// every 10 cm (plane UVs are in metres) and a bright edge, the usual "found floor" look.
    /// Uses Sprites/Default, which is always included in builds and supports alpha.
    /// </summary>
    public static void EnsurePlaneVisuals(ARPlaneManager planeManager)
    {
        if (planeManager == null || planeManager.planePrefab != null) return;

        foreach (var plane in planeManager.trackables)
        {
            if (plane == null || plane.GetComponent<ARPlaneMeshVisualizer>() != null) continue;

            if (plane.GetComponent<MeshFilter>() == null)
                plane.gameObject.AddComponent<MeshFilter>();
            var meshRenderer = plane.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
                meshRenderer = plane.gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = GetPlaneMaterial();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            var edge = plane.GetComponent<LineRenderer>();
            if (edge == null) edge = plane.gameObject.AddComponent<LineRenderer>();
            edge.useWorldSpace = false;
            edge.loop = true;
            edge.widthMultiplier = 0.006f;
            edge.numCornerVertices = 2;
            edge.sharedMaterial = GetEdgeMaterial();
            edge.startColor = edge.endColor = new Color(1f, 1f, 1f, 0.75f);
            edge.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            edge.receiveShadows = false;
            plane.gameObject.AddComponent<ARPlaneMeshVisualizer>();
        }
    }

    private static Material GetEdgeMaterial()
    {
        if (_planeEdgeMaterial != null) return _planeEdgeMaterial;
        var shader = Shader.Find("Sprites/Default");
        if (shader == null) return null;
        _planeEdgeMaterial = new Material(shader) { name = "AR Plane Edge (runtime)" };
        return _planeEdgeMaterial;
    }

    private static Material GetPlaneMaterial()
    {
        if (_planeMaterial != null) return _planeMaterial;
        var shader = Shader.Find("Sprites/Default");
        if (shader == null) return null;
        _planeMaterial = new Material(shader)
        {
            name = "AR Plane Dots (runtime)",
            mainTexture = DotTexture(),
            color = new Color(1f, 1f, 1f, 1f)
        };
        return _planeMaterial;
    }

    /// <summary>One metre of floor: a light teal wash with a white dot every 10 cm.</summary>
    private static Texture2D DotTexture()
    {
        const int size = 160;
        const int spacing = 16;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
        {
            name = "AR Plane Dots",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4
        };
        var pixels = new Color32[size * size];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var dx = (x % spacing) - spacing * 0.5f + 0.5f;
            var dy = (y % spacing) - spacing * 0.5f + 0.5f;
            var dot = Mathf.Clamp01(2.6f - Mathf.Sqrt(dx * dx + dy * dy));
            var color = Color.Lerp(new Color(0.10f, 0.82f, 0.70f, 0.10f), new Color(1f, 1f, 1f, 0.85f), dot);
            pixels[y * size + x] = color;
        }

        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        return texture;
    }
}
