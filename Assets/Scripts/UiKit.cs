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
    private GUIStyle _shadedBox;   // rounded, lighter at the top: buttons and panels
    private GUIStyle _ringBox;     // rounded outline
    private GUIStyle _glossBox;    // glossy button face
    private Texture2D _backdrop;
    private readonly Dictionary<int, GUIStyle> _labels = new Dictionary<int, GUIStyle>();
    private Font _font;
    private Font _fontBold;
    private bool _fontsLoaded;
    private float _alpha = 1f;

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
        _alpha = 1f;
        GUI.matrix = Matrix4x4.identity;
        UpdateScale();
        EnsureTextures();
    }

#if UI_CAPTURE
    /// <summary>UI review build: the scale a real phone of the emulated size would use.</summary>
    public static float ForcedScale;
#endif

    private void UpdateScale()
    {
#if UI_CAPTURE
        if (ForcedScale > 0f) { Scale = ForcedScale; return; }
#endif
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

    // ------------------------------------------------------------------ screen transitions

    /// <summary>Fades and slides a screen in during its first quarter second.</summary>
    public void BeginScreen(float age)
    {
        if (!Drawing) return;
        var t = Mathf.Clamp01(age / 0.25f);
        var eased = 1f - (1f - t) * (1f - t) * (1f - t);
        _alpha = eased;
        GUI.matrix = Matrix4x4.Translate(new Vector3(0f, (1f - eased) * U(18f), 0f));
    }

    public void EndScreen()
    {
        if (!Drawing) return;
        _alpha = 1f;
        GUI.matrix = Matrix4x4.identity;
    }

    /// <summary>Big text that pops in (countdown, GO, knockouts): scale overshoots, then settles.</summary>
    public void PopText(Rect rect, string text, int fontSize, Color color, float age)
    {
        if (!Drawing || string.IsNullOrEmpty(text)) return;
        var scale = 1f + 0.7f * Mathf.Exp(-age * 9f) * Mathf.Cos(age * 18f);
        var previous = GUI.matrix;
        GUI.matrix = previous * Matrix4x4.TRS(rect.center, Quaternion.identity, new Vector3(scale, scale, 1f)) * Matrix4x4.Translate(-rect.center);
        Text(rect, text, fontSize, TextAnchor.MiddleCenter, color, true, true);
        GUI.matrix = previous;
    }

    private Color A(Color c) => new Color(c.r, c.g, c.b, c.a * _alpha);

    public bool IconButton(Rect rect, UiIcon icon, Look look = Look.Normal, bool selected = false)
        => Button(rect, null, look, selected, 15, icon);

    public bool Button(Rect rect, string label, Look look = Look.Normal, bool selected = false, int fontSize = 15, UiIcon icon = UiIcon.None)
    {
        if (!Drawing)
        {
            if (!TapIn(rect)) return false;
            GameAudio.Instance.Play(GameAudio.Sfx.Click, 0.6f);
            return true;
        }

        var pressed = IsPressed(rect);
        if (look != Look.Ghost)
        {
            var face = DrawChunky(rect, Tint(look, selected, false), selected, pressed);
            DrawContent(face, label, fontSize, icon);
            return false;
        }

        if (pressed) rect = Shrink(rect, 0.04f);
        DrawRounded(rect, Tint(look, selected, pressed));
        DrawRing(rect, selected ? AccentColor : new Color(1f, 1f, 1f, 0.16f));
        DrawContent(rect, label, fontSize, icon);
        return false;
    }

    /// <summary>
    /// Chunky game button body: shadow, a darker lip below the face, and the glossy face, which
    /// sinks onto the lip while pressed. Returns the face rect for the content.
    /// </summary>
    private Rect DrawChunky(Rect rect, Color tint, bool selected, bool pressed)
    {
        var lip = Mathf.Round(Mathf.Min(U(4f), rect.height * 0.1f));
        var face = new Rect(rect.x, rect.y + (pressed ? lip : 0f), rect.width, rect.height - lip);
        DrawRounded(new Rect(rect.x, rect.y + lip + U(2f), rect.width, rect.height - lip), new Color(0f, 0f, 0f, 0.30f));
        DrawRounded(new Rect(rect.x, rect.y + lip, rect.width, rect.height - lip), Darken(tint, 0.55f));
        DrawGloss(face, pressed ? Color.Lerp(tint, Color.white, 0.12f) : tint);
        DrawRing(face, selected ? new Color(1f, 0.80f, 0.45f, 0.9f) : new Color(1f, 1f, 1f, 0.16f));
        return face;
    }

    /// <summary>
    /// Big coloured tile for the main choices: icon top-left, a large faded icon as a watermark,
    /// a bold title and a short line under it.
    /// </summary>
    public bool Tile(Rect rect, Color tint, UiIcon icon, string title, string subtitle)
    {
        if (!Drawing)
        {
            if (!TapIn(rect)) return false;
            GameAudio.Instance.Play(GameAudio.Sfx.Click, 0.6f);
            return true;
        }

        var face = DrawChunky(rect, tint, false, IsPressed(rect));
        var pad = U(12f);
        var mark = face.height * 0.95f;
        Icon(new Rect(face.xMax - mark * 0.78f, face.y + face.height * 0.08f, mark, mark), icon, new Color(1f, 1f, 1f, 0.13f));
        var iconSize = Mathf.Min(face.height * 0.34f, U(40f));
        Icon(new Rect(face.x + pad, face.y + pad, iconSize, iconSize), icon, Color.white);
        var subtitleHeight = string.IsNullOrEmpty(subtitle) ? 0f : U(18f);
        var titleRect = new Rect(face.x + pad, face.yMax - pad - subtitleHeight - U(30f), face.width - pad * 2f, U(30f));
        DrawLabel(titleRect, title, FitSize(title, 24, titleRect.width, true), TextAnchor.LowerLeft, Color.white, true, true);
        if (subtitleHeight > 0f)
            DrawLabel(new Rect(titleRect.x, titleRect.yMax, titleRect.width, subtitleHeight), subtitle, FitSize(subtitle, 12, titleRect.width, false),
                TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.85f), false, true);
        return false;
    }

    private static Color Darken(Color c, float amount) => new Color(c.r * amount, c.g * amount, c.b * amount, Mathf.Max(c.a, 0.9f));

    /// <summary>Button face: glossy upper half, deeper lower half.</summary>
    private void DrawGloss(Rect rect, Color color)
    {
        if (rect.height < _roundedRadius * 2f) { DrawRounded(rect, color); return; }
        var previous = GUI.color;
        GUI.color = A(color);
        GUI.Box(rect, GUIContent.none, _glossBox);
        GUI.color = previous;
    }

    /// <summary>A control that is active while a finger rests on it (steering, throttle).</summary>
    public bool Hold(Rect rect, string label, Look look, bool round = false, int fontSize = 22, UiIcon icon = UiIcon.None)
    {
        if (!Drawing) return HeldIn(rect, out _);

        var pressed = IsPressed(rect);
        if (pressed) rect = Shrink(rect, 0.06f);
        var color = Tint(look, false, pressed);
        if (round)
        {
            DrawCircle(new Rect(rect.x, rect.y + U(2.5f), rect.width, rect.height), new Color(0f, 0f, 0f, 0.3f));
            DrawCircle(rect, color);
            // Inner disc so the control reads on any background.
            DrawCircle(Shrink(rect, 0.88f), new Color(1f, 1f, 1f, 0.07f));
        }
        else DrawRounded(rect, color);
        DrawContent(rect, label, fontSize, icon);
        return false;
    }

    private int _stickId = -1;
    private Vector2 _stickOrigin;
    private Vector2 _stickPosition;

    /// <summary>
    /// Floating thumb stick: a finger that lands anywhere in <paramref name="zone"/> becomes the
    /// stick's centre; the result is the drag from there, -1..1 per axis, +y = up the screen.
    /// When idle the stick is drawn at <paramref name="rest"/> so players see where to put a thumb.
    /// </summary>
    public Vector2 Stick(Rect zone, Vector2 rest, float radius)
    {
        if (Drawing)
        {
            var active = _stickId >= 0;
            var centre = active ? _stickOrigin : rest;
            var knob = active ? _stickPosition : rest;
            var baseRect = new Rect(centre.x - radius, centre.y - radius, radius * 2f, radius * 2f);
            DrawCircle(new Rect(baseRect.x, baseRect.y + U(3f), baseRect.width, baseRect.height), new Color(0f, 0f, 0f, 0.22f));
            DrawCircle(baseRect, new Color(0.06f, 0.09f, 0.13f, active ? 0.55f : 0.40f));
            DrawCircle(Shrink(baseRect, 0.94f), new Color(1f, 1f, 1f, active ? 0.10f : 0.06f));
            // Four direction ticks around the rim.
            var tick = U(5f);
            for (var i = 0; i < 4; i++)
            {
                var direction = new Vector2(Mathf.Cos(i * Mathf.PI * 0.5f), Mathf.Sin(i * Mathf.PI * 0.5f));
                var p = centre + direction * (radius - U(12f));
                DrawCircle(new Rect(p.x - tick * 0.5f, p.y - tick * 0.5f, tick, tick), new Color(1f, 1f, 1f, 0.35f));
            }

            var knobRadius = radius * 0.46f;
            var knobRect = new Rect(knob.x - knobRadius, knob.y - knobRadius, knobRadius * 2f, knobRadius * 2f);
            DrawCircle(new Rect(knobRect.x, knobRect.y + U(3f), knobRect.width, knobRect.height), new Color(0f, 0f, 0f, 0.35f));
            DrawCircle(knobRect, active ? new Color(1.00f, 0.56f, 0.10f, 0.95f) : new Color(0.92f, 0.94f, 0.97f, 0.85f));
            DrawCircle(Shrink(knobRect, 0.62f), new Color(1f, 1f, 1f, active ? 0.28f : 0.5f));
            return Vector2.zero;
        }

        var found = false;
        if (_stickId >= 0)
        {
            foreach (var finger in _fingers)
            {
                if (finger.Id != _stickId) continue;
                _stickPosition = finger.Position;
                found = true;
                break;
            }
        }

        if (!found)
        {
            _stickId = -1;
            foreach (var finger in _fingers)
            {
                if (!finger.Began || _tapsUsed.Contains(finger.Id) || !zone.Contains(finger.Position) || Blocked(finger.Position)) continue;
                _stickId = finger.Id;
                _stickOrigin = _stickPosition = finger.Position;
                found = true;
                break;
            }
        }

        if (!found) return Vector2.zero;
        _owned.Add(_stickId);
        _tapsUsed.Add(_stickId);
        // The centre trails a finger that slides past the rim, so turning back responds at once.
        var offset = _stickPosition - _stickOrigin;
        if (offset.magnitude > radius) _stickOrigin = _stickPosition - offset.normalized * radius;
        offset = (_stickPosition - _stickOrigin) / radius;
        return new Vector2(offset.x, -offset.y);
    }

    /// <summary>Lets go of the stick (screen changes, round ends).</summary>
    public void ReleaseStick() => _stickId = -1;

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
        if (!Drawing) return;
        DrawShadow(rect);
        DrawShaded(rect, color ?? PanelColor);
        DrawRing(rect, new Color(1f, 1f, 1f, 0.07f));
    }

    /// <summary>A rounded filled shape that, unlike Panel, does not swallow taps (decoration on buttons).</summary>
    public void RoundedFill(Rect rect, Color color)
    {
        if (!Drawing) return;
        DrawRounded(rect, color);
    }

    /// <summary>Like RoundedFill, with the soft vertical shade buttons use.</summary>
    public void ShadedFill(Rect rect, Color color)
    {
        if (!Drawing) return;
        DrawShaded(rect, color);
    }

    public void Icon(Rect rect, UiIcon icon, Color color)
    {
        if (!Drawing || icon == UiIcon.None) return;
        var previous = GUI.color;
        GUI.color = A(color);
        GUI.DrawTexture(rect, UiIcons.Get(icon), ScaleMode.ScaleToFit, true);
        GUI.color = previous;
    }

    public void Text(Rect rect, string text, int fontSize, TextAnchor anchor, Color color, bool bold = false, bool shadow = false)
    {
        if (!Drawing || string.IsNullOrEmpty(text)) return;
        DrawLabel(rect, text, Mathf.RoundToInt(fontSize * Scale), anchor, color, bold, shadow);
    }

    /// <summary>
    /// One line of text that shrinks (down to 60% of <paramref name="fontSize"/>) instead of wrapping
    /// or spilling out of <paramref name="rect"/>: titles, chips and anything in a fixed-width slot.
    /// </summary>
    public void TextFit(Rect rect, string text, int fontSize, TextAnchor anchor, Color color, bool bold = false, bool shadow = false)
    {
        if (!Drawing || string.IsNullOrEmpty(text)) return;
        DrawLabel(rect, text, FitSize(text, fontSize, rect.width, bold), anchor, color, bold, shadow);
    }

    private readonly Dictionary<(string, int, int, bool), float> _heights = new Dictionary<(string, int, int, bool), float>();

    /// <summary>
    /// Height wrapped text needs in <paramref name="width"/>, so layouts can stack paragraphs without
    /// overlap. Measured in the draw pass (GUI styles only work in OnGUI) and reused by the input pass,
    /// so both passes place controls identically; before the first draw it is estimated.
    /// </summary>
    public float TextHeight(string text, int fontSize, float width, bool bold = false)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        var size = Mathf.RoundToInt(fontSize * Scale);
        var key = (text, size, Mathf.RoundToInt(width), bold);
        if (_heights.TryGetValue(key, out var height)) return height;
        if (!Drawing)
        {
            var lines = Mathf.Max(1, Mathf.CeilToInt(text.Length * size * 0.55f / Mathf.Max(1f, width)));
            return lines * size * 1.3f;
        }

        height = LabelStyle(size, TextAnchor.UpperLeft, bold).CalcHeight(new GUIContent(text), width);
        _heights[key] = height;
        return height;
    }

    /// <summary>Pixel font size at which <paramref name="text"/> fits on one line in <paramref name="width"/>.</summary>
    private int FitSize(string text, int fontSize, float width, bool bold)
    {
        var size = Mathf.RoundToInt(fontSize * Scale);
        var smallest = Mathf.Max(8, Mathf.RoundToInt(size * 0.6f));
        var content = new GUIContent(text);
        while (size > smallest && LabelStyle(size, TextAnchor.MiddleLeft, bold).CalcSize(content).x > width) size--;
        return size;
    }

    private void DrawLabel(Rect rect, string text, int pixelSize, TextAnchor anchor, Color color, bool bold, bool shadow)
    {
        var style = LabelStyle(pixelSize, anchor, bold);
        var previous = GUI.contentColor;
        var previousColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, _alpha);
        if (shadow)
        {
            GUI.contentColor = new Color(0f, 0f, 0f, color.a * 0.6f);
            GUI.Label(new Rect(rect.x + U(1.5f), rect.y + U(1.5f), rect.width, rect.height), text, style);
        }

        GUI.contentColor = color;
        GUI.Label(rect, text, style);
        GUI.contentColor = previous;
        GUI.color = previousColor;
    }

    public void Image(Rect rect, Texture texture, float alpha = 1f)
    {
        if (!Drawing || texture == null) return;
        var previous = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha * _alpha);
        GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
        GUI.color = previous;
    }

    public void Fill(Rect rect, Color color)
    {
        if (!Drawing) return;
        var previous = GUI.color;
        GUI.color = A(color);
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
            default: color = new Color(0.19f, 0.24f, 0.33f, 0.94f); break;
        }

        if (selected && look == Look.Ghost) color = new Color(1f, 0.56f, 0.10f, 0.25f);
        if (pressed) color = Color.Lerp(color, Color.white, 0.35f);
        return color;
    }

    private static Rect Shrink(Rect r, float amount) =>
        amount < 0.5f
            ? new Rect(r.x + r.width * amount * 0.5f, r.y + r.height * amount * 0.5f, r.width * (1f - amount), r.height * (1f - amount))
            : new Rect(r.center.x - r.width * amount * 0.5f, r.center.y - r.height * amount * 0.5f, r.width * amount, r.height * amount);

    private void DrawShadow(Rect rect)
    {
        DrawRounded(new Rect(rect.x, rect.y + U(3f), rect.width, rect.height), new Color(0f, 0f, 0f, 0.28f));
    }

    /// <summary>Icon and/or label centred in a control.</summary>
    private void DrawContent(Rect rect, string label, int fontSize, UiIcon icon)
    {
        if (icon == UiIcon.None)
        {
            if (!string.IsNullOrEmpty(label))
                DrawLabel(rect, label, FitSize(label, fontSize, rect.width - U(10f), true), TextAnchor.MiddleCenter, Color.white, true, true);
            return;
        }

        var iconSize = Mathf.Min(rect.height * 0.52f, U(28f));
        if (string.IsNullOrEmpty(label))
        {
            Icon(new Rect(rect.center.x - iconSize * 0.5f, rect.center.y - iconSize * 0.5f, iconSize, iconSize), icon, Color.white);
            return;
        }

        // Shrink the label to fit beside the icon rather than spill past the button edges.
        var size = FitSize(label, fontSize, rect.width - iconSize - U(8f) - U(14f), true);
        var textWidth = LabelStyle(size, TextAnchor.MiddleLeft, true).CalcSize(new GUIContent(label)).x;
        var total = iconSize + U(8f) + textWidth;
        var x = rect.center.x - total * 0.5f;
        Icon(new Rect(x, rect.center.y - iconSize * 0.5f, iconSize, iconSize), icon, Color.white);
        DrawLabel(new Rect(x + iconSize + U(8f), rect.y, textWidth + U(4f), rect.height), label, size, TextAnchor.MiddleLeft, Color.white, true, true);
    }

    private void DrawRounded(Rect rect, Color color, float radius = -1f)
    {
        var previous = GUI.color;
        GUI.color = A(color);
        // Thin bars use the stretched circle (a pill); everything else the 9-sliced rounded box.
        if (radius >= 0f && rect.height < _roundedRadius * 2f)
            GUI.DrawTexture(rect, _circle, ScaleMode.StretchToFill, true);
        else
            GUI.Box(rect, GUIContent.none, _box);

        GUI.color = previous;
    }

    /// <summary>Rounded box with a soft vertical gradient (light top, darker bottom) in the given tint.</summary>
    private void DrawShaded(Rect rect, Color color)
    {
        if (rect.height < _roundedRadius * 2f) { DrawRounded(rect, color); return; }
        var previous = GUI.color;
        GUI.color = A(color);
        GUI.Box(rect, GUIContent.none, _shadedBox);
        GUI.color = previous;
    }

    /// <summary>Thin rounded outline around a box.</summary>
    private void DrawRing(Rect rect, Color color)
    {
        if (rect.height < _roundedRadius * 2f) return;
        var previous = GUI.color;
        GUI.color = A(color);
        GUI.Box(rect, GUIContent.none, _ringBox);
        GUI.color = previous;
    }

    /// <summary>
    /// Full-screen backdrop for menu screens: deep navy-to-violet gradient, an orange glow top
    /// right, a blue glow bottom left and faint diagonal speed lines.
    /// </summary>
    public void Backdrop(Rect rect, float alpha = 1f)
    {
        if (!Drawing) return;
        if (_backdrop == null) _backdrop = BackdropTexture(360, 640);
        var previous = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha * _alpha);
        GUI.DrawTexture(rect, _backdrop, ScaleMode.StretchToFill, true);
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
        GUI.color = A(color);
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
            _shadedBox = new GUIStyle
            {
                normal = { background = ShadedTexture(radius) },
                border = new RectOffset(radius, radius, radius, radius)
            };
            _glossBox = new GUIStyle
            {
                normal = { background = GlossTexture(radius) },
                border = new RectOffset(radius, radius, radius, radius)
            };
            _ringBox = new GUIStyle
            {
                normal = { background = RingTexture(radius, Mathf.Max(1f, Scale * 1.2f)) },
                border = new RectOffset(radius, radius, radius, radius)
            };
        }

        if (_circle == null) _circle = CircleTexture(128);
        if (!_fontsLoaded)
        {
            _fontsLoaded = true;
            _font = Resources.Load<Font>("Fonts/ChakraPetch-SemiBold");
            _fontBold = Resources.Load<Font>("Fonts/ChakraPetch-Bold");
        }
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
        if (_font != null) style.font = bold && _fontBold != null ? _fontBold : _font;
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

    /// <summary>9-sliced rounded box whose rows fade from full brightness at the top to 78% at the bottom.</summary>
    private static Texture2D ShadedTexture(int radius)
    {
        var width = radius * 2 + 2;
        const int height = 96;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var pixels = new Color32[width * height];
        for (var y = 0; y < height; y++)
        {
            // Texture rows run bottom-up; IMGUI draws row 0 at the bottom of the box.
            var shade = Mathf.Lerp(0.78f, 1f, Mathf.SmoothStep(0f, 1f, y / (height - 1f)));
            var value = (byte)(shade * 255f);
            for (var x = 0; x < width; x++)
            {
                var cx = Mathf.Clamp(x + 0.5f, radius, width - radius);
                var cy = Mathf.Clamp(y + 0.5f, radius, height - radius);
                var distance = new Vector2(x + 0.5f - cx, y + 0.5f - cy).magnitude;
                var alpha = Mathf.Clamp01(radius - distance + 0.5f);
                pixels[y * width + x] = new Color32(value, value, value, (byte)(alpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    /// <summary>Glossy button face: bright upper half with a soft step to a deeper lower half.</summary>
    private static Texture2D GlossTexture(int radius)
    {
        var width = radius * 2 + 2;
        const int height = 96;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var pixels = new Color32[width * height];
        for (var y = 0; y < height; y++)
        {
            // Rows run bottom-up: t = 1 at the top edge.
            var t = y / (height - 1f);
            var lower = Mathf.Lerp(0.74f, 0.86f, t / 0.5f);
            var upper = Mathf.Lerp(0.95f, 1.08f, (t - 0.5f) / 0.5f);
            var shade = Mathf.Lerp(lower, upper, Mathf.SmoothStep(0f, 1f, (t - 0.46f) / 0.08f));
            for (var x = 0; x < width; x++)
            {
                var cx = Mathf.Clamp(x + 0.5f, radius, width - radius);
                var cy = Mathf.Clamp(y + 0.5f, radius, height - radius);
                var distance = new Vector2(x + 0.5f - cx, y + 0.5f - cy).magnitude;
                var alpha = Mathf.Clamp01(radius - distance + 0.5f);
                // A thin light line along the very top edge reads as a bevel.
                var bevel = radius - distance < 2.5f && y > height - radius ? 0.12f : 0f;
                var value = (byte)(Mathf.Clamp01(shade + bevel) * 255f);
                pixels[y * width + x] = new Color32(value, value, value, (byte)(alpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    /// <summary>9-sliced rounded outline, thickness in pixels.</summary>
    private static Texture2D RingTexture(int radius, float thickness)
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
                // Edge pixels (outside the inner square) measure from the rounded edge.
                var edge = radius - distance;
                var alpha = Mathf.Clamp01(edge + 0.5f) * Mathf.Clamp01(thickness - edge + 0.5f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private static Texture2D BackdropTexture(int width, int height)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var pixels = new Color32[width * height];
        var top = new Color(0.08f, 0.09f, 0.19f);
        var bottom = new Color(0.02f, 0.02f, 0.05f);
        var orange = new Color(1.00f, 0.45f, 0.08f);
        var blue = new Color(0.10f, 0.42f, 0.95f);
        for (var y = 0; y < height; y++)
        {
            var v = y / (height - 1f); // 0 bottom, 1 top
            for (var x = 0; x < width; x++)
            {
                var u = x / (width - 1f);
                var color = Color.Lerp(bottom, top, Mathf.SmoothStep(0f, 1f, v));
                var aspect = width / (float)height;
                var glowA = Mathf.Exp(-new Vector2((u - 0.95f) * aspect, v - 0.98f).sqrMagnitude / 0.10f);
                var glowB = Mathf.Exp(-new Vector2((u - 0.05f) * aspect, v - 0.05f).sqrMagnitude / 0.14f);
                color += orange * (0.30f * glowA) + blue * (0.18f * glowB);
                // Faint diagonal speed lines.
                var stripe = Mathf.Repeat(x * 0.5f + y, 22f);
                if (stripe < 1.4f) color += new Color(1f, 1f, 1f) * 0.018f;
                pixels[y * width + x] = new Color32((byte)(Mathf.Clamp01(color.r) * 255f), (byte)(Mathf.Clamp01(color.g) * 255f),
                    (byte)(Mathf.Clamp01(color.b) * 255f), 255);
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
