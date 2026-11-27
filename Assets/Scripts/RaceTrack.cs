using System.Collections.Generic;
using UnityEngine;

public enum TrackLayout { Classic, Kidney, Hairpin, Stadium, Twister, Serpent, Delta, Chicane }
public enum TrackSize { S, M, L }

/// <summary>
/// A closed RC racetrack in arena-local metres (x, z on the floor plane): a textured asphalt
/// mat with red/white curbs, an astroturf run-off strip, foam-tube barriers and a start gantry.
/// Five layouts in three sizes; bigger tracks get a wider road. It has no infield, so in AR the
/// real floor shows through the middle. It also answers "where is the car on the track?"
/// in constant time through a precomputed nearest-segment grid.
/// </summary>
public sealed class RaceTrack
{
    public const float TubeRadius = 0.011f;
    public static readonly string[] LayoutNames = { "Classic", "Kidney", "Hairpins", "Stadium", "Twister", "Serpent", "Delta", "Chicane" };
    // Floor square per size (half side, metres). L fits the clear middle of the virtual living room.
    public static readonly float[] SizeHalfExtent = { 1.0f, 1.35f, 1.7f };
    // Wanted road half widths (S, M, L). Tight layouts get the widest road their barriers allow,
    // but never less than MinRoadHalfWidth.
    private static readonly float[] SizeRoadHalfWidth = { 0.22f, 0.23f, 0.24f };
    // 40 cm: four in-game cars still fit side by side.
    private const float MinRoadHalfWidth = 0.20f;
    // Curb plus astroturf run-off between the road edge and the foam tube.
    private const float RunOffWidth = 0.038f;
    // Bare floor kept between the tubes of two neighbouring parts of the track.
    public const float MinBarrierGap = 0.02f;

    private const float SampleSpacing = 0.025f;
    private const float GridCell = 0.08f;
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

    private static readonly Dictionary<int, RaceTrack> Cache = new Dictionary<int, RaceTrack>();
    private static RaceTrack _current;

    private readonly Vector2[] _center;
    private readonly Vector2[] _tangent;
    private readonly Vector2[] _outward;
    private readonly int _count;
    private readonly int[] _grid;
    private readonly int _gridWidth;
    private readonly int _gridHeight;
    private readonly Vector2 _gridOrigin;
    private Mesh _mesh;

    public TrackLayout Layout { get; }
    public TrackSize Size { get; }
    public float RoadHalfWidth { get; }
    public float RailOffset { get; }
    /// <summary>Everything the track covers, rails included, plus a small margin.</summary>
    public Rect Bounds { get; }
    public float Length { get; }
    private int SampleCount => _count;

    /// <summary>The track in use (set by the game when the player picks one).</summary>
    public static RaceTrack Shared => _current ?? (_current = Get(TrackLayout.Classic, TrackSize.S));

    public static RaceTrack Select(TrackLayout layout, TrackSize size) => _current = Get(layout, size);

    /// <summary>
    /// Smallest size at which the layout keeps a road at least MinRoadHalfWidth wide with its barriers
    /// apart (layouts with many parallel runs need more floor).
    /// </summary>
    public static TrackSize MinSize(TrackLayout layout)
    {
        for (var size = TrackSize.S; size < TrackSize.L; size++)
            if (Build(layout, size).BarriersClear) return size;
        return TrackSize.L;
    }

    public static RaceTrack Get(TrackLayout layout, TrackSize size)
    {
        var smallest = MinSize(layout);
        return Build(layout, size < smallest ? smallest : size);
    }

    private static RaceTrack Build(TrackLayout layout, TrackSize size)
    {
        var key = (int)layout * 10 + (int)size;
        if (!Cache.TryGetValue(key, out var track))
        {
            track = new RaceTrack(layout, size);
            Cache[key] = track;
        }

        return track;
    }

    /// <summary>True when the barriers of neighbouring parts of the track keep MinBarrierGap apart.</summary>
    public bool BarriersClear { get; }

