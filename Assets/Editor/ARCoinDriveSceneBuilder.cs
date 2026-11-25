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

    /// <summary>
    /// The car shader's material lives in Resources with GPU instancing on, so the build keeps
    /// the instancing variants (the four wheels of a car draw in one call).
    /// </summary>
    private static void EnsureCarMaterial()
    {
        const string path = "Assets/Resources/ARCoinDriveCar.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader = Shader.Find("AR Coin Drive/Car");
        if (shader == null) return;
        if (material == null)
        {
            material = new Material(shader) { name = "ARCoinDriveCar", enableInstancing = true };
            AssetDatabase.CreateAsset(material, path);
        }
        else if (!material.enableInstancing || material.shader != shader)
        {
            material.shader = shader;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
        }
    }

    [MenuItem("Tools/AR Coin Drive/Create Demo Scene")]
    public static void CreateDemoScene()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");

        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
        // Two-player mode talks over the local network.
        PlayerSettings.Android.forceInternetPermission = true;
        // The game picks landscape, portrait or free rotation at runtime (Settings > Screen).
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.allowedAutorotateToPortrait = true;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        ARCoinDriveBranding.Apply();
        EnsureCarMaterial();

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
        // The toy car is ~30 cm long; the default 0.3 m near plane clips it when the phone gets close.
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 30f;
        cameraObject.AddComponent<AudioListener>();
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

        var lightObject = new GameObject("AR Key Light");
        lightObject.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
        var keyLight = lightObject.AddComponent<Light>();
        keyLight.type = LightType.Directional;
        keyLight.intensity = 1.05f;
        keyLight.shadows = LightShadows.None;

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