using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Small immediate-mode UI for touch screens. The same layout code runs twice per frame:
/// once from Update to handle input (taps, held fingers, slider drags; every finger is tracked
/// separately so steering and throttle work with two thumbs) and once from OnGUI to draw.
/// Draws rounded translucent panels and buttons with generated textures (no image assets).
/// </summary>
public sealed class UiKit
{
    public enum Look { Normal, Primary, Accent, Danger, Ghost, Steer, Go, Brake }

    private struct Finger
    {
        public int Id;
        public Vector2 Position;
        public Vector2 Start;
        public bool Began;
    }

    public static readonly Color PanelColor = new Color(0.05f, 0.07f, 0.10f, 0.84f);
    public static readonly Color AccentColor = new Color(1.00f, 0.56f, 0.10f);
    public static readonly Color TextDim = new Color(1f, 1f, 1f, 0.62f);

    private readonly List<Finger> _fingers = new List<Finger>();
    private readonly HashSet<int> _owned = new HashSet<int>();
    private readonly HashSet<int> _tapsUsed = new HashSet<int>();
    private readonly List<Rect> _blockers = new List<Rect>();
    private Texture2D _rounded;
    private Texture2D _circle;
    private int _roundedRadius = -1;
    private GUIStyle _box;
    private readonly Dictionary<int, GUIStyle> _labels = new Dictionary<int, GUIStyle>();

    public bool Drawing { get; private set; }
    public float Scale { get; private set; } = 1f;
    public float Width => Screen.width;
    public float Height => Screen.height;

    /// <summary>Density-independent unit (about 1 dp).</summary>
    public float U(float value) => value * Scale;

    // ------------------------------------------------------------------ frame

    public void BeginInput()
    {
        Drawing = false;
        UpdateScale();
        _fingers.Clear();
        _tapsUsed.Clear();
        _blockers.Clear();
        for (var i = 0; i < Input.touchCount; i++)
        {
            var touch = Input.GetTouch(i);
            if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
            {
                _owned.Remove(touch.fingerId);
                continue;
            }

            _fingers.Add(new Finger
            {
                Id = touch.fingerId,
                Position = ToGui(touch.position),
                Start = ToGui(touch.position - touch.deltaPosition),
                Began = touch.phase == TouchPhase.Began
            });
            if (touch.phase == TouchPhase.Began) _owned.Remove(touch.fingerId);
        }

#if UNITY_EDITOR
        const int mouseId = 99;
        if (Input.GetMouseButton(0) || Input.GetMouseButtonDown(0))
            _fingers.Add(new Finger { Id = mouseId, Position = ToGui(Input.mousePosition), Began = Input.GetMouseButtonDown(0) });
        else
            _owned.Remove(mouseId);
#endif
    }

    public void BeginDraw()
    {
        Drawing = true;
        UpdateScale();
        EnsureTextures();
    }

    private void UpdateScale()
    {
        // Size buttons by physical size; landscape phones are short, so cap by height too.
        var dpiScale = Screen.dpi > 0f ? Screen.dpi / 160f : Screen.width / 420f;
        var shortSide = Mathf.Min(Screen.width, Screen.height);
        Scale = Mathf.Clamp(Mathf.Min(dpiScale, shortSide / 340f), 0.75f, 4f);
    }

    private static Vector2 ToGui(Vector2 screen) => new Vector2(screen.x, Screen.height - screen.y);

    // ------------------------------------------------------------------ input queries

    /// <summary>Panels swallow taps so nothing behind them reacts.</summary>
    public void Block(Rect rect)
    {
        if (!Drawing) _blockers.Add(rect);
    }

    private Rect Grow(Rect r)
    {
        var pad = U(5f);
        return new Rect(r.x - pad, r.y - pad, r.width + pad * 2f, r.height + pad * 2f);
    }

    private bool TapIn(Rect rect)
    {
        var area = Grow(rect);
        for (var i = 0; i < _fingers.Count; i++)
        {
            var finger = _fingers[i];
            if (!finger.Began || _tapsUsed.Contains(finger.Id) || !area.Contains(finger.Position)) continue;
            _tapsUsed.Add(finger.Id);
            _owned.Add(finger.Id);
            return true;
        }

        return false;
    }

    private bool HeldIn(Rect rect, out Vector2 position)
    {
        var area = Grow(rect);
        for (var i = 0; i < _fingers.Count; i++)
        {
            if (!area.Contains(_fingers[i].Position)) continue;
            _owned.Add(_fingers[i].Id);
            _tapsUsed.Add(_fingers[i].Id);
            position = _fingers[i].Position;
            return true;
        }

        position = default;
        return false;
    }

