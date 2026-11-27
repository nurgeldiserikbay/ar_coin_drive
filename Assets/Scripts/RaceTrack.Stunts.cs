using System.Collections.Generic;
using UnityEngine;

public enum StuntKind { Loop, Ramp }

/// <summary>A jump ramp or a loop-the-loop placed on a straight part of a race track.</summary>
public sealed class TrackStunt
{
    public StuntKind Kind;
    public int Index;          // sample at the loop's foot / the ramp's lip
    public Vector2 Point;      // centre line at Index
    public Vector2 Tangent;
    public Vector2 Outward;
    public float ZoneBack;     // metres before Index that belong to the stunt (no coins there)
    public float ZoneForward;  // metres after Index
    public float Heading => Mathf.Atan2(Tangent.x, Tangent.y) * Mathf.Rad2Deg;
}

/// <summary>
/// Stunts on the race track. Every track gets a jump ramp in the middle of the road (two on long
/// tracks) and, when the lap is long enough, a loop-the-loop. The loop is a corkscrew: cars enter
/// in the right half of the road and come out in the left half, so a low lane divider funnels
/// everyone into the right half before the loop and keeps the exit lane clear after it.
/// Stunts go on the straightest stretches, away from the start line.
/// </summary>
public sealed partial class RaceTrack
{
    public const float LoopRadius = 0.14f;
    public const float RampLength = 0.20f;
    public const float RampHeight = 0.04f;
    // Divider in front of the loop: it angles in from the left barrier over LoopFunnel metres.
    private const float LoopApproach = 0.60f;
    private const float LoopFunnel = 0.36f;
    private const float LoopExit = LoopRadius + 0.14f;
    // A loop entry only counts within this distance past the foot (cars move about 1.5 cm a frame).
    private const float LoopEntryWindow = 0.06f;
    private const float RampFlightRoom = 0.55f;

    private readonly List<TrackStunt> _stunts = new List<TrackStunt>();

    public IReadOnlyList<TrackStunt> Stunts => _stunts;
    public float RampHalfSpan => RoadHalfWidth * 0.62f;
    private float LoopBandHalfWidth => RoadHalfWidth * 0.40f;
    /// <summary>Centre of the lane a car is in when it enters a loop, and the lane shift it ends with.</summary>
    public float LoopEntryLane => RoadHalfWidth * 0.5f;
    public float LoopLaneShift => RoadHalfWidth;
    private float SampleStep => Length / _count;

    // ------------------------------------------------------------------ placement

    private void PlaceStunts()
    {
        _stunts.Clear();
        var taken = new bool[_count];
        // Keep the starting grid and the gantry clear.
        Mark(taken, 0, 0.8f, 0.45f);
        if (Length >= 4.5f)
            TryPlace(StuntKind.Loop, taken, LoopRadius + 0.08f, LoopRadius + 0.08f, 9f, LoopApproach + 0.05f, LoopExit + 0.05f);
        var ramps = Length >= 7f ? 2 : 1;
        for (var i = 0; i < ramps; i++)
            TryPlace(StuntKind.Ramp, taken, RampLength, RampFlightRoom * 0.6f, 32f, RampLength + 0.25f, RampFlightRoom + 0.15f);
    }

    private void Mark(bool[] taken, int index, float back, float forward)
    {
        var from = Mathf.CeilToInt(back / SampleStep);
        var to = Mathf.CeilToInt(forward / SampleStep);
        for (var k = -from; k <= to; k++) taken[Wrap(index + k)] = true;
    }

    private int Wrap(int index) => (index % _count + _count) % _count;

