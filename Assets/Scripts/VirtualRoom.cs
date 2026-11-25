using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The living room used when AR is unavailable: a wooden floor with a rug, sofa, coffee table
/// (the car fits between its legs), armchair, bookshelf, TV stand, cardboard boxes, a plant
/// and a floor lamp, lit by sunlight from a window. The open floor in the middle keeps room
/// for the race track. Furniture footprints are returned so the car bumps into them.
/// </summary>
public static class VirtualRoom
{
    public const float HalfWidth = 2.4f;   // x
    public const float HalfDepth = 2.8f;   // z
    private const float Height = 2.6f;

    private const int Floor = 0;
    private const int Wall = 1;
    private const int Ceiling = 2;
    private const int Trim = 3;
    private const int RugTop = 4;
    private const int SofaFabric = 5;
    private const int ChairFabric = 6;
    private const int Wood = 7;
    private const int Dark = 8;
    private const int Cardboard = 9;
    private const int Terracotta = 10;
    private const int Leaves = 11;
    private const int Metal = 12;
    private const int Shade = 13;
    private const int Submeshes = 14;

    public sealed class Info
    {
        public readonly List<WorldObstacle> Obstacles = new List<WorldObstacle>();
        public Rect Bounds;
        public readonly List<(Vector3 position, Vector3 lookAt)> CameraSpots = new List<(Vector3, Vector3)>();
        public Vector3 SunDirection;
    }

