using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Screens and HUD: main menu, multiplayer lobby, settings, pause menu, in-game HUD and results.
/// RunUi runs twice per frame (input pass from Update, draw pass from OnGUI) via UiKit, so the
/// layout code is written once and buttons always match what is drawn.
/// </summary>
public sealed partial class ARDemoController
{
    private UiKit _ui;
    private UiScreen _screen;
    private UiScreen _settingsReturn = UiScreen.Menu;
    private Texture2D _logo;
    private bool _lobbyUseAR = true;
    private bool _leftHeld;
    private bool _rightHeld;
    private bool _goHeld;
    private bool _backHeld;
    private TouchScreenKeyboard _nameKeyboard;

    private float W => _ui.Width;
    private float H => _ui.Height;
    private bool Landscape => W > H;
    private float U(float v) => _ui.U(v);

    private void RunUi()
    {
        PollNameKeyboard();
        if (_screen != _lastScreen)
        {
            _lastScreen = _screen;
            _screenAt = Time.unscaledTime;
        }

        // Every overlay screen fades and slides in briefly; the driving HUD appears at once.
        var age = Time.unscaledTime - _screenAt;
        switch (_screen)
        {
            case UiScreen.Game:
                GameHud(true);
                return;
            case UiScreen.Pause:
                GameHud(false);
                break;
        }

        _ui.BeginScreen(age);
        switch (_screen)
        {
            case UiScreen.Menu: MenuScreen(); break;
            case UiScreen.Lobby: LobbyScreen(); break;
            case UiScreen.Settings: SettingsScreen(); break;
            case UiScreen.Garage: GarageScreen(); break;
            case UiScreen.Tracks: TracksScreen(); break;
            case UiScreen.Locations: LocationsScreen(); break;
            case UiScreen.Modes: ModesScreen(); break;
            case UiScreen.Pause: PauseScreen(); break;
        }

        _ui.EndScreen();
    }

    private UiScreen _lastScreen = (UiScreen)(-1);
    private float _screenAt;

    /// <summary>Safe area (notches, rounded corners) in GUI coordinates with a margin.</summary>
    private Rect SafeRect()
    {
        var safe = Screen.safeArea;
        var margin = U(12f);
        return new Rect(safe.x + margin, Screen.height - safe.yMax + margin, safe.width - margin * 2f, safe.height - margin * 2f);
    }

    // ------------------------------------------------------------------ main menu

    private bool _vipOpen;

    private void MenuScreen()
    {
        var area = SafeRect();
        _ui.Fill(new Rect(0f, 0f, W, H), new Color(0.03f, 0.04f, 0.06f, _cameraFrameCount > 0 ? 0.7f : 1f));
        if (_vipOpen)
        {
            VipPanel(area);
            return;
        }

        // Top bar: coins and VIP side by side in the top-right corner; everything else starts below it.
        var topBar = U(44f);
        var vipRect = new Rect(area.xMax - U(100f), area.y, U(100f), U(36f));
        if (PlayerProfile.Vip)
        {
            _ui.Panel(vipRect);
            _ui.Icon(new Rect(vipRect.x + U(8f), vipRect.center.y - U(11f), U(22f), U(22f)), UiIcon.Trophy, UiKit.AccentColor);
            _ui.TextFit(new Rect(vipRect.x + U(34f), vipRect.y, vipRect.width - U(40f), vipRect.height), "VIP x2", 14, TextAnchor.MiddleLeft, UiKit.AccentColor, true);
        }
        else if (_ui.Button(vipRect, "VIP", UiKit.Look.Accent, false, 14, UiIcon.Trophy))
        {
            _vipOpen = true;
        }

        // Layout: the player's car on a turntable as the hero image, logo, then the actions.
        Rect hero;
        Rect logoRect;
        Rect column;
        var buttonWidth = Mathf.Min(U(320f), area.width);
        if (Landscape)
        {
            var left = area.width * 0.54f;
            // Never wider than the space right of the hero image.
            buttonWidth = Mathf.Min(buttonWidth, area.width - left - U(16f));
            hero = new Rect(area.x, area.y + area.height * 0.22f, left, area.height * 0.78f);
            logoRect = new Rect(area.x, area.y, left, area.height * 0.34f);
            column = new Rect(area.x + left + (area.width - left - buttonWidth) * 0.5f, area.y + topBar, buttonWidth, area.height - topBar);
        }
        else
        {
            logoRect = new Rect(area.x, area.y + topBar, area.width, area.height * 0.24f - topBar * 0.5f);
            hero = new Rect(area.x, logoRect.yMax - U(10f), area.width, area.height * 0.30f);
            column = new Rect(area.center.x - buttonWidth * 0.5f, hero.yMax, buttonWidth, area.yMax - hero.yMax);
        }

        if (!_ui.Drawing)
        {
            if (_garage == null) _garage = new GaragePreview();
            _garage.Show(GameSettings.CarType, GameSettings.CarColor, hero.size, Time.unscaledDeltaTime, 0f);
        }

        if (_garage != null && _garage.Texture != null) _ui.Image(hero, _garage.Texture);
        _ui.Image(Inset(logoRect, U(4f)), _logo);

        const int rows = 4;
        var gap = U(10f);
        var rowHeight = Mathf.Min(U(56f), (column.height - U(40f)) / rows - gap);
        var y = column.y + (column.height - rows * (rowHeight + gap) - U(26f)) * 0.5f;
        var arLabel = !_arChecked ? "CHECKING AR..." : _arSupported ? "DRIVE IN YOUR ROOM" : "AR NOT SUPPORTED";
        if (_ui.Button(new Rect(column.x, y, column.width, rowHeight), arLabel, _arSupported ? UiKit.Look.Primary : UiKit.Look.Ghost, false, 17, UiIcon.Play)
            && _arSupported)
            OpenGamePicker(true);
        y += rowHeight + gap;
        if (_ui.Button(new Rect(column.x, y, column.width, rowHeight), "PLAY WITHOUT AR", UiKit.Look.Accent, false, 17, UiIcon.Home))
            OpenGamePicker(false);
        y += rowHeight + gap;
        if (_ui.Button(new Rect(column.x, y, column.width, rowHeight), "MULTIPLAYER", UiKit.Look.Normal, false, 17, UiIcon.People))
        {
            _lobbyUseAR = _arSupported;
            _screen = UiScreen.Lobby;
        }

        y += rowHeight + gap;
        var pair = (column.width - gap) * 0.5f;
        if (_ui.Button(new Rect(column.x, y, pair, rowHeight), "GARAGE", UiKit.Look.Normal, false, 16, UiIcon.Car))
            OpenGarage(UiScreen.Menu);
        if (_ui.Button(new Rect(column.x + pair + gap, y, pair, rowHeight), "SETTINGS", UiKit.Look.Normal, false, 16, UiIcon.Gear))
            OpenSettings(UiScreen.Menu);
        y += rowHeight + U(10f);
        _ui.Text(new Rect(column.x, y, column.width, U(20f)), GameSettings.PlayerName + "  •  " + GameSettings.CarColorNames[GameSettings.CarColor]
            + " " + CarCatalog.Get(GameSettings.CarType).Name, 12, TextAnchor.MiddleCenter, UiKit.TextDim);
        CoinChip(new Rect(vipRect.x - U(138f), area.y, U(130f), U(36f)));
    }

    /// <summary>The VIP pass offer, plus restoring purchases on a new phone.</summary>
    private void VipPanel(Rect area)
    {
        var panel = CenterPanel(area, U(420f), Mathf.Min(area.height, U(370f)));
        _ui.Panel(panel);
        var inner = Inset(panel, U(18f));
        var y = inner.y;
        _ui.Icon(new Rect(inner.center.x - U(24f), y, U(48f), U(48f)), UiIcon.Trophy, UiKit.AccentColor);
        y += U(54f);
        _ui.Text(new Rect(inner.x, y, inner.width, U(32f)), "VIP PASS", 24, TextAnchor.MiddleCenter, Color.white, true);
        y += U(40f);
        foreach (var line in new[] { "Double coins from every race, battle and pickup", "No ads, ever", "One-time purchase, yours for good" })
        {
            _ui.Icon(new Rect(inner.x, y + U(4f), U(18f), U(18f)), UiIcon.Coin, UiKit.AccentColor);
            _ui.Text(new Rect(inner.x + U(26f), y, inner.width - U(26f), U(26f)), line, 14, TextAnchor.MiddleLeft, Color.white);
            y += U(30f);
        }

        if (GameStore.Message != null && Time.unscaledTime - GameStore.MessageAt < 4f)
            _ui.Text(new Rect(inner.x, y, inner.width, U(22f)), GameStore.Message, 12, TextAnchor.MiddleCenter, UiKit.TextDim);

        var buttonY = inner.yMax - U(100f);
        if (PlayerProfile.Vip)
            _ui.Text(new Rect(inner.x, buttonY, inner.width, U(46f)), "You are VIP. Thank you!", 16, TextAnchor.MiddleCenter, UiKit.AccentColor, true);
        else if (_ui.Button(new Rect(inner.x, buttonY, inner.width, U(46f)),
                     GameStore.Busy ? "PLEASE WAIT..." : "GET VIP  " + GameStore.Price(GameStore.VipProduct), UiKit.Look.Primary, false, 17, UiIcon.Trophy))
            GameStore.Buy(GameStore.VipProduct);

        var half = (inner.width - U(10f)) * 0.5f;
        if (_ui.Button(new Rect(inner.x, inner.yMax - U(44f), half, U(44f)), "RESTORE", UiKit.Look.Ghost, false, 13, UiIcon.Restart))
            GameStore.Restore();
        if (_ui.Button(new Rect(inner.x + half + U(10f), inner.yMax - U(44f), half, U(44f)), "CLOSE", UiKit.Look.Normal, false, 13, UiIcon.Back))
            _vipOpen = false;
    }

    // ------------------------------------------------------------------ garage

    private GaragePreview _garage;
    private UiScreen _garageReturn = UiScreen.Menu;
    private float _garageDrag;
    private Vector2 _garageLastTouch;
    private bool _garageDragging;

    private int _garageView;
    private int _garageTab;
    private string _garageNote;
    private float _garageNoteAt = -10f;
    private bool _garageNoteBad;

    private void OpenGarage(UiScreen returnTo)
    {
        _garageReturn = returnTo;
        _screen = UiScreen.Garage;
        _garageView = GameSettings.CarType;
        _garageTab = 0;
        _garageNote = null;
        if (_garage == null) _garage = new GaragePreview();
    }

