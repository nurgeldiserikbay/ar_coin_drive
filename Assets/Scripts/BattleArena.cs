using UnityEngine;

/// <summary>
/// A round toy sumo ring for the push-out battle. The mesh is built at radius 1 and the
/// transform is scaled to the current radius, so the ring can shrink during a round.
/// </summary>
public static class BattleArena
{
    private const int Segments = 96;

    private const int Clay = 0;
    private const int Line = 1;
    private const int RimRed = 2;
    private const int RimWhite = 3;
    private const int Side = 4;

    private static Mesh _mesh;

    public static Transform Create(Transform parent)
    {
        var ring = new GameObject("Battle Ring");
        ring.transform.SetParent(parent, false);
        ring.AddComponent<MeshFilter>().sharedMesh = GetMesh();
        var ringRenderer = ring.AddComponent<MeshRenderer>();
        ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ringRenderer.sharedMaterials = new[]
        {
            ProceduralCarFactory.GetMaterial(new Color(0.30f, 0.30f, 0.32f), 0.12f, 0f, ProceduralTextures.Asphalt, 1f),
            ProceduralCarFactory.GetMaterial(new Color(0.88f, 0.88f, 0.85f), 0.2f),
            ProceduralCarFactory.GetMaterial(new Color(0.70f, 0.10f, 0.08f), 0.3f),
            ProceduralCarFactory.GetMaterial(new Color(0.86f, 0.86f, 0.84f), 0.3f),
            ProceduralCarFactory.GetMaterial(new Color(0.07f, 0.07f, 0.08f), 0.2f)
        };
        return ring.transform;
    }

    public static void SetRadius(Transform ring, float radius)
    {
        if (ring != null)
            ring.localScale = new Vector3(radius, 1f, radius);
    }

    private static Mesh GetMesh()
    {
        if (_mesh != null) return _mesh;

        const float top = 0.004f;
        var builder = new RaceTrack.FlatMeshBuilder(5, 3f);
        for (var i = 0; i < Segments; i++)
        {
            builder.AddTriangle(Clay, new Vector3(0f, top, 0f), Rim(i, 0.955f, top), Rim(i + 1, 0.955f, top), Vector3.up);
            AddBand(builder, i % 4 < 2 ? RimRed : RimWhite, i, 0.955f, 1f, top + 0.0012f);
            AddBand(builder, Line, i, 0.235f, 0.25f, top + 0.0008f);

            // A low skirt so the ring reads as a raised platform on the floor.
            var a = Rim(i, 1f, top + 0.0012f);
            var b = Rim(i + 1, 1f, top + 0.0012f);
            var outward = Rim(i, 1f, 0f);
            builder.AddQuad(Side, Rim(i, 1f, -0.002f), Rim(i + 1, 1f, -0.002f), b, a,
                new Vector3(outward.x, 0f, outward.z));
        }

        // Starting lines where each car lines up.
        foreach (var z in new[] { -0.47f, 0.47f })
        {
            builder.AddQuad(Line, new Vector3(-0.16f, top + 0.0008f, z - 0.012f), new Vector3(0.16f, top + 0.0008f, z - 0.012f),
                new Vector3(0.16f, top + 0.0008f, z + 0.012f), new Vector3(-0.16f, top + 0.0008f, z + 0.012f), Vector3.up);
        }

        _mesh = builder.Build("Battle Ring");
        return _mesh;
    }

    private static void AddBand(RaceTrack.FlatMeshBuilder builder, int submesh, int index, float inner, float outer, float height)
    {
        builder.AddQuad(submesh, Rim(index, inner, height), Rim(index, outer, height),
            Rim(index + 1, outer, height), Rim(index + 1, inner, height), Vector3.up);
    }

    private static Vector3 Rim(int index, float radius, float height)
    {
        var angle = index / (float)Segments * Mathf.PI * 2f;
        return new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius);
    }
}
