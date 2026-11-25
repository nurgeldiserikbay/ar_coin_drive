using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Up to four players on the local network. The host simulates every car and sends the whole
/// state about 30 times a second; guests send their controls and draw what the host reports.
/// Each phone uses its own floor (AR) or its own virtual room; the track is the same everywhere.
/// </summary>
public sealed partial class ARDemoController
{
    private LanSession _net;
    private readonly byte[] _statePacket = new byte[512];
    private float _nextStateSend;
    private bool _rebuildForPlayers;

    private bool IsOnline => _net != null;
    private bool IsHost => _net != null && _net.Role == LanSession.SessionRole.Host;
    private bool IsClient => _net != null && _net.Role == LanSession.SessionRole.Client;

    private void StartHosting()
    {
        LeaveNetwork(false);
        try
        {
            _net = LanSession.Host(GameSettings.PlayerName, GameSettings.CarColor, GameSettings.CarType);
            if (_mode == GameMode.Free) _mode = GameMode.Race;
            _buildMode = false;
        }
        catch (System.Exception exception)
        {
            _net = null;
            ShowToast("Could not open the network: " + exception.Message);
        }
    }

    private void StartBrowsing()
    {
        LeaveNetwork(false);
        try
        {
            _net = LanSession.Browse(GameSettings.PlayerName, GameSettings.CarColor, GameSettings.CarType);
        }
        catch (System.Exception exception)
        {
            _net = null;
            ShowToast("Could not open the network: " + exception.Message);
        }
    }

    private void LeaveNetwork(bool restart)
    {
        if (_net == null) return;
        _net.Dispose();
        _net = null;
        if (restart && _arena != null)
        {
            SetModeVisuals();
            StartRound();
        }
    }

    /// <summary>Polls the link; reacts to players joining, leaving or the host going away.</summary>
    private void UpdateNetwork()
    {
        if (_net == null) return;
        _net.Poll(Time.unscaledTime);

        if (IsClient)
        {
            if (_net.LostConnection)
            {
                LeaveNetwork(true);
                ShowToast("The host left. You are playing on your own again.");
            }

            return;
        }

        while (_net.Joined.Count > 0)
        {
            var slot = _net.Joined.Dequeue();
            var peer = _net.FindPeer(slot);
            GameAudio.Instance.Play(GameAudio.Sfx.Place);
            ShowToast((peer != null ? peer.Name : "A player") + " joined (" + _net.PlayerCount + "/" + LanSession.MaxPlayers + ")");
            _rebuildForPlayers = true;
        }

        while (_net.Left.Count > 0)
        {
            var slot = _net.Left.Dequeue();
            var car = _cars.Find(c => c.Id == slot && !c.IsBot && !c.IsLocal);
            if (car != null)
            {
                // Mid-round, a computer driver takes over the car.
                car.IsBot = true;
                car.Name += " (CPU)";
            }

            ShowToast("A player left. " + (_net.PlayerCount > 1 ? _net.PlayerCount + " players now." : "Waiting for players."));
            _rebuildForPlayers = _state != GameState.Racing && _state != GameState.Countdown;
        }

        // New players join at the start line: rebuild the round when nobody is racing.
        if (_rebuildForPlayers && _arena != null && (_state == GameState.Ready || _state == GameState.Finished))
        {
            _rebuildForPlayers = false;
            StartRound();
        }

        SendState();
    }

    // ------------------------------------------------------------------ host -> guests

