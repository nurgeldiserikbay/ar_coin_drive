using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.Rendering;
using Unity.XR.CoreUtils;

public static class ARCoinDriveSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/ARCoinDriveDemo.unity";

    private static GameObject CreatePlaneVisualizerPrefab()
    {
        const string generatedFolder = "Assets/Generated";
        const string materialPath = generatedFolder + "/ARPlaneTint.mat";
        const string prefabPath = generatedFolder + "/ARPlaneVisualizer.prefab";

        if (!AssetDatabase.IsValidFolder(generatedFolder))
            AssetDatabase.CreateFolder("Assets", "Generated");

        var existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (existingPrefab != null)
            return existingPrefab;

        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            var shader = Shader.Find("Standard");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Lit");
            material = new Material(shader)
            {
                name = "AR Plane Tint",
                color = new Color(0.10f, 0.82f, 0.70f, 0.16f)
            };
            if (material.HasProperty("_Mode"))
            {
                material.SetFloat("_Mode", 3f);
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            AssetDatabase.CreateAsset(material, materialPath);
        }

        var visualizer = new GameObject("AR Plane Visualizer");
        visualizer.AddComponent<ARPlane>();
        visualizer.AddComponent<MeshFilter>();
        var renderer = visualizer.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        visualizer.AddComponent<ARPlaneMeshVisualizer>();

        var prefab = PrefabUtility.SaveAsPrefabAsset(visualizer, prefabPath);
        Object.DestroyImmediate(visualizer);
        AssetDatabase.SaveAssets();
        return prefab;
    }

    [MenuItem("Tools/AR Coin Drive/Create Demo Scene")]
    public static void CreateDemoScene()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");

        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;

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
        var occlusion = cameraObject.AddComponent<AROcclusionManager>();
        occlusion.requestedEnvironmentDepthMode = EnvironmentDepthMode.Fastest;
        occlusion.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.PreferEnvironmentOcclusion;
        cameraObject.AddComponent<ARDepthObstacleDetector>();

        var origin = originObject.AddComponent<XROrigin>();
        origin.Camera = camera;
        origin.CameraFloorOffsetObject = cameraOffset;

        originObject.AddComponent<ARAnchorManager>();
        var planes = originObject.AddComponent<ARPlaneManager>();
        planes.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
        planes.planePrefab = CreatePlaneVisualizerPrefab();
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