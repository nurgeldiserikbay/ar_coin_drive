using System.Collections.Generic;
using UnityEngine;

public enum UiIcon
{
    None, Pause, Play, Back, Gear, Car, Flag, People, Camera, Pin, Radar, Cone, Trophy, Coin, Restart, Online, Home
}

/// <summary>
/// Crisp white UI icons drawn from signed-distance shapes into small textures the first time
/// they are used (no image assets). Tinted at draw time with GUI.color.
/// </summary>
public static class UiIcons
{
    private const int Size = 96;
    private static readonly Dictionary<UiIcon, Texture2D> Cache = new Dictionary<UiIcon, Texture2D>();

    public static Texture2D Get(UiIcon icon)
    {
        if (icon == UiIcon.None) return null;
        if (Cache.TryGetValue(icon, out var texture) && texture != null) return texture;
        texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true)
        {
            name = "Icon " + icon,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Trilinear
        };
        var pixels = new Color32[Size * Size];
        var pixel = 2f / Size;
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                // Coordinates -1..1, y up.
                var p = new Vector2((x + 0.5f) / Size * 2f - 1f, (y + 0.5f) / Size * 2f - 1f);
                var d = Distance(icon, p);
                var alpha = Mathf.Clamp01(0.5f - d / pixel);
                pixels[y * Size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        Cache[icon] = texture;
        return texture;
    }

    // ------------------------------------------------------------------ shapes (negative = inside)