    /// <summary>Puts a stunt on the straightest free stretch, if one is straight enough.</summary>
    private void TryPlace(StuntKind kind, bool[] taken, float straightBack, float straightForward, float maxTurn,
        float zoneBack, float zoneForward)
    {
        var step = SampleStep;
        var sb = Mathf.CeilToInt(straightBack / step);
        var sf = Mathf.CeilToInt(straightForward / step);
        var zb = Mathf.CeilToInt((zoneBack + 0.25f) / step);
        var zf = Mathf.CeilToInt((zoneForward + 0.25f) / step);
        var best = -1;
        var bestTurn = maxTurn;
        for (var i = 0; i < _count; i++)
        {
            var free = true;
            for (var k = -zb; k <= zf && free; k++) free = !taken[Wrap(i + k)];
            if (!free) continue;

            var turn = 0f;
            for (var k = -sb; k <= sf && turn < bestTurn; k++)
                turn = Mathf.Max(turn, Vector2.Angle(_tangent[i], _tangent[Wrap(i + k)]));
            if (turn >= bestTurn) continue;
            bestTurn = turn;
            best = i;
        }

        if (best < 0) return;
        Mark(taken, best, zoneBack, zoneForward);
        _stunts.Add(new TrackStunt
        {
            Kind = kind, Index = best, Point = _center[best], Tangent = _tangent[best], Outward = _outward[best],
            ZoneBack = zoneBack, ZoneForward = zoneForward
        });
    }

    // ------------------------------------------------------------------ queries

    /// <summary>Signed metres along the lap from a sample to a position (positive is ahead).</summary>
    private float AlongFrom(int baseIndex, Vector2 position, int nearest)
    {
        var delta = nearest - baseIndex;
        if (delta > _count / 2) delta -= _count;
        if (delta < -_count / 2) delta += _count;
        return delta * SampleStep + Vector2.Dot(position - _center[nearest], _tangent[nearest]);
    }

    /// <summary>True when a lap fraction falls on a stunt (coins stay off ramps and loops).</summary>
    public bool InStuntZone(float lapFraction)
    {
        var index = Mathf.RoundToInt(Mathf.Repeat(lapFraction, 1f) * _count) % _count;
        foreach (var stunt in _stunts)
        {
            var delta = index - stunt.Index;
            if (delta > _count / 2) delta -= _count;
            if (delta < -_count / 2) delta += _count;
            var along = delta * SampleStep;
            if (along >= -stunt.ZoneBack && along <= stunt.ZoneForward) return true;
        }

        return false;
    }

    /// <summary>Height of a ramp's surface under a position (0 off the ramps) and its slope in degrees.</summary>
    public float RampSurface(Vector2 position, out float slope)
    {
        slope = 0f;
        if (_stunts.Count == 0) return 0f;
        var nearest = NearestIndex(position);
        foreach (var stunt in _stunts)
        {
            if (stunt.Kind != StuntKind.Ramp) continue;
            var along = AlongFrom(stunt.Index, position, nearest);
            if (along < -RampLength || along > 0f) continue;
            if (Mathf.Abs(Vector2.Dot(position - _center[nearest], _outward[nearest])) > RampHalfSpan) continue;
            slope = Mathf.Atan2(RampHeight, RampLength) * Mathf.Rad2Deg;
            return RampHeight * (along + RampLength) / RampLength;
        }

        return 0f;
    }

    /// <summary>The ramp whose lip a car just drove off going forward, or null.</summary>
    public TrackStunt RampLaunched(Vector2 from, Vector2 to)
    {
        if (_stunts.Count == 0) return null;
        var nearFrom = NearestIndex(from);
        var nearTo = NearestIndex(to);
        foreach (var stunt in _stunts)
        {
            if (stunt.Kind != StuntKind.Ramp) continue;
            var before = AlongFrom(stunt.Index, from, nearFrom);
            var after = AlongFrom(stunt.Index, to, nearTo);
            if (before > 0f || after <= 0f || before < -RampLength) continue;
            if (Mathf.Abs(Vector2.Dot(from - _center[nearFrom], _outward[nearFrom])) <= RampHalfSpan) return stunt;
        }

        return null;
    }

    /// <summary>The loop whose foot a car just drove over in the entry lane, or null.</summary>
    public TrackStunt LoopEntered(Vector2 from, Vector2 to)
    {
        if (_stunts.Count == 0) return null;
        var nearFrom = NearestIndex(from);
        var nearTo = NearestIndex(to);
        foreach (var stunt in _stunts)
        {
            if (stunt.Kind != StuntKind.Loop) continue;
            var before = AlongFrom(stunt.Index, from, nearFrom);
            var after = AlongFrom(stunt.Index, to, nearTo);
            if (before >= 0f || after < 0f || before < -LoopEntryWindow) continue;
            if (Vector2.Dot(to - _center[nearTo], _outward[nearTo]) > 0f) return stunt;
        }

        return null;
    }

