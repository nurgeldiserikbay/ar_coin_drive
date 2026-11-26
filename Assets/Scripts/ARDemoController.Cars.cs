using System.Collections.Generic;
using UnityEngine;

/// <summary>Cars, rounds, race and battle rules, the computer drivers and the driving physics.</summary>
public sealed partial class ARDemoController
{
    /// <summary>One buggy: the player on this phone, another player over the network, or a computer driver.</summary>
    private sealed class Car
    {
        public int Id;              // 0 = host player, 1-3 = guests, 4+ = computer drivers
        public string Name;
        public int ColorIndex;
        public bool IsLocal;
        public bool IsBot;
        public Transform Root;
        public ProceduralCarRig Rig;
        public GameAudio.EngineVoice Voice;
        public CarSpec Spec;
        public int SpecIndex;
        public int Build;           // upgrades and look (PlayerProfile.PackBuild)
        public float Lateral;       // sideways slide speed (drift)

        public Vector2 Position;
        public float Heading;
        public float Speed;
        public float Drive;
        public float Steer;
        public Vector2 Knock;
        public float Fall;
        public bool Out;
        public float BoostUntil;

        public float Laps;
        public float LastProgress;
        public bool Finished;
        public float FinishTime;
        public int Place;
        public int Wins;
        public int Coins;

        // Network smoothing on guests.
        public Vector2 TargetPosition;
        public float TargetHeading;
        public bool Seen;
    }

    private const float HalfArena = 0.75f;
    private const int CoinCount = 6;
    private const int LapCount = 5;
    private const float CountdownSeconds = 3f;
    private const float BoostSeconds = 1.6f;
    private const float BoostMultiplier = 1.35f;
    private const float CoinRespawnSeconds = 5f;
    private const float CarContactDistance = 0.165f;
    private const float RaceFinishGrace = 15f;

    // Driving feel: the car accelerates, brakes and only turns while rolling, like an RC car.
    private const float MaxSpeed = 0.48f;
    private const float ReverseInput = -0.65f;
    private const float Acceleration = 0.9f;
    private const float Braking = 2.2f;
    private const float RollingFriction = 1.1f;
    private const float TurnSpeed = 140f;
    private const float FullSteerSpeed = 0.16f;

    // Free drive: the car is quicker than on the track and roams the room.
    private const float FreeSpeedMultiplier = 1.5f;
    private const int FreeCoinCount = 8;

    // Push-out battle: last car in the shrinking ring scores; first to three wins.
    private const int BattleWinsNeeded = 3;
    private ArenaShape _arenaShape;
    private int _arenaSize = 1;
    private float BattleStartRadius => BattleArena.SizeRadius[_arenaSize];
    private float BattleEndRadius => BattleStartRadius * 0.48f;
    private const float BattleShrinkSeconds = 40f;
    private float BattleStartOffset => BattleStartRadius * 0.58f;

    private readonly List<Car> _cars = new List<Car>();
    private Car _player;
    private readonly List<Transform> _coins = new List<Transform>();
    private readonly List<float> _coinRespawnAt = new List<float>();
    private readonly List<Vector2> _coinLayout = new List<Vector2>();
    private float _battleRadius = 0.62f;
    private float _knockoutUntil;
    private string _knockoutText;
    private float _firstFinishTime = -1f;
    private int _finishCounter;
    private int _lastCountdownBeep;

    private bool IsSimulating => !IsClient;
    private float SpeedScale => _mode == GameMode.Free ? FreeSpeedMultiplier : 1f;

    // ------------------------------------------------------------------ rounds

    /// <summary>Starts a new round: cars on the grid / ring / floor, coins laid out.</summary>
    private void StartRound()
    {
        ClearRound();
        _roundRoot = new GameObject("Round").transform;
        _roundRoot.SetParent(_arena, false);
        UpdateBounds();
        _raceTime = 0f;
        _goFlashUntil = 0f;
        _knockoutUntil = 0f;
        _firstFinishTime = -1f;
        _finishCounter = 0;
        _newBest = false;
        _hint = null;
        _roundEarned = 0;
        _placePrize = 0;
        _coinsRewarded = 0;
        _prizeGiven = false;
        _adBonus = 0;

        if (IsClient)
        {
            // Guests build their cars from the host's state packets.
            if (_mode == GameMode.Race) CreateRaceCoins();
            _state = GameState.Ready;
            return;
        }

        CreateCars();
        if (_mode == GameMode.Free)
        {
            PlaceFreeDrive();
            _state = GameState.Racing;
            SetMessage(FreeDriveMessage());
        }
        else if (_mode == GameMode.Battle)
        {
            ResetBattleRound();
            _state = GameState.Ready;
            SetMessage("Battle! Last car in the ring scores. First to " + BattleWinsNeeded + " wins. Hold GO.");
        }
        else
        {
            var track = RaceTrack.Shared;
            for (var i = 0; i < _cars.Count; i++)
            {
                _cars[i].Position = track.GridPosition(i);
                _cars[i].Heading = track.StartHeading;
                StartLapCounter(_cars[i].Position, out _cars[i].Laps, out _cars[i].LastProgress);
            }

            CreateRaceCoins();
            _state = GameState.Ready;
            SetMessage(LapCount + " laps. Hold GO to start the countdown.");
        }

        foreach (var car in _cars) ApplyTransform(car);
        _snapChaseCamera = true;
        _phoneView?.Recenter();
    }

