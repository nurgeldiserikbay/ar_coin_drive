using System.Collections.Generic;
using UnityEngine;

/// <summary>Placeable obstacle kinds for free-drive building.</summary>
public enum ObstacleKind { Cone, Tyres, Wall }

/// <summary>
/// Creates the cars, coins and placeable obstacles, and hands out shared materials so new
/// rounds do not leak materials.
/// </summary>
public static class ProceduralCarFactory
{
    // Collision footprints that match the generated meshes (half sizes, metres, on the floor plane).
    public const float CarScale = 0.6f;          // about a 22 cm long buggy, like a 1:18 RC car
    public const float CarHalfWidth = 0.115f * CarScale;
    public const float CarHalfLength = 0.192f * CarScale;
    public const float CoinScale = 0.72f;
    public const float CoinRadius = 0.0375f * CoinScale;

    public const float ConeRadius = 0.034f;
    public const float TyreStackRadius = 0.05f;
    public static readonly Vector2 WallHalfSize = new Vector2(0.16f, 0.022f);

    public static GameObject CreateCar(Transform parent, Color? paint = null, Color? secondary = null)
    {
        return ProceduralCarModel.Create(parent, paint ?? new Color(0.80f, 0.06f, 0.05f),
            secondary ?? new Color(0.94f, 0.94f, 0.92f));
    }

    /// <summary>Footprint of an obstacle placed at a floor point with a heading.</summary>
    public static WorldObstacle Footprint(ObstacleKind kind, Vector2 position, float angle)
    {
        switch (kind)
        {
            case ObstacleKind.Cone: return WorldObstacle.Circle(position, ConeRadius);
            case ObstacleKind.Tyres: return WorldObstacle.Circle(position, TyreStackRadius);
            default: return WorldObstacle.Box(position, WallHalfSize, angle);
        }
    }

    public static GameObject CreateObstacle(Transform parent, ObstacleKind kind, Vector2 position, float angle)
    {
        var root = new GameObject(kind + " Obstacle");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = new Vector3(position.x, 0f, position.y);
        root.transform.localRotation = Quaternion.Euler(0f, angle, 0f);
        var renderer = root.AddComponent<MeshRenderer>();
        root.AddComponent<MeshFilter>().sharedMesh = ObstacleMeshes.Get(kind);
        switch (kind)
        {
            case ObstacleKind.Cone:
                renderer.sharedMaterials = new[]
                {
                    GetMaterial(new Color(0.98f, 0.36f, 0.06f), 0.45f),
                    GetMaterial(new Color(0.95f, 0.95f, 0.93f), 0.6f),
                    GetMaterial(new Color(0.08f, 0.08f, 0.09f), 0.2f)
                };
                break;
            case ObstacleKind.Tyres:
                renderer.sharedMaterials = new[]
                {
                    GetMaterial(new Color(0.05f, 0.05f, 0.055f), 0.18f),
                    GetMaterial(new Color(0.95f, 0.78f, 0.10f), 0.4f),
                    GetMaterial(new Color(0.08f, 0.08f, 0.09f), 0.2f)
                };
                break;
            default:
                renderer.sharedMaterials = new[]
                {
                    GetMaterial(new Color(0.85f, 0.10f, 0.08f), 0.35f),
                    GetMaterial(new Color(0.95f, 0.95f, 0.93f), 0.35f),
                    GetMaterial(new Color(0.08f, 0.08f, 0.09f), 0.2f)
                };
                break;
        }

        return root;
    }

    public static GameObject CreateCoin(Transform parent, Vector3 localPosition)
    {
        var root = new GameObject("Coin");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;
        root.transform.localScale = Vector3.one * CoinScale;
        root.AddComponent<MeshFilter>().sharedMesh = ObstacleMeshes.Coin;
        root.AddComponent<MeshRenderer>().sharedMaterials = new[]
        {
            GetMaterial(new Color(1.00f, 0.74f, 0.22f), 0.82f, 1f),
            GetMaterial(new Color(0.92f, 0.62f, 0.12f), 0.6f, 1f)
        };
        return root;
    }

    /// <summary>
    /// Returns a shared material for a colour (and optional tiled texture). Materials are cached
    /// for the whole session, so placing or resetting the arena does not leak new materials.
    /// </summary>
    public static Material GetMaterial(Color color, float smoothness = 0.35f, float metallic = 0f,
        Texture texture = null, float tiling = 1f)
    {
        var key = (color, smoothness, metallic, texture, tiling);
        if (MaterialCache.TryGetValue(key, out var cached) && cached != null)
            return cached;

        var material = CreateBaseMaterial();
        if (material == null) return null;
        material.name = "AR Coin Drive " + ColorUtility.ToHtmlStringRGB(color);
        material.color = color;
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
        if (texture != null)
        {
            material.mainTexture = texture;
            material.mainTextureScale = new Vector2(tiling, tiling);
        }

        MaterialCache[key] = material;
        return material;
    }

    private static readonly Dictionary<(Color, float, float, Texture, float), Material> MaterialCache =
        new Dictionary<(Color, float, float, Texture, float), Material>();
    private static Material _template;