    private static float Distance(UiIcon icon, Vector2 p)
    {
        switch (icon)
        {
            case UiIcon.Pause:
                return Mathf.Min(Box(p - new Vector2(-0.28f, 0f), new Vector2(0.14f, 0.55f), 0.06f), Box(p - new Vector2(0.28f, 0f), new Vector2(0.14f, 0.55f), 0.06f));
            case UiIcon.Play:
                return Triangle(p, new Vector2(-0.4f, -0.6f), new Vector2(-0.4f, 0.6f), new Vector2(0.6f, 0f)) - 0.04f;
            case UiIcon.Back:
                return Mathf.Min(Segment(p, new Vector2(0.35f, 0.55f), new Vector2(-0.25f, 0f)), Segment(p, new Vector2(-0.25f, 0f), new Vector2(0.35f, -0.55f))) - 0.13f;
            case UiIcon.Gear:
            {
                var angle = Mathf.Atan2(p.y, p.x);
                var teeth = Mathf.Clamp(Mathf.Cos(angle * 8f) * 3f, -1f, 1f) * 0.5f + 0.5f;
                var outer = p.magnitude - (0.58f + teeth * 0.16f);
                return Mathf.Max(outer, -(p.magnitude - 0.24f));
            }
            case UiIcon.Car:
            {
                var body = Box(p - new Vector2(0f, -0.08f), new Vector2(0.78f, 0.2f), 0.12f);
                var cabin = Box(p - new Vector2(-0.05f, 0.22f), new Vector2(0.42f, 0.16f), 0.14f);
                var wheels = Mathf.Min((p - new Vector2(-0.45f, -0.32f)).magnitude - 0.19f, (p - new Vector2(0.45f, -0.32f)).magnitude - 0.19f);
                var car = Mathf.Min(Mathf.Min(body, cabin), wheels);
                // Cut a gap around the wheels so they read separately.
                var gaps = Mathf.Min((p - new Vector2(-0.45f, -0.32f)).magnitude - 0.25f, (p - new Vector2(0.45f, -0.32f)).magnitude - 0.25f);
                return Mathf.Min(Mathf.Max(car, -gaps), wheels);
            }
            case UiIcon.Flag:
            {
                var pole = Box(p - new Vector2(-0.55f, 0f), new Vector2(0.06f, 0.75f), 0.03f);
                var flag = Box(p - new Vector2(0.05f, 0.32f), new Vector2(0.55f, 0.36f), 0.03f);
                // Checks: punch out alternate squares.
                var cx = Mathf.FloorToInt((p.x + 0.5f) / 0.275f);
                var cy = Mathf.FloorToInt((p.y - 0.0f) / 0.24f);
                var hole = (cx + cy) % 2 == 0 ? Box(new Vector2(Mathf.Repeat(p.x + 0.5f, 0.275f) - 0.1375f, Mathf.Repeat(p.y, 0.24f) - 0.12f), new Vector2(0.105f, 0.09f), 0.01f) : 1f;
                return Mathf.Min(pole, Mathf.Max(flag, -hole));
            }
            case UiIcon.People:
            {
                var back = Mathf.Min((p - new Vector2(0.3f, 0.35f)).magnitude - 0.2f, Ellipse(p - new Vector2(0.3f, -0.35f), new Vector2(0.38f, 0.32f)));
                var front = Mathf.Min((p - new Vector2(-0.2f, 0.25f)).magnitude - 0.24f, Ellipse(p - new Vector2(-0.2f, -0.5f), new Vector2(0.44f, 0.38f)));
                var cut = Mathf.Min((p - new Vector2(-0.2f, 0.25f)).magnitude - 0.32f, Ellipse(p - new Vector2(-0.2f, -0.5f), new Vector2(0.52f, 0.46f)));
                return Mathf.Max(Mathf.Min(Mathf.Max(back, -cut), front), p.y + 0.72f > 0f ? -1f : 1f);
            }
            case UiIcon.Camera:
            {
                var body = Box(p - new Vector2(0f, -0.08f), new Vector2(0.7f, 0.45f), 0.12f);
                var bump = Box(p - new Vector2(-0.15f, 0.42f), new Vector2(0.24f, 0.1f), 0.05f);
                var lens = Mathf.Abs(p.magnitude - 0.25f + 0.08f * 0f) - 0.07f;
                return Mathf.Max(Mathf.Min(body, bump), -(Mathf.Abs((p - new Vector2(0f, -0.08f)).magnitude - 0.24f) - 0.06f));
            }
            case UiIcon.Pin:
            {
                var head = (p - new Vector2(0f, 0.22f)).magnitude - 0.42f;
                var tip = Triangle(p, new Vector2(-0.36f, 0.05f), new Vector2(0.36f, 0.05f), new Vector2(0f, -0.75f));
                return Mathf.Max(Mathf.Min(head, tip), -((p - new Vector2(0f, 0.22f)).magnitude - 0.16f));
            }
            case UiIcon.Radar:
            {
                var c = p - new Vector2(-0.55f, -0.55f);
                var angle = Mathf.Atan2(c.y, c.x);
                var inside = angle > 0.05f && angle < 1.52f ? -1f : 1f;
                var arc1 = Mathf.Abs(c.magnitude - 0.7f) - 0.08f;
                var arc2 = Mathf.Abs(c.magnitude - 1.15f) - 0.08f;
                return Mathf.Min(Mathf.Max(Mathf.Min(arc1, arc2), inside), c.magnitude - 0.2f);
            }
            case UiIcon.Cone:
            {
                var cone = Triangle(p, new Vector2(-0.38f, -0.55f), new Vector2(0.38f, -0.55f), new Vector2(0f, 0.78f));
                var stripe = Mathf.Abs(p.y + 0.05f) - 0.1f;
                var basePlate = Box(p - new Vector2(0f, -0.65f), new Vector2(0.62f, 0.09f), 0.04f);
                return Mathf.Min(Mathf.Max(cone, -stripe), basePlate);
            }
            case UiIcon.Trophy:
            {
                var cup = Mathf.Max(Ellipse(p - new Vector2(0f, 0.38f), new Vector2(0.48f, 0.5f)), p.y - 0.62f);
                var handles = Mathf.Abs((new Vector2(Mathf.Abs(p.x), p.y) - new Vector2(0.48f, 0.38f)).magnitude - 0.18f) - 0.06f;
                var stem = Box(p - new Vector2(0f, -0.25f), new Vector2(0.08f, 0.2f), 0.02f);
                var foot = Box(p - new Vector2(0f, -0.55f), new Vector2(0.36f, 0.1f), 0.04f);
                return Mathf.Min(Mathf.Min(cup, Mathf.Max(handles, -p.x * p.x + 0.0f > 0 ? 1f : -1f)), Mathf.Min(stem, foot));
            }
            case UiIcon.Coin:
                return Mathf.Min(Mathf.Abs(p.magnitude - 0.62f) - 0.1f, Box(p, new Vector2(0.08f, 0.3f), 0.04f));
            case UiIcon.Restart:
            {
                var angle = Mathf.Atan2(p.y, p.x);
                var ring = Mathf.Abs(p.magnitude - 0.5f) - 0.11f;
                var gap = angle > 0.3f && angle < 1.3f ? 1f : -1f;
                var head = Triangle(p, new Vector2(0.18f, 0.72f), new Vector2(0.52f, 0.2f), new Vector2(0.75f, 0.68f));
                return Mathf.Min(Mathf.Max(ring, gap), head);
            }
            case UiIcon.Online:
            {
                var c = p - new Vector2(0f, -0.55f);
                var angle = Mathf.Atan2(c.y, c.x);
                var wedge = angle > 0.6f && angle < 2.54f ? -1f : 1f;
                var arcs = Mathf.Min(Mathf.Abs(c.magnitude - 0.55f) - 0.09f, Mathf.Abs(c.magnitude - 0.95f) - 0.09f);
                return Mathf.Min(Mathf.Max(arcs, wedge), c.magnitude - 0.16f);
            }
            case UiIcon.Home:
            {
                var roof = Triangle(p, new Vector2(-0.75f, 0.05f), new Vector2(0.75f, 0.05f), new Vector2(0f, 0.72f));
                var walls = Box(p - new Vector2(0f, -0.3f), new Vector2(0.5f, 0.38f), 0.03f);
                var door = Box(p - new Vector2(0f, -0.45f), new Vector2(0.14f, 0.24f), 0.02f);
                return Mathf.Max(Mathf.Min(roof, walls), -door);
            }
            default:
                return 1f;
        }
    }