    private void CloseGarage()
    {
        _garage?.Release();
        _screen = _garageReturn;
        GameSettings.Save();
        // A new car, upgrade or look takes effect with a fresh round (host or offline only).
        if (_arena != null && !IsClient && _player != null
            && (_player.SpecIndex != GameSettings.CarType || _player.Build != PlayerProfile.BuildOf(GameSettings.CarType)
                || _player.ColorIndex != GameSettings.CarColor))
            StartRound();
    }

    private void GarageNote(string text, bool bad)
    {
        _garageNote = text;
        _garageNoteAt = Time.unscaledTime;
        _garageNoteBad = bad;
        if (_ui.Drawing) return;
        if (bad) GameAudio.Instance.Play(GameAudio.Sfx.Bump, 0.4f);
        else GameAudio.Instance.Play(GameAudio.Sfx.Coin);
    }

    /// <summary>Coin balance: a small panel with the coin icon.</summary>
    private void CoinChip(Rect rect)
    {
        _ui.Panel(rect);
        _ui.Icon(new Rect(rect.x + U(8f), rect.center.y - U(11f), U(22f), U(22f)), UiIcon.Coin, UiKit.AccentColor);
        _ui.TextFit(new Rect(rect.x + U(34f), rect.y, rect.width - U(40f), rect.height), PlayerProfile.Coins.ToString("N0"), 15, TextAnchor.MiddleLeft, Color.white, true);
    }

    private void GarageScreen()
    {
        _ui.Fill(new Rect(0f, 0f, W, H), new Color(0.03f, 0.04f, 0.06f, 1f));
        var area = SafeRect();
        Rect preview;
        Rect info;
        if (Landscape)
        {
            preview = new Rect(area.x, area.y + U(40f), area.width * 0.54f, area.height - U(40f));
            info = new Rect(preview.xMax + U(14f), area.y + U(44f), area.width - preview.width - U(14f), area.height - U(44f));
        }
        else
        {
            // The info panel takes what its content needs; the car gets the rest of the screen.
            var infoHeight = Mathf.Min(U(390f), area.height * 0.6f);
            preview = new Rect(area.x, area.y + U(40f), area.width, area.height - U(50f) - infoHeight);
            info = new Rect(area.x, area.yMax - infoHeight, area.width, infoHeight);
        }

        var garageCoins = new Rect(area.xMax - U(110f) - U(8f) - U(112f), area.y, U(112f), U(36f));
        _ui.TextFit(new Rect(area.x, area.y, garageCoins.x - area.x - U(8f), U(32f)), "GARAGE", 22, TextAnchor.MiddleLeft, Color.white, true);
        if (_ui.Button(new Rect(area.xMax - U(110f), area.y, U(110f), U(36f)), "DONE", UiKit.Look.Primary)) CloseGarage();
        CoinChip(garageCoins);

        var count = CarCatalog.All.Length;
        var index = Mathf.Clamp(_garageView, 0, count - 1);
        var owned = PlayerProfile.Owns(index);
        var baseSpec = CarCatalog.Get(index);
        var spec = PlayerProfile.Spec(index);
        var look = PlayerProfile.Look(index);

        // Turntable render (input pass only: rendering belongs in Update, not OnGUI).
        if (!_ui.Drawing && _garage != null)
        {
            var drag = 0f;
            var fingers = _ui.FreeFingers(out var finger, out _);
            if (fingers == 1 && preview.Contains(finger))
            {
                if (_garageDragging) drag = (finger.x - _garageLastTouch.x) * 0.4f;
                _garageLastTouch = finger;
                _garageDragging = true;
            }
            else _garageDragging = false;

            _garageDrag = drag;
            _garage.Show(index, look.Paint, PlayerProfile.BuildOf(index), preview.size, Time.unscaledDeltaTime, drag);
        }

        if (_garage != null && _garage.Texture != null) _ui.Image(preview, _garage.Texture, owned ? 1f : 0.55f);
        if (!owned)
            _ui.Text(new Rect(preview.x, preview.y + U(8f), preview.width, U(30f)), PlayerProfile.IsPremium(index) ? "PREMIUM" : "LOCKED", 18, TextAnchor.MiddleCenter,
                PlayerProfile.IsPremium(index) ? UiKit.AccentColor : Color.white, true, true);

        // Arrows browse the cars; browsing onto a car you own selects it.
        var arrow = U(54f);
        var next = index;
        if (_ui.IconButton(new Rect(preview.x, preview.center.y - arrow * 0.5f, arrow, arrow), UiIcon.Back)) next = (index + count - 1) % count;
        if (_ui.IconButton(new Rect(preview.xMax - arrow, preview.center.y - arrow * 0.5f, arrow, arrow), UiIcon.Play)) next = (index + 1) % count;
        if (next != index)
        {
            _garageView = next;
            if (PlayerProfile.Owns(next)) GameSettings.CarType = next;
            else _garageTab = 0;
            _garageNote = null;
        }

        // Page dots (the selected car's dot is orange, locked cars are faint) and a hint.
        var dot = U(8f);
        var dotsWidth = count * dot * 2f;
        for (var i = 0; i < count; i++)
            _ui.Fill(new Rect(preview.center.x - dotsWidth * 0.5f + i * dot * 2f, preview.yMax - U(30f), dot, dot * 0.5f),
                i == index ? UiKit.AccentColor : PlayerProfile.Owns(i) ? new Color(1f, 1f, 1f, 0.45f) : new Color(1f, 1f, 1f, 0.14f));
        _ui.Text(new Rect(preview.x, preview.yMax - U(22f), preview.width, U(18f)), "drag to rotate", 11, TextAnchor.MiddleCenter, UiKit.TextDim);

        _ui.Panel(info);
        var inner = Inset(info, U(14f));
        var y = inner.y;
        _ui.Text(new Rect(inner.x, y, inner.width, U(28f)), spec.Name.ToUpperInvariant(), 21, TextAnchor.MiddleLeft, Color.white, true);
        var selected = owned && GameSettings.CarType == index;
        _ui.Text(new Rect(inner.x, y, inner.width, U(28f)), selected ? "SELECTED" : owned ? "OWNED" : "", 11, TextAnchor.MiddleRight, selected ? new Color(0.45f, 1f, 0.5f) : UiKit.TextDim, true);
        y += U(26f);
        _ui.Text(new Rect(inner.x, y, inner.width, U(20f)), spec.Class, 13, TextAnchor.MiddleLeft, UiKit.AccentColor);
        y += U(26f);

        // The note line (purchases, not enough coins) sits at the bottom of the panel.
        var noteHeight = U(20f);
        var bottom = inner.yMax - noteHeight;
        if (GameStore.MessageAt > _garageNoteAt && Time.unscaledTime - GameStore.MessageAt < 4f)
            _ui.Text(new Rect(inner.x, bottom, inner.width, noteHeight), GameStore.Message, 12, TextAnchor.MiddleCenter, Color.white, true);
        else if (_garageNote != null && Time.unscaledTime - _garageNoteAt < 2.5f)
            _ui.Text(new Rect(inner.x, bottom, inner.width, noteHeight), _garageNote, 12, TextAnchor.MiddleCenter,
                _garageNoteBad ? new Color(1f, 0.45f, 0.4f) : new Color(0.45f, 1f, 0.5f), true);

        if (!owned)
        {
            GarageStats(inner.x, ref y, inner.width, bottom - U(56f) - y, baseSpec, spec);
            if (PlayerProfile.IsPremium(index))
            {
                var product = GameStore.ProductForCar(index);
                var label = GameStore.Busy ? "PLEASE WAIT..." : "BUY  " + GameStore.Price(product);
                if (_ui.Button(new Rect(inner.x, bottom - U(50f), inner.width, U(46f)), label, UiKit.Look.Primary, false, 16, UiIcon.Car))
                    GameStore.Buy(product);
                return;
            }

            var price = PlayerProfile.CarPrice(index);
            var affordable = PlayerProfile.Coins >= price;
            if (_ui.Button(new Rect(inner.x, bottom - U(50f), inner.width, U(46f)), "BUY  " + price.ToString("N0"),
                    affordable ? UiKit.Look.Primary : UiKit.Look.Ghost, false, 16, UiIcon.Coin))
            {
                if (PlayerProfile.TryBuyCar(index))
                {
                    GameSettings.CarType = index;
                    GarageNote(spec.Name + " is yours!", false);
                }
                else GarageNote("Not enough coins: race and collect coins to earn more.", true);
            }

            return;
        }

        _garageTab = _ui.Segmented(new Rect(inner.x, y, inner.width, U(34f)), new[] { "STATS", "TUNE", "STYLE" }, _garageTab, 12);
        y += U(42f);
        var room = bottom - y;
        if (_garageTab == 0) GarageStats(inner.x, ref y, inner.width, room, baseSpec, spec);
        else if (_garageTab == 1) GarageTune(index, inner.x, y, inner.width, room);
        else GarageStyle(index, look, baseSpec.Body, inner.x, y, inner.width, room);
    }

    private void GarageStats(float x, ref float y, float width, float room, CarSpec baseSpec, CarSpec spec)
    {
        var barRow = Mathf.Clamp((room - U(28f)) / 4f - U(4f), U(16f), U(28f));
        StatBar(x, ref y, width, barRow, "Speed", spec.SpeedRating, baseSpec.SpeedRating);
        StatBar(x, ref y, width, barRow, "Acceleration", spec.AccelRating, baseSpec.AccelRating);
        StatBar(x, ref y, width, barRow, "Handling", spec.HandlingRating, baseSpec.HandlingRating);
        StatBar(x, ref y, width, barRow, "Weight", spec.WeightRating, spec.WeightRating);
        var trait = spec.Drift > 0.6f ? "Loves to drift." : spec.Mass > 1.3f ? "Heavy: hard to push out in battle." : spec.OffRoad > 0.85f ? "Keeps its speed off the road." : spec.Grip > 13f ? "Glued to the road." : "Balanced.";
        _ui.Text(new Rect(x, y, width, U(20f)), trait, 12, TextAnchor.MiddleLeft, UiKit.TextDim);
        y += U(24f);
    }

    private static readonly string[] UpgradeNames = { "ENGINE", "TYRES", "WEIGHT" };
    private static readonly string[] UpgradeHints = { "top speed + acceleration", "grip + steering + off-road", "acceleration + steering, lighter in battle" };