    private void ClearRound()
    {
        if (_roundRoot != null) Destroy(_roundRoot.gameObject);
        _roundRoot = null;
        _cars.Clear();
        _player = null;
        _coins.Clear();
        _coinRespawnAt.Clear();
    }

    /// <summary>The local player, the network players (host only) and computer drivers.</summary>
    private void CreateCars()
    {
        var used = new HashSet<int>();
        var playerBuild = PlayerProfile.BuildOf(GameSettings.CarType);
        _player = AddCar(0, GameSettings.PlayerName, GameSettings.CarColor, GameSettings.CarType, playerBuild, true, false);
        used.Add(_player.ColorIndex);

        if (IsHost)
        {
            foreach (var peer in _net.Peers)
            {
                var color = used.Contains(peer.Color) ? FreeColor(used) : peer.Color;
                used.Add(color);
                AddCar(peer.Slot, peer.Name, color, peer.CarType, peer.Build, false, false);
            }
        }

        if (_mode == GameMode.Free) return;
        var bots = Mathf.Min(GameSettings.Bots, LanSession.MaxPlayers - _cars.Count);
        if (_cars.Count == 1) bots = Mathf.Max(bots, 1); // racing alone is no race
        for (var i = 0; i < bots; i++)
        {
            var color = FreeColor(used);
            used.Add(color);
            // Computer drivers get a mix of the other cars.
            var spec = (GameSettings.CarType + 1 + i * 2) % CarCatalog.All.Length;
            if (PlayerProfile.IsPremium(spec)) spec = (spec + 2) % 6; // bots stick to the regular cars
            // ...tuned like the player's car (stock looks), so upgrades never make races a walkover.
            PlayerProfile.UnpackBuild(playerBuild, color, out var tune, out _);
            var botLook = CarLook.Stock(color);
            botLook.Rim = (i + 1) % CarLook.RimNames.Length;
            AddCar(4 + i, "CPU " + (i + 1), color, spec, PlayerProfile.PackBuild(tune, botLook), false, true);
        }
    }

    private static int FreeColor(HashSet<int> used)
    {
        foreach (var preferred in new[] { 1, 2, 3, 4, 5, 0, 6, 7, 8, 9, 10, 11 })
            if (!used.Contains(preferred)) return preferred;
        return 0;
    }

    private Car AddCar(int id, string name, int colorIndex, int specIndex, int build, bool local, bool bot)
    {
        specIndex = Mathf.Clamp(specIndex, 0, CarCatalog.All.Length - 1);
        PlayerProfile.UnpackBuild(build, colorIndex, out var tune, out var look);
        var spec = CarTuning.Apply(specIndex, tune);
        var car = new Car { Id = id, Name = name, ColorIndex = colorIndex, IsLocal = local, IsBot = bot, Spec = spec, SpecIndex = specIndex, Build = build };
        car.Root = ProceduralCarFactory.CreateCar(_roundRoot, spec, GameSettings.CarColors[colorIndex], GameSettings.TrimFor(colorIndex), look).transform;
        car.Rig = car.Root.GetComponent<ProceduralCarRig>();
        car.Root.localScale = Vector3.one * CarCatalog.GameScale;
        if (car.Rig != null) car.Rig.SizeScale = CarCatalog.GameScale;
        if (car.Rig != null) car.Rig.ReferenceSpeed = spec.TopSpeed * SpeedScale;
        car.Voice = GameAudio.Instance.AttachEngine(car.Root, local);
        _cars.Add(car);
        return car;
    }

    private void RemoveCar(Car car)
    {
        if (car.Root != null) Destroy(car.Root.gameObject);
        _cars.Remove(car);
    }

    /// <summary>Where the car may drive: the room in VR, the depth map area in AR free drive, else the arena.</summary>
    private void UpdateBounds()
    {
        if (_mode == GameMode.Race)
            _bounds = RaceTrack.Shared.Bounds;
        else if (_mode == GameMode.Battle)
        {
            var edge = BattleStartRadius * 1.3f;
            _bounds = Rect.MinMaxRect(-edge, -edge, edge, edge);
        }
        else if (_room != null)
            _bounds = _room.Bounds;
        else
            _bounds = Rect.MinMaxRect(-FreeARHalfExtent, -FreeARHalfExtent, FreeARHalfExtent, FreeARHalfExtent);
    }

    /// <summary>Free drive: the buggy faces away from the phone; coins are scattered around the room.</summary>
    private void PlaceFreeDrive()
    {
        _player.Position = _virtualMode ? new Vector2(0f, -0.5f) : Vector2.zero;
        _player.Heading = 0f;
        if (!_virtualMode && _arCamera != null)
        {
            var forward = _arena.InverseTransformDirection(_arCamera.transform.forward);
            if (forward.x * forward.x + forward.z * forward.z > 0.0001f)
                _player.Heading = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }

        var random = new System.Random();
        // In the house coins are spread through every room; in AR around the tapped spot.
        var count = _room != null ? 18 : FreeCoinCount;
        for (var attempt = 0; attempt < 400 && _coins.Count < count; attempt++)
        {
            Vector2 point;
            if (_room != null)
            {
                point = new Vector2(Mathf.Lerp(_room.Bounds.xMin, _room.Bounds.xMax, (float)random.NextDouble()),
                    Mathf.Lerp(_room.Bounds.yMin, _room.Bounds.yMax, (float)random.NextDouble()));
            }
            else
            {
                var angle = (float)random.NextDouble() * Mathf.PI * 2f;
                point = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (Mathf.Sqrt((float)random.NextDouble()) * 1.3f);
            }

            if (IsGoodCoinSpot(point)) AddCoin(point);
        }
    }