    /// <summary>New taps that no control or panel used (floor taps, placing obstacles).</summary>
    public IEnumerable<Vector2> FreeTaps()
    {
        foreach (var finger in _fingers)
            if (finger.Began && !_tapsUsed.Contains(finger.Id) && !Blocked(finger.Position))
                yield return new Vector2(finger.Position.x, Screen.height - finger.Position.y);
    }

    /// <summary>Fingers resting on empty screen (look-around drags and pinch).</summary>
    public int FreeFingers(out Vector2 first, out Vector2 second)
    {
        first = second = default;
        var count = 0;
        foreach (var finger in _fingers)
        {
            if (_owned.Contains(finger.Id) || Blocked(finger.Start)) continue;
            if (count == 0) first = finger.Position;
            else if (count == 1) second = finger.Position;
            count++;
        }

        return count;
    }

    private bool Blocked(Vector2 point)
    {
        foreach (var rect in _blockers)
            if (rect.Contains(point)) return true;
        return false;
    }

    // ------------------------------------------------------------------ widgets

    public bool Button(Rect rect, string label, Look look = Look.Normal, bool selected = false, int fontSize = 15)
    {
        if (!Drawing)
        {
            if (!TapIn(rect)) return false;
            GameAudio.Instance.Play(GameAudio.Sfx.Click, 0.6f);
            return true;
        }

        var pressed = IsPressed(rect);
        DrawRounded(rect, Tint(look, selected, pressed));
        if (look == Look.Ghost || selected)
            DrawOutline(rect, selected ? AccentColor : new Color(1f, 1f, 1f, 0.25f));
        Text(rect, label, fontSize, TextAnchor.MiddleCenter, Color.white, true);
        return false;
    }

    /// <summary>A control that is active while a finger rests on it (steering, throttle).</summary>
    public bool Hold(Rect rect, string label, Look look, bool round = false, int fontSize = 22)
    {
        if (!Drawing) return HeldIn(rect, out _);

        var pressed = IsPressed(rect);
        var color = Tint(look, false, pressed);
        if (round) DrawCircle(rect, color);
        else DrawRounded(rect, color);
        Text(rect, label, fontSize, TextAnchor.MiddleCenter, Color.white, true);
        return false;
    }

    public float Slider(Rect rect, float value)
    {
        if (!Drawing)
        {
            if (HeldIn(rect, out var position))
                value = Mathf.Clamp01((position.x - rect.x - U(10f)) / Mathf.Max(1f, rect.width - U(20f)));
            return value;
        }

        var track = new Rect(rect.x + U(10f), rect.center.y - U(3f), rect.width - U(20f), U(6f));
        DrawRounded(track, new Color(1f, 1f, 1f, 0.18f), U(3f));
        DrawRounded(new Rect(track.x, track.y, track.width * value, track.height), AccentColor, U(3f));
        var knob = U(22f);
        DrawCircle(new Rect(track.x + track.width * value - knob * 0.5f, track.center.y - knob * 0.5f, knob, knob), Color.white);
        return value;
    }

    /// <summary>Segmented choice; returns the (possibly new) selected index.</summary>
    public int Segmented(Rect rect, string[] options, int selected, int fontSize = 13)
    {
        var width = rect.width / options.Length;
        for (var i = 0; i < options.Length; i++)
        {
            var cell = new Rect(rect.x + width * i + U(2f), rect.y, width - U(4f), rect.height);
            if (Button(cell, options[i], i == selected ? Look.Primary : Look.Ghost, i == selected, fontSize))
                selected = i;
        }

        return selected;
    }

    public bool Swatch(Rect rect, Color color, bool selected)
    {
        if (!Drawing)
        {
            if (!TapIn(rect)) return false;
            GameAudio.Instance.Play(GameAudio.Sfx.Click, 0.6f);
            return true;
        }

        if (selected) DrawCircle(new Rect(rect.x - U(3f), rect.y - U(3f), rect.width + U(6f), rect.height + U(6f)), AccentColor);
        DrawCircle(rect, color);
        return false;
    }

    public void Panel(Rect rect, Color? color = null)
    {
        Block(rect);
        if (Drawing) DrawRounded(rect, color ?? PanelColor);
    }

    public void Text(Rect rect, string text, int fontSize, TextAnchor anchor, Color color, bool bold = false, bool shadow = false)
    {
        if (!Drawing || string.IsNullOrEmpty(text)) return;
        var style = LabelStyle(Mathf.RoundToInt(fontSize * Scale), anchor, bold);
        var previous = GUI.contentColor;
        if (shadow)
        {
            GUI.contentColor = new Color(0f, 0f, 0f, color.a * 0.6f);
            GUI.Label(new Rect(rect.x + U(1.5f), rect.y + U(1.5f), rect.width, rect.height), text, style);
        }

        GUI.contentColor = color;
        GUI.Label(rect, text, style);
        GUI.contentColor = previous;
    }