    private void GarageTune(int car, float x, float y, float width, float room)
    {
        var row = Mathf.Clamp(room / 3f - U(6f), U(40f), U(64f));
        var buttonWidth = Mathf.Min(U(130f), width * 0.38f);
        for (var i = 0; i < 3; i++)
        {
            var upgrade = (PlayerProfile.Upgrade)i;
            var level = PlayerProfile.Level(car, upgrade);
            var cost = PlayerProfile.UpgradeCost(car, upgrade);
            _ui.Text(new Rect(x, y, width - buttonWidth, row * 0.5f), UpgradeNames[i], 15, TextAnchor.LowerLeft, Color.white, true);
            _ui.Text(new Rect(x, y + row * 0.5f, width - buttonWidth - U(8f), row * 0.5f), UpgradeHints[i], 11, TextAnchor.UpperLeft, UiKit.TextDim);
            // Level pips.
            var pip = U(16f);
            for (var p = 0; p < CarTune.MaxLevel; p++)
                _ui.Fill(new Rect(x + U(92f) + p * (pip + U(4f)), y + row * 0.5f - U(12f), pip, U(6f)),
                    p < level ? new Color(0.45f, 1f, 0.5f) : new Color(1f, 1f, 1f, 0.14f));

            var buttonRect = new Rect(x + width - buttonWidth, y + (row - U(40f)) * 0.5f, buttonWidth, U(40f));
            if (cost <= 0)
            {
                _ui.Button(buttonRect, "MAX", UiKit.Look.Ghost, false, 13);
            }
            else if (_ui.Button(buttonRect, cost.ToString("N0"), PlayerProfile.Coins >= cost ? UiKit.Look.Accent : UiKit.Look.Ghost, false, 14, UiIcon.Coin))
            {
                if (PlayerProfile.TryUpgrade(car, upgrade)) GarageNote(UpgradeNames[i] + " level " + (level + 1) + " fitted", false);
                else GarageNote("Not enough coins.", true);
            }

            y += row + U(6f);
        }
    }

    private void GarageStyle(int car, CarLook look, CarBody body, float x, float y, float width, float room)
    {
        // Paint: two rows of swatches.
        var colors = GameSettings.CarColors.Length;
        var perRow = (colors + 1) / 2;
        var gap = U(6f);
        var rows = body == CarBody.Buggy ? 0 : 4;
        var swatch = Mathf.Min(U(32f), (width - gap * (perRow - 1)) / perRow, (room - rows * U(32f)) / 2f - gap);
        for (var i = 0; i < colors; i++)
        {
            var rect = new Rect(x + (i % perRow) * (swatch + gap), y + (i / perRow) * (swatch + gap), swatch, swatch);
            if (_ui.Swatch(rect, GameSettings.CarColors[i], look.Paint == i)) PlayerProfile.SetPaint(car, i);
        }

        y += (swatch + gap) * 2f + U(4f);
        if (body == CarBody.Buggy)
        {
            _ui.Text(new Rect(x, y, width, U(36f)), "The RC buggy comes in any paint.\nRoad cars also take finishes, rims, wings and stripes.", 12, TextAnchor.UpperLeft, UiKit.TextDim);
            return;
        }

        var row = Mathf.Clamp((room - (swatch + gap) * 2f - U(4f)) / 4f - U(4f), U(26f), U(36f));
        StyleRow(car, PlayerProfile.Option.Finish, "FINISH", CarLook.FinishNames, look.Finish, x, ref y, width, row);
        StyleRow(car, PlayerProfile.Option.Rim, "RIMS", CarLook.RimNames, look.Rim, x, ref y, width, row);
        StyleRow(car, PlayerProfile.Option.Spoiler, "WING", CarLook.SpoilerNames, look.Spoiler, x, ref y, width, row);
        StyleRow(car, PlayerProfile.Option.Stripes, "STRIPES", new[] { "OFF", "ON" }, look.Stripes ? 1 : 0, x, ref y, width, row);
    }

    /// <summary>One look option as a segmented control; locked choices show their price and buy on tap.</summary>
    private void StyleRow(int car, PlayerProfile.Option option, string label, string[] names, int current, float x, ref float y, float width, float row)
    {
        var labels = new string[names.Length];
        for (var i = 0; i < names.Length; i++)
            labels[i] = PlayerProfile.Unlocked(car, option, i) ? names[i] : names[i] + " " + PlayerProfile.OptionCost(option, i);
        _ui.Text(new Rect(x, y, U(72f), row), label, 12, TextAnchor.MiddleLeft, UiKit.TextDim, true);
        var choice = _ui.Segmented(new Rect(x + U(74f), y, width - U(74f), row), labels, current, 10);
        if (choice != current)
        {
            var wasLocked = !PlayerProfile.Unlocked(car, option, choice);
            if (PlayerProfile.TrySetOption(car, option, choice))
            {
                if (wasLocked) GarageNote(names[choice] + " unlocked", false);
            }
            else GarageNote("Not enough coins.", true);
        }

        y += row + U(4f);
    }

    // ------------------------------------------------------------------ tracks and arenas

    /// <summary>"S 2.0 m" etc.: the floor square each track size needs.</summary>
    private static readonly string[] SizeLabels =
    {
        "S " + (RaceTrack.SizeHalfExtent[0] * 2f).ToString("0.0") + " m",
        "M " + (RaceTrack.SizeHalfExtent[1] * 2f).ToString("0.0") + " m",
        "L " + (RaceTrack.SizeHalfExtent[2] * 2f).ToString("0.0") + " m"
    };

    private void TracksScreen()
    {
        _ui.Fill(new Rect(0f, 0f, W, H), new Color(0.03f, 0.04f, 0.06f, 0.96f));
        var area = SafeRect();
        _ui.TextFit(new Rect(area.x, area.y, area.width - U(120f), U(32f)), "TRACKS & ARENAS", 22, TextAnchor.MiddleLeft, Color.white, true);
        if (_ui.Button(new Rect(area.xMax - U(110f), area.y, U(110f), U(36f)), "DONE", UiKit.Look.Primary))
        {
            GameSettings.Save();
            _screen = UiScreen.Pause;
            var track = RaceTrack.Shared;
            if (track.Layout != GameSettings.Track || track.Size != GameSettings.TrackSize
                || _arenaShape != GameSettings.Arena || _arenaSize != GameSettings.ArenaSize)
            {
                ApplyTrackChoice();
                StartRound();
            }
        }

        Rect list;
        Rect map;
        if (Landscape)
        {
            // Wide enough for the size and arena buttons on small phones, never more than half the screen.
            var listWidth = Mathf.Max(area.width * 0.38f, Mathf.Min(U(320f), area.width * 0.5f));
            list = new Rect(area.x, area.y + U(44f), listWidth, area.height - U(44f));
            map = new Rect(list.xMax + U(16f), list.y, area.width - list.width - U(16f), list.height);
        }
        else
        {
            map = new Rect(area.x, area.y + U(44f), area.width, area.height * 0.42f);
            list = new Rect(area.x, map.yMax + U(10f), area.width, area.yMax - map.yMax - U(10f));
        }

        // Layout list, then size and arena choices underneath.
        var y = list.y;
        // Layouts in two columns.
        var layouts = RaceTrack.LayoutNames.Length;
        var rows = (layouts + 1) / 2;
        var row = Mathf.Min(U(42f), (list.height - U(150f)) / rows - U(6f));
        var column = (list.width - U(6f)) * 0.5f;
        for (var i = 0; i < layouts; i++)
        {
            var preview = RaceTrack.Get((TrackLayout)i, GameSettings.TrackSize);
            var label = RaceTrack.LayoutNames[i] + "  " + Mathf.RoundToInt(preview.Length) + " m";
            var cell = new Rect(list.x + (i % 2) * (column + U(6f)), y + (i / 2) * (row + U(6f)), column, row);
            if (_ui.Button(cell, label, UiKit.Look.Normal, (int)GameSettings.Track == i, 14))
                GameSettings.Track = (TrackLayout)i;
        }

        y += rows * (row + U(6f));

        y += U(6f);
        _ui.Text(new Rect(list.x, y, U(90f), U(36f)), "Track size", 13, TextAnchor.MiddleLeft, Color.white);
        GameSettings.TrackSize = (TrackSize)_ui.Segmented(new Rect(list.x + U(96f), y, list.width - U(96f), U(36f)),
            SizeLabels, (int)GameSettings.TrackSize, 12);
        y += U(42f);
        if (GameSettings.TrackSize < RaceTrack.MinSize(GameSettings.Track))
        {
            _ui.Text(new Rect(list.x, y - U(6f), list.width, U(16f)), RaceTrack.LayoutNames[(int)GameSettings.Track] + " needs at least size " + RaceTrack.MinSize(GameSettings.Track) + ": that size will be used.", 11, TextAnchor.MiddleLeft, UiKit.AccentColor);
            y += U(14f);
        }
        _ui.Text(new Rect(list.x, y, U(90f), U(36f)), "Battle", 13, TextAnchor.MiddleLeft, Color.white);
        var half = (list.width - U(96f) - U(8f)) * 0.5f;
        GameSettings.Arena = (ArenaShape)_ui.Segmented(new Rect(list.x + U(96f), y, half, U(36f)), new[] { "RING", "SQUARE" }, (int)GameSettings.Arena, 12);
        GameSettings.ArenaSize = _ui.Segmented(new Rect(list.x + U(96f) + half + U(8f), y, half, U(36f)), BattleArena.SizeNames, GameSettings.ArenaSize, 12);

        // Mini map of the chosen layout: dots along the centre line.
        _ui.Panel(map, new Color(0.08f, 0.10f, 0.13f, 0.9f));
        var chosen = RaceTrack.Get(GameSettings.Track, GameSettings.TrackSize);
        var line = chosen.CenterLine;
        var extent = RaceTrack.SizeHalfExtent[(int)GameSettings.TrackSize];
        var box = Mathf.Min(map.width, map.height) - U(40f);
        var origin = map.center;
        var step = Mathf.Max(1, line.Count / 90);
        var dot = U(5f);
        for (var i = 0; i < line.Count; i += step)
        {
            var p = line[i] / extent * box * 0.5f;
            _ui.Fill(new Rect(origin.x + p.x - dot * 0.5f, origin.y - p.y - dot * 0.5f, dot, dot), i == 0 ? Color.white : UiKit.AccentColor);
        }

        // Stunts: yellow ramps, blue loops, and a count above the floor size.
        var ramps = 0;
        var loops = 0;
        var marker = U(12f);
        foreach (var stunt in chosen.Stunts)
        {
            var isLoop = stunt.Kind == StuntKind.Loop;
            if (isLoop) loops++;
            else ramps++;
            var p = stunt.Point / extent * box * 0.5f;
            _ui.RoundedFill(new Rect(origin.x + p.x - marker * 0.5f, origin.y - p.y - marker * 0.5f, marker, marker),
                isLoop ? new Color(0.25f, 0.55f, 1f) : new Color(1f, 0.80f, 0.15f));
        }

        if (ramps + loops > 0)
        {
            var parts = (loops > 0 ? loops + (loops == 1 ? " loop" : " loops") : "") + (loops > 0 && ramps > 0 ? "  •  " : "")
                        + (ramps > 0 ? ramps + (ramps == 1 ? " jump ramp" : " jump ramps") : "");
            _ui.TextFit(new Rect(map.x + U(8f), map.yMax - U(46f), map.width - U(16f), U(20f)), parts, 12, TextAnchor.MiddleCenter, Color.white);
        }

        _ui.Text(new Rect(map.x, map.yMax - U(26f), map.width, U(22f)),
            "Needs about " + (extent * 2f).ToString("0.0") + " x " + (extent * 2f).ToString("0.0") + " m of floor", 12, TextAnchor.MiddleCenter, UiKit.TextDim);
    }