    private bool IsGoodCoinSpot(Vector2 point)
    {
        var inner = new Rect(_bounds.xMin + 0.15f, _bounds.yMin + 0.15f, _bounds.width - 0.3f, _bounds.height - 0.3f);
        if (!inner.Contains(point) || (point - _player.Position).magnitude < 0.4f) return false;
        foreach (var coin in _coins)
            if ((new Vector2(coin.localPosition.x, coin.localPosition.z) - point).magnitude < 0.3f) return false;
        foreach (var obstacle in _placed)
            if (obstacle.Contains(point, 0.12f)) return false;
        if (_room != null)
            foreach (var obstacle in _room.Obstacles)
                if (obstacle.Contains(point, 0.12f)) return false;
        return _depthDetector == null || !_depthDetector.IsSolid(point);
    }

    /// <summary>Boost coins spread around the lap (deterministic, so every phone sees the same).</summary>
    private void CreateRaceCoins()
    {
        _coinLayout.Clear();
        for (var i = 0; i < CoinCount; i++)
        {
            var lapFraction = (i + 0.5f) / CoinCount;
            _coinLayout.Add(RaceTrack.Shared.PointAt(lapFraction, Mathf.Sin(i * 2.3f + 0.7f) * 0.07f));
        }

        foreach (var point in _coinLayout) AddCoin(point);
    }

    private void AddCoin(Vector2 point)
    {
        var coin = ProceduralCarFactory.CreateCoin(_roundRoot, new Vector3(point.x, 0.06f, point.y));
        _coins.Add(coin.transform);
        _coinRespawnAt.Add(0f);
    }

    // ------------------------------------------------------------------ simulation step

    /// <summary>One frame of the game on this phone (host or single player).</summary>
    private void SimulateGame(float dt)
    {
        if (_state == GameState.Ready && _player != null && _driveInput > 0.01f && !_buildMode)
        {
            _state = GameState.Countdown;
            _countdown = CountdownSeconds;
            _lastCountdownBeep = 4;
            SetMessage("Get ready...");
        }

        if (_state == GameState.Countdown)
        {
            _countdown -= dt;
            var second = Mathf.CeilToInt(_countdown);
            if (second < _lastCountdownBeep && second > 0)
            {
                _lastCountdownBeep = second;
                GameAudio.Instance.Play(GameAudio.Sfx.Beep);
            }

            if (_countdown <= 0f)
            {
                _state = GameState.Racing;
                _goFlashUntil = Time.time + 0.9f;
                GameAudio.Instance.Play(GameAudio.Sfx.Go);
                SetMessage(_mode == GameMode.Race
                    ? LapCount + " laps! Drive through coins for a speed boost."
                    : "Push the others out of the ring! It keeps shrinking.");
            }
        }

        if (_state == GameState.Racing && _knockoutUntil <= 0f) _raceTime += dt;

        var canDrive = _state == GameState.Racing && _knockoutUntil <= 0f;
        foreach (var car in _cars)
        {
            if (!canDrive || car.Out || car.Fall > 0f)
            {
                car.Drive = 0f;
                car.Steer = 0f;
            }
            else if (car.IsLocal)
            {
                car.Drive = _buildMode || _screen != UiScreen.Game ? 0f : _driveInput;
                car.Steer = _buildMode || _screen != UiScreen.Game ? 0f : _steerInput;
            }
            else if (car.IsBot)
            {
                BotInput(car, out car.Drive, out car.Steer);
            }
            else
            {
                var peer = _net?.FindPeer(car.Id);
                car.Drive = peer != null ? Mathf.Clamp(peer.Drive, ReverseInput, 1f) : 0f;
                car.Steer = peer != null ? peer.Steer : 0f;
            }

            if (car.Finished && _mode == GameMode.Race && !car.IsBot)
                car.Drive = Mathf.Min(car.Drive, 0.4f); // cool-down lap
            DriveCar(car, dt);
        }

        ResolveContacts();
        foreach (var car in _cars) ApplyTransform(car);

        if (_mode == GameMode.Race)
        {
            UpdateRaceProgress();
            AnimateAndCollectCoins(true);
        }
        else if (_mode == GameMode.Battle)
        {
            UpdateBattle(dt);
        }
        else
        {
            AnimateAndCollectCoins(true);
        }
    }

    private void UpdateCarVisuals(float dt)
    {
        var paused = _screen == UiScreen.Pause && !IsOnline;
        foreach (var car in _cars)
        {
            if (car.Rig != null) car.Rig.UpdateVisual(car.Speed, car.Steer, dt);
            var ratio = car.Speed / (car.Spec.TopSpeed * SpeedScale);
            car.Voice?.Update(ratio, car.Drive, car.Steer, Mathf.Max(dt, 0.0001f), !paused && car.Fall <= 0f && _arena != null);
        }
    }