    public void Image(Rect rect, Texture texture, float alpha = 1f)
    {
        if (!Drawing || texture == null) return;
        var previous = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
        GUI.color = previous;
    }

    public void Fill(Rect rect, Color color)
    {
        if (!Drawing) return;
        var previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    // ------------------------------------------------------------------ drawing helpers

    private bool IsPressed(Rect rect)
    {
        var area = Grow(rect);
        for (var i = 0; i < Input.touchCount; i++)
        {
            var touch = Input.GetTouch(i);
            if (touch.phase != TouchPhase.Ended && touch.phase != TouchPhase.Canceled && area.Contains(ToGui(touch.position)))
                return true;
        }

#if UNITY_EDITOR
        if (Input.GetMouseButton(0) && area.Contains(ToGui(Input.mousePosition))) return true;
#endif
        return false;
    }

    private static Color Tint(Look look, bool selected, bool pressed)
    {
        Color color;
        switch (look)
        {
            case Look.Primary: color = new Color(0.98f, 0.50f, 0.08f, 0.95f); break;
            case Look.Accent: color = new Color(0.10f, 0.52f, 0.85f, 0.95f); break;
            case Look.Danger: color = new Color(0.72f, 0.15f, 0.12f, 0.92f); break;
            case Look.Ghost: color = new Color(1f, 1f, 1f, 0.08f); break;
            case Look.Steer: color = new Color(0.10f, 0.13f, 0.17f, 0.62f); break;
            case Look.Go: color = new Color(0.10f, 0.62f, 0.30f, 0.80f); break;
            case Look.Brake: color = new Color(0.78f, 0.24f, 0.12f, 0.75f); break;
            default: color = new Color(0.16f, 0.20f, 0.26f, 0.90f); break;
        }

        if (selected && look == Look.Ghost) color = new Color(1f, 0.56f, 0.10f, 0.25f);
        if (pressed) color = Color.Lerp(color, Color.white, 0.35f);
        return color;
    }

    private void DrawRounded(Rect rect, Color color, float radius = -1f)
    {
        var previous = GUI.color;
        GUI.color = color;
        // Thin bars use the stretched circle (a pill); everything else the 9-sliced rounded box.
        if (radius >= 0f && rect.height < _roundedRadius * 2f)
            GUI.DrawTexture(rect, _circle, ScaleMode.StretchToFill, true);
        else
            GUI.Box(rect, GUIContent.none, _box);

        GUI.color = previous;
    }

    private void DrawOutline(Rect rect, Color color)
    {
        // A slightly larger rounded box behind would need a second pass; a bottom bar reads well.
        var bar = U(2.5f);
        Fill(new Rect(rect.x + U(8f), rect.yMax - bar, rect.width - U(16f), bar), color);
    }

    private void DrawCircle(Rect rect, Color color)
    {
        var previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, _circle, ScaleMode.StretchToFill, true);
        GUI.color = previous;
    }

    private void EnsureTextures()
    {
        var radius = Mathf.Max(6, Mathf.RoundToInt(12f * Scale));
        if (_rounded == null || radius != _roundedRadius)
        {
            _roundedRadius = radius;
            _rounded = RoundedTexture(radius);
            _box = new GUIStyle
            {
                normal = { background = _rounded },
                border = new RectOffset(radius, radius, radius, radius)
            };
        }

        if (_circle == null) _circle = CircleTexture(128);
    }

    private GUIStyle LabelStyle(int size, TextAnchor anchor, bool bold)
    {
        var key = size * 100 + (int)anchor * 2 + (bold ? 1 : 0);
        if (_labels.TryGetValue(key, out var style)) return style;
        style = new GUIStyle(GUI.skin.label)
        {
            fontSize = size,
            alignment = anchor,
            fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
            wordWrap = true,
            clipping = TextClipping.Overflow,
            normal = { textColor = Color.white },
            padding = new RectOffset(0, 0, 0, 0),
            margin = new RectOffset(0, 0, 0, 0)
        };
        _labels[key] = style;
        return style;
    }

    private static Texture2D RoundedTexture(int radius)
    {
        var size = radius * 2 + 2;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var pixels = new Color32[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                var cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                var distance = new Vector2(x + 0.5f - cx, y + 0.5f - cy).magnitude;
                var alpha = Mathf.Clamp01(radius - distance + 0.5f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private static Texture2D CircleTexture(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
        var pixels = new Color32[size * size];
        var radius = size * 0.5f;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var distance = new Vector2(x + 0.5f - radius, y + 0.5f - radius).magnitude;
                var alpha = Mathf.Clamp01(radius - distance - 0.5f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        return texture;
    }
}