    public static Info Build(Transform parent)
    {
        var info = new Info
        {
            Bounds = Rect.MinMaxRect(-HalfWidth + 0.03f, -HalfDepth + 0.03f, HalfWidth - 0.03f, HalfDepth - 0.03f),
            SunDirection = new Vector3(0.62f, -0.62f, 0.30f).normalized
        };

        var shell = new RaceTrack.FlatMeshBuilder(Submeshes, 1.2f);
        var furniture = new RaceTrack.FlatMeshBuilder(Submeshes, 1.2f);
        var round = new ProceduralCarModel.MeshBuilder(Submeshes);

        BuildShell(shell);


        // Sofa against the far wall.
        var sofa = new Vector2(0f, HalfDepth - 0.45f);
        Box(furniture, SofaFabric, sofa, 0f, 0.05f, 0.40f, new Vector2(1.0f, 0.42f));
        Box(furniture, SofaFabric, sofa + new Vector2(0f, 0.30f), 0f, 0.40f, 0.82f, new Vector2(1.0f, 0.12f));
        Box(furniture, SofaFabric, sofa + new Vector2(-0.92f, 0f), 0f, 0.40f, 0.60f, new Vector2(0.09f, 0.42f));
        Box(furniture, SofaFabric, sofa + new Vector2(0.92f, 0f), 0f, 0.40f, 0.60f, new Vector2(0.09f, 0.42f));
        Box(furniture, Dark, sofa, 0f, 0f, 0.05f, new Vector2(0.96f, 0.38f));
        info.Obstacles.Add(WorldObstacle.Box(sofa, new Vector2(1.0f, 0.42f)));

        // Coffee table: only the legs block the car.
        var table = new Vector2(0f, 1.55f);
        Box(furniture, Wood, table, 0f, 0.40f, 0.44f, new Vector2(0.55f, 0.28f));
        Box(furniture, Wood, table, 0f, 0.18f, 0.20f, new Vector2(0.50f, 0.24f));
        foreach (var leg in new[] { new Vector2(-0.5f, -0.23f), new Vector2(0.5f, -0.23f), new Vector2(-0.5f, 0.23f), new Vector2(0.5f, 0.23f) })
        {
            Box(furniture, Wood, table + leg, 0f, 0f, 0.40f, new Vector2(0.022f, 0.022f));
            info.Obstacles.Add(WorldObstacle.Box(table + leg, new Vector2(0.024f, 0.024f)));
        }

        // Armchair angled toward the room.
        var chair = new Vector2(-1.75f, 1.85f);
        const float chairAngle = 35f;
        Box(furniture, ChairFabric, chair, chairAngle, 0.06f, 0.42f, new Vector2(0.40f, 0.40f));
        Box(furniture, ChairFabric, chair + Rotate(new Vector2(0f, 0.32f), chairAngle), chairAngle, 0.42f, 0.86f, new Vector2(0.40f, 0.09f));
        Box(furniture, ChairFabric, chair + Rotate(new Vector2(-0.34f, 0f), chairAngle), chairAngle, 0.42f, 0.60f, new Vector2(0.07f, 0.40f));
        Box(furniture, ChairFabric, chair + Rotate(new Vector2(0.34f, 0f), chairAngle), chairAngle, 0.42f, 0.60f, new Vector2(0.07f, 0.40f));
        Box(furniture, Dark, chair, chairAngle, 0f, 0.06f, new Vector2(0.37f, 0.37f));
        info.Obstacles.Add(WorldObstacle.Box(chair, new Vector2(0.41f, 0.41f), chairAngle));

        // Bookshelf on the right wall.
        var shelf = new Vector2(HalfWidth - 0.18f, 0.6f);
        Box(furniture, Wood, shelf, 0f, 0f, 1.8f, new Vector2(0.17f, 0.6f));
        for (var i = 0; i < 5; i++)
        {
            var y = 0.08f + i * 0.36f;
            Box(furniture, i % 2 == 0 ? Cardboard : Dark, shelf + new Vector2(-0.16f, -0.25f + (i % 3) * 0.12f), 0f,
                y, y + 0.24f, new Vector2(0.02f, 0.2f));
        }

        info.Obstacles.Add(WorldObstacle.Box(shelf, new Vector2(0.18f, 0.61f)));

        // TV stand and TV on the near wall.
        var tv = new Vector2(-1.45f, -HalfDepth + 0.22f);
        Box(furniture, Wood, tv, 0f, 0.04f, 0.48f, new Vector2(0.6f, 0.2f));
        Box(furniture, Dark, tv, 0f, 0f, 0.04f, new Vector2(0.56f, 0.18f));
        Box(furniture, Dark, tv + new Vector2(0f, 0.02f), 0f, 0.55f, 1.12f, new Vector2(0.52f, 0.02f));
        Box(furniture, Dark, tv, 0f, 0.48f, 0.56f, new Vector2(0.12f, 0.08f));
        info.Obstacles.Add(WorldObstacle.Box(tv, new Vector2(0.61f, 0.21f)));

        // Cardboard boxes on the floor: good RC obstacles.
        AddCrate(furniture, info, new Vector2(1.45f, -0.95f), 18f, new Vector2(0.21f, 0.16f), 0.30f);
        AddCrate(furniture, info, new Vector2(1.75f, -1.42f), -8f, new Vector2(0.15f, 0.15f), 0.24f);
        AddCrate(furniture, info, new Vector2(-1.55f, -0.55f), 40f, new Vector2(0.12f, 0.18f), 0.16f);

        // Plant in a pot and a floor lamp.
        var plant = new Vector2(1.95f, -2.25f);
        AddUprightCylinder(round, Terracotta, plant, 0f, 0.34f, 0.15f, 0.18f);
        round.AddEllipsoid(Leaves, new Vector3(plant.x, 0.62f, plant.y), new Vector3(0.26f, 0.32f, 0.26f));
        round.AddEllipsoid(Leaves, new Vector3(plant.x - 0.1f, 0.85f, plant.y + 0.05f), new Vector3(0.16f, 0.2f, 0.16f));
        info.Obstacles.Add(WorldObstacle.Circle(plant, 0.19f));

        var lamp = new Vector2(1.35f, HalfDepth - 0.3f);
        AddUprightCylinder(round, Metal, lamp, 0f, 0.025f, 0.15f, 0.15f);
        AddUprightCylinder(round, Metal, lamp, 0.025f, 1.45f, 0.012f, 0.012f);
        AddUprightCylinder(round, Shade, lamp, 1.40f, 1.70f, 0.20f, 0.13f);
        info.Obstacles.Add(WorldObstacle.Circle(lamp, 0.15f));

        var materials = Materials();
        CreateRenderer(parent, "Room Shell", shell.Build("Room Shell"), materials, false);
        CreateRenderer(parent, "Furniture", furniture.Build("Furniture"), materials, true);
        CreateRenderer(parent, "Furniture Round", round.Build("Furniture Round"), materials, true);
        CreateWindow(parent);
        CreateRug(parent, materials[RugTop]);

        info.CameraSpots.Add((new Vector3(0f, 1.35f, -1.95f), new Vector3(0f, 0f, 0.1f)));
        info.CameraSpots.Add((new Vector3(1.55f, 1.30f, -1.55f), new Vector3(0f, 0f, 0f)));
        info.CameraSpots.Add((new Vector3(-1.75f, 1.35f, 0.25f), new Vector3(0.2f, 0f, 0f)));
        info.CameraSpots.Add((new Vector3(0.30f, 0.40f, -1.25f), new Vector3(0f, 0f, 0.2f)));
        return info;
    }