    private void StatBar(float x, ref float y, float width, float row, string label, int rating, int baseRating = -1)
    {
        if (baseRating < 0) baseRating = rating;
        _ui.Text(new Rect(x, y, U(100f), row), label, 13, TextAnchor.MiddleLeft, Color.white);
        var barX = x + U(104f);
        var barWidth = width - U(104f);
        var segment = barWidth / 10f;
        for (var i = 0; i < 10; i++)
            _ui.Fill(new Rect(barX + i * segment, y + row * 0.3f, segment - U(3f), row * 0.4f),
                i < Mathf.Min(rating, baseRating) ? UiKit.AccentColor : i < rating ? new Color(0.45f, 1f, 0.5f) : new Color(1f, 1f, 1f, 0.12f));
        y += row + U(4f);
    }

    private static Rect Inset(Rect r, float by) => new Rect(r.x + by, r.y + by, r.width - by * 2f, r.height - by * 2f);

    // ------------------------------------------------------------------ lobby

    private void LobbyScreen()
    {
        _ui.Fill(new Rect(0f, 0f, W, H), new Color(0.02f, 0.04f, 0.07f, 0.9f));
        var area = SafeRect();
        var panel = CenterPanel(area, U(560f), Mathf.Min(area.height, U(470f)));
        _ui.Panel(panel);
        var inner = Inset(panel, U(16f));
        var y = inner.y;
        _ui.Text(new Rect(inner.x, y, inner.width, U(28f)), "MULTIPLAYER", 20, TextAnchor.MiddleLeft, Color.white, true);
        y += U(28f);
        const string lobbyIntro = "Up to 4 players on the same Wi-Fi, or connect to one phone's hotspot. Each phone plays on its own floor or in its own virtual room; mixing both works.";
        var introHeight = _ui.TextHeight(lobbyIntro, 12, inner.width);
        _ui.Text(new Rect(inner.x, y, inner.width, introHeight), lobbyIntro, 12, TextAnchor.UpperLeft, UiKit.TextDim);
        y += introHeight + U(12f);

        var row = U(42f);
        if (_arena == null)
        {
            _ui.Text(new Rect(inner.x, y, U(110f), row), "Play area", 14, TextAnchor.MiddleLeft, Color.white);
            var choice = _ui.Segmented(new Rect(inner.x + U(110f), y, inner.width - U(110f), row),
                new[] { _arSupported ? "MY ROOM (AR)" : "AR N/A", "VIRTUAL ROOM" }, _lobbyUseAR ? 0 : 1);
            _lobbyUseAR = choice == 0 && _arSupported;
            y += row + U(10f);
            if (!_lobbyUseAR)
            {
                _ui.Text(new Rect(inner.x, y, U(110f), row), "Location", 14, TextAnchor.MiddleLeft, Color.white);
                GameSettings.Location = (GameLocation)_ui.Segmented(new Rect(inner.x + U(110f), y, inner.width - U(110f), row),
                    VirtualHouse.LocationNames, (int)GameSettings.Location, 12);
                y += row + U(10f);
            }
        }

        if (_net == null)
        {
            var half = (inner.width - U(10f)) * 0.5f;
            if (_ui.Button(new Rect(inner.x, y, half, U(52f)), "HOST A GAME", UiKit.Look.Primary, false, 16, UiIcon.People))
            {
                StartHosting();
                EnterPlayAreaForNetwork();
            }

            if (_ui.Button(new Rect(inner.x + half + U(10f), y, half, U(52f)), "JOIN A GAME", UiKit.Look.Accent, false, 16, UiIcon.Online))
                StartBrowsing();
            y += U(62f);
        }
        else if (IsClient)
        {
            var status = _net.RejectedFull ? "That game is full." : _net.Joining != null ? "Joining " + _net.Joining.Name + "..." : "Looking for games on this network...";
            _ui.Text(new Rect(inner.x, y, inner.width, U(22f)), status, 13, TextAnchor.MiddleLeft, UiKit.AccentColor);
            y += U(26f);
            foreach (var offer in _net.Offers)
            {
                if (y + U(46f) > inner.yMax - U(56f)) break;
                var label = offer.Name + "    " + offer.Players + "/" + LanSession.MaxPlayers + " players";
                if (_ui.Button(new Rect(inner.x, y, inner.width, U(44f)), label, UiKit.Look.Normal, _net.Joining == offer, 15))
                    _net.Join(offer);
                y += U(50f);
            }

            if (_net.Offers.Count == 0)
            {
                _ui.Text(new Rect(inner.x, y, inner.width, U(40f)), "Ask the host to open MULTIPLAYER > HOST A GAME. Both phones must be on the same Wi-Fi or hotspot.",
                    12, TextAnchor.UpperLeft, UiKit.TextDim);
            }

            if (_net.Slot > 0) EnterPlayAreaForNetwork();
        }

        var back = new Rect(inner.x, inner.yMax - U(44f), U(140f), U(44f));
        if (_ui.Button(back, IsClient ? "CANCEL" : "BACK", UiKit.Look.Ghost))
        {
            if (IsClient) LeaveNetwork(true);
            _screen = _arena != null ? UiScreen.Pause : UiScreen.Menu;
        }

        if (_ui.Button(new Rect(inner.xMax - U(140f), inner.yMax - U(44f), U(140f), U(44f)), "MY CAR", UiKit.Look.Ghost))
            OpenSettings(UiScreen.Lobby);
    }

    /// <summary>After hosting or joining: go to the chosen play area (or stay on the current one).</summary>
    private void EnterPlayAreaForNetwork()
    {
        if (_arena != null)
        {
            ClosePause();
            SetModeVisuals();
            StartRound();
            return;
        }

        if (_lobbyUseAR && _arSupported) BeginAR();
        else EnterVirtualMode();
    }

    // ------------------------------------------------------------------ game type and location pickers

    /// <summary>True when the game-type picker leads to AR (scan the real floor), false for a virtual location.</summary>
    private bool _pickForAR;

    private static readonly GameMode[] PickerModes = { GameMode.Free, GameMode.Race, GameMode.Battle };
    private static readonly string[] ModeTitles = { "FREE DRIVE", "RACE", "BATTLE" };
    private static readonly string[] ModeBlurbs =
    {
        "Drive anywhere at your own pace, collect coins and build obstacle courses.",
        "Five laps of a toy track against CPU rivals. Beat your best time.",
        "Ram rivals out of a shrinking ring. First to three knockouts wins."
    };
    private static readonly UiIcon[] ModeIcons = { UiIcon.Coin, UiIcon.Flag, UiIcon.Trophy };
    private static readonly Color[] ModeTints =
    {
        new Color(0.13f, 0.52f, 0.46f), new Color(0.90f, 0.42f, 0.08f), new Color(0.68f, 0.17f, 0.30f)
    };

    private const string LastModeKey = "ARCoinDrive.LastMode";

    /// <summary>Play from the menu: first the game type, then (without AR) the location.</summary>
    private void OpenGamePicker(bool ar)
    {
        _pickForAR = ar;
        _mode = (GameMode)Mathf.Clamp(PlayerPrefs.GetInt(LastModeKey, (int)GameMode.Free), 0, 2);
        _screen = UiScreen.Modes;
    }

    /// <summary>Step 1: what to play. Tapping a card goes straight on.</summary>
    private void ModesScreen()
    {
        _ui.Fill(new Rect(0f, 0f, W, H), new Color(0.03f, 0.04f, 0.06f, 1f));
        var area = SafeRect();
        PickerHeader(area, "CHOOSE A GAME", _pickForAR ? "In your room (AR)" : "Step 1 of 2", UiScreen.Menu);
        for (var i = 0; i < PickerModes.Length; i++)
        {
            var card = PickerCard(area, i, PickerModes.Length);
            if (!ChoiceCard(card, ModeTints[i], ModeIcons[i], ModeTitles[i], ModeBlurbs[i], _mode == PickerModes[i] ? "LAST PLAYED" : null))
                continue;
            _mode = PickerModes[i];
            PlayerPrefs.SetInt(LastModeKey, (int)_mode);
            PlayerPrefs.Save();
            if (_pickForAR) BeginAR();
            else _screen = UiScreen.Locations;
        }
    }

    private static readonly Color[] LocationTints =
    {
        new Color(0.62f, 0.42f, 0.24f), new Color(0.20f, 0.55f, 0.25f), new Color(0.30f, 0.38f, 0.52f)
    };

    private static readonly UiIcon[] LocationIcons = { UiIcon.Home, UiIcon.Flag, UiIcon.Car };

