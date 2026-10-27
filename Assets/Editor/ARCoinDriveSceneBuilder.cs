using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Android;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.XR.CoreUtils;

public static class ARCoinDriveSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/ARCoinDriveDemo.unity";

    [MenuItem("Tools/AR Coin Drive/Create Demo Scene")]
    public static void CreateDemoScene()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");

        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var sessionObject = new GameObject("AR Session");
        sessionObject.AddComponent<ARSession>();
        sessionObject.AddComponent<ARInputManager>();

        var originObject = new GameObject("XR Origin");
        var cameraOffset = new GameObject("Camera Offset");
        cameraOffset.transform.SetParent(originObject.transform, false);

        var cameraObject = new GameObject("AR Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.SetParent(cameraOffset.transform, false);
        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        cameraObject.AddComponent<ARCameraManager>();
        cameraObject.AddComponent<ARCameraBackground>();

        var origin = originObject.AddComponent<XROrigin>();
        origin.Camera = camera;
        origin.CameraFloorOffsetObject = cameraOffset;

        var planes = originObject.AddComponent<ARPlaneManager>();
        planes.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
        originObject.AddComponent<ARRaycastManager>();

        var game = new GameObject("AR Coin Drive");
        game.AddComponent<ARDemoController>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(ScenePath, true)
        };

        AssetDatabase.SaveAssets();
        Selection.activeGameObject = game;
        Debug.Log("AR Coin Drive scene created. Enable ARCore under Project Settings > XR Plug-in Management for Android.");
    }
}