    /// <summary>
    /// The lane a computer driver should hold near a loop: the right half before it, the left half
    /// after it. Returns false away from loops.
    /// </summary>
    public bool LoopLaneFor(Vector2 position, out float lane)
    {
        lane = 0f;
        if (_stunts.Count == 0) return false;
        var nearest = NearestIndex(position);
        foreach (var stunt in _stunts)
        {
            if (stunt.Kind != StuntKind.Loop) continue;
            var along = AlongFrom(stunt.Index, position, nearest);
            if (along < -LoopApproach - 0.5f || along > LoopExit) continue;
            lane = along < 0f ? LoopEntryLane : LoopEntryLane - LoopLaneShift;
            return true;
        }

        return false;
    }

    /// <summary>Narrows the allowed lateral range around loops (see the class summary).</summary>
    private void LoopLanes(Vector2 position, int index, float lateral, float carHalfWidth, ref float low, ref float high)
    {
        if (_stunts.Count == 0) return;
        foreach (var stunt in _stunts)
        {
            if (stunt.Kind != StuntKind.Loop) continue;
            var along = AlongFrom(stunt.Index, position, index);
            if (along < -LoopApproach || along > LoopExit) continue;
            if (along < 0f)
                low = Mathf.Max(low, DividerLateral(along) + TubeRadius + carHalfWidth);
            else if (along > LoopEntryWindow || lateral < 0f)
                high = Mathf.Min(high, -(TubeRadius + carHalfWidth));
        }
    }

    /// <summary>Divider position before a loop: from the left barrier into the centre line.</summary>
    private float DividerLateral(float along)
    {
        if (along >= 0f) return 0f;
        var t = Mathf.Clamp01((along + LoopApproach) / LoopFunnel);
        return Mathf.Lerp(-RailOffset, 0f, Mathf.SmoothStep(0f, 1f, t));
    }

    /// <summary>Road point on a loop at an angle (0 at the foot), height above the floor in y.</summary>
    public Vector3 LoopPoint(TrackStunt stunt, float angle, float lateral)
    {
        var point = stunt.Point + stunt.Tangent * (LoopRadius * Mathf.Sin(angle)) + stunt.Outward * lateral;
        return new Vector3(point.x, LoopRadius * (1f - Mathf.Cos(angle)), point.y);
    }

    // ------------------------------------------------------------------ meshes

    private void AddStunts(FlatMeshBuilder builder)
    {
        foreach (var stunt in _stunts)
        {
            if (stunt.Kind == StuntKind.Ramp) AddRamp(builder, stunt);
            else AddLoop(builder, stunt);
        }
    }

    /// <summary>Wedge across the middle of the road with yellow and black stripes, open at the lip.</summary>
    private void AddRamp(FlatMeshBuilder builder, TrackStunt stunt)
    {
        var samples = Mathf.Max(2, Mathf.RoundToInt(RampLength / SampleStep));
        var span = RampHalfSpan;
        const float lift = 0.0035f;
        for (var k = 0; k < samples; k++)
        {
            var i0 = stunt.Index - samples + k;
            var i1 = i0 + 1;
            var h0 = RampHeight * k / samples + lift;
            var h1 = RampHeight * (k + 1) / samples + lift;
            var paint = k / 2 % 2 == 0 ? RampPaint : CheckerDark;
            builder.AddQuad(paint, Ground(i0, -span, h0), Ground(i0, span, h0), Ground(i1, span, h1), Ground(i1, -span, h1), Vector3.up);
            foreach (var side in new[] { -1f, 1f })
            {
                var normal = new Vector3(_outward[Wrap(i0)].x, 0f, _outward[Wrap(i0)].y) * side;
                builder.AddQuad(Gantry, Ground(i0, side * span, 0f), Ground(i1, side * span, 0f),
                    Ground(i1, side * span, h1), Ground(i0, side * span, h0), normal);
            }
        }

        var forward = new Vector3(stunt.Tangent.x, 0f, stunt.Tangent.y);
        builder.AddQuad(Gantry, Ground(stunt.Index, -span, 0f), Ground(stunt.Index, span, 0f),
            Ground(stunt.Index, span, RampHeight + lift), Ground(stunt.Index, -span, RampHeight + lift), forward);
    }

