using UnityEngine;

/// <summary>
/// Lightweight placeholder models built from Unity primitives. These keep the first
/// AR prototype self-contained; replace them with authored meshes when the game style is set.
/// </summary>
public static class ProceduralCarFactory
{
    public static GameObject CreateCar(Transform parent)
    {
        var root = new GameObject("Player Car");
        root.transform.SetParent(parent, false);

        var red = MakeMaterial(new Color(0.94f, 0.22f, 0.16f));
        var dark = MakeMaterial(new Color(0.12f, 0.16f, 0.20f));
        var glass = MakeMaterial(new Color(0.40f, 0.78f, 0.91f));
        var cream = MakeMaterial(new Color(1.00f, 0.88f, 0.54f));

        Part(root.transform, PrimitiveType.Cube, "Body", new Vector3(0f, 0.07f, 0f),
            new Vector3(0.17f, 0.07f, 0.31f), red, Quaternion.identity);
        Part(root.transform, PrimitiveType.Cube, "Cabin", new Vector3(0f, 0.13f, -0.015f),
            new Vector3(0.12f, 0.075f, 0.15f), glass, Quaternion.identity);
        Part(root.transform, PrimitiveType.Cube, "Front Bumper", new Vector3(0f, 0.045f, 0.165f),
            new Vector3(0.18f, 0.035f, 0.025f), dark, Quaternion.identity);
        Part(root.transform, PrimitiveType.Cube, "Rear Bumper", new Vector3(0f, 0.045f, -0.165f),
            new Vector3(0.18f, 0.035f, 0.025f), dark, Quaternion.identity);
        Part(root.transform, PrimitiveType.Cube, "Left Headlight", new Vector3(-0.055f, 0.083f, 0.163f),
            new Vector3(0.035f, 0.022f, 0.012f), cream, Quaternion.identity);
        Part(root.transform, PrimitiveType.Cube, "Right Headlight", new Vector3(0.055f, 0.083f, 0.163f),
            new Vector3(0.035f, 0.022f, 0.012f), cream, Quaternion.identity);

        Wheel(root.transform, "Front Left Wheel", -0.095f, 0.055f, 0.095f, dark);
        Wheel(root.transform, "Front Right Wheel", 0.095f, 0.055f, 0.095f, dark);
        Wheel(root.transform, "Rear Left Wheel", -0.095f, 0.055f, -0.095f, dark);
        Wheel(root.transform, "Rear Right Wheel", 0.095f, 0.055f, -0.095f, dark);
        DisableColliders(root);
        return root;
    }

    public static GameObject CreateCoin(Transform parent, Vector3 localPosition)
    {
        var root = new GameObject("Coin");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;

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

    private static void Wheel(Transform parent, string name, float x, float y, float z, Material material)
    {
        Part(parent, PrimitiveType.Cylinder, name, new Vector3(x, y, z),
            new Vector3(0.045f, 0.029f, 0.045f), material, Quaternion.Euler(0f, 0f, 90f));
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
        renderer.sharedMaterial = material;
        return part;
    }

    private static Material MakeMaterial(Color color)
    {
        var shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        return new Material(shader) { color = color };
    }

    private static void DisableColliders(GameObject root)
    {
        foreach (var collider in root.GetComponentsInChildren<Collider>())
            collider.enabled = false;
    }
}