    /// <summary>Rug under the play area (no collision). Sits below the race track's surface.</summary>
    private static void CreateRug(Transform parent, Material material)
    {
        var rug = new GameObject("Rug");
        rug.transform.SetParent(parent, false);
        var mesh = new Mesh { name = "Rug" };
        const float x = 1.35f;
        const float z = 1.15f;
        const float y = 0.0012f;
        mesh.SetVertices(new[] { new Vector3(-x, y, -z), new Vector3(-x, y, z), new Vector3(x, y, z), new Vector3(x, y, -z) });
        mesh.SetNormals(new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
        mesh.SetUVs(0, new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right });
        mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
        mesh.RecalculateBounds();
        rug.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = rug.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private static void BuildShell(RaceTrack.FlatMeshBuilder b)
    {
        var w = HalfWidth;
        var d = HalfDepth;
        b.AddQuad(Floor, new Vector3(-w, 0f, -d), new Vector3(-w, 0f, d), new Vector3(w, 0f, d), new Vector3(w, 0f, -d), Vector3.up);
        b.AddQuad(Ceiling, new Vector3(-w, Height, -d), new Vector3(w, Height, -d), new Vector3(w, Height, d), new Vector3(-w, Height, d), Vector3.down);
        b.AddQuad(Wall, new Vector3(-w, 0f, d), new Vector3(-w, Height, d), new Vector3(w, Height, d), new Vector3(w, 0f, d), Vector3.back);
        b.AddQuad(Wall, new Vector3(-w, 0f, -d), new Vector3(w, 0f, -d), new Vector3(w, Height, -d), new Vector3(-w, Height, -d), Vector3.forward);
        b.AddQuad(Wall, new Vector3(w, 0f, -d), new Vector3(w, 0f, d), new Vector3(w, Height, d), new Vector3(w, Height, -d), Vector3.left);
        b.AddQuad(Wall, new Vector3(-w, 0f, -d), new Vector3(-w, Height, -d), new Vector3(-w, Height, d), new Vector3(-w, 0f, d), Vector3.right);

        // Skirting boards.
        const float t = 0.012f;
        const float h = 0.08f;
        b.AddBoxAxes(Trim, new Vector3(0f, h * 0.5f, d - t), new Vector3(w, 0f, 0f), new Vector3(0f, h * 0.5f, 0f), new Vector3(0f, 0f, t));
        b.AddBoxAxes(Trim, new Vector3(0f, h * 0.5f, -d + t), new Vector3(w, 0f, 0f), new Vector3(0f, h * 0.5f, 0f), new Vector3(0f, 0f, t));
        b.AddBoxAxes(Trim, new Vector3(w - t, h * 0.5f, 0f), new Vector3(t, 0f, 0f), new Vector3(0f, h * 0.5f, 0f), new Vector3(0f, 0f, d));
        b.AddBoxAxes(Trim, new Vector3(-w + t, h * 0.5f, 0f), new Vector3(t, 0f, 0f), new Vector3(0f, h * 0.5f, 0f), new Vector3(0f, 0f, d));

        // Window frame on the left wall (the glass is a separate unlit quad).
        var frameX = -w + 0.02f;
        b.AddBoxAxes(Trim, new Vector3(frameX, 0.80f, 0.2f), new Vector3(0.03f, 0f, 0f), new Vector3(0f, 0.03f, 0f), new Vector3(0f, 0f, 0.78f));
        b.AddBoxAxes(Trim, new Vector3(frameX, 2.10f, 0.2f), new Vector3(0.03f, 0f, 0f), new Vector3(0f, 0.03f, 0f), new Vector3(0f, 0f, 0.78f));
        b.AddBoxAxes(Trim, new Vector3(frameX, 1.45f, -0.55f), new Vector3(0.03f, 0f, 0f), new Vector3(0f, 0.68f, 0f), new Vector3(0f, 0f, 0.03f));
        b.AddBoxAxes(Trim, new Vector3(frameX, 1.45f, 0.95f), new Vector3(0.03f, 0f, 0f), new Vector3(0f, 0.68f, 0f), new Vector3(0f, 0f, 0.03f));
        b.AddBoxAxes(Trim, new Vector3(frameX, 1.45f, 0.2f), new Vector3(0.02f, 0f, 0f), new Vector3(0f, 0.65f, 0f), new Vector3(0f, 0f, 0.015f));
    }

    private static void CreateWindow(Transform parent)
    {
        var shader = Shader.Find("AR Coin Drive/Overlay");
        if (shader == null) return;
        var window = new GameObject("Window Glass");
        window.transform.SetParent(parent, false);
        var mesh = new Mesh { name = "Window Glass" };
        var x = -HalfWidth + 0.01f;
        mesh.SetVertices(new[]
        {
            new Vector3(x, 0.80f, -0.55f), new Vector3(x, 2.10f, -0.55f), new Vector3(x, 2.10f, 0.95f), new Vector3(x, 0.80f, 0.95f)
        });
        var low = new Color(0.86f, 0.93f, 0.98f);
        var high = new Color(0.70f, 0.84f, 0.98f);
        mesh.SetColors(new[] { low, high, high, low });
        mesh.SetUVs(0, new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right });
        mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
        mesh.RecalculateBounds();
        window.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = window.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = new Material(shader) { name = "Window Glass", renderQueue = 2450 };
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private static Material[] Materials()
    {
        return new[]
        {
            ProceduralCarFactory.GetMaterial(new Color(0.82f, 0.80f, 0.78f), 0.45f, 0f, ProceduralTextures.Wood, 1f),
            ProceduralCarFactory.GetMaterial(new Color(0.80f, 0.78f, 0.74f), 0.08f),
            ProceduralCarFactory.GetMaterial(new Color(0.90f, 0.90f, 0.89f), 0.05f),
            ProceduralCarFactory.GetMaterial(new Color(0.92f, 0.92f, 0.91f), 0.35f),
            ProceduralCarFactory.GetMaterial(new Color(0.85f, 0.85f, 0.85f), 0.04f, 0f, ProceduralTextures.Rug, 1f),
            ProceduralCarFactory.GetMaterial(new Color(0.34f, 0.40f, 0.48f), 0.08f, 0f, ProceduralTextures.Fabric, 4f),
            ProceduralCarFactory.GetMaterial(new Color(0.72f, 0.52f, 0.20f), 0.08f, 0f, ProceduralTextures.Fabric, 4f),
            ProceduralCarFactory.GetMaterial(new Color(0.62f, 0.48f, 0.36f), 0.5f, 0f, ProceduralTextures.Wood, 1.5f),
            ProceduralCarFactory.GetMaterial(new Color(0.06f, 0.06f, 0.07f), 0.7f),
            ProceduralCarFactory.GetMaterial(new Color(1f, 1f, 1f), 0.08f, 0f, ProceduralTextures.Cardboard, 3f),
            ProceduralCarFactory.GetMaterial(new Color(0.70f, 0.36f, 0.22f), 0.15f),
            ProceduralCarFactory.GetMaterial(new Color(0.18f, 0.42f, 0.17f), 0.25f),
            ProceduralCarFactory.GetMaterial(new Color(0.25f, 0.25f, 0.27f), 0.7f, 0.9f),
            ProceduralCarFactory.GetMaterial(new Color(0.95f, 0.90f, 0.78f), 0.1f)
        };
    }

    private static void CreateRenderer(Transform parent, string name, Mesh mesh, Material[] materials, bool castShadows)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = materials;
        renderer.shadowCastingMode = castShadows
            ? UnityEngine.Rendering.ShadowCastingMode.On
            : UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = true;
    }

