using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A closed toy racetrack in arena-local metres (x, z on the floor plane). It draws the same
/// diorama in AR and on the virtual table, and answers "where is the car on the track?".
/// </summary>
public sealed class RaceTrack
{
    public const float RoadHalfWidth = 0.125f;
    public const float RailOffset = 0.178f;

    private const int SampleCount = 180;
    private const float RailHeight = 0.020f;
    private const float RailThickness = 0.010f;

    private const int Grass = 0;
    private const int Road = 1;
    private const int CurbRed = 2;
    private const int CurbWhite = 3;
    private const int Line = 4;
    private const int CheckerDark = 5;
    private const int Rail = 6;
    private const int SubmeshCount = 7;

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

    /// <summary>Keeps a position between the guard rails. Returns true when a rail was hit.</summary>
    public bool ConstrainToRails(ref Vector2 position, float carHalfWidth)
    {
        var index = NearestIndex(position);
        var lateral = Vector2.Dot(position - _center[index], _outward[index]);
        var limit = RailOffset - RailThickness - carHalfWidth;
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
        trackRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trackRenderer.sharedMaterials = new[]
        {
            ProceduralCarFactory.GetMaterial(new Color(0.33f, 0.68f, 0.30f), 0.04f),
            ProceduralCarFactory.GetMaterial(new Color(0.20f, 0.21f, 0.26f), 0.10f),
            ProceduralCarFactory.GetMaterial(new Color(0.93f, 0.16f, 0.14f), 0.25f),
            ProceduralCarFactory.GetMaterial(new Color(0.98f, 0.97f, 0.93f), 0.25f),
            ProceduralCarFactory.GetMaterial(new Color(0.99f, 0.86f, 0.36f), 0.15f),
            ProceduralCarFactory.GetMaterial(new Color(0.06f, 0.06f, 0.08f), 0.15f),
            ProceduralCarFactory.GetMaterial(new Color(0.10f, 0.52f, 0.90f), 0.55f)
        };
        return track;
    }

    private Mesh GetMesh()
    {
        if (_mesh != null) return _mesh;

        var builder = new FlatMeshBuilder(SubmeshCount);
        var groundEdge = RailOffset + 0.012f;
        for (var i = 0; i < SampleCount; i++)
        {
            var next = (i + 1) % SampleCount;
            builder.AddTriangle(Grass, new Vector3(0f, 0.001f, 0f), Ground(i, groundEdge, 0.001f),
                Ground(next, groundEdge, 0.001f), Vector3.up);
            AddBand(builder, Road, i, -RoadHalfWidth, RoadHalfWidth, 0.003f);

            var curb = i / 3 % 2 == 0 ? CurbRed : CurbWhite;
            AddBand(builder, curb, i, RoadHalfWidth, RoadHalfWidth + 0.016f, 0.0045f);
            AddBand(builder, curb, i, -RoadHalfWidth - 0.016f, -RoadHalfWidth, 0.0045f);

            if (i / 4 % 2 == 0 && i > 8 && i < SampleCount - 2)
                AddBand(builder, Line, i, -0.0035f, 0.0035f, 0.0042f);

            AddRail(builder, i, RailOffset, -1f);
            AddRail(builder, i, -RailOffset, 1f);
        }

        AddStartLine(builder);
        _mesh = builder.Build("Race Track");
        return _mesh;
    }

    private Vector3 Ground(int index, float lateral, float height)
    {
        var point = _center[index] + _outward[index] * lateral;
        return new Vector3(point.x, height, point.y);
    }

    private void AddBand(FlatMeshBuilder builder, int submesh, int index, float from, float to, float height)
    {
        var next = (index + 1) % SampleCount;
        builder.AddQuad(submesh, Ground(index, from, height), Ground(index, to, height),
            Ground(next, to, height), Ground(next, from, height), Vector3.up);
    }

    private void AddRail(FlatMeshBuilder builder, int index, float lateral, float faceSign)
    {
        var next = (index + 1) % SampleCount;
        var inner = lateral + faceSign * RailThickness * 0.5f;
        var outer = lateral - faceSign * RailThickness * 0.5f;
        var a = Ground(index, inner, 0f);
        var b = Ground(next, inner, 0f);
        var c = Ground(next, inner, RailHeight);
        var d = Ground(index, inner, RailHeight);
        var normal = new Vector3(_outward[index].x, 0f, _outward[index].y) * faceSign;
        builder.AddQuad(Rail, a, b, c, d, normal);
        builder.AddQuad(Rail, d, c, Ground(next, outer, RailHeight), Ground(index, outer, RailHeight), Vector3.up);
        builder.AddQuad(Rail, Ground(index, outer, 0f), Ground(next, outer, 0f),
            Ground(next, outer, RailHeight), Ground(index, outer, RailHeight), -normal);
    }

    private void AddStartLine(FlatMeshBuilder builder)
    {
        const int across = 10;
        const int along = 2;
        const float height = 0.0052f;
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
                    Checker(center, tangent, outward, alongFrom, lateralFrom, height),
                    Checker(center, tangent, outward, alongFrom, lateralFrom + cell, height),
                    Checker(center, tangent, outward, alongFrom + cell, lateralFrom + cell, height),
                    Checker(center, tangent, outward, alongFrom + cell, lateralFrom, height),
                    Vector3.up);
            }
        }
    }

    private static Vector3 Checker(Vector2 center, Vector2 tangent, Vector2 outward, float along, float lateral, float height)
    {
        var point = center + tangent * along + outward * lateral;
        return new Vector3(point.x, height, point.y);
    }

    internal sealed class FlatMeshBuilder
    {
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<int>[] _triangles;

        public FlatMeshBuilder(int submeshCount)
        {
            _triangles = new List<int>[submeshCount];
            for (var i = 0; i < submeshCount; i++)
                _triangles[i] = new List<int>();
        }

        public void AddTriangle(int submesh, Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f)
            {
                var swap = b;
                b = c;
                c = swap;
            }

            var start = _vertices.Count;
            _vertices.Add(a);
            _vertices.Add(b);
            _vertices.Add(c);
            _normals.Add(normal);
            _normals.Add(normal);
            _normals.Add(normal);
            _triangles[submesh].Add(start);
            _triangles[submesh].Add(start + 1);
            _triangles[submesh].Add(start + 2);
        }

        public void AddQuad(int submesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            AddTriangle(submesh, a, b, c, normal);
            AddTriangle(submesh, a, c, d, normal);
        }

        public Mesh Build(string meshName)
        {
            var mesh = new Mesh { name = meshName };
            mesh.indexFormat = _vertices.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.subMeshCount = _triangles.Length;
            for (var i = 0; i < _triangles.Length; i++)
                mesh.SetTriangles(_triangles[i], i);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }
    }
}