    /// <summary>Step 2 (without AR): where to drive. Tapping a card starts the game there.</summary>
    private void LocationsScreen()
    {
        _ui.Fill(new Rect(0f, 0f, W, H), new Color(0.03f, 0.04f, 0.06f, 1f));
        var area = SafeRect();
        var modeIndex = Mathf.Max(0, System.Array.IndexOf(PickerModes, _mode));
        PickerHeader(area, "CHOOSE A LOCATION", "Step 2 of 2  •  " + ModeTitles[modeIndex], UiScreen.Modes);
        var count = VirtualHouse.LocationNames.Length;
        for (var i = 0; i < count; i++)
        {
            var card = PickerCard(area, i, count);
            var last = (int)GameSettings.Location == i ? "LAST PLAYED" : null;
            if (!ChoiceCard(card, LocationTints[i], LocationIcons[i], VirtualHouse.LocationNames[i].ToUpperInvariant(), VirtualHouse.LocationBlurbs[i], last))
                continue;
            GameSettings.Location = (GameLocation)i;
            GameSettings.Save();
            EnterVirtualMode();
        }
    }

    /// <summary>Title, a small step line under it, and BACK.</summary>
    private void PickerHeader(Rect area, string title, string step, UiScreen back)
    {
        _ui.TextFit(new Rect(area.x, area.y, area.width - U(120f), U(32f)), title, 22, TextAnchor.MiddleLeft, Color.white, true);
        _ui.TextFit(new Rect(area.x, area.y + U(30f), area.width - U(120f), U(18f)), step, 12, TextAnchor.MiddleLeft, UiKit.AccentColor, true);
        if (_ui.Button(new Rect(area.xMax - U(110f), area.y, U(110f), U(36f)), "BACK", UiKit.Look.Ghost, false, 14, UiIcon.Back))
            _screen = back;
    }

    /// <summary>Card slot: a column of wide cards in portrait, a row of tall cards in landscape.</summary>
    private Rect PickerCard(Rect area, int index, int count)
    {
        var top = area.y + U(60f);
        var gap = U(12f);
        if (Landscape)
        {
            var width = (area.width - gap * (count - 1)) / count;
            return new Rect(area.x + index * (width + gap), top, width, area.yMax - top);
        }

        var height = Mathf.Min(U(150f), (area.yMax - top - gap * (count - 1)) / count);
        var maxWidth = Mathf.Min(area.width, U(560f));
        return new Rect(area.center.x - maxWidth * 0.5f, top + index * (height + gap), maxWidth, height);
    }

    /// <summary>
    /// Big tappable card for the pickers: a tinted tile with the icon, the title, a short description
    /// and a play arrow. Wide cards put the tile on the left, tall cards on top.
    /// </summary>
    private bool ChoiceCard(Rect card, Color tint, UiIcon icon, string title, string blurb, string badge)
    {
        var tapped = _ui.Button(card, null, UiKit.Look.Normal, false);
        var pad = U(10f);
        Rect tile;
        Rect text;
        var wide = card.width > card.height * 1.5f;
        if (wide)
        {
            var side = card.height - pad * 2f;
            tile = new Rect(card.x + pad, card.y + pad, Mathf.Min(side * 1.15f, card.width * 0.34f), side);
            text = new Rect(tile.xMax + U(14f), card.y + pad, card.xMax - tile.xMax - U(14f) - U(52f), card.height - pad * 2f);
        }
        else
        {
            tile = new Rect(card.x + pad, card.y + pad, card.width - pad * 2f, Mathf.Min(card.height * 0.48f, U(170f)));
            // Leave room on the right for the play arrow in the bottom corner.
            text = new Rect(card.x + U(16f), tile.yMax + U(10f), card.width - U(32f) - U(40f), card.yMax - tile.yMax - U(20f));
        }

        // Tile: colour, a lighter top half for depth, the icon in the middle.
        _ui.RoundedFill(tile, tint);
        _ui.RoundedFill(new Rect(tile.x, tile.y, tile.width, tile.height * 0.5f), new Color(1f, 1f, 1f, 0.10f));
        var iconSize = Mathf.Min(tile.height * 0.5f, tile.width * 0.5f, U(64f));
        _ui.Icon(new Rect(tile.center.x - iconSize * 0.5f, tile.center.y - iconSize * 0.5f, iconSize, iconSize), icon, Color.white);

        // Text block, vertically centred on wide cards.
        var blurbHeight = _ui.TextHeight(blurb, 12, text.width);
        var blockHeight = (badge != null ? U(18f) : 0f) + U(28f) + blurbHeight;
        var y = wide ? text.y + Mathf.Max(0f, (text.height - blockHeight) * 0.5f) : text.y;
        if (badge != null)
        {
            _ui.TextFit(new Rect(text.x, y, text.width, U(16f)), badge, 10, TextAnchor.MiddleLeft, UiKit.AccentColor, true);
            y += U(18f);
        }

        _ui.TextFit(new Rect(text.x, y, text.width, U(26f)), title, 18, TextAnchor.MiddleLeft, Color.white, true);
        y += U(28f);
        _ui.Text(new Rect(text.x, y, text.width, blurbHeight), blurb, 12, TextAnchor.UpperLeft, UiKit.TextDim);

        // Play arrow: right edge on wide cards, bottom right on tall ones.
        var arrow = U(36f);
        var arrowRect = wide
            ? new Rect(card.xMax - pad - arrow, card.center.y - arrow * 0.5f, arrow, arrow)
            : new Rect(card.xMax - U(16f) - arrow, card.yMax - U(14f) - arrow, arrow, arrow);
        _ui.RoundedFill(arrowRect, UiKit.AccentColor);
        var glyph = arrow * 0.5f;
        _ui.Icon(new Rect(arrowRect.center.x - glyph * 0.4f, arrowRect.center.y - glyph * 0.5f, glyph, glyph), UiIcon.Play, Color.white);
        return tapped;
    }

    private Rect CenterPanel(Rect area, float maxWidth, float height)
    {
        var width = Mathf.Min(maxWidth, area.width);
        return new Rect(area.center.x - width * 0.5f, area.y + (area.height - height) * 0.5f, width, height);
    }

    // ------------------------------------------------------------------ settings

    private void OpenSettings(UiScreen returnTo)
    {
        _settingsReturn = returnTo;
        _screen = UiScreen.Settings;
    }

    private void SettingsScreen()
    {
        _ui.Fill(new Rect(0f, 0f, W, H), new Color(0.02f, 0.04f, 0.07f, 0.9f));
        var area = SafeRect();
        var columns = Landscape ? 2 : 1;
        var rows = columns == 2 ? 6 : 12;
        // Panel fits its rows; rows shrink only when the screen is too short.
        var row = Mathf.Min(U(44f), (area.height - U(72f)) / rows - U(6f));
        var panel = CenterPanel(area, Landscape ? U(820f) : U(520f), Mathf.Min(area.height, U(72f) + rows * (row + U(6f))));
        _ui.Panel(panel);
        var inner = Inset(panel, U(16f));
        _ui.Text(new Rect(inner.x, inner.y, inner.width, U(28f)), "SETTINGS", 20, TextAnchor.MiddleLeft, Color.white, true);
        if (_ui.Button(new Rect(inner.xMax - U(110f), inner.y - U(4f), U(110f), U(36f)), "DONE", UiKit.Look.Primary))
        {
            GameSettings.Save();
            _screen = _settingsReturn;
        }

        var top = inner.y + U(40f);
        var gap = U(24f);
        var columnWidth = (inner.width - gap * (columns - 1)) / columns;
        var x = inner.x;
        var y = top;

        // Sound and feel
        GameSettings.MasterVolume = SliderRow(x, ref y, columnWidth, row, "Volume", GameSettings.MasterVolume);
        GameSettings.EngineVolume = SliderRow(x, ref y, columnWidth, row, "Motor sound", GameSettings.EngineVolume);
        GameSettings.EffectsVolume = SliderRow(x, ref y, columnWidth, row, "Effects", GameSettings.EffectsVolume);
        var vibration = SegmentRow(x, ref y, columnWidth, row, "Vibration", new[] { "ON", "OFF" }, GameSettings.Vibration ? 0 : 1);
        GameSettings.Vibration = vibration == 0;
        var steering = SegmentRow(x, ref y, columnWidth, row, "Steering", new[] { "BUTTONS", "TILT" }, (int)GameSettings.Steering);
        GameSettings.Steering = (SteeringSetting)steering;
        PrivacyRow(x, ref y, columnWidth, row);

        if (columns == 2)
        {
            x = inner.x + columnWidth + gap;
            y = top;
        }

        var orientation = SegmentRow(x, ref y, columnWidth, row, "Screen", new[] { "LANDSCAPE", "PORTRAIT", "AUTO" }, (int)GameSettings.Orientation);
        if (orientation != (int)GameSettings.Orientation)
        {
            GameSettings.Orientation = (OrientationSetting)orientation;
            GameSettings.ApplyOrientation();
        }

        GameSettings.Bots = SegmentRow(x, ref y, columnWidth, row, "CPU rivals", new[] { "0", "1", "2", "3" }, GameSettings.Bots);

        // Car colour swatches and name.
        _ui.Text(new Rect(x, y, U(100f), row), "Car colour", 14, TextAnchor.MiddleLeft, Color.white);
        var swatch = Mathf.Min(row - U(10f), (columnWidth - U(110f)) / GameSettings.CarColors.Length - U(6f));
        for (var i = 0; i < GameSettings.CarColors.Length; i++)
        {
            var rect = new Rect(x + U(110f) + i * (swatch + U(6f)), y + (row - swatch) * 0.5f, swatch, swatch);
            if (_ui.Swatch(rect, GameSettings.CarColors[i], GameSettings.CarColor == i)) GameSettings.CarColor = i;
        }

        y += row + U(6f);
        _ui.Text(new Rect(x, y, U(100f), row), "Name", 14, TextAnchor.MiddleLeft, Color.white);
        if (_ui.Button(new Rect(x + U(110f), y + U(3f), columnWidth - U(110f), row - U(6f)), GameSettings.PlayerName + "   (edit)", UiKit.Look.Ghost))
            EditName();
        y += row + U(6f);
        var debug = SegmentRow(x, ref y, columnWidth, row, "AR details", new[] { "HIDE", "SHOW" }, GameSettings.ShowDebug ? 1 : 0);
        GameSettings.ShowDebug = debug == 1;
        var saver = SegmentRow(x, ref y, columnWidth, row, "Frame rate", new[] { "60 FPS", "BATTERY 30" }, GameSettings.BatterySaver ? 1 : 0);
        if ((saver == 1) != GameSettings.BatterySaver)
        {
            GameSettings.BatterySaver = saver == 1;
            GameSettings.ApplyFrameRate();
        }
    }