    // ------------------------------------------------------------------ race

    private static void StartLapCounter(Vector2 position, out float laps, out float lastProgress)
    {
        lastProgress = RaceTrack.Shared.Progress(position);
        // Cars start just behind the line, so their progress begins slightly below zero.
        laps = lastProgress > 0.5f ? lastProgress - 1f : lastProgress;
    }

    private static void AdvanceLapCounter(Vector2 position, ref float laps, ref float lastProgress)
    {
        var progress = RaceTrack.Shared.Progress(position);
        var delta = progress - lastProgress;
        if (delta > 0.5f) delta -= 1f;
        if (delta < -0.5f) delta += 1f;
        laps += delta; // driving backwards subtracts, so laps cannot be faked
        lastProgress = progress;
    }

    private void UpdateRaceProgress()
    {
        if (_state != GameState.Racing) return;

        foreach (var car in _cars)
        {
            if (car.Finished) continue;
            var lapBefore = Mathf.FloorToInt(car.Laps);
            AdvanceLapCounter(car.Position, ref car.Laps, ref car.LastProgress);
            var lapAfter = Mathf.FloorToInt(car.Laps);
            if (car.Laps >= LapCount)
            {
                car.Finished = true;
                car.FinishTime = _raceTime;
                car.Place = ++_finishCounter;
                if (!car.IsBot && _firstFinishTime < 0f) _firstFinishTime = _raceTime;
                if (car.IsLocal)
                {
                    GameAudio.Instance.Play(car.Place == 1 ? GameAudio.Sfx.Win : GameAudio.Sfx.Lap);
                    ShowHint(car.Place == 1 ? "1ST PLACE!" : "Finished " + Ordinal(car.Place));
                }
            }
            else if (car.IsLocal && lapAfter > lapBefore && lapAfter > 0)
            {
                GameAudio.Instance.Play(GameAudio.Sfx.Lap);
                ShowHint(lapAfter == LapCount - 1 ? "FINAL LAP!" : "LAP " + (lapAfter + 1) + "/" + LapCount);
            }
        }

        // The race ends when every player has finished (bots may still be driving), or a while
        // after the first player finished so nobody waits forever.
        var humansDone = _cars.TrueForAll(c => c.IsBot || c.Finished);
        var timedOut = _firstFinishTime >= 0f && _raceTime - _firstFinishTime > RaceFinishGrace;
        if (humansDone || timedOut) FinishRace();
    }

    private void FinishRace()
    {
        _state = GameState.Finished;
        // Unfinished cars are ranked by how far they got.
        var order = RaceOrder();
        for (var i = 0; i < order.Count; i++)
            if (!order[i].Finished) order[i].Place = i + 1;

        var player = _player;
        var bestKey = "Race";
        _newBest = player != null && player.Finished && (_bestTime <= 0f || player.FinishTime < _bestTime);
        if (_newBest)
        {
            _bestTime = player.FinishTime;
            GameSettings.SetBestTime(bestKey, _bestTime);
        }

        if (player != null && player.Place != 1) GameAudio.Instance.Play(GameAudio.Sfx.Lose);
        SetMessage(player == null ? "Race over." : (player.Place == 1 ? "You won! " : Ordinal(player.Place) + " place. ")
            + (player != null && player.Finished ? "Time " + FormatTime(player.FinishTime) : "") + (_newBest ? "  NEW BEST!" : ""));
    }

    /// <summary>Cars from first to last: finishers by finish order, then by laps driven.</summary>
    private List<Car> RaceOrder()
    {
        var order = new List<Car>(_cars);
        order.Sort((a, b) =>
        {
            if (a.Finished != b.Finished) return a.Finished ? -1 : 1;
            if (a.Finished) return a.Place.CompareTo(b.Place);
            return b.Laps.CompareTo(a.Laps);
        });
        return order;
    }

    private List<Car> BattleOrder()
    {
        var order = new List<Car>(_cars);
        order.Sort((a, b) => b.Wins.CompareTo(a.Wins));
        return order;
    }

    private int PlayerPosition()
    {
        if (_player == null) return 1;
        if (_state == GameState.Finished && _player.Place > 0) return _player.Place;
        var order = _mode == GameMode.Battle ? BattleOrder() : RaceOrder();
        return order.IndexOf(_player) + 1;
    }

    private static string Ordinal(int place) =>
        place + (place == 1 ? "st" : place == 2 ? "nd" : place == 3 ? "rd" : "th");

    // ------------------------------------------------------------------ battle

    /// <summary>Cars evenly around the ring facing the centre, the ring at full size.</summary>
    private void ResetBattleRound()
    {
        _battleRadius = BattleStartRadius;
        BattleArena.SetRadius(_battleRing, _battleRadius);
        _raceTime = 0f;
        for (var i = 0; i < _cars.Count; i++)
        {
            var car = _cars[i];
            // Player 0 starts nearest the phone (-z), the rest spread evenly around.
            var angle = Mathf.PI * 1.5f + i * Mathf.PI * 2f / _cars.Count;
            car.Position = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * BattleStartOffset;
            car.Heading = Mathf.Atan2(-car.Position.x, -car.Position.y) * Mathf.Rad2Deg;
            car.Speed = 0f;
            car.Knock = Vector2.zero;
            car.Lateral = 0f;
            car.Fall = 0f;
            car.Out = false;
            ApplyTransform(car);
        }
    }