    /// <summary>
    /// The loop: a blue band that climbs from the right half of the road, turns over and lands in
    /// the left half, with white lips along both edges, plus the lane divider tube.
    /// </summary>
    private void AddLoop(FlatMeshBuilder builder, TrackStunt stunt)
    {
        const int segments = 72;
        const float thickness = 0.006f;
        const float lip = 0.012f;
        const float lift = 0.004f;
        var half = LoopBandHalfWidth;
        var tangent = new Vector3(stunt.Tangent.x, 0f, stunt.Tangent.y);
        var outward = new Vector3(stunt.Outward.x, 0f, stunt.Outward.y);
        for (var s = 0; s < segments; s++)
        {
            var a0 = s / (float)segments * Mathf.PI * 2f;
            var a1 = (s + 1) / (float)segments * Mathf.PI * 2f;
            var c0 = LoopEntryLane - LoopLaneShift * (a0 / (Mathf.PI * 2f));
            var c1 = LoopEntryLane - LoopLaneShift * (a1 / (Mathf.PI * 2f));
            // Inner surface normal points at the loop's centre.
            var n0 = -tangent * Mathf.Sin(a0) + Vector3.up * Mathf.Cos(a0);
            var n1 = -tangent * Mathf.Sin(a1) + Vector3.up * Mathf.Cos(a1);
            var up = Vector3.up * lift;
            var l0 = LoopPoint(stunt, a0, c0 - half) + up;
            var r0 = LoopPoint(stunt, a0, c0 + half) + up;
            var l1 = LoopPoint(stunt, a1, c1 - half) + up;
            var r1 = LoopPoint(stunt, a1, c1 + half) + up;
            builder.AddQuadSmooth(LoopPaint, l0, r0, r1, l1, n0, n0, n1, n1);
            builder.AddQuadSmooth(LoopPaint, l0 - n0 * thickness, l1 - n1 * thickness, r1 - n1 * thickness, r0 - n0 * thickness,
                -n0, -n1, -n1, -n0);
            // Edge lips that keep the cars on the band.
            builder.AddQuad(TubeLight, l0 - n0 * thickness, l1 - n1 * thickness, l1 + n1 * lip, l0 + n0 * lip, -outward);
            builder.AddQuad(TubeLight, r0 - n0 * thickness, r0 + n0 * lip, r1 + n1 * lip, r1 - n1 * thickness, outward);
        }

        // Lane divider: a low tube from the left barrier into the centre line, then on past the exit.
        var back = Mathf.CeilToInt(LoopApproach / SampleStep);
        var forward = Mathf.CeilToInt(LoopExit / SampleStep);
        for (var k = -back; k < forward; k++)
        {
            var i0 = stunt.Index + k;
            var lat0 = DividerLateral(k * SampleStep);
            var lat1 = DividerLateral((k + 1) * SampleStep);
            var sleeve = (k + back) / 3 % 2 == 0 ? TubeLight : TubeDark;
            AddTubeBetween(builder, sleeve, i0, lat0, lat1);
        }
    }

    /// <summary>A barrier tube segment whose distance from the centre line changes along it.</summary>
    private void AddTubeBetween(FlatMeshBuilder builder, int submesh, int index, float lateralFrom, float lateralTo)
    {
        const int sides = 8;
        var next = Wrap(index + 1);
        index = Wrap(index);
        for (var s = 0; s < sides; s++)
        {
            var a0 = s / (float)sides * Mathf.PI * 2f;
            var a1 = (s + 1) / (float)sides * Mathf.PI * 2f;
            builder.AddQuadSmooth(submesh,
                TubePoint(index, lateralFrom, a0, out var n0), TubePoint(next, lateralTo, a0, out var n1),
                TubePoint(next, lateralTo, a1, out var n2), TubePoint(index, lateralFrom, a1, out var n3),
                n0, n1, n2, n3);
        }
    }
}