    private static Material CreateBaseMaterial()
    {
        // The Resources template references the game's shader, which guarantees the shader is
        // included in player builds (Shader.Find alone can return null there).
        if (_template == null)
            _template = Resources.Load<Material>(TemplateMaterialName);
        if (_template != null)
            return new Material(_template);

        var shader = Shader.Find("AR Coin Drive/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null)
        {
            Debug.LogError("AR Coin Drive: no usable shader found for procedural models.");
            return null;
        }

        return new Material(shader);
    }

    private const string TemplateMaterialName = "ARCoinDriveLit";
}

/// <summary>Shared meshes for placeable obstacles and coins.</summary>
internal static class ObstacleMeshes
{
    private static Mesh _cone;
    private static Mesh _tyres;
    private static Mesh _wall;
    private static Mesh _coin;

    public static Mesh Get(ObstacleKind kind)
    {
        switch (kind)
        {
            case ObstacleKind.Cone: return _cone ? _cone : (_cone = BuildCone());
            case ObstacleKind.Tyres: return _tyres ? _tyres : (_tyres = BuildTyres());
            default: return _wall ? _wall : (_wall = BuildWall());
        }
    }

    public static Mesh Coin => _coin ? _coin : (_coin = BuildCoin());

    private static readonly Matrix4x4 Upright = Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, 90f));

    /// <summary>Traffic cone: square rubber base, orange cone with a reflective white band.</summary>
    private static Mesh BuildCone()
    {
        var b = new ProceduralCarModel.MeshBuilder(3);
        b.AddBox(2, new Vector3(0f, 0.003f, 0f), new Vector3(0.066f, 0.006f, 0.066f));
        const float h = 0.095f;
        // Lathe around X turned upright, profile (height, radius).
        b.AddLathe(0, new[] { new Vector2(0.006f, 0.0001f), new Vector2(0.006f, 0.028f), new Vector2(0.040f, 0.0195f) }, 24, Upright);
        b.AddLathe(1, new[] { new Vector2(0.040f, 0.0195f), new Vector2(0.058f, 0.0150f) }, 24, Upright);
        b.AddLathe(0, new[] { new Vector2(0.058f, 0.0150f), new Vector2(h, 0.0055f), new Vector2(h, 0.0001f) }, 24, Upright);
        return b.Build("Traffic Cone");
    }

    /// <summary>Three stacked tyres with a yellow band, like an RC track tyre wall.</summary>
    private static Mesh BuildTyres()
    {
        var b = new ProceduralCarModel.MeshBuilder(3);
        for (var i = 0; i < 3; i++)
        {
            var y = 0.002f + i * 0.026f;
            b.AddLathe(0, new[]
            {
                new Vector2(y, 0.028f), new Vector2(y, 0.046f), new Vector2(y + 0.004f, 0.050f),
                new Vector2(y + 0.020f, 0.050f), new Vector2(y + 0.024f, 0.046f), new Vector2(y + 0.024f, 0.028f),
                new Vector2(y, 0.028f)
            }, 28, Upright);
            if (i == 1)
                b.AddLathe(1, new[] { new Vector2(y + 0.008f, 0.0506f), new Vector2(y + 0.016f, 0.0506f) }, 28, Upright);
        }

        return b.Build("Tyre Stack");
    }

    /// <summary>Low foam barrier block with red and white sections.</summary>
    private static Mesh BuildWall()
    {
        var b = new ProceduralCarModel.MeshBuilder(3);
        const int sections = 6;
        var length = ProceduralCarFactory.WallHalfSize.x * 2f;
        var width = ProceduralCarFactory.WallHalfSize.y * 2f;
        var piece = length / sections;
        for (var i = 0; i < sections; i++)
        {
            var x = -length * 0.5f + piece * (i + 0.5f);
            b.AddBox(i % 2, new Vector3(x, 0.024f, 0f), new Vector3(piece, 0.044f, width));
        }

        b.AddBox(2, new Vector3(0f, 0.0015f, 0f), new Vector3(length + 0.004f, 0.003f, width + 0.004f));
        return b.Build("Barrier Block");
    }

    private static Mesh BuildCoin()
    {
        var b = new ProceduralCarModel.MeshBuilder(2);
        // Faces along Z so the coin stands up and spins around Y.
        var standUp = Matrix4x4.Rotate(Quaternion.Euler(0f, 90f, 0f));
        b.AddLathe(0, new[]
        {
            new Vector2(-0.006f, 0.0001f), new Vector2(-0.006f, 0.034f), new Vector2(-0.0045f, 0.0375f),
            new Vector2(0.0045f, 0.0375f), new Vector2(0.006f, 0.034f), new Vector2(0.006f, 0.0001f)
        }, 32, standUp);
        // Lathe faces point +X when the radius shrinks along the profile, -X when it grows.
        b.AddLathe(1, new[] { new Vector2(0.0062f, 0.022f), new Vector2(0.0062f, 0.0001f) }, 32, standUp);
        b.AddLathe(1, new[] { new Vector2(-0.0062f, 0.0001f), new Vector2(-0.0062f, 0.022f) }, 32, standUp);
        return b.Build("Coin");
    }
}
