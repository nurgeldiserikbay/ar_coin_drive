using UnityEngine;

/// <summary>
/// Small tiling textures generated once at start-up (no image assets): asphalt, wood floor
/// planks, upholstery, a patterned rug and cardboard. Each is created on first use and cached.
/// </summary>
public static class ProceduralTextures
{
    private static Texture2D _asphalt;
    private static Texture2D _wood;
    private static Texture2D _fabric;
    private static Texture2D _rug;
    private static Texture2D _cardboard;
    private static Texture2D _softDot;

    public static Texture2D Asphalt => _asphalt ? _asphalt : (_asphalt = Build("Asphalt", 256, AsphaltPixel));
    public static Texture2D Wood => _wood ? _wood : (_wood = Build("Wood Floor", 512, WoodPixel));
    public static Texture2D Fabric => _fabric ? _fabric : (_fabric = Build("Fabric", 128, FabricPixel));
    public static Texture2D Rug => _rug ? _rug : (_rug = Build("Rug", 256, RugPixel, TextureWrapMode.Clamp));
    public static Texture2D Cardboard => _cardboard ? _cardboard : (_cardboard = Build("Cardboard", 128, CardboardPixel));

    /// <summary>Round soft-edged alpha spot (contact shadows, cursor).</summary>
    public static Texture2D SoftDot => _softDot ? _softDot : (_softDot = Build("Soft Dot", 64, SoftDotPixel, TextureWrapMode.Clamp));

    private delegate Color PixelFunction(float u, float v);

    private static Texture2D Build(string name, int size, PixelFunction pixel, TextureWrapMode wrap = TextureWrapMode.Repeat)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
        {
            name = name,
            wrapMode = wrap,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4
        };
        var pixels = new Color32[size * size];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                pixels[y * size + x] = pixel((x + 0.5f) / size, (y + 0.5f) / size);
        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        return texture;
    }

    // Tileable value noise: lattice wraps at `period` cells.
    private static float Noise(float u, float v, int period, int seed)
    {
        var x = u * period;
        var y = v * period;
        var x0 = Mathf.FloorToInt(x);
        var y0 = Mathf.FloorToInt(y);
        var fx = x - x0;
        var fy = y - y0;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        var a = Hash(x0, y0, period, seed);
        var b = Hash(x0 + 1, y0, period, seed);
        var c = Hash(x0, y0 + 1, period, seed);
        var d = Hash(x0 + 1, y0 + 1, period, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    private static float Hash(int x, int y, int period, int seed)
    {
        x = ((x % period) + period) % period;
        y = ((y % period) + period) % period;
        var h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (float)0xFFFFFF;
    }

    private static float Fbm(float u, float v, int basePeriod, int octaves, int seed)
    {
        var sum = 0f;
        var amplitude = 0.5f;
        var period = basePeriod;
        for (var i = 0; i < octaves; i++)
        {
            sum += Noise(u, v, period, seed + i * 17) * amplitude;
            amplitude *= 0.5f;
            period *= 2;
        }

        return sum / (1f - amplitude * 2f);
    }

    private static Color AsphaltPixel(float u, float v)
    {
        var grain = Noise(u, v, 128, 3);
        var stones = Noise(u, v, 64, 5);
        var patches = Fbm(u, v, 4, 3, 9);
        var value = 0.80f + (grain - 0.5f) * 0.28f + (patches - 0.5f) * 0.22f;
        if (stones > 0.86f) value += 0.22f;
        if (grain < 0.08f) value -= 0.2f;
        return new Color(value, value, value * 1.02f, 1f);
    }

    private static Color WoodPixel(float u, float v)
    {
        // Six planks across the tile, each with its own tone, grain and staggered end joints.
        const int planks = 6;
        var plank = Mathf.FloorToInt(u * planks);
        var across = u * planks - plank;
        var offset = Hash(plank, 0, 64, 11);
        var along = Mathf.Repeat(v + offset, 1f);
        var tone = Hash(plank, 1, 64, 13);
        var grain = Noise(across * 0.25f + plank * 0.37f, along, 48, 21);
        var rings = Mathf.Sin((along * 22f + grain * 4f + across * 1.3f) * Mathf.PI) * 0.5f + 0.5f;
        var baseColor = Color.Lerp(new Color(0.52f, 0.40f, 0.30f), new Color(0.68f, 0.55f, 0.42f), tone);
        var color = Color.Lerp(baseColor * 0.86f, baseColor, rings * 0.6f + grain * 0.4f);
        var seam = across < 0.025f || across > 0.975f || along < 0.004f;
        if (seam) color *= 0.55f;
        return new Color(color.r, color.g, color.b, 1f);
    }

    private static Color FabricPixel(float u, float v)
    {
        var weave = (Mathf.Sin(u * 128f * Mathf.PI) * Mathf.Sin(v * 128f * Mathf.PI)) * 0.04f;
        var fuzz = (Noise(u, v, 64, 31) - 0.5f) * 0.10f;
        var value = 0.92f + weave + fuzz;
        return new Color(value, value, value, 1f);
    }

    private static Color RugPixel(float u, float v)
    {
        var dx = Mathf.Abs(u - 0.5f) * 2f;
        var dy = Mathf.Abs(v - 0.5f) * 2f;
        var edge = Mathf.Max(dx, dy);
        var fuzz = (Noise(u, v, 96, 41) - 0.5f) * 0.08f;
        Color color;
        if (edge > 0.93f) color = new Color(0.86f, 0.80f, 0.68f);
        else if (edge > 0.86f) color = new Color(0.47f, 0.16f, 0.14f);
        else if (edge > 0.80f) color = new Color(0.86f, 0.80f, 0.68f);
        else
        {
            var diamond = Mathf.Repeat((dx + dy) * 3f, 1f);
            color = diamond < 0.12f ? new Color(0.80f, 0.62f, 0.34f) : new Color(0.20f, 0.27f, 0.38f);
        }

        return new Color(color.r + fuzz, color.g + fuzz, color.b + fuzz, 1f);
    }

    private static Color CardboardPixel(float u, float v)
    {
        var fibres = (Noise(u * 0.3f, v * 2f, 64, 51) - 0.5f) * 0.10f;
        var corrugation = Mathf.Sin(v * 64f * Mathf.PI) * 0.015f;
        var value = 1f + fibres + corrugation;
        return new Color(0.72f * value, 0.55f * value, 0.36f * value, 1f);
    }

    private static Color SoftDotPixel(float u, float v)
    {
        var distance = new Vector2(u - 0.5f, v - 0.5f).magnitude * 2f;
        var alpha = 1f - Mathf.SmoothStep(0.25f, 1f, distance);
        return new Color(1f, 1f, 1f, alpha * alpha);
    }
}
