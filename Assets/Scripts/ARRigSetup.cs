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

    /// <summary>
    /// Adds a translucent mesh to planes when the plane manager has no prefab.
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
            plane.gameObject.AddComponent<ARPlaneMeshVisualizer>();
        }
    }

    private static Material GetPlaneMaterial()
    {
        if (_planeMaterial != null) return _planeMaterial;
        var shader = Shader.Find("Sprites/Default");
        if (shader == null) return null;
        _planeMaterial = new Material(shader)
        {
            name = "AR Plane Tint (runtime)",
            color = new Color(0.10f, 0.82f, 0.70f, 0.22f)
        };
        return _planeMaterial;
    }
}