    /// <summary>Centre line points (for the menu's mini map).</summary>
    public IReadOnlyList<Vector2> CenterLine => _center;

    /// <summary>Starting grid behind the line: up to four cars side by side across the road.</summary>
    public Vector2 GridPosition(int slot)
    {
        var lane = (RoadHalfWidth * 2f - 0.02f) / 4f;
        return _center[0] - _tangent[0] * (0.15f + (slot / 4) * 0.24f) + _outward[0] * (((slot % 4) - 1.5f) * lane);
    }

    /// <summary>How far around the lap a position is, from 0 at the start line to 1.</summary>
    public float Progress(Vector2 position) => NearestIndex(position) / (float)SampleCount;

    public float StartHeading => Mathf.Atan2(_tangent[0].x, _tangent[0].y) * Mathf.Rad2Deg;

    private RaceTrack(TrackLayout layout, TrackSize size)
    {
        Layout = layout;
        Size = size;
        // Smooth closed curve through the layout's control points, normalised to the size.
        var raw = DenseCurve(ControlPoints(layout), 2000);
        var max = 0f;
        foreach (var p in raw) max = Mathf.Max(max, Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)));

        var roadHalf = SizeRoadHalfWidth[(int)size];
        for (var attempt = 0; ; attempt++)
        {
            RoadHalfWidth = roadHalf;
            RailOffset = roadHalf + RunOffWidth;
            var scale = (SizeHalfExtent[(int)size] - RailOffset - 0.03f) / max;

            // Resample at an even spacing so speed and progress are uniform around the lap.
            var length = 0f;
            for (var i = 0; i < raw.Count; i++) length += (raw[(i + 1) % raw.Count] - raw[i]).magnitude * scale;
            Length = length;
            _count = Mathf.Max(60, Mathf.RoundToInt(length / SampleSpacing));
            _center = new Vector2[_count];
            _tangent = new Vector2[_count];
            _outward = new Vector2[_count];
            var step = length / _count;
            var travelled = 0f;
            var target = 0f;
            var k = 0;
            for (var i = 0; i < raw.Count && k < _count; i++)
            {
                var a = raw[i] * scale;
                var b = raw[(i + 1) % raw.Count] * scale;
                var segment = (b - a).magnitude;
                while (target <= travelled + segment && k < _count)
                {
                    _center[k++] = Vector2.Lerp(a, b, segment > 0f ? (target - travelled) / segment : 0f);
                    target += step;
                }

                travelled += segment;
            }

            for (; k < _count; k++) _center[k] = _center[k - 1];
            for (var i = 0; i < _count; i++)
            {
                var delta = _center[(i + 1) % _count] - _center[(i + _count - 1) % _count];
                _tangent[i] = delta.normalized;
                _outward[i] = new Vector2(_tangent[i].y, -_tangent[i].x);
            }

            // Narrow the road until the barriers of neighbouring parts of the track keep apart.
            var fits = (ClosestApproach() - MinBarrierGap) * 0.5f - TubeRadius - RunOffWidth;
            if (roadHalf <= fits + 0.0005f || roadHalf <= MinRoadHalfWidth || attempt >= 5) break;
            roadHalf = Mathf.Max(MinRoadHalfWidth, fits - 0.002f);
        }

        BarriersClear = ClosestApproach() >= 2f * (RailOffset + TubeRadius) + MinBarrierGap;

        var extent = SizeHalfExtent[(int)size];
        Bounds = Rect.MinMaxRect(-extent, -extent, extent, extent);

        // Nearest-sample lookup grid: one brute-force pass at build time, O(1) queries after.
        _gridOrigin = new Vector2(-extent - GridCell, -extent - GridCell);
        _gridWidth = Mathf.CeilToInt((extent * 2f + GridCell * 2f) / GridCell);
        _gridHeight = _gridWidth;
        _grid = new int[_gridWidth * _gridHeight];
        for (var z = 0; z < _gridHeight; z++)
        {
            for (var x = 0; x < _gridWidth; x++)
            {
                var cell = _gridOrigin + new Vector2((x + 0.5f) * GridCell, (z + 0.5f) * GridCell);
                _grid[z * _gridWidth + x] = BruteNearest(cell);
            }
        }
    }

    private static Vector2[] ControlPoints(TrackLayout layout)
    {
        switch (layout)
        {
            case TrackLayout.Kidney:
                return new[]
                {
                    new Vector2(-0.85f, -0.2f), new Vector2(-0.65f, -0.8f), new Vector2(0.1f, -0.92f), new Vector2(0.8f, -0.62f),
                    new Vector2(0.93f, 0.12f), new Vector2(0.62f, 0.8f), new Vector2(0.15f, 0.48f), new Vector2(-0.32f, 0.85f),
                    new Vector2(-0.82f, 0.55f)
                };
            case TrackLayout.Hairpin:
                return new[]
                {
                    new Vector2(-0.88f, -0.88f), new Vector2(0.80f, -0.88f), new Vector2(0.97f, -0.62f), new Vector2(0.80f, -0.34f),
                    new Vector2(-0.30f, -0.26f), new Vector2(-0.50f, 0.0f), new Vector2(-0.30f, 0.26f), new Vector2(0.80f, 0.34f),
                    new Vector2(0.97f, 0.62f), new Vector2(0.80f, 0.88f), new Vector2(-0.88f, 0.88f), new Vector2(-0.98f, 0.0f)
                };
            case TrackLayout.Stadium:
                return new[]
                {
                    new Vector2(-0.85f, -0.58f), new Vector2(0f, -0.62f), new Vector2(0.85f, -0.58f), new Vector2(1.0f, 0f),
                    new Vector2(0.85f, 0.58f), new Vector2(0f, 0.62f), new Vector2(-0.85f, 0.58f), new Vector2(-1.0f, 0f)
                };
            case TrackLayout.Twister:
                return new[]
                {
                    new Vector2(-0.88f, -0.88f), new Vector2(-0.2f, -0.72f), new Vector2(0.2f, -0.95f), new Vector2(0.86f, -0.78f),
                    new Vector2(0.58f, -0.28f), new Vector2(0.95f, 0.22f), new Vector2(0.72f, 0.86f), new Vector2(0.12f, 0.58f),
                    new Vector2(-0.32f, 0.92f), new Vector2(-0.86f, 0.70f), new Vector2(-0.52f, 0.12f), new Vector2(-0.95f, -0.32f)
                };
            case TrackLayout.Serpent:
                // A big loop with an S-bend notch cut into each side.
                return new[]
                {
                    new Vector2(-0.90f, -0.88f), new Vector2(0.90f, -0.88f), new Vector2(0.96f, -0.30f), new Vector2(0.38f, 0.0f),
                    new Vector2(0.96f, 0.30f), new Vector2(0.90f, 0.88f), new Vector2(-0.90f, 0.88f), new Vector2(-0.96f, 0.30f),
                    new Vector2(-0.38f, 0.0f), new Vector2(-0.96f, -0.30f)
                };
            case TrackLayout.Delta:
                // Rounded triangle: three long straights and three tight corners.
                return new[]
                {
                    new Vector2(-0.92f, -0.86f), new Vector2(0f, -0.90f), new Vector2(0.92f, -0.86f),
                    new Vector2(0.48f, 0.06f), new Vector2(0.02f, 0.94f), new Vector2(-0.46f, 0.06f)
                };
            case TrackLayout.Chicane:
                // Oval with a chicane in the middle of both straights.
                return new[]
                {
                    new Vector2(-0.90f, -0.66f), new Vector2(-0.28f, -0.66f), new Vector2(0f, -0.36f), new Vector2(0.28f, -0.66f),
                    new Vector2(0.90f, -0.66f), new Vector2(1.0f, 0f), new Vector2(0.90f, 0.66f), new Vector2(0.28f, 0.66f),
                    new Vector2(0f, 0.36f), new Vector2(-0.28f, 0.66f), new Vector2(-0.90f, 0.66f), new Vector2(-1.0f, 0f)
                };
            default:
            {
                var points = new Vector2[16];
                for (var i = 0; i < points.Length; i++)
                {
                    var angle = i / (float)points.Length * Mathf.PI * 2f;
                    var radius = 0.43f + 0.085f * Mathf.Cos(2f * angle + 0.6f) + 0.045f * Mathf.Sin(3f * angle);
                    points[i] = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
                }

                return points;
            }
        }
    }

    private static List<Vector2> DenseCurve(Vector2[] points, int samples)
    {
        var result = new List<Vector2>(samples);
        var n = points.Length;
        for (var s = 0; s < samples; s++)
        {
            var t = s / (float)samples * n;
            var i = Mathf.FloorToInt(t);
            var f = t - i;
            var p0 = points[(i - 1 + n) % n];
            var p1 = points[i % n];
            var p2 = points[(i + 1) % n];
            var p3 = points[(i + 2) % n];
            var f2 = f * f;
            var f3 = f2 * f;
            result.Add(0.5f * (2f * p1 + (p2 - p0) * f + (2f * p0 - 5f * p1 + 4f * p2 - p3) * f2 + (3f * p1 - p0 - 3f * p2 + p3) * f3));
        }

        return result;
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

    /// <summary>Grid lookup, then a short local search around the cell's answer.</summary>
    private int NearestIndex(Vector2 position)
    {
        var x = Mathf.Clamp(Mathf.FloorToInt((position.x - _gridOrigin.x) / GridCell), 0, _gridWidth - 1);
        var z = Mathf.Clamp(Mathf.FloorToInt((position.y - _gridOrigin.y) / GridCell), 0, _gridHeight - 1);
        var guess = _grid[z * _gridWidth + x];
        var best = guess;
        var bestDistance = (position - _center[guess]).sqrMagnitude;
        const int window = 8;
        for (var offset = -window; offset <= window; offset++)
        {
            var i = (guess + offset + _count) % _count;
            var distance = (position - _center[i]).sqrMagnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = i;
        }

        return best;
    }

    private int BruteNearest(Vector2 position)
    {
        var best = 0;
        var bestDistance = float.MaxValue;
        for (var i = 0; i < _count; i++)
        {
            var distance = (position - _center[i]).sqrMagnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = i;
        }

        return best;
    }

    /// <summary>Smallest gap between two parts of the track that are not neighbours (layout check).</summary>
    public float ClosestApproach()
    {
        var window = Mathf.CeilToInt((RailOffset * 4f) / (Length / _count));
        var min = float.MaxValue;
        for (var i = 0; i < _count; i += 2)
        {
            for (var j = 0; j < _count; j += 2)
            {
                var gap = Mathf.Abs(i - j);
                gap = Mathf.Min(gap, _count - gap);
                if (gap < window) continue;
                min = Mathf.Min(min, (_center[i] - _center[j]).magnitude);
            }
        }

        return min;
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
        // Curb blocks and tube sleeves keep the same real length on every track size.
        var curbBlock = Mathf.Max(1, Mathf.RoundToInt(0.075f / (Length / _count)));
        for (var i = 0; i < SampleCount; i++)
        {
            AddBand(builder, Road, i, -RoadHalfWidth, RoadHalfWidth, 0.003f);

            var curb = i / curbBlock % 2 == 0 ? CurbRed : CurbWhite;
            AddBand(builder, curb, i, RoadHalfWidth, RoadHalfWidth + CurbWidth, 0.0042f);
            AddBand(builder, curb, i, -RoadHalfWidth - CurbWidth, -RoadHalfWidth, 0.0042f);
            AddBand(builder, Turf, i, RoadHalfWidth + CurbWidth, RailOffset + 0.02f, 0.002f);
            AddBand(builder, Turf, i, -RailOffset - 0.02f, -RoadHalfWidth - CurbWidth, 0.002f);

            if (i / curbBlock % 2 == 0 && i > curbBlock * 2 && i < SampleCount - 2)
                AddBand(builder, Line, i, -0.003f, 0.003f, 0.0036f);

            var sleeve = i / (curbBlock * 2) % 4 == 0 ? TubeLight : TubeDark;
            AddTube(builder, sleeve, i, RailOffset);
            AddTube(builder, sleeve, i, -RailOffset);
        }

        AddStartLine(builder);
        AddGantry(builder);
        _mesh = builder.Build("Race Track " + Layout + " " + Size);
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

        /// <summary>Builds only the submeshes that received geometry; `used` lists their original indices.</summary>
        public Mesh BuildCompact(string meshName, List<int> used)
        {
            used.Clear();
            for (var i = 0; i < _triangles.Length; i++)
                if (_triangles[i].Count > 0) used.Add(i);
            var mesh = new Mesh { name = meshName };
            mesh.indexFormat = _vertices.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uvs);
            mesh.subMeshCount = used.Count;
            for (var i = 0; i < used.Count; i++)
                mesh.SetTriangles(_triangles[used[i]], i);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        /// <summary>Upright (tapered) cylinder with smooth sides and flat caps.</summary>
        public void AddUprightCylinder(int submesh, Vector2 at, float y0, float y1, float r0, float r1, int segments = 16)
        {
            for (var s = 0; s < segments; s++)
            {
                var a0 = s / (float)segments * Mathf.PI * 2f;
                var a1 = (s + 1) / (float)segments * Mathf.PI * 2f;
                var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                var c = new Vector3(at.x, 0f, at.y);
                AddQuadSmooth(submesh, c + d0 * r0 + Vector3.up * y0, c + d1 * r0 + Vector3.up * y0,
                    c + d1 * r1 + Vector3.up * y1, c + d0 * r1 + Vector3.up * y1, d0, d1, d1, d0);
                AddTriangle(submesh, c + Vector3.up * y1, c + d0 * r1 + Vector3.up * y1, c + d1 * r1 + Vector3.up * y1, Vector3.up);
                if (y0 > 0.001f)
                    AddTriangle(submesh, c + Vector3.up * y0, c + d1 * r0 + Vector3.up * y0, c + d0 * r0 + Vector3.up * y0, Vector3.down);
            }
        }

        /// <summary>Smooth ellipsoid (foliage, balls, cushions).</summary>
        public void AddSphere(int submesh, Vector3 center, Vector3 radii, int segments = 12)
        {
            var rings = segments / 2;
            for (var r = 0; r < rings; r++)
            {
                var p0 = Mathf.PI * r / rings;
                var p1 = Mathf.PI * (r + 1) / rings;
                for (var s = 0; s < segments; s++)
                {
                    var a0 = s / (float)segments * Mathf.PI * 2f;
                    var a1 = (s + 1) / (float)segments * Mathf.PI * 2f;
                    var n00 = Unit(p0, a0);
                    var n01 = Unit(p0, a1);
                    var n11 = Unit(p1, a1);
                    var n10 = Unit(p1, a0);
                    AddQuadSmooth(submesh, center + Vector3.Scale(n00, radii), center + Vector3.Scale(n01, radii),
                        center + Vector3.Scale(n11, radii), center + Vector3.Scale(n10, radii), n00, n01, n11, n10);
                }
            }

            Vector3 Unit(float polar, float azimuth) =>
                new Vector3(Mathf.Sin(polar) * Mathf.Cos(azimuth), Mathf.Cos(polar), Mathf.Sin(polar) * Mathf.Sin(azimuth));
        }
    }
}
