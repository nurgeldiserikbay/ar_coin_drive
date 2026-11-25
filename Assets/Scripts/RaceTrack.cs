using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A closed RC racetrack in arena-local metres (x, z on the floor plane): a textured asphalt
/// mat with red/white curbs, an astroturf run-off strip, foam-tube barriers and a start gantry.
/// It has no infield, so in AR the real floor shows through the middle. It also answers
/// "where is the car on the track?".
/// </summary>
public sealed class RaceTrack
{
    public const float RoadHalfWidth = 0.125f;
    public const float RailOffset = 0.178f;
    public const float TubeRadius = 0.011f;

    private const int SampleCount = 180;
    private const float CurbWidth = 0.016f;

    private const int Road = 0;
    private const int CurbRed = 1;
    private const int CurbWhite = 2;
    private const int Line = 3;
    private const int CheckerDark = 4;
    private const int TubeDark = 5;
    private const int TubeLight = 6;
    private const int Turf = 7;
    private const int Gantry = 8;
    private const int Banner = 9;
    private const int SubmeshCount = 10;

    private static RaceTrack _shared;
    private static Mesh _mesh;

    private readonly Vector2[] _center = new Vector2[SampleCount];
    private readonly Vector2[] _tangent = new Vector2[SampleCount];
    private readonly Vector2[] _outward = new Vector2[SampleCount];

    public static RaceTrack Shared => _shared ?? (_shared = new RaceTrack());

    /// <summary>Starting grid just behind the line: slot 0 on the inside, slot 1 on the outside.</summary>
    public Vector2 GridPosition(int slot) =>
        _center[0] - _tangent[0] * 0.15f + _outward[0] * (slot == 0 ? -0.058f : 0.058f);

    /// <summary>How far around the lap a position is, from 0 at the start line to 1.</summary>
    public float Progress(Vector2 position) => NearestIndex(position) / (float)SampleCount;

    public float StartHeading => Mathf.Atan2(_tangent[0].x, _tangent[0].y) * Mathf.Rad2Deg;

    private RaceTrack()
    {
        for (var i = 0; i < SampleCount; i++)
            _center[i] = CenterAt(i / (float)SampleCount * Mathf.PI * 2f);

        for (var i = 0; i < SampleCount; i++)
        {
            var delta = _center[(i + 1) % SampleCount] - _center[(i + SampleCount - 1) % SampleCount];
            _tangent[i] = delta.normalized;
            _outward[i] = new Vector2(_tangent[i].y, -_tangent[i].x);
        }
    }

    private static Vector2 CenterAt(float angle)
    {
        var radius = 0.43f + 0.085f * Mathf.Cos(2f * angle + 0.6f) + 0.045f * Mathf.Sin(3f * angle);
        return new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
    }

    /// <summary>Point at a lap fraction (0..1), offset sideways from the centre line.</summary>
    public Vector2 PointAt(float lapFraction, float lateral)
    {
        var index = Mathf.RoundToInt(Mathf.Repeat(lapFraction, 1f) * SampleCount) % SampleCount;
        return _center[index] + _outward[index] * lateral;
    }

    /// <summary>Signed distance from the centre line (positive is the outer side).</summary>
    public float LateralOffset(Vector2 position)
    {
        var index = NearestIndex(position);
        return Vector2.Dot(position - _center[index], _outward[index]);
    }

    /// <summary>Keeps a position between the barriers. Returns true when a barrier was hit.</summary>
    public bool ConstrainToRails(ref Vector2 position, float carHalfWidth)
    {
        var index = NearestIndex(position);
        var lateral = Vector2.Dot(position - _center[index], _outward[index]);
        var limit = RailOffset - TubeRadius - carHalfWidth;
        if (Mathf.Abs(lateral) <= limit) return false;

        position = _center[index] + _tangent[index] * Vector2.Dot(position - _center[index], _tangent[index])
            + _outward[index] * (Mathf.Sign(lateral) * limit);
        return true;
    }

    private int NearestIndex(Vector2 position)
    {
        var best = 0;
        var bestDistance = float.MaxValue;
        for (var i = 0; i < SampleCount; i++)
        {
            var distance = (position - _center[i]).sqrMagnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = i;
        }

        return best;
    }

