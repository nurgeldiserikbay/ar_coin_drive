using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight placeholder models built from Unity primitives. These keep the first
/// AR prototype self-contained; replace them with authored meshes when the game style is set.
/// </summary>
public static class ProceduralCarFactory
{
    // Collision footprints that match the generated meshes (half sizes, metres, on the floor plane).
    public const float CarScale = 0.6f;          // keeps the car in proportion to the 1.5 m track
    public const float CarHalfWidth = 0.115f * CarScale;
    public const float CarHalfLength = 0.178f * CarScale;
    public const float ObstacleHalfSize = 0.075f;
    public const float CoinScale = 0.72f;
    public const float CoinRadius = 0.0375f * CoinScale;

    public static GameObject CreateCar(Transform parent, Color? paint = null)
    {
        return ProceduralCarModel.Create(parent, paint ?? new Color(0.88f, 0.08f, 0.07f));
    }

    public static GameObject CreateObstacle(Transform parent, Vector3 localPosition, Color color)
    {
        var root = new GameObject("Toy Barrier");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;

        var body = MakeMaterial(color);
        var accent = MakeMaterial(Color.Lerp(color, Color.white, 0.48f));
        Part(root.transform, PrimitiveType.Cube, "Barrier Body", new Vector3(0f, 0.055f, 0f),
            new Vector3(0.15f, 0.11f, 0.15f), body, Quaternion.identity);
        Part(root.transform, PrimitiveType.Cube, "Top Accent", new Vector3(0f, 0.115f, 0f),
            new Vector3(0.12f, 0.02f, 0.12f), accent, Quaternion.identity);
        DisableColliders(root);
        return root;
    }

    public static GameObject CreateCoin(Transform parent, Vector3 localPosition)
    {
        var root = new GameObject("Coin");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;
        root.transform.localScale = Vector3.one * CoinScale;

        var gold = MakeMaterial(new Color(1.00f, 0.72f, 0.12f));
        var highlight = MakeMaterial(new Color(1.00f, 0.91f, 0.45f));
        Part(root.transform, PrimitiveType.Cylinder, "Gold Token",
            Vector3.zero, new Vector3(0.075f, 0.012f, 0.075f), gold,
            Quaternion.Euler(90f, 0f, 0f));
        Part(root.transform, PrimitiveType.Cylinder, "Center Mark",
            new Vector3(0f, 0f, 0.014f), new Vector3(0.032f, 0.003f, 0.032f),
            highlight, Quaternion.Euler(90f, 0f, 0f));
        DisableColliders(root);
        return root;
    }

    private static GameObject Part(Transform parent, PrimitiveType type, string name,
        Vector3 position, Vector3 scale, Material material, Quaternion rotation)
    {
        var part = GameObject.CreatePrimitive(type);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        part.transform.localRotation = rotation;
        var renderer = part.GetComponent<Renderer>();
        if (material != null)
            renderer.sharedMaterial = material;
        return part;
    }

    /// <summary>
    /// Returns a shared material for a colour. Materials are cached for the whole session,
    /// so placing or resetting the arena does not leak new materials each time.
    /// </summary>
    public static Material GetMaterial(Color color, float smoothness = 0.35f, float metallic = 0f)
    {
        var key = (color, smoothness, metallic);
        if (MaterialCache.TryGetValue(key, out var cached) && cached != null)
            return cached;

        var material = CreateBaseMaterial();
        if (material == null) return null;
        material.name = "AR Coin Drive " + ColorUtility.ToHtmlStringRGB(color);
        material.color = color;
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
        MaterialCache[key] = material;
        return material;
    }

    private static readonly Dictionary<(Color, float, float), Material> MaterialCache =
        new Dictionary<(Color, float, float), Material>();
    private static Material _template;

    private static Material CreateBaseMaterial()
    {
        // The Resources template references the Standard shader, which guarantees the
        // shader is included in player builds (Shader.Find alone can return null there).
        if (_template == null)
            _template = Resources.Load<Material>(TemplateMaterialName);
        if (_template != null)
            return new Material(_template);

        var shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Debug.LogError("AR Coin Drive: no usable shader found for procedural models.");
            return null;
        }

        return new Material(shader);
    }

    private const string TemplateMaterialName = "ARCoinDriveLit";

    private static Material MakeMaterial(Color color) => GetMaterial(color);

    private static void DisableColliders(GameObject root)
    {
        foreach (var collider in root.GetComponentsInChildren<Collider>())
            collider.enabled = false;
    }
}