    private void UpdateBattle(float dt)
    {
        foreach (var car in _cars)
            if (car.Fall > 0f) car.Fall += dt;

        if (_state != GameState.Racing) return;

        if (_knockoutUntil > 0f)
        {
            if (Time.time < _knockoutUntil) return;
            _knockoutUntil = 0f;
            if (_cars.Exists(c => c.Wins >= BattleWinsNeeded))
            {
                FinishBattle();
                return;
            }

            ResetBattleRound();
            _state = GameState.Countdown;
            _countdown = 2f;
            _lastCountdownBeep = 3;
            SetMessage("Next round!  " + ScoreLine());
            return;
        }

        // After the main shrink the ring keeps closing, so a crowded centre always ends the round.
        _battleRadius = _raceTime <= BattleShrinkSeconds
            ? Mathf.Lerp(BattleStartRadius, BattleEndRadius, _raceTime / BattleShrinkSeconds)
            : Mathf.Lerp(BattleEndRadius, BattleStartRadius * 0.1f, (_raceTime - BattleShrinkSeconds) / 20f);
        BattleArena.SetRadius(_battleRing, _battleRadius);

        foreach (var car in _cars)
        {
            if (car.Out || BattleArena.Norm(car.Position, _arenaShape) <= _battleRadius + 0.015f) continue;
            car.Out = true;
            car.Fall = 0.0001f;
            GameAudio.Instance.Play(GameAudio.Sfx.Knockout, car.IsLocal ? 1f : 0.6f);
            if (car.IsLocal)
            {
                GameAudio.Haptic(120);
                ShowHint("OUT!");
            }
            else
            {
                ShowHint(car.Name + " is out!");
            }
        }

        var alive = _cars.FindAll(c => !c.Out);
        if (alive.Count > 1) return;

        if (alive.Count == 1) alive[0].Wins++;
        _knockoutText = alive.Count == 1 ? (alive[0].IsLocal ? "ROUND WON!" : alive[0].Name.ToUpperInvariant() + " SCORES") : "DRAW";
        _knockoutUntil = Time.time + 1.8f;
        SetMessage(ScoreLine());
    }

    private string ScoreLine()
    {
        var parts = new List<string>();
        foreach (var car in BattleOrder()) parts.Add((car.IsLocal ? "YOU" : car.Name) + " " + car.Wins);
        return string.Join("  •  ", parts);
    }

    private void FinishBattle()
    {
        _state = GameState.Finished;
        var order = BattleOrder();
        for (var i = 0; i < order.Count; i++) order[i].Place = i + 1;
        var won = _player != null && _player.Place == 1;
        GameAudio.Instance.Play(won ? GameAudio.Sfx.Win : GameAudio.Sfx.Lose);
        SetMessage((won ? "You win the battle!  " : order[0].Name + " wins the battle.  ") + ScoreLine());
    }

    // ------------------------------------------------------------------ computer drivers

    private void BotInput(Car car, out float drive, out float steer)
    {
        if (_mode == GameMode.Battle)
        {
            // Ram the nearest car toward the edge, but back off to the centre near the edge.
            Car target = null;
            var best = float.MaxValue;
            foreach (var other in _cars)
            {
                if (other == car || other.Out) continue;
                var distance = (other.Position - car.Position).sqrMagnitude;
                if (distance < best) { best = distance; target = other; }
            }

            var aim = Vector2.zero;
            if (BattleArena.Norm(car.Position, _arenaShape) < _battleRadius - 0.11f && target != null)
                aim = target.Position + (target.Position.sqrMagnitude > 0.0001f ? target.Position.normalized * 0.08f : Vector2.zero);
            var toAim = aim - car.Position;
            var error = Mathf.DeltaAngle(car.Heading, Mathf.Atan2(toAim.x, toAim.y) * Mathf.Rad2Deg);
            steer = Mathf.Clamp(error / 25f, -1f, 1f);
            drive = Mathf.Abs(error) < 75f ? 0.9f : 0.5f;
            return;
        }

        // Racing line follower with a personal lane, easing off in corners and rubber-banding
        // against the leading player so races stay close.
        var track = RaceTrack.Shared;
        var lane = Mathf.Sin(_raceTime * 0.45f + car.Id * 1.7f) * 0.045f;
        var targetPoint = track.PointAt(track.Progress(car.Position) + 0.055f, lane);
        var toTarget = targetPoint - car.Position;
        steer = Mathf.Clamp(Mathf.DeltaAngle(car.Heading, Mathf.Atan2(toTarget.x, toTarget.y) * Mathf.Rad2Deg) / 20f, -1f, 1f);

        var leadingHuman = -1f;
        foreach (var other in _cars)
            if (!other.IsBot) leadingHuman = Mathf.Max(leadingHuman, other.Laps);
        var gap = car.Laps - leadingHuman;
        var skill = 0.94f + (car.Id % 3) * 0.03f;
        skill *= gap > 0.15f ? 0.9f : gap < -0.08f ? 1.12f : 1f;
        drive = car.Finished ? 0.4f : skill * (1f - 0.35f * Mathf.Abs(steer));
    }

    // ------------------------------------------------------------------ driving