    public GameObject CreateVisual(Transform parent)
    {
        var track = new GameObject("Race Track");
        track.transform.SetParent(parent, false);
        track.AddComponent<MeshFilter>().sharedMesh = GetMesh();
        var trackRenderer = track.AddComponent<MeshRenderer>();
        trackRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        trackRenderer.sharedMaterials = new[]
        {
            ProceduralCarFactory.GetMaterial(new Color(0.34f, 0.34f, 0.35f), 0.22f, 0f, ProceduralTextures.Asphalt, 1f),
            ProceduralCarFactory.GetMaterial(new Color(0.70f, 0.10f, 0.08f), 0.3f),
            ProceduralCarFactory.GetMaterial(new Color(0.86f, 0.86f, 0.84f), 0.3f),
            ProceduralCarFactory.GetMaterial(new Color(0.85f, 0.85f, 0.82f), 0.2f),
            ProceduralCarFactory.GetMaterial(new Color(0.05f, 0.05f, 0.06f), 0.2f),
            ProceduralCarFactory.GetMaterial(new Color(0.06f, 0.06f, 0.065f), 0.25f),
            ProceduralCarFactory.GetMaterial(new Color(0.88f, 0.88f, 0.86f), 0.3f),
            ProceduralCarFactory.GetMaterial(new Color(0.20f, 0.36f, 0.16f), 0.05f, 0f, ProceduralTextures.Fabric, 1f),
            ProceduralCarFactory.GetMaterial(new Color(0.62f, 0.64f, 0.67f), 0.6f, 0.85f),
            ProceduralCarFactory.GetMaterial(new Color(0.08f, 0.09f, 0.11f), 0.4f)
        };
        return track;
    }

    private Mesh GetMesh()
    {
        if (_mesh != null) return _mesh;

        var builder = new FlatMeshBuilder(SubmeshCount, 4f);
        for (var i = 0; i < SampleCount; i++)
        {
            AddBand(builder, Road, i, -RoadHalfWidth, RoadHalfWidth, 0.003f);

            var curb = i / 3 % 2 == 0 ? CurbRed : CurbWhite;
            AddBand(builder, curb, i, RoadHalfWidth, RoadHalfWidth + CurbWidth, 0.0042f);
            AddBand(builder, curb, i, -RoadHalfWidth - CurbWidth, -RoadHalfWidth, 0.0042f);
            AddBand(builder, Turf, i, RoadHalfWidth + CurbWidth, RailOffset + 0.02f, 0.002f);
            AddBand(builder, Turf, i, -RailOffset - 0.02f, -RoadHalfWidth - CurbWidth, 0.002f);

            if (i / 4 % 2 == 0 && i > 8 && i < SampleCount - 2)
                AddBand(builder, Line, i, -0.003f, 0.003f, 0.0036f);

            var sleeve = i / 6 % 4 == 0 ? TubeLight : TubeDark;
            AddTube(builder, sleeve, i, RailOffset);
            AddTube(builder, sleeve, i, -RailOffset);
        }

        AddStartLine(builder);
        AddGantry(builder);
        _mesh = builder.Build("Race Track");
        return _mesh;
    }

    private Vector3 Ground(int index, float lateral, float height)
    {
        index = (index % SampleCount + SampleCount) % SampleCount;
        var point = _center[index] + _outward[index] * lateral;
        return new Vector3(point.x, height, point.y);
    }

    private void AddBand(FlatMeshBuilder builder, int submesh, int index, float from, float to, float height)
    {
        var next = (index + 1) % SampleCount;
        builder.AddQuad(submesh, Ground(index, from, height), Ground(index, to, height),
            Ground(next, to, height), Ground(next, from, height), Vector3.up);
    }

    /// <summary>One segment of a foam barrier tube lying along the track edge.</summary>
    private void AddTube(FlatMeshBuilder builder, int submesh, int index, float lateral)
    {
        const int sides = 8;
        var next = (index + 1) % SampleCount;
        for (var s = 0; s < sides; s++)
        {
            var a0 = s / (float)sides * Mathf.PI * 2f;
            var a1 = (s + 1) / (float)sides * Mathf.PI * 2f;
            builder.AddQuadSmooth(submesh,
                TubePoint(index, lateral, a0, out var n0), TubePoint(next, lateral, a0, out var n1),
                TubePoint(next, lateral, a1, out var n2), TubePoint(index, lateral, a1, out var n3),
                n0, n1, n2, n3);
        }
    }