    private void SendState()
    {
        if (!IsHost || _net.Peers.Count == 0 || _arena == null || Time.unscaledTime < _nextStateSend) return;
        _nextStateSend = Time.unscaledTime + 1f / 30f;

        using (var stream = new MemoryStream(_statePacket))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((byte)0); // packet type, filled in by LanSession
            writer.Write((byte)_mode);
            writer.Write((byte)_state);
            writer.Write(_countdown);
            writer.Write(_raceTime);
            writer.Write(_battleRadius);
            writer.Write(Mathf.Max(0f, _knockoutUntil - Time.time));
            writer.Write(Mathf.Max(0f, _goFlashUntil - Time.time));
            var coinBits = 0;
            for (var i = 0; i < _coins.Count && i < 16; i++)
                if (_coins[i] != null && _coins[i].gameObject.activeSelf) coinBits |= 1 << i;
            writer.Write((ushort)coinBits);
            writer.Write((byte)_cars.Count);
            foreach (var car in _cars)
            {
                writer.Write((byte)car.Id);
                writer.Write((byte)car.ColorIndex);
                writer.Write((byte)car.SpecIndex);
                var flags = (car.Finished ? 1 : 0) | (car.Out ? 2 : 0) | (Time.time < car.BoostUntil ? 4 : 0) | (car.IsBot ? 8 : 0);
                writer.Write((byte)flags);
                writer.Write(car.Position.x);
                writer.Write(car.Position.y);
                writer.Write(car.Heading);
                writer.Write(car.Speed);
                writer.Write(car.Steer);
                writer.Write(car.Fall);
                writer.Write(car.Laps);
                writer.Write(car.FinishTime);
                writer.Write((byte)car.Wins);
                writer.Write((byte)car.Place);
                writer.Write((byte)car.Coins);
                var name = Encoding.UTF8.GetBytes(car.Name ?? "");
                var length = Mathf.Min(name.Length, 30);
                writer.Write((byte)length);
                writer.Write(name, 0, length);
            }

            if (stream.Position <= _statePacket.Length) _net.SendState(_statePacket, (int)stream.Position);
        }
    }

    // ------------------------------------------------------------------ guest side

    /// <summary>Guest: send controls, apply the latest host state and smooth the cars toward it.</summary>
    private void UpdateClient(float dt)
    {
        var playing = _screen == UiScreen.Game && !_buildMode;
        _net.SendInput(playing ? _driveInput : 0f, playing ? _steerInput : 0f);

        var packet = _net.TakeState();
        if (packet != null) ApplyHostState(packet);

        var follow = 1f - Mathf.Exp(-18f * dt);
        foreach (var car in _cars)
        {
            if (!car.Seen)
            {
                car.Position = car.TargetPosition;
                car.Heading = car.TargetHeading;
                car.Seen = true;
            }

            car.Position = Vector2.Lerp(car.Position, car.TargetPosition, follow);
            car.Heading = Mathf.LerpAngle(car.Heading, car.TargetHeading, follow);
            if (car.Fall > 0f && _mode == GameMode.Battle) car.Fall += dt * 0.5f;
            ApplyTransform(car);
        }

        if (_mode != GameMode.Battle) AnimateAndCollectCoins(false);
    }

    private void ApplyHostState(byte[] packet)
    {
        if (_arena == null) return;
        try
        {
            using (var reader = new BinaryReader(new MemoryStream(packet)))
            {
                reader.ReadByte();
                var mode = (GameMode)reader.ReadByte();
                var state = (GameState)reader.ReadByte();
                var countdown = reader.ReadSingle();
                var raceTime = reader.ReadSingle();
                var battleRadius = reader.ReadSingle();
                var knockoutLeft = reader.ReadSingle();
                var goFlash = reader.ReadSingle();
                var coinBits = reader.ReadUInt16();
                var count = reader.ReadByte();

                if (mode != _mode)
                {
                    _mode = mode;
                    SetModeVisuals();
                    StartRound();
                }

                var previous = _state;
                if (state == GameState.Countdown && previous != GameState.Countdown) _lastCountdownBeep = 4;
                if (state == GameState.Countdown)
                {
                    var second = Mathf.CeilToInt(countdown);
                    if (second < _lastCountdownBeep && second > 0)
                    {
                        _lastCountdownBeep = second;
                        GameAudio.Instance.Play(GameAudio.Sfx.Beep);
                    }
                }

                if (state == GameState.Racing && previous == GameState.Countdown) GameAudio.Instance.Play(GameAudio.Sfx.Go);
                _state = state;
                _countdown = countdown;
                _raceTime = raceTime;
                _battleRadius = battleRadius;
                BattleArena.SetRadius(_battleRing, battleRadius);
                _knockoutUntil = knockoutLeft > 0f ? Time.time + knockoutLeft : 0f;
                _goFlashUntil = Time.time + goFlash;
                for (var i = 0; i < _coins.Count && i < 16; i++)
                    if (_coins[i] != null) _coins[i].gameObject.SetActive((coinBits & (1 << i)) != 0);

                var seen = new System.Collections.Generic.HashSet<int>();
                for (var i = 0; i < count; i++)
                {
                    var id = reader.ReadByte();
                    var color = reader.ReadByte();
                    var carType = reader.ReadByte();
                    var flags = reader.ReadByte();
                    var position = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    var heading = reader.ReadSingle();
                    var speed = reader.ReadSingle();
                    var steer = reader.ReadSingle();
                    var fall = reader.ReadSingle();
                    var laps = reader.ReadSingle();
                    var finishTime = reader.ReadSingle();
                    var wins = reader.ReadByte();
                    var place = reader.ReadByte();
                    var coins = reader.ReadByte();
                    var name = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadByte()));
                    seen.Add(id);

                    var car = _cars.Find(c => c.Id == id);
                    if (car == null || car.ColorIndex != color || car.SpecIndex != carType)
                    {
                        if (car != null) RemoveCar(car);
                        car = AddCar(id, name, Mathf.Clamp(color, 0, GameSettings.CarColors.Length - 1), carType, id == _net.Slot, (flags & 8) != 0);
                        if (car.IsLocal) _player = car;
                    }

                    car.Name = id == _net.Slot ? GameSettings.PlayerName : name;
                    car.IsBot = (flags & 8) != 0;
                    car.TargetPosition = position;
                    car.TargetHeading = heading;
                    car.Speed = speed;
                    car.Steer = steer;
                    if (car.IsLocal && car.Coins < coins) GameAudio.Instance.Play(GameAudio.Sfx.Coin);
                    if (car.IsLocal && (flags & 4) != 0 && Time.time >= car.BoostUntil) ShowHint("BOOST!");
                    car.BoostUntil = (flags & 4) != 0 ? Time.time + 0.1f : 0f;
                    if (fall > 0f && car.Fall <= 0f) car.Fall = fall;
                    if (fall <= 0f) car.Fall = 0f;
                    var finishedNow = (flags & 1) != 0 && !car.Finished;
                    car.Finished = (flags & 1) != 0;
                    car.Out = (flags & 2) != 0;
                    car.Laps = laps;
                    car.FinishTime = finishTime;
                    car.Wins = wins;
                    car.Place = place;
                    car.Coins = coins;
                    if (finishedNow && car.IsLocal) GameAudio.Instance.Play(place == 1 ? GameAudio.Sfx.Win : GameAudio.Sfx.Lap);
                }

                for (var i = _cars.Count - 1; i >= 0; i--)
                    if (!seen.Contains(_cars[i].Id)) RemoveCar(_cars[i]);

                if (state != previous) SetMessage(ClientMessageFor(state));
            }
        }
        catch (EndOfStreamException)
        {
            // A truncated packet is skipped; the next one arrives in a few milliseconds.
        }
    }

    private string ClientMessageFor(GameState state)
    {
        switch (state)
        {
            case GameState.Ready: return "Connected. Waiting for the host to start.";
            case GameState.Countdown: return _mode == GameMode.Battle ? ScoreLine() : "Get ready...";
            case GameState.Racing:
                return _mode == GameMode.Race ? LapCount + " laps! Drive through coins for a speed boost."
                    : "Push the others out of the ring! It keeps shrinking.";
            case GameState.Finished:
                var place = _player != null ? _player.Place : 0;
                return (place == 1 ? "You won! " : place > 0 ? Ordinal(place) + " place. " : "") + "The host can start a rematch.";
            default: return _message;
        }
    }
}