    private void DriveCar(Car car, float dt)
    {
        if (car.Fall > 0f)
        {
            car.Position += (car.Position.sqrMagnitude > 0.0001f ? car.Position.normalized : Vector2.up) * (0.2f * dt);
            return;
        }

        var start = car.Position;
        var startHeading = car.Heading;
        var position = start;
        var heading = startHeading;
        var boost = (Time.time < car.BoostUntil ? BoostMultiplier : 1f) * SpeedScale;
        StepCar(car, ref position, ref heading, boost, dt, _mode == GameMode.Race);
        if (_mode == GameMode.Battle)
            ApplyKnock(car, ref position, dt);

        // Real and placed obstacles only exist around this phone, so they stop the local car.
        if (car.IsLocal)
            ResolveWorldCollision(car, start, startHeading, ref position, ref heading, dt);

        car.Position = position;
        car.Heading = heading;
    }

    private void ResolveWorldCollision(Car car, Vector2 start, float startHeading, ref Vector2 position, ref float heading, float dt)
    {
        var travel = position - start;
        if (!CollidesWithWorld(car, position, heading, travel, out var normal, out _)) return;

        if (CollidesWithWorld(car, start, startHeading, travel, out var startNormal, out var startDepth))
        {
            // Already touching (a new obstacle appeared in the depth map, or one was placed):
            // let the car ease out instead of freezing it.
            position += startNormal * Mathf.Min(startDepth, 0.6f * dt);
            return;
        }

        // Slide along the surface: drop the part of the move that goes into it.
        var into = Vector2.Dot(travel, normal);
        var slide = start + (into < 0f ? travel - normal * into : travel);
        if (!CollidesWithWorld(car, slide, heading, travel, out _, out _)) position = slide;
        else if (!CollidesWithWorld(car, start, heading, travel, out _, out _)) position = start;
        else
        {
            position = start;
            heading = startHeading;
        }

        // Head-on hits bounce the car back and rock the body; glancing hits scrape.
        var velocity = CarCollision.Forward(startHeading) * car.Speed + CarCollision.Right(startHeading) * car.Lateral;
        var approach = -Vector2.Dot(velocity, normal);
        car.Lateral *= 0.4f;
        if (approach > 0.3f)
        {
            var strength = approach / Mathf.Max(0.1f, car.Spec.TopSpeed * FreeSpeedMultiplier);
            if (car.Rig != null) car.Rig.Bump(Mathf.Sign(car.Speed) * strength);
            car.Speed = -car.Speed * 0.25f;
            var crash = approach > 0.45f;
            ShowHint(crash ? "CRASH!" : "Bump!");
            GameAudio.Instance.Play(crash ? GameAudio.Sfx.Crash : GameAudio.Sfx.Bump, Mathf.Clamp01(strength + 0.3f));
            GameAudio.Haptic(crash ? 70 : 35);
        }
        else
        {
            car.Speed *= Mathf.Exp(-(4f + 10f * Mathf.Clamp01(-Vector2.Dot(CarCollision.Forward(startHeading), normal))) * dt);
        }
    }

    /// <summary>
    /// Car physics with the car's own stats: throttle and brakes along the car, steering that
    /// grows with speed, and a sideways slide. When the car turns, its velocity is carried over
    /// into the new direction, so part of it becomes sideways; grip then kills the slide. At
    /// speed with the wheel turned, drift-happy cars lose rear grip and slide longer.
    /// </summary>
    private void StepCar(Car car, ref Vector2 position, ref float heading, float speedMultiplier, float dt, bool onTrack)
    {
        var spec = car.Spec;
        var onRoad = !onTrack
            || Mathf.Abs(RaceTrack.Shared.LateralOffset(position)) <= RaceTrack.Shared.RoadHalfWidth + 0.016f;
        var topSpeed = spec.TopSpeed * speedMultiplier * (onRoad ? 1f : spec.OffRoad);
        var targetSpeed = car.Drive * topSpeed;
        float rate;
        if (Mathf.Abs(targetSpeed) < 0.001f)
            rate = RollingFriction;
        else if (Mathf.Abs(car.Speed) > 0.01f && Mathf.Sign(targetSpeed) != Mathf.Sign(car.Speed))
            rate = Braking;
        else
            rate = spec.Acceleration * Mathf.Max(1f, speedMultiplier * 0.8f);
        car.Speed = Mathf.MoveTowards(car.Speed, targetSpeed, rate * dt);

        if (Mathf.Abs(car.Speed) < 0.0005f && Mathf.Abs(targetSpeed) < 0.001f && Mathf.Abs(car.Lateral) < 0.0005f)
        {
            car.Speed = 0f;
            car.Lateral = 0f;
            return;
        }

        // Steering: authority grows with speed, flips in reverse, eases off at very high speed.
        var steerAuthority = Mathf.Clamp01(Mathf.Abs(car.Speed) / FullSteerSpeed) * Mathf.Sign(car.Speed);
        var highSpeedEase = Mathf.Lerp(1f, 0.72f, Mathf.Clamp01((Mathf.Abs(car.Speed) - MaxSpeed) / MaxSpeed));
        var turn = car.Steer * spec.TurnRate * steerAuthority * highSpeedEase * dt;

        // Carry the velocity into the rotated frame: some forward speed becomes slide.
        if (Mathf.Abs(turn) > 0.0001f)
        {
            var radians = turn * Mathf.Deg2Rad;
            var cos = Mathf.Cos(radians);
            var sin = Mathf.Sin(radians);
            var speed = car.Speed;
            var lateral = car.Lateral;
            car.Speed = speed * cos + lateral * sin;
            car.Lateral = lateral * cos - speed * sin;
            heading += turn;
        }

        var speedRatio = Mathf.Clamp01(Mathf.Abs(car.Speed) / Mathf.Max(0.01f, spec.TopSpeed));
        var looseness = spec.Drift * Mathf.Clamp01((speedRatio - 0.55f) / 0.45f) * Mathf.Abs(car.Steer);
        var grip = spec.Grip * (onRoad ? 1f : 0.7f) * (1f - 0.75f * looseness);
        car.Lateral *= Mathf.Exp(-grip * dt);
        // Sliding scrubs speed.
        car.Speed -= Mathf.Sign(car.Speed) * Mathf.Min(Mathf.Abs(car.Speed), Mathf.Abs(car.Lateral) * 0.6f * dt);

        position += (CarCollision.Forward(heading) * car.Speed + CarCollision.Right(heading) * car.Lateral) * dt;
        if (onTrack && RaceTrack.Shared.ConstrainToRails(ref position, spec.HalfWidth))
        {
            // Scrape along the barrier, but keep enough speed to steer back onto the road.
            var scrapeSpeed = Mathf.Sign(car.Speed) * Mathf.Min(Mathf.Abs(car.Speed), spec.TopSpeed * 0.4f);
            car.Speed = Mathf.MoveTowards(car.Speed, scrapeSpeed, 2.5f * dt);
            car.Lateral *= 0.5f;
        }

        var clamped = ClampInsideArena(car, position, heading);
        if ((clamped - position).sqrMagnitude > 0.0000001f && _mode == GameMode.Free)
        {
            car.Speed *= Mathf.Exp(-8f * dt); // the edge of the mapped area / the room wall
            car.Lateral *= 0.5f;
        }

        position = clamped;
    }

