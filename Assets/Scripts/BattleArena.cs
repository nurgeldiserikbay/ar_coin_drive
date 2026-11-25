using UnityEngine;

public enum ArenaShape { Circle, Square }

/// <summary>
/// The push-out battle platform: a round ring or a rounded square, in three sizes. The mesh is
/// built at unit size and the transform is scaled to the current size, so it can shrink during
/// a round. Both shapes are superellipses, so "is this car out?" is one cheap norm.
/// </summary>
public static class BattleArena
{
    public static readonly float[] SizeRadius = { 0.62f, 1.0f, 1.45f };
    public static readonly string[] SizeNames = { "S", "M", "L" };
    private const int Segments = 128;
    private const float SquareExponent = 8f;

    private const int Clay = 0;
    private const int Line = 1;
    private const int RimRed = 2;
    private const int RimWhite = 3;
    private const int Side = 4;

    private static readonly Mesh[] Meshes = new Mesh[2];

    public static Transform Create(Transform parent, ArenaShape shape)
    {
        var ring = new GameObject("Battle Arena");
        ring.transform.SetParent(parent, false);
        ring.AddComponent<MeshFilter>().sharedMesh = GetMesh(shape);
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

    /// <summary>Distance-like measure that equals the radius on the arena edge.</summary>
    public static float Norm(Vector2 position, ArenaShape shape)
    {
        if (shape == ArenaShape.Circle) return position.magnitude;
        var x = Mathf.Abs(position.x);
        var z = Mathf.Abs(position.y);
        return Mathf.Pow(Mathf.Pow(x, SquareExponent) + Mathf.Pow(z, SquareExponent), 1f / SquareExponent);
    }

    private static Mesh GetMesh(ArenaShape shape)
    {
        var index = (int)shape;
        if (Meshes[index] != null) return Meshes[index];

        const float top = 0.004f;
        var builder = new RaceTrack.FlatMeshBuilder(5, 3f);
        for (var i = 0; i < Segments; i++)
        {
            builder.AddTriangle(Clay, new Vector3(0f, top, 0f), Rim(shape, i, 0.96f, top), Rim(shape, i + 1, 0.96f, top), Vector3.up);
            AddBand(builder, shape, i % 6 < 3 ? RimRed : RimWhite, i, 0.96f, 1f, top + 0.0012f);
            AddBand(builder, shape, Line, i, 0.235f, 0.25f, top + 0.0008f);

            // A low skirt so the arena reads as a raised platform on the floor.
            var a = Rim(shape, i, 1f, top + 0.0012f);
            var b = Rim(shape, i + 1, 1f, top + 0.0012f);
            var outward = Rim(shape, i, 1f, 0f);
            builder.AddQuad(Side, Rim(shape, i, 1f, -0.002f), Rim(shape, i + 1, 1f, -0.002f), b, a, new Vector3(outward.x, 0f, outward.z));
        }

        Meshes[index] = builder.Build("Battle Arena " + shape);
        return Meshes[index];
    }

    private static void AddBand(RaceTrack.FlatMeshBuilder builder, ArenaShape shape, int submesh, int index, float inner, float outer, float height)
    {
        builder.AddQuad(submesh, Rim(shape, index, inner, height), Rim(shape, index, outer, height),
            Rim(shape, index + 1, outer, height), Rim(shape, index + 1, inner, height), Vector3.up);
    }

    private static Vector3 Rim(ArenaShape shape, int index, float radius, float height)
    {
        var angle = index / (float)Segments * Mathf.PI * 2f;
        var c = Mathf.Cos(angle);
        var s = Mathf.Sin(angle);
        if (shape == ArenaShape.Square)
        {
            const float power = 2f / SquareExponent;
            c = Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), power);
            s = Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), power);
        }

        return new Vector3(c * radius, height, s * radius);
    }
}
