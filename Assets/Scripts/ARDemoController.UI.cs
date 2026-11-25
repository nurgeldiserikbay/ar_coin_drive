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
        switch (_screen)
        {
            case UiScreen.Menu: MenuScreen(); break;
            case UiScreen.Lobby: LobbyScreen(); break;
            case UiScreen.Settings: SettingsScreen(); break;
            case UiScreen.Pause:
                GameHud(false);
                PauseScreen();
                break;
            default: GameHud(true); break;
        }
    }

    /// <summary>Safe area (notches, rounded corners) in GUI coordinates with a margin.</summary>
    private Rect SafeRect()
    {
        var safe = Screen.safeArea;
        var margin = U(12f);
        return new Rect(safe.x + margin, Screen.height - safe.yMax + margin, safe.width - margin * 2f, safe.height - margin * 2f);
    }

    // ------------------------------------------------------------------ main menu

    private void MenuScreen()
    {
        var area = SafeRect();
        _ui.Fill(new Rect(0f, 0f, W, H), new Color(0.02f, 0.04f, 0.07f, _arena == null && _cameraFrameCount > 0 ? 0.55f : 0.92f));

        Rect logoRect;
        Rect column;
        var buttonWidth = Mathf.Min(U(300f), area.width);
        if (Landscape)
        {
            var half = area.width * 0.5f;
            logoRect = new Rect(area.x, area.y, half, area.height);
            column = new Rect(area.x + half + (half - buttonWidth) * 0.5f, area.y, buttonWidth, area.height);
        }
        else
        {
            logoRect = new Rect(area.x, area.y, area.width, area.height * 0.42f);
            column = new Rect(area.center.x - buttonWidth * 0.5f, area.y + area.height * 0.44f, buttonWidth, area.height * 0.56f);
        }

        _ui.Image(Inset(logoRect, U(8f)), _logo);

        var rows = 4;
        var rowHeight = Mathf.Min(U(54f), (column.height - U(40f)) / rows - U(10f));
        var y = column.y + (column.height - rows * (rowHeight + U(10f)) - U(30f)) * 0.5f;
        var arLabel = !_arChecked ? "CHECKING AR..." : _arSupported ? "DRIVE IN YOUR ROOM  (AR)" : "AR NOT SUPPORTED";
        if (_ui.Button(new Rect(column.x, y, column.width, rowHeight), arLabel, _arSupported ? UiKit.Look.Primary : UiKit.Look.Ghost, false, 16)
            && _arSupported)
            BeginAR();
        y += rowHeight + U(10f);
        if (_ui.Button(new Rect(column.x, y, column.width, rowHeight), "VIRTUAL ROOM", UiKit.Look.Accent, false, 16))
            EnterVirtualMode();
        y += rowHeight + U(10f);
        if (_ui.Button(new Rect(column.x, y, column.width, rowHeight), "MULTIPLAYER", UiKit.Look.Normal, false, 16))
        {
            _lobbyUseAR = _arSupported;
            _screen = UiScreen.Lobby;
        }

        y += rowHeight + U(10f);
        if (_ui.Button(new Rect(column.x, y, column.width, rowHeight), "SETTINGS", UiKit.Look.Normal, false, 16))
            OpenSettings(UiScreen.Menu);
        y += rowHeight + U(12f);
        _ui.Text(new Rect(column.x, y, column.width, U(20f)), GameSettings.PlayerName + "  •  " + GameSettings.CarColorNames[GameSettings.CarColor] + " buggy",
            12, TextAnchor.MiddleCenter, UiKit.TextDim);
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
        _ui.Text(new Rect(inner.x, y, inner.width, U(36f)), "Up to 4 players on the same Wi-Fi, or connect to one phone's hotspot. Each phone plays on its own floor or in its own virtual room; mixing both works.",
            12, TextAnchor.UpperLeft, UiKit.TextDim);
        y += U(42f);

        var row = U(42f);
        if (_arena == null)
        {
            _ui.Text(new Rect(inner.x, y, U(110f), row), "Play area", 14, TextAnchor.MiddleLeft, Color.white);
            var choice = _ui.Segmented(new Rect(inner.x + U(110f), y, inner.width - U(110f), row),
                new[] { _arSupported ? "MY ROOM (AR)" : "AR N/A", "VIRTUAL ROOM" }, _lobbyUseAR ? 0 : 1);
            _lobbyUseAR = choice == 0 && _arSupported;
            y += row + U(10f);
        }

        if (_net == null)
        {
            var half = (inner.width - U(10f)) * 0.5f;
            if (_ui.Button(new Rect(inner.x, y, half, U(52f)), "HOST A GAME", UiKit.Look.Primary, false, 16))
            {
                StartHosting();
                EnterPlayAreaForNetwork();
            }

            if (_ui.Button(new Rect(inner.x + half + U(10f), y, half, U(52f)), "JOIN A GAME", UiKit.Look.Accent, false, 16))
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
        var rows = columns == 2 ? 6 : 11;
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

        if (_ui.Button(new Rect(x, y, columnWidth, row), "RESUME", UiKit.Look.Primary, false, 16)) ClosePause();
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

            if (_ui.Button(new Rect(x, y, columnWidth, row), "RESTART ROUND"))
            {
                ClosePause();
                StartRound();
            }

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
            if (_ui.Button(new Rect(x, y, half, row), "HOST GAME", UiKit.Look.Accent))
            {
                StartHosting();
                EnterPlayAreaForNetwork();
            }

            if (_ui.Button(new Rect(x + half + U(8f), y, half, row), "JOIN GAME", UiKit.Look.Accent))
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
        if (_ui.Button(new Rect(x, y, columnWidth, row), "SETTINGS")) OpenSettings(UiScreen.Pause);
        y += row + U(8f);
        if (_ui.Button(new Rect(x, y, columnWidth, row), "MAIN MENU", UiKit.Look.Ghost)) ReturnToMenu();
    }

    // ------------------------------------------------------------------ in-game HUD

    private void GameHud(bool interactive)
    {
        var area = SafeRect();
        var pauseSize = U(46f);
        var pauseRect = new Rect(area.xMax - pauseSize, area.y, pauseSize, pauseSize);
        if (interactive && _ui.Button(pauseRect, "II", UiKit.Look.Normal, false, 16)) OpenPause();
        else if (!interactive) _ui.Button(pauseRect, "II", UiKit.Look.Normal, false, 16);

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
            _ui.Text(chip, IsHost ? "• " + _net.PlayerCount + "/" + LanSession.MaxPlayers : "• ONLINE", 13, TextAnchor.MiddleCenter, new Color(0.4f, 1f, 0.55f), true);
        }

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
        var width = Mathf.Min(U(300f), pauseRect.x - area.x - U(110f));
        var chip = new Rect(area.x, area.y, width, U(52f));
        _ui.Panel(chip);
        var inner = Inset(chip, U(8f));
        string title;
        string sub;
        var player = _player;
        if (_mode == GameMode.Free)
        {
            var speed = player != null ? Mathf.RoundToInt(Mathf.Abs(player.Speed) * 3.6f * RcScale) : 0;
            title = "FREE DRIVE";
            sub = "Coins " + (player != null ? player.Coins : 0) + "   •   " + speed + " km/h";
        }
        else if (_mode == GameMode.Battle)
        {
            title = "BATTLE  •  RING " + Mathf.RoundToInt(_battleRadius * 200f) + " cm";
            sub = "First to " + BattleWinsNeeded + " points  •  you " + (player != null ? player.Wins : 0);
        }
        else
        {
            var lap = player != null ? Mathf.Clamp(Mathf.FloorToInt(player.Laps) + 1, 1, LapCount) : 1;
            title = "LAP " + lap + "/" + LapCount + "   P" + PlayerPosition() + "/" + Mathf.Max(1, _cars.Count);
            sub = FormatTime(_raceTime) + "   •   best " + (_bestTime > 0f ? FormatTime(_bestTime) : "--");
        }

        _ui.Text(new Rect(inner.x, inner.y, inner.width, inner.height * 0.55f), title, 15, TextAnchor.MiddleLeft, Color.white, true);
        _ui.Text(new Rect(inner.x, inner.y + inner.height * 0.55f, inner.width, inner.height * 0.45f), sub, 12, TextAnchor.MiddleLeft, UiKit.TextDim);

        // The current instruction under the chip, fading out after a few seconds.
        var age = Time.unscaledTime - _messageTime;
        var alpha = Mathf.Clamp01(1f - (age - 6f) / 1.5f);
        if (alpha > 0f && !string.IsNullOrEmpty(_message))
            _ui.Text(new Rect(area.x + U(2f), chip.yMax + U(6f), Mathf.Min(U(420f), area.width * 0.6f), U(40f)), _message, 13,
                TextAnchor.UpperLeft, new Color(1f, 1f, 1f, alpha), false, true);
    }

    private void Standings(Rect area, Rect pauseRect)
    {
        if (_mode == GameMode.Free || _cars.Count < 2 || _state == GameState.Finished) return;
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
        string big = null;
        var color = Color.white;
        if (_state == GameState.Countdown) big = Mathf.CeilToInt(_countdown).ToString();
        else if (_state == GameState.Racing && _knockoutUntil > Time.time) big = _knockoutText;
        else if (_state == GameState.Racing && Time.time < _goFlashUntil) big = _mode == GameMode.Battle ? "PUSH!" : "GO!";
        else if (_hint != null && Time.time < _hintUntil && _state == GameState.Racing)
        {
            _ui.Text(new Rect(0f, H * 0.24f, W, U(40f)), _hint, 26, TextAnchor.MiddleCenter, UiKit.AccentColor, true, true);
            return;
        }

        if (big == null) return;
        _ui.Text(new Rect(0f, H * 0.28f, W, U(90f)), big, 64, TextAnchor.MiddleCenter, color, true, true);
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
        var width = U(84f);
        var height = U(36f);
        var y = area.y + U(52f) + U(50f);
        var x = area.x;
        if (_virtualMode && !_buildMode)
        {
            var camLabel = _view == ViewMode.Phone ? "CAM: PHONE" : _view == ViewMode.Chase ? "CAM: BEHIND" : "CAM: DRIVER";
            if (_ui.Button(new Rect(x, y, width, height), camLabel, UiKit.Look.Normal, false, 11))
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
                if (_ui.Button(new Rect(x, y, width, height), "MOVE", UiKit.Look.Normal, false, 11) && _phoneView != null)
                {
                    _phoneView.NextSpot();
                    ShowToast("Standing at " + _phoneView.SpotName);
                }

                y += height + U(6f);
            }
        }

        if (!_virtualMode && _depthDetector != null && _depthDetector.DepthSupported && !_buildMode)
        {
            if (_ui.Button(new Rect(x, y, width, height), _depthDetector.ShowOverlay ? "SCAN: ON" : "SCAN: OFF", UiKit.Look.Normal, _depthDetector.ShowOverlay, 11))
                _depthDetector.ShowOverlay = !_depthDetector.ShowOverlay;
            y += height + U(6f);
        }

        if (_mode == GameMode.Free && !IsOnline)
        {
            if (_ui.Button(new Rect(x, y, width, height), _buildMode ? "DONE" : "BUILD", _buildMode ? UiKit.Look.Primary : UiKit.Look.Normal, false, 12))
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
        if (_ui.Button(new Rect(x, y, width, height), "CONE", UiKit.Look.Normal, _buildKind == ObstacleKind.Cone)) { _buildKind = ObstacleKind.Cone; SetMessage(BuildMessage()); }
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
        var height = U(70f) + rowHeight * order.Count + U(64f);
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
            _ui.Text(new Rect(inner.x, y, inner.width, U(20f)), "NEW BEST TIME!", 13, TextAnchor.MiddleCenter, UiKit.AccentColor, true);

        var buttonY = inner.yMax - U(46f);
        var half = (inner.width - U(10f)) * 0.5f;
        if (IsClient)
        {
            _ui.Text(new Rect(inner.x, buttonY, inner.width, U(46f)), "Waiting for the host to start again...", 13, TextAnchor.MiddleCenter, UiKit.TextDim);
            return;
        }

        if (_ui.Button(new Rect(inner.x, buttonY, half, U(46f)), "AGAIN", UiKit.Look.Primary, false, 16)) StartRound();
        if (_ui.Button(new Rect(inner.x + half + U(10f), buttonY, half, U(46f)), "MENU", UiKit.Look.Ghost, false, 16)) OpenPause();
    }

    private void DebugLine(Rect area)
    {
        var planeCount = _planeManager != null ? _planeManager.trackables.count : 0;
        var depthState = _depthDetector == null ? "off"
            : !_depthDetector.DepthSupported ? "not supported"
            : _depthDetector.HasDepthFrame ? "ready, " + _depthDetector.SolidCellCount + " cells" : "waiting";
        var line = _virtualMode
            ? "Virtual room • " + (_phoneView != null && _phoneView.HasGyro ? "gyroscope" : "no gyroscope") + " • " + Screen.orientation
            : "AR " + ARSession.state + " (" + ARSession.notTrackingReason + ") • frames " + _cameraFrameCount + " • planes " + planeCount
              + " • anchor " + (_anchor == null ? "none" : _anchor.trackingState.ToString()) + " • depth " + depthState;
        if (_net != null) line += " • net: " + _net.Status;
        _ui.Text(new Rect(area.x, area.yMax - U(112f), area.width, U(16f)), line, 10, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.7f), false, true);
    }
}