    /// <summary>Privacy policy link and, where consent law applies, the ad consent choice.</summary>
    private void PrivacyRow(float x, ref float y, float width, float row)
    {
        var policy = !string.IsNullOrEmpty(GameAds.PrivacyPolicyUrl);
        var choices = GameAds.PrivacyOptionsRequired;
        if (!policy && !choices) return;
        _ui.Text(new Rect(x, y, U(100f), row), "Privacy", 14, TextAnchor.MiddleLeft, Color.white);
        var left = x + U(110f);
        var buttons = (policy ? 1 : 0) + (choices ? 1 : 0);
        var buttonWidth = (width - U(110f) - U(8f) * (buttons - 1)) / buttons;
        if (policy)
        {
            if (_ui.Button(new Rect(left, y + U(3f), buttonWidth, row - U(6f)), "POLICY", UiKit.Look.Ghost, false, 12))
                Application.OpenURL(GameAds.PrivacyPolicyUrl);
            left += buttonWidth + U(8f);
        }
        if (choices && _ui.Button(new Rect(left, y + U(3f), buttonWidth, row - U(6f)), "AD CHOICES", UiKit.Look.Ghost, false, 12))
            GameAds.ShowPrivacyOptions();
        y += row + U(6f);
    }

    private float SliderRow(float x, ref float y, float width, float row, string label, float value)
    {
        _ui.Text(new Rect(x, y, U(100f), row), label, 14, TextAnchor.MiddleLeft, Color.white);
        value = _ui.Slider(new Rect(x + U(110f), y, width - U(160f), row), value);
        _ui.Text(new Rect(x + width - U(46f), y, U(46f), row), Mathf.RoundToInt(value * 100f) + "%", 12, TextAnchor.MiddleRight, UiKit.TextDim);
        y += row + U(6f);
        return value;
    }

    private int SegmentRow(float x, ref float y, float width, float row, string label, string[] options, int selected)
    {
        _ui.Text(new Rect(x, y, U(100f), row), label, 14, TextAnchor.MiddleLeft, Color.white);
        selected = _ui.Segmented(new Rect(x + U(110f), y + U(3f), width - U(110f), row - U(6f)), options, selected, 12);
        y += row + U(6f);
        return selected;
    }

    private void EditName()
    {
        if (TouchScreenKeyboard.isSupported)
            _nameKeyboard = TouchScreenKeyboard.Open(GameSettings.PlayerName, TouchScreenKeyboardType.Default, false, false, false, false, "Your name", 14);
        else
            GameSettings.PlayerName = "Driver " + Random.Range(10, 99);
    }

    private void PollNameKeyboard()
    {
        if (_nameKeyboard == null || _ui.Drawing) return;
        if (_nameKeyboard.status == TouchScreenKeyboard.Status.Done)
        {
            GameSettings.PlayerName = _nameKeyboard.text;
            GameSettings.Save();
        }

        if (_nameKeyboard.status != TouchScreenKeyboard.Status.Visible) _nameKeyboard = null;
    }

    // ------------------------------------------------------------------ pause

    private void PauseScreen()
    {
        _ui.Fill(new Rect(0f, 0f, W, H), new Color(0f, 0f, 0f, 0.55f));
        var area = SafeRect();
        var columns = Landscape ? 2 : 1;
        // Height follows the buttons that are actually shown.
        var leftRows = 1 + (IsClient ? 0 : 2 + (_mode != GameMode.Free ? 1 : 0));
        var rightRows = (!_virtualMode && _arena != null ? 1 : 0) + 3;
        var rowCount = columns == 2 ? Mathf.Max(leftRows, rightRows) : leftRows + rightRows;
        var panel = CenterPanel(area, columns == 2 ? U(700f) : U(420f), Mathf.Min(area.height, U(72f) + rowCount * U(52f)));
        _ui.Panel(panel);
        var inner = Inset(panel, U(16f));
        _ui.Text(new Rect(inner.x, inner.y, inner.width, U(28f)), "PAUSED", 20, TextAnchor.MiddleLeft, Color.white, true);
        if (IsOnline)
            _ui.Text(new Rect(inner.x, inner.y, inner.width, U(28f)), IsHost ? "Hosting • " + _net.PlayerCount + "/" + LanSession.MaxPlayers + " players" : _net.Status,
                12, TextAnchor.MiddleRight, UiKit.AccentColor);

        var gap = U(20f);
        var columnWidth = (inner.width - gap * (columns - 1)) / columns;
        var row = U(44f);
        var x = inner.x;
        var y = inner.y + U(40f);

        if (_ui.Button(new Rect(x, y, columnWidth, row), "RESUME", UiKit.Look.Primary, false, 16, UiIcon.Play)) ClosePause();
        y += row + U(8f);

        if (!IsClient)
        {
            var modes = IsOnline ? new[] { "RACE", "BATTLE" } : new[] { "FREE", "RACE", "BATTLE" };
            var current = IsOnline ? (_mode == GameMode.Battle ? 1 : 0) : (_mode == GameMode.Free ? 0 : _mode == GameMode.Race ? 1 : 2);
            var picked = _ui.Segmented(new Rect(x, y, columnWidth, row), modes, current, 13);
            if (picked != current)
            {
                var mode = IsOnline ? (picked == 0 ? GameMode.Race : GameMode.Battle)
                    : (picked == 0 ? GameMode.Free : picked == 1 ? GameMode.Race : GameMode.Battle);
                ChangeMode(mode);
                ClosePause();
            }

            y += row + U(8f);
            if (_mode != GameMode.Free)
            {
                _ui.Text(new Rect(x, y, U(110f), row), "CPU rivals", 13, TextAnchor.MiddleLeft, Color.white);
                var bots = _ui.Segmented(new Rect(x + U(110f), y, columnWidth - U(110f), row), new[] { "0", "1", "2", "3" }, GameSettings.Bots, 13);
                if (bots != GameSettings.Bots)
                {
                    GameSettings.Bots = bots;
                    if (_state != GameState.Racing && _state != GameState.Countdown) StartRound();
                }

                y += row + U(8f);
            }

            var pairWidth = (columnWidth - U(8f)) * 0.5f;
            if (_ui.Button(new Rect(x, y, pairWidth, row), "RESTART", UiKit.Look.Normal, false, 15, UiIcon.Restart))
            {
                ClosePause();
                StartRound();
            }

            if (_ui.Button(new Rect(x + pairWidth + U(8f), y, pairWidth, row), "TRACKS", UiKit.Look.Accent, false, 15, UiIcon.Flag))
                _screen = UiScreen.Tracks;

            y += row + U(8f);
        }

        if (columns == 2)
        {
            x = inner.x + columnWidth + gap;
            y = inner.y + U(40f);
        }

        if (!_virtualMode && _arena != null && _ui.Button(new Rect(x, y, columnWidth, row), "PLACE ON A NEW SPOT"))
        {
            ResetToScanning();
            ClosePause();
            BeginAR();
        }

        if (!_virtualMode && _arena != null) y += row + U(8f);

        if (!IsOnline)
        {
            var half = (columnWidth - U(8f)) * 0.5f;
            if (_ui.Button(new Rect(x, y, half, row), "HOST GAME", UiKit.Look.Accent, false, 15, UiIcon.People))
            {
                StartHosting();
                EnterPlayAreaForNetwork();
            }

            if (_ui.Button(new Rect(x + half + U(8f), y, half, row), "JOIN GAME", UiKit.Look.Accent, false, 15, UiIcon.Online))
            {
                StartBrowsing();
                _screen = UiScreen.Lobby;
            }
        }
        else if (_ui.Button(new Rect(x, y, columnWidth, row), "LEAVE MULTIPLAYER", UiKit.Look.Danger))
        {
            LeaveNetwork(true);
            ClosePause();
        }

        y += row + U(8f);
        var halfWidth = (columnWidth - U(8f)) * 0.5f;
        if (_ui.Button(new Rect(x, y, halfWidth, row), "GARAGE", UiKit.Look.Normal, false, 15, UiIcon.Car)) OpenGarage(UiScreen.Pause);
        if (_ui.Button(new Rect(x + halfWidth + U(8f), y, halfWidth, row), "SETTINGS", UiKit.Look.Normal, false, 15, UiIcon.Gear)) OpenSettings(UiScreen.Pause);
        y += row + U(8f);
        if (_ui.Button(new Rect(x, y, columnWidth, row), "MAIN MENU", UiKit.Look.Ghost, false, 15, UiIcon.Home)) ReturnToMenu();
    }

    // ------------------------------------------------------------------ in-game HUD

    private void GameHud(bool interactive)
    {
        var area = SafeRect();
        var pauseSize = U(46f);
        var pauseRect = new Rect(area.xMax - pauseSize, area.y, pauseSize, pauseSize);
        if (_ui.IconButton(pauseRect, UiIcon.Pause) && interactive) OpenPause();

        if (_arena == null)
        {
            ScanningHud(area, interactive);
            return;
        }

        StatusChip(area, pauseRect);
        if (IsOnline)
        {
            var chip = new Rect(pauseRect.x - U(96f), area.y + U(6f), U(88f), U(34f));
            _ui.Panel(chip);
            var online = new Color(0.4f, 1f, 0.55f);
            _ui.Icon(new Rect(chip.x + U(8f), chip.y + U(7f), U(20f), U(20f)), UiIcon.Online, online);
            _ui.Text(new Rect(chip.x + U(30f), chip.y, chip.width - U(34f), chip.height), IsHost ? _net.PlayerCount + "/" + LanSession.MaxPlayers : "ON", 14, TextAnchor.MiddleLeft, online, true);
        }

        HealthBars();
        Standings(area, pauseRect);
        BigCenterText();
        Toasts(area);

        if (!interactive) return;
        var tools = ToolColumn(area);
        if (_buildMode)
        {
            BuildPalette(area);
        }
        else if (_state == GameState.Finished)
        {
            ResultsPanel(area);
        }
        else
        {
            DrivingControls(area);
        }

        if (GameSettings.ShowDebug) DebugLine(area);
        _ = tools;
    }