    /// <summary>Box from y0 to y1 with the given floor half size, rotated by angle (heading convention).</summary>
    private static void Box(RaceTrack.FlatMeshBuilder b, int submesh, Vector2 center, float angle, float y0, float y1, Vector2 half)
    {
        var right = CarCollision.Right(angle);
        var forward = CarCollision.Forward(angle);
        b.AddBoxAxes(submesh, new Vector3(center.x, (y0 + y1) * 0.5f, center.y),
            new Vector3(right.x, 0f, right.y) * half.x, Vector3.up * ((y1 - y0) * 0.5f),
            new Vector3(forward.x, 0f, forward.y) * half.y);
    }

    private static void AddCrate(RaceTrack.FlatMeshBuilder b, Info info, Vector2 center, float angle, Vector2 half, float height)
    {
        Box(b, Cardboard, center, angle, 0f, height, half);
        Box(b, Trim, center, angle, height, height + 0.002f, new Vector2(half.x, 0.025f));
        info.Obstacles.Add(WorldObstacle.Box(center, half + new Vector2(0.005f, 0.005f), angle));
    }

    private static void AddUprightCylinder(ProceduralCarModel.MeshBuilder b, int submesh, Vector2 at, float y0, float y1,
        float bottomRadius, float topRadius)
    {
        var matrix = Matrix4x4.TRS(new Vector3(at.x, 0f, at.y), Quaternion.Euler(0f, 0f, 90f), Vector3.one);
        b.AddLathe(submesh, new[]
        {
            new Vector2(y0, 0.0001f), new Vector2(y0, bottomRadius), new Vector2(y1, topRadius), new Vector2(y1, 0.0001f)
        }, 24, matrix);
    }

    private static Vector2 Rotate(Vector2 v, float angle) =>
        CarCollision.Right(angle) * v.x + CarCollision.Forward(angle) * v.y;
}