    /// <summary>Knock-back from hits slides a car and fades out quickly.</summary>
    private void ApplyKnock(Car car, ref Vector2 position, float dt)
    {
        position += car.Knock * dt;
        car.Knock *= Mathf.Exp(-3.2f * dt);
        position = ClampInsideArena(car, position, car.Heading);
    }

    /// <summary>Keeps the whole rotated car body inside the current bounds.</summary>
    private Vector2 ClampInsideArena(Car car, Vector2 position, float heading)
    {
        var forward = CarCollision.Forward(heading);
        var right = CarCollision.Right(heading);
        var spec = car.Spec;
        var extentX = spec.HalfWidth * Mathf.Abs(right.x) + spec.HalfLength * Mathf.Abs(forward.x);
        var extentZ = spec.HalfWidth * Mathf.Abs(right.y) + spec.HalfLength * Mathf.Abs(forward.y);
        const float inset = 0.012f;
        position.x = Mathf.Clamp(position.x, _bounds.xMin + inset + extentX, _bounds.xMax - inset - extentX);
        position.y = Mathf.Clamp(position.y, _bounds.yMin + inset + extentZ, _bounds.yMax - inset - extentZ);
        return position;
    }

    /// <summary>Cars bump and push each other apart instead of driving through one another.</summary>
    private void ResolveContacts()
    {
        for (var i = 0; i < _cars.Count; i++)
            for (var j = i + 1; j < _cars.Count; j++)
                ResolveContact(_cars[i], _cars[j]);
    }

    private static float ContactRadius(Car car) => (car.Spec.HalfWidth + car.Spec.HalfLength) * 0.45f;

    private void ResolveContact(Car a, Car b)
    {
        if (a.Fall > 0f || b.Fall > 0f) return;
        var delta = b.Position - a.Position;
        var distance = delta.magnitude;
        var contact = ContactRadius(a) + ContactRadius(b);
        if (distance >= contact || distance < 0.0001f) return;

        // Heavier cars get pushed less.
        var normal = delta / distance;
        var overlap = contact - distance;
        var aShare = b.Spec.Mass / (a.Spec.Mass + b.Spec.Mass);
        a.Position -= normal * overlap * aShare;
        b.Position += normal * overlap * (1f - aShare);

        var aVelocity = CarCollision.Forward(a.Heading) * a.Speed + CarCollision.Right(a.Heading) * a.Lateral + a.Knock;
        var bVelocity = CarCollision.Forward(b.Heading) * b.Speed + CarCollision.Right(b.Heading) * b.Lateral + b.Knock;
        var aAttack = Mathf.Max(0f, Vector2.Dot(aVelocity, normal));
        var bAttack = Mathf.Max(0f, Vector2.Dot(bVelocity, -normal));

        if (_mode == GameMode.Battle)
        {
            // Ramming transfers momentum, scaled by weight: a heavy car hits harder and moves less.
            if (aAttack + bAttack < 0.02f) return;
            var ratio = a.Spec.Mass / b.Spec.Mass;
            b.Knock += normal * (aAttack * 1.45f * ratio + bAttack * 0.2f);
            a.Knock -= normal * (bAttack * 1.45f / ratio + aAttack * 0.2f);
            a.Speed *= 0.55f;
            b.Speed *= 0.55f;
            if (aAttack > 0.15f || bAttack > 0.15f) ContactFeedback(a, b, aAttack, bAttack);
            return;
        }

        if (_mode == GameMode.Race)
        {
            RaceTrack.Shared.ConstrainToRails(ref a.Position, a.Spec.HalfWidth);
            RaceTrack.Shared.ConstrainToRails(ref b.Position, b.Spec.HalfWidth);
        }

        a.Speed *= 0.93f;
        b.Speed *= 0.93f;
        if (aAttack + bAttack > 0.25f) ContactFeedback(a, b, aAttack, bAttack);
    }