    private void ScanningHud(Rect area, bool interactive)
    {
        var width = Mathf.Min(U(520f), area.width - U(60f));
        var banner = new Rect(area.center.x - width * 0.5f, area.y, width, U(54f));
        _ui.Panel(banner);
        _ui.Text(Inset(banner, U(10f)), GetPlacingMessage(), 14, TextAnchor.MiddleCenter, Color.white);
        if (IsOnline)
            _ui.Text(new Rect(banner.x, banner.yMax + U(4f), banner.width, U(20f)), IsHost ? "Hosting: players can join now." : _net.Status,
                12, TextAnchor.MiddleCenter, UiKit.AccentColor, false, true);
        Toasts(area);
        if (!interactive) return;

        var buttonWidth = Mathf.Min(U(260f), area.width);
        var stuck = Time.unscaledTime - _scanStartTime > 6f || ARSession.state == ARSessionState.Unsupported;
        if (stuck && _ui.Button(new Rect(area.center.x - buttonWidth * 0.5f, area.yMax - U(48f), buttonWidth, U(48f)), "PLAY IN VIRTUAL ROOM", UiKit.Look.Accent))
            EnterVirtualMode();
        if (GameSettings.ShowDebug) DebugLine(area);
    }

    private void StatusChip(Rect area, Rect pauseRect)
    {
        // The standings sit below the pause button, so the chip may reach almost to it.
        var width = Mathf.Min(U(330f), pauseRect.x - area.x - U(10f));
        var chip = new Rect(area.x, area.y, width, U(52f));
        _ui.Panel(chip);
        var modeIcon = _mode == GameMode.Free ? UiIcon.Coin : _mode == GameMode.Battle ? UiIcon.Trophy : UiIcon.Flag;
        _ui.Icon(new Rect(chip.x + U(10f), chip.center.y - U(13f), U(26f), U(26f)), modeIcon, UiKit.AccentColor);
        var inner = new Rect(chip.x + U(44f), chip.y + U(8f), chip.width - U(52f), chip.height - U(16f));
        string title;
        string sub;
        var player = _player;
        if (_mode == GameMode.Free)
        {
            var speed = player != null ? Mathf.RoundToInt(Mathf.Abs(player.Speed) * 3.6f * RcScale) : 0;
            title = "FREE DRIVE";
            sub = "Coins " + (player != null ? player.Coins : 0) + "  •  " + speed + " km/h  •  bank " + PlayerProfile.Coins.ToString("N0");
        }
        else if (_mode == GameMode.Battle)
        {
            title = "BATTLE  •  RING " + Mathf.RoundToInt(_battleRadius * 200f) + " CM";
            sub = "HP " + (player != null ? Mathf.CeilToInt(player.Health) : 0) + "  •  wins " + (player != null ? player.Wins : 0) + "/" + BattleWinsNeeded;
        }
        else
        {
            var lap = player != null ? Mathf.Clamp(Mathf.FloorToInt(player.Laps) + 1, 1, LapCount) : 1;
            title = "LAP " + lap + "/" + LapCount + "   P" + PlayerPosition() + "/" + Mathf.Max(1, _cars.Count);
            sub = FormatTime(_raceTime) + "   •   best " + (_bestTime > 0f ? FormatTime(_bestTime) : "--");
        }

        _ui.TextFit(new Rect(inner.x, inner.y, inner.width, inner.height * 0.55f), title, 15, TextAnchor.MiddleLeft, Color.white, true);
        _ui.TextFit(new Rect(inner.x, inner.y + inner.height * 0.55f, inner.width, inner.height * 0.45f), sub, 12, TextAnchor.MiddleLeft, UiKit.TextDim);

        // Race progress along the bottom edge of the chip.
        if (_mode == GameMode.Race && player != null)
        {
            var progress = Mathf.Clamp01(player.Laps / LapCount);
            var bar = new Rect(chip.x + U(10f), chip.yMax - U(5f), chip.width - U(20f), U(3f));
            _ui.Fill(bar, new Color(1f, 1f, 1f, 0.12f));
            _ui.Fill(new Rect(bar.x, bar.y, bar.width * progress, bar.height), UiKit.AccentColor);
        }

        // The current instruction under the chip, fading out after a few seconds.
        var age = Time.unscaledTime - _messageTime;
        var alpha = Mathf.Clamp01(1f - (age - 6f) / 1.5f);
        if (alpha > 0f && !string.IsNullOrEmpty(_message))
        {
            // Stop short of the standings box (top right, 150 wide) when it is shown.
            var messageWidth = Mathf.Min(U(420f), area.width * 0.6f);
            if (StandingsShown) messageWidth = Mathf.Min(messageWidth, area.width - U(150f) - U(12f));
            _ui.Text(new Rect(area.x + U(2f), chip.yMax + U(6f), messageWidth, U(60f)), _message, 13,
                TextAnchor.UpperLeft, new Color(1f, 1f, 1f, alpha), false, true);
        }
    }

    /// <summary>Battle: a small HP bar floating above every car still in the round.</summary>
    private void HealthBars()
    {
        if (_mode != GameMode.Battle || _state == GameState.Finished || _arCamera == null || !_ui.Drawing) return;
        var width = U(44f);
        var height = U(6f);
        foreach (var car in _cars)
        {
            if (car.Out || car.Root == null) continue;
            var screen = _arCamera.WorldToScreenPoint(car.Root.position + car.Root.up * 0.09f);
            if (screen.z <= 0f) continue;
            var bar = new Rect(screen.x - width * 0.5f, Screen.height - screen.y - height, width, height);
            var health = Mathf.Clamp01(car.Health / MaxHealth);
            _ui.Fill(new Rect(bar.x - U(1f), bar.y - U(1f), bar.width + U(2f), bar.height + U(2f)), new Color(0f, 0f, 0f, 0.6f));
            var colour = health > 0.5f ? Color.Lerp(new Color(1f, 0.85f, 0.1f), new Color(0.3f, 0.9f, 0.35f), (health - 0.5f) * 2f)
                : Color.Lerp(new Color(0.95f, 0.2f, 0.15f), new Color(1f, 0.85f, 0.1f), health * 2f);
            _ui.Fill(new Rect(bar.x, bar.y, bar.width * health, bar.height), colour);
            if (car.IsLocal) _ui.Fill(new Rect(bar.x, bar.yMax + U(2f), bar.width, U(2f)), UiKit.AccentColor);
        }
    }

    private bool StandingsShown => _mode != GameMode.Free && _cars.Count >= 2 && _state != GameState.Finished;

    private void Standings(Rect area, Rect pauseRect)
    {
        if (!StandingsShown) return;
        var order = _mode == GameMode.Battle ? BattleOrder() : RaceOrder();
        var rowHeight = U(22f);
        var width = U(150f);
        var box = new Rect(area.xMax - width, pauseRect.yMax + U(8f), width, rowHeight * order.Count + U(10f));
        _ui.Panel(box, new Color(0.05f, 0.07f, 0.10f, 0.6f));
        for (var i = 0; i < order.Count; i++)
        {
            var car = order[i];
            var y = box.y + U(5f) + i * rowHeight;
            _ui.Fill(new Rect(box.x + U(8f), y + rowHeight * 0.3f, U(8f), rowHeight * 0.4f), GameSettings.CarColors[car.ColorIndex]);
            var label = (i + 1) + "  " + (car.IsLocal ? "YOU" : car.Name) + (_mode == GameMode.Battle ? "  " + car.Wins : car.Finished ? "  done" : "");
            if (car.Out && _mode == GameMode.Battle) label += "  out";
            _ui.Text(new Rect(box.x + U(22f), y, width - U(26f), rowHeight), label, 12, TextAnchor.MiddleLeft,
                car.IsLocal ? UiKit.AccentColor : Color.white, car.IsLocal);
        }
    }

    private void BigCenterText()
    {
        // Each big word pops in: age is the time since it appeared.
        string big = null;
        var age = 0f;
        var color = Color.white;
        if (_state == GameState.Countdown)
        {
            var second = Mathf.CeilToInt(_countdown);
            big = second.ToString();
            age = second - _countdown;
        }
        else if (_state == GameState.Racing && _knockoutUntil > Time.time)
        {
            big = _knockoutText;
            age = 1.8f - (_knockoutUntil - Time.time);
            color = UiKit.AccentColor;
        }
        else if (_state == GameState.Racing && Time.time < _goFlashUntil)
        {
            big = _mode == GameMode.Battle ? "PUSH!" : "GO!";
            age = 0.9f - (_goFlashUntil - Time.time);
            color = new Color(0.45f, 1f, 0.5f);
        }
        else if (_hint != null && Time.time < _hintUntil && _state == GameState.Racing)
        {
            _ui.PopText(new Rect(0f, H * 0.24f, W, U(40f)), _hint, 26, UiKit.AccentColor, 1.4f - (_hintUntil - Time.time));
            return;
        }

        if (big == null) return;
        _ui.PopText(new Rect(0f, H * 0.28f, W, U(90f)), big, 72, color, age);
    }

    private void Toasts(Rect area)
    {
        if (_toast == null || Time.unscaledTime > _toastUntil) return;
        var width = Mathf.Min(U(440f), area.width - U(40f));
        var rect = new Rect(area.center.x - width * 0.5f, area.y + U(64f), width, U(40f));
        if (_arena == null) rect.y += U(30f);
        _ui.Panel(rect, new Color(0.10f, 0.12f, 0.16f, 0.92f));
        _ui.Text(Inset(rect, U(8f)), _toast, 13, TextAnchor.MiddleCenter, Color.white);
    }