    private Vector3 TubePoint(int index, float lateral, float angle, out Vector3 normal)
    {
        var outward = new Vector3(_outward[index].x, 0f, _outward[index].y);
        normal = outward * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle);
        return Ground(index, lateral, TubeRadius) + normal * TubeRadius;
    }

    private void AddStartLine(FlatMeshBuilder builder)
    {
        const int across = 10;
        const int along = 2;
        const float height = 0.0045f;
        var cell = RoadHalfWidth * 2f / across;
        var center = _center[0];
        var tangent = _tangent[0];
        var outward = _outward[0];

        for (var row = 0; row < along; row++)
        {
            for (var column = 0; column < across; column++)
            {
                var submesh = (row + column) % 2 == 0 ? CheckerDark : CurbWhite;
                var lateralFrom = -RoadHalfWidth + column * cell;
                var alongFrom = row * cell;
                builder.AddQuad(submesh,
                    Flat(center, tangent, outward, alongFrom, lateralFrom, height),
                    Flat(center, tangent, outward, alongFrom, lateralFrom + cell, height),
                    Flat(center, tangent, outward, alongFrom + cell, lateralFrom + cell, height),
                    Flat(center, tangent, outward, alongFrom + cell, lateralFrom, height),
                    Vector3.up);
            }
        }
    }

    /// <summary>Start/finish gantry: two posts outside the barriers and a banner over the line.</summary>
    private void AddGantry(FlatMeshBuilder builder)
    {
        var center = _center[0];
        var tangent = _tangent[0];
        var outward = _outward[0];
        var span = RailOffset + 0.035f;
        const float height = 0.20f;
        const float post = 0.006f;
        foreach (var side in new[] { -1f, 1f })
            builder.AddBox(Gantry, Flat(center, tangent, outward, 0.012f, side * span, height * 0.5f),
                new Vector3(post, height, post), tangent, outward);

        builder.AddBox(Banner, Flat(center, tangent, outward, 0.012f, 0f, height - 0.018f),
            new Vector3(span * 2f + 0.012f, 0.036f, 0.004f), tangent, outward);
        builder.AddBox(Gantry, Flat(center, tangent, outward, 0.012f, 0f, height + 0.002f),
            new Vector3(span * 2f + 0.014f, 0.005f, 0.008f), tangent, outward);
    }

    private static Vector3 Flat(Vector2 center, Vector2 tangent, Vector2 outward, float along, float lateral, float height)
    {
        var point = center + tangent * along + outward * lateral;
        return new Vector3(point.x, height, point.y);
    }

    /// <summary>
    /// Mesh builder with explicit normals and planar UVs (x, z in metres times uvScale), so
    /// tiled textures keep a constant real-world size across the track.
    /// </summary>
    internal sealed class FlatMeshBuilder
    {
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<int>[] _triangles;
        private readonly float _uvScale;

        public FlatMeshBuilder(int submeshCount, float uvScale = 1f)
        {
            _uvScale = uvScale;
            _triangles = new List<int>[submeshCount];
            for (var i = 0; i < submeshCount; i++)
                _triangles[i] = new List<int>();
        }

        public void AddTriangle(int submesh, Vector3 a, Vector3 b, Vector3 c, Vector3 normal) =>
            AddTriangleSmooth(submesh, a, b, c, normal, normal, normal);

        private void AddTriangleSmooth(int submesh, Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), na + nb + nc) < 0f)
            {
                var swap = b;
                b = c;
                c = swap;
                var swapNormal = nb;
                nb = nc;
                nc = swapNormal;
            }

            var start = _vertices.Count;
            Add(a, na);
            Add(b, nb);
            Add(c, nc);
            _triangles[submesh].Add(start);
            _triangles[submesh].Add(start + 1);
            _triangles[submesh].Add(start + 2);
        }

        private void Add(Vector3 position, Vector3 normal)
        {
            _vertices.Add(position);
            _normals.Add(normal);
            // Planar mapping chosen by the dominant normal axis.
            var absolute = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
            var uv = absolute.y >= absolute.x && absolute.y >= absolute.z ? new Vector2(position.x, position.z)
                : absolute.x >= absolute.z ? new Vector2(position.z, position.y) : new Vector2(position.x, position.y);
            _uvs.Add(uv * _uvScale);
        }

        public void AddQuad(int submesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            AddTriangle(submesh, a, b, c, normal);
            AddTriangle(submesh, a, c, d, normal);
        }

        public void AddQuadSmooth(int submesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d,
            Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd)
        {
            AddTriangleSmooth(submesh, a, b, c, na, nb, nc);
            AddTriangleSmooth(submesh, a, c, d, na, nc, nd);
        }

        /// <summary>Box whose local x runs along `lateralAxis`, z along `alongAxis` (floor directions).</summary>
        public void AddBox(int submesh, Vector3 center, Vector3 size, Vector2 alongAxis, Vector2 lateralAxis)
        {
            var x = new Vector3(lateralAxis.x, 0f, lateralAxis.y) * (size.x * 0.5f);
            var y = Vector3.up * (size.y * 0.5f);
            var z = new Vector3(alongAxis.x, 0f, alongAxis.y) * (size.z * 0.5f);
            AddBoxAxes(submesh, center, x, y, z);
        }

        public void AddBoxAxes(int submesh, Vector3 center, Vector3 x, Vector3 y, Vector3 z)
        {
            AddBoxFace(submesh, center + x, y, z);
            AddBoxFace(submesh, center - x, y, z);
            AddBoxFace(submesh, center + y, x, z);
            AddBoxFace(submesh, center - y, x, z);
            AddBoxFace(submesh, center + z, x, y);
            AddBoxFace(submesh, center - z, x, y);

            void AddBoxFace(int sm, Vector3 faceCenter, Vector3 u, Vector3 v)
            {
                var normal = (faceCenter - center).normalized;
                AddQuad(sm, faceCenter - u - v, faceCenter - u + v, faceCenter + u + v, faceCenter + u - v, normal);
            }
        }

        public Mesh Build(string meshName)
        {
            var mesh = new Mesh { name = meshName };
            mesh.indexFormat = _vertices.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uvs);
            mesh.subMeshCount = _triangles.Length;
            for (var i = 0; i < _triangles.Length; i++)
                mesh.SetTriangles(_triangles[i], i);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }
    }
}