    private void ContactFeedback(Car a, Car b, float aAttack, float bAttack)
    {
        if (!a.IsLocal && !b.IsLocal) return;
        var mine = a.IsLocal ? aAttack : bAttack;
        var theirs = a.IsLocal ? bAttack : aAttack;
        ShowHint(mine >= theirs ? "SMASH!" : "Ouch!");
        GameAudio.Instance.Play(GameAudio.Sfx.Bump, Mathf.Clamp01(mine + theirs));
        GameAudio.Haptic(40);
    }

    private void ApplyTransform(Car car)
    {
        if (car.Root == null) return;
        var drop = car.Fall * car.Fall * 1.4f;
        car.Root.localPosition = new Vector3(car.Position.x, 0.002f - drop, car.Position.y);
        car.Root.localRotation = Quaternion.Euler(0f, car.Heading, 0f) * Quaternion.Euler(car.Fall * 70f, 0f, 0f);
    }

    // ------------------------------------------------------------------ garage rewards

    /// <summary>
    /// Pays garage coins: a few for every coin the player picks up, and a prize for the final
    /// place in a race or battle. Works the same on the host and on guests (it reads the synced state).
    /// </summary>
    private void UpdateRewards()
    {
        if (_player == null) return;
        var picked = _player.Coins - _coinsRewarded;
        if (picked > 0)
        {
            var amount = picked * (_mode == GameMode.Free ? PlayerProfile.CoinPickupFree : PlayerProfile.CoinPickupRound);
            _roundEarned += PlayerProfile.Reward(amount);
            _earnFlashAt = Time.time;
        }

        _coinsRewarded = _player.Coins;
        if (_prizeGiven || _state != GameState.Finished || _mode == GameMode.Free || _player.Place <= 0) return;
        _prizeGiven = true;
        _placePrize = _mode == GameMode.Race ? PlayerProfile.RaceReward(_player.Place, _cars.Count) : PlayerProfile.BattleReward(_player.Place, _cars.Count);
        _placePrize = PlayerProfile.Reward(_placePrize);
        _roundEarned += _placePrize;
        _earnFlashAt = Time.time;
        GameAudio.Instance.Play(GameAudio.Sfx.Coin);
    }

    // ------------------------------------------------------------------ coins

    /// <summary>
    /// Spins the coins and, when this phone runs the simulation, lets any car pick them up.
    /// A network guest only animates; the host tells it which coins are present.
    /// </summary>
    private void AnimateAndCollectCoins(bool simulate)
    {
        for (var i = 0; i < _coins.Count; i++)
        {
            var coin = _coins[i];
            if (coin == null) continue;
            if (!coin.gameObject.activeSelf)
            {
                if (simulate && Time.time >= _coinRespawnAt[i]) coin.gameObject.SetActive(true);
                continue;
            }

            coin.Rotate(0f, 100f * Time.deltaTime, 0f, Space.Self);
            var basePosition = coin.localPosition;
            basePosition.y = 0.06f + Mathf.Sin(Time.time * 3f + i) * 0.01f;
            coin.localPosition = basePosition;

            if (!simulate || _state != GameState.Racing) continue;
            var coinPoint = new Vector2(basePosition.x, basePosition.z);
            foreach (var car in _cars)
            {
                if (car.Fall > 0f || !TouchesCoin(car, coinPoint)) continue;
                // Coins are boost pads: they vanish for a few seconds and then come back.
                coin.gameObject.SetActive(false);
                _coinRespawnAt[i] = Time.time + CoinRespawnSeconds;
                car.BoostUntil = Time.time + BoostSeconds;
                car.Coins++;
                if (car.IsLocal)
                {
                    ShowHint("BOOST!");
                    GameAudio.Instance.Play(GameAudio.Sfx.Coin);
                }

                break;
            }
        }
    }

    // Collect when any part of the car body touches the coin, not just its centre.
    private static bool TouchesCoin(Car car, Vector2 coinPoint)
    {
        var forward = CarCollision.Forward(car.Heading);
        var right = CarCollision.Right(car.Heading);
        var delta = coinPoint - car.Position;
        var alongRight = Mathf.Clamp(Vector2.Dot(delta, right), -car.Spec.HalfWidth, car.Spec.HalfWidth);
        var alongForward = Mathf.Clamp(Vector2.Dot(delta, forward), -car.Spec.HalfLength, car.Spec.HalfLength);
        var closest = right * alongRight + forward * alongForward;
        return (delta - closest).magnitude <= ProceduralCarFactory.CoinRadius + 0.02f;
    }

    private static string FormatTime(float seconds)
    {
        var minutes = Mathf.FloorToInt(seconds / 60f);
        return minutes + ":" + (seconds - minutes * 60f).ToString("00.0");
    }
}