    /// <summary>View and build tools in a column under the status chip.</summary>
    private float ToolColumn(Rect area)
    {
        var width = U(104f);
        var height = U(38f);
        // Below the status chip and its hint: the hint is narrower in portrait, so it may take three lines.
        var y = area.y + U(52f) + (Landscape ? U(50f) : U(72f));
        var x = area.x;
        if (_virtualMode && !_buildMode)
        {
            var camLabel = _view == ViewMode.Phone ? "PHONE" : _view == ViewMode.Chase ? "BEHIND" : "DRIVER";
            if (_ui.Button(new Rect(x, y, width, height), camLabel, UiKit.Look.Normal, false, 12, UiIcon.Camera))
            {
                // PHONE -> BEHIND -> DRIVER -> PHONE
                _view = (ViewMode)(((int)_view + 1) % 3);
                _snapChaseCamera = true;
                _phoneView?.Recenter();
                ShowToast(_view == ViewMode.Phone ? "Phone camera: turn your phone to look around, drag to adjust"
                    : _view == ViewMode.Chase ? "Camera behind the car" : "Driver's view");
            }

            y += height + U(6f);
            if (_view == ViewMode.Phone)
            {
                // The car drove into another room: offer to follow it there.
                var carRoom = _room != null && _player != null ? _room.RoomAt(_player.Position) : -1;
                var carElsewhere = _phoneView != null && carRoom >= 0 && carRoom != _phoneView.Room;
                if (_ui.Button(new Rect(x, y, width, height), carElsewhere ? "FOLLOW" : "MOVE", carElsewhere ? UiKit.Look.Primary : UiKit.Look.Normal, false, 12, UiIcon.Pin)
                    && _phoneView != null)
                {
                    var walked = carRoom >= 0 && _phoneView.WalkTowards(carRoom, _player.Root.position);
                    if (!walked && carRoom < 0) _phoneView.NextSpot();
                    ShowToast(walked ? "Walked to the car" : "Other side of the room");
                }

                y += height + U(6f);
            }
        }

        if (!_virtualMode && _depthDetector != null && _depthDetector.Mapping && !_buildMode)
        {
            if (_ui.Button(new Rect(x, y, width, height), _depthDetector.ShowOverlay ? "SCAN ON" : "SCAN OFF", UiKit.Look.Normal, _depthDetector.ShowOverlay, 12, UiIcon.Radar))
                _depthDetector.ShowOverlay = !_depthDetector.ShowOverlay;
            y += height + U(6f);
        }

        if (_mode == GameMode.Free && !IsOnline)
        {
            if (_ui.Button(new Rect(x, y, width, height), _buildMode ? "DONE" : "BUILD", _buildMode ? UiKit.Look.Primary : UiKit.Look.Normal, false, 12, UiIcon.Cone))
            {
                _buildMode = !_buildMode;
                SetMessage(_buildMode ? BuildMessage() : FreeDriveMessage());
            }

            y += height + U(6f);
        }

        return y;
    }

    private void BuildPalette(Rect area)
    {
        var count = 4;
        var gap = U(8f);
        var width = Mathf.Min(U(110f), (area.width - gap * (count - 1)) / count);
        var height = U(54f);
        var x = area.center.x - (width * count + gap * (count - 1)) * 0.5f;
        var y = area.yMax - height;
        if (_ui.Button(new Rect(x, y, width, height), "CONE", UiKit.Look.Normal, _buildKind == ObstacleKind.Cone, 14, UiIcon.Cone)) { _buildKind = ObstacleKind.Cone; SetMessage(BuildMessage()); }
        if (_ui.Button(new Rect(x + (width + gap), y, width, height), "TYRES", UiKit.Look.Normal, _buildKind == ObstacleKind.Tyres)) { _buildKind = ObstacleKind.Tyres; SetMessage(BuildMessage()); }
        if (_ui.Button(new Rect(x + (width + gap) * 2f, y, width, height), "WALL", UiKit.Look.Normal, _buildKind == ObstacleKind.Wall)) { _buildKind = ObstacleKind.Wall; SetMessage(BuildMessage()); }
        if (_ui.Button(new Rect(x + (width + gap) * 3f, y, width, height), "CLEAR", UiKit.Look.Danger)) ClearPlacedObstacles();
    }

    /// <summary>Round thumb buttons: steering on the left, throttle and reverse on the right.</summary>
    private void DrivingControls(Rect area)
    {
        var big = U(Landscape ? 84f : 78f);
        var small = U(Landscape ? 70f : 66f);
        var bottom = area.yMax;
        var gap = U(12f);

        if (GameSettings.Steering == SteeringSetting.Buttons)
        {
            var left = _ui.Hold(new Rect(area.x, bottom - small, small, small), "◄", UiKit.Look.Steer, true, 26);
            var right = _ui.Hold(new Rect(area.x + small + gap, bottom - small, small, small), "►", UiKit.Look.Steer, true, 26);
            if (!_ui.Drawing)
            {
                _leftHeld = left;
                _rightHeld = right;
            }
        }
        else
        {
            _ui.Text(new Rect(area.x, bottom - U(24f), U(200f), U(24f)), "Tilt the phone to steer", 12, TextAnchor.MiddleLeft, UiKit.TextDim, false, true);
        }

        var go = _ui.Hold(new Rect(area.xMax - big, bottom - big, big, big), "GO", UiKit.Look.Go, true, 20);
        var back = _ui.Hold(new Rect(area.xMax - big - gap - small, bottom - small, small, small), "REV", UiKit.Look.Brake, true, 15);
        if (!_ui.Drawing)
        {
            _goHeld = go;
            _backHeld = back;
        }

        if (_state == GameState.Ready && !IsClient)
            _ui.Text(new Rect(0f, bottom - big - U(34f), W, U(26f)), "Hold GO to start", 15, TextAnchor.MiddleCenter, Color.white, true, true);
    }

    private void ResultsPanel(Rect area)
    {
        var order = _mode == GameMode.Battle ? BattleOrder() : RaceOrder();
        var rowHeight = U(30f);
        // Offered once per round, never in LAN games (a full-screen ad pauses the session) or to VIP players.
        var offerAd = !IsOnline && _adBonus == 0 && _roundEarned > 0 && GameAds.RewardedReady;
#if UI_CAPTURE
        offerAd |= CaptureForceAdOffer;
#endif
        var height = U(70f) + rowHeight * order.Count + U(114f) + (offerAd || _adBonus > 0 ? U(54f) : 0f);
        var panel = CenterPanel(area, U(420f), Mathf.Min(height, area.height));
        _ui.Panel(panel);
        var inner = Inset(panel, U(14f));
        var place = _player != null ? _player.Place : 0;
        var title = _mode == GameMode.Battle ? (place == 1 ? "YOU WIN THE BATTLE" : "BATTLE OVER")
            : place == 1 ? "YOU WON!" : place > 0 ? Ordinal(place).ToUpperInvariant() + " PLACE" : "RACE OVER";
        _ui.Text(new Rect(inner.x, inner.y, inner.width, U(34f)), title, 22, TextAnchor.MiddleCenter, place == 1 ? UiKit.AccentColor : Color.white, true);
        var y = inner.y + U(44f);
        for (var i = 0; i < order.Count; i++)
        {
            var car = order[i];
            _ui.Fill(new Rect(inner.x, y + rowHeight * 0.3f, U(10f), rowHeight * 0.4f), GameSettings.CarColors[car.ColorIndex]);
            _ui.Text(new Rect(inner.x + U(18f), y, inner.width * 0.6f, rowHeight), (i + 1) + ".  " + (car.IsLocal ? "You" : car.Name), 15,
                TextAnchor.MiddleLeft, car.IsLocal ? UiKit.AccentColor : Color.white, car.IsLocal);
            var detail = _mode == GameMode.Battle ? car.Wins + " pts" : car.Finished ? FormatTime(car.FinishTime) : "DNF";
            _ui.Text(new Rect(inner.x, y, inner.width, rowHeight), detail, 14, TextAnchor.MiddleRight, UiKit.TextDim);
            y += rowHeight;
        }

        if (_newBest && _mode == GameMode.Race)
        {
            _ui.Text(new Rect(inner.x, y, inner.width, U(20f)), "NEW BEST TIME!", 13, TextAnchor.MiddleCenter, UiKit.AccentColor, true);
            y += U(20f);
        }

        if (_roundEarned > 0)
        {
            var earned = new Rect(inner.center.x - U(110f), y + U(2f), U(220f), U(24f));
            _ui.Icon(new Rect(earned.x, earned.y, U(22f), U(22f)), UiIcon.Coin, UiKit.AccentColor);
            _ui.Text(new Rect(earned.x + U(28f), earned.y, earned.width - U(28f), earned.height),
                "+" + (_roundEarned + _adBonus) + " coins" + (PlayerProfile.Vip ? " (VIP x2)" : "") + "  •  total " + PlayerProfile.Coins.ToString("N0"), 13, TextAnchor.MiddleLeft, Color.white, true);
        }

        var buttonY = inner.yMax - U(46f);
        var half = (inner.width - U(10f)) * 0.5f;
        var adRect = new Rect(inner.x, buttonY - U(54f), inner.width, U(46f));
        if (_adBonus > 0)
            _ui.Text(adRect, "Ad bonus: +" + _adBonus + " coins. Thanks for watching!", 14, TextAnchor.MiddleCenter, UiKit.AccentColor, true);
        else if (offerAd && _ui.Button(adRect, "WATCH AD:  +" + _roundEarned + " COINS", UiKit.Look.Accent, false, 15, UiIcon.Play))
        {
            var bonus = _roundEarned;
            GameAds.ShowRewarded(() =>
            {
                PlayerProfile.Earn(bonus);
                _adBonus = bonus;
                _earnFlashAt = Time.time;
                GameAudio.Instance.Play(GameAudio.Sfx.Coin);
            });
        }

        if (IsClient)
        {
            _ui.Text(new Rect(inner.x, buttonY, inner.width, U(46f)), "Waiting for the host to start again...", 13, TextAnchor.MiddleCenter, UiKit.TextDim);
            return;
        }

        if (_ui.Button(new Rect(inner.x, buttonY, half, U(46f)), "AGAIN", UiKit.Look.Primary, false, 16, UiIcon.Restart))
            AfterRound(StartRound);
        if (_ui.Button(new Rect(inner.x + half + U(10f), buttonY, half, U(46f)), "MENU", UiKit.Look.Ghost, false, 16))
            AfterRound(OpenPause);
    }

    /// <summary>Leaves the results: an interstitial may play first, except in LAN games.</summary>
    private void AfterRound(System.Action next)
    {
        if (IsOnline) next();
        else GameAds.AfterRound(_adBonus > 0, next);
    }

    private void DebugLine(Rect area)
    {
        var planeCount = _planeManager != null ? _planeManager.trackables.count : 0;
        var depthState = _depthDetector == null ? "off"
            : _depthDetector.DepthSupported ? (_depthDetector.HasDepthFrame ? "depth, " + _depthDetector.SolidCellCount + " cells" : "waiting")
            : _depthDetector.HasPointFrame ? "feature points, " + _depthDetector.SolidCellCount + " cells" : "no depth, waiting for points";
        var line = _virtualMode
            ? "Virtual room • " + (_phoneView != null && _phoneView.HasGyro ? "gyroscope" : "no gyroscope") + " • " + Screen.orientation
            : "AR " + ARSession.state + " (" + ARSession.notTrackingReason + ") • frames " + _cameraFrameCount + " • planes " + planeCount
              + " • anchor " + (_anchor == null ? "none" : _anchor.trackingState.ToString()) + " • depth " + depthState;
        if (_net != null) line += " • net: " + _net.Status;
        _ui.Text(new Rect(area.x, area.yMax - U(112f), area.width, U(16f)), line, 10, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.7f), false, true);
    }
}