    private static float Box(Vector2 p, Vector2 half, float radius)
    {
        var q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - half + new Vector2(radius, radius);
        return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
    }

    private static float Segment(Vector2 p, Vector2 a, Vector2 b)
    {
        var pa = p - a;
        var ba = b - a;
        var h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
        return (pa - ba * h).magnitude;
    }

    private static float Ellipse(Vector2 p, Vector2 radii)
    {
        // Approximate distance (good enough for icon edges).
        var k = new Vector2(p.x / radii.x, p.y / radii.y).magnitude;
        return (k - 1f) * Mathf.Min(radii.x, radii.y);
    }

    private static float Triangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        var e0 = b - a;
        var e1 = c - b;
        var e2 = a - c;
        var v0 = p - a;
        var v1 = p - b;
        var v2 = p - c;
        var pq0 = v0 - e0 * Mathf.Clamp01(Vector2.Dot(v0, e0) / Vector2.Dot(e0, e0));
        var pq1 = v1 - e1 * Mathf.Clamp01(Vector2.Dot(v1, e1) / Vector2.Dot(e1, e1));
        var pq2 = v2 - e2 * Mathf.Clamp01(Vector2.Dot(v2, e2) / Vector2.Dot(e2, e2));
        var s = Mathf.Sign(e0.x * e2.y - e0.y * e2.x);
        var d0 = new Vector2(pq0.sqrMagnitude, s * (v0.x * e0.y - v0.y * e0.x));
        var d1 = new Vector2(pq1.sqrMagnitude, s * (v1.x * e1.y - v1.y * e1.x));
        var d2 = new Vector2(pq2.sqrMagnitude, s * (v2.x * e2.y - v2.y * e2.x));
        var d = Vector2.Min(Vector2.Min(d0, d1), d2);
        return -Mathf.Sqrt(d.x) * Mathf.Sign(d.y);
    }
}
