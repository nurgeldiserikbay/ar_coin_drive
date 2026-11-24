using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Playable loop: scan and tap a horizontal surface (or play on the virtual island), then race
/// a computer rival for three laps of the toy track. Coins on the road give a short speed boost;
/// the fastest finish is saved as the best time.
/// Real obstacle sensing is approximate and device-dependent.
/// </summary>
public sealed class ARDemoController : MonoBehaviour
{
    private enum GameState { Placing, Ready, Countdown, Racing, Finished }
    private enum GameMode { Race, Battle }
    private enum ControlAction { None, Reset, Again, Mode }

    private sealed class Rival
    {
        public Transform Car;
        public ProceduralCarRig Rig;
        public Vector2 Position;
        public float Heading;
        public float Speed;
        public float Steer;
        public Vector2 Knock;
        public float Fall;
        public float Laps;
        public float LastProgress;
        public bool Finished;
    }

    private const float HalfArena = 0.75f;
    private const int CoinCount = 6;
    private const int LapCount = 5;
    private const float CountdownSeconds = 3f;
    private const float BoostSeconds = 1.6f;
    private const float BoostMultiplier = 1.35f;
    private const float CoinRespawnSeconds = 5f;
    private const float CarContactDistance = 0.165f;
    private const string BestTimeKey = "ARCoinDrive.BestRaceTime";

    // Driving feel: the car accelerates, brakes and only turns while rolling, like a toy RC car.
    private const float MaxSpeed = 0.48f;
    private const float ReverseInput = -0.65f;
    private const float Acceleration = 0.9f;
    private const float Braking = 2.2f;
    private const float RollingFriction = 1.1f;
    private const float TurnSpeed = 140f;
    private const float FullSteerSpeed = 0.16f;

    private static readonly List<ARRaycastHit> RaycastHits = new List<ARRaycastHit>();
    private static readonly Color[] ObstacleColors =
    {
        new Color(0.12f, 0.72f, 0.86f),
        new Color(0.98f, 0.46f, 0.18f),
        new Color(0.43f, 0.78f, 0.28f)
    };

    // Seconds without a camera frame before the HUD suggests the virtual table.
    private const float ARStartTimeout = 12f;

    private ARSession _session;
    private Camera _arCamera;
    private bool _virtualMode;
    private Transform _virtualTable;
    private Vector3 _chaseVelocity;
    private bool _snapChaseCamera;
    private Rect _noArRect;
    private ARRaycastManager _raycastManager;
    private ARPlaneManager _planeManager;
    private ARCameraManager _cameraManager;
    private int _cameraFrameCount;
    private ARAnchorManager _anchorManager;
    private ARAnchor _anchor;
    private ARDepthObstacleDetector _depthDetector;
    private Transform _arena;
    private Transform _roundRoot;
    private Transform _car;
    private ProceduralCarRig _carRig;
    private readonly List<Transform> _coins = new List<Transform>();
    private readonly List<float> _coinRespawnAt = new List<float>();
    private readonly List<Vector2> _obstaclePositions = new List<Vector2>();
    private readonly List<Vector2> _coinLayout = new List<Vector2>();
    private Rival _rival;

    // Push-out battle: a shrinking ring, first to three knockouts wins the match.
    private const int BattleWinsNeeded = 3;
    private const float BattleStartRadius = 0.62f;
    private const float BattleEndRadius = 0.30f;
    private const float BattleShrinkSeconds = 40f;
    private const float BattleStartOffset = 0.29f;
    private GameMode _mode = GameMode.Race;
    private GameObject _trackVisual;
    private Transform _battleRing;
    private float _battleRadius = BattleStartRadius;
    private Vector2 _playerKnock;
    private float _playerFall;
    private int _playerWins;
    private int _rivalWins;
    private float _knockoutUntil;
    private string _knockoutText;
    private Rect _modeRect;

    private GameState _state = GameState.Placing;
    private Vector2 _carPosition;
    private float _carHeading;
    private float _speed;
    private int _score;
    private float _countdown;
    private float _raceTime;
    private float _goFlashUntil;
    private float _boostUntil;
    private float _playerLaps;
    private float _playerLastProgress;
    private int _finishPlace = 1;
    private float _bestTime;
    private bool _newBest;
    private float _driveInput;
    private float _steerInput;
    private bool _leftHeld;
    private bool _rightHeld;
    private bool _goHeld;
    private bool _backHeld;
    private string _message = "Move your phone slowly to scan the floor, then tap the floor.";
    private string _hint;
    private float _hintUntil;

    private float _uiScale = 1f;
    private Rect _hudRect;
    private Rect _leftRect;
    private Rect _rightRect;
    private Rect _goRect;
    private Rect _backRect;
    private Rect _resetRect;
    private Rect _againRect;
    private float _cachedStyleScale = -1f;
    private GUIStyle _titleStyle;
    private GUIStyle _messageStyle;
    private GUIStyle _statusStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _smallButtonStyle;
    private GUIStyle _bigStyle;

    private void Awake()
    {
        _arCamera = Camera.main;
        ARRigSetup.EnsureRig(_arCamera);
        _session = FindAnyObjectByType<ARSession>();
        _raycastManager = FindAnyObjectByType<ARRaycastManager>();
        _planeManager = FindAnyObjectByType<ARPlaneManager>();
        _cameraManager = FindAnyObjectByType<ARCameraManager>();
        if (_cameraManager != null)
            _cameraManager.frameReceived += OnCameraFrameReceived;
        _anchorManager = FindAnyObjectByType<ARAnchorManager>();
        _depthDetector = FindAnyObjectByType<ARDepthObstacleDetector>();
        _bestTime = PlayerPrefs.GetFloat(BestTimeKey, 0f);
        EnsureSceneLight();
    }

    private void Start()
    {
        StartCoroutine(CheckARSupport());
    }

    /// <summary>
    /// Falls back to the virtual table when the device cannot run AR at all. If ARCore is only
    /// missing, ARSession (attemptUpdate) prompts the install and the HUD offers PLAY WITHOUT AR.
    /// </summary>
    private IEnumerator CheckARSupport()
    {
        if (ARSession.state == ARSessionState.None || ARSession.state == ARSessionState.CheckingAvailability)
            yield return ARSession.CheckAvailability();

        if (ARSession.state == ARSessionState.Unsupported && !_virtualMode)
            EnterVirtualMode("This device does not support AR, so you are playing on a virtual table.");
    }

    private void EnterVirtualMode(string reason)
    {
        if (_virtualMode) return;
        if (_arena != null) ResetGame();
        _virtualMode = true;

        // Shut down every AR feature so nothing keeps waiting for tracking or the camera feed.
        if (_planeManager != null)
        {
            _planeManager.SetTrackablesActive(false);
            _planeManager.enabled = false;
            _planeManager = null;
        }

        if (_raycastManager != null) _raycastManager.enabled = false;
        if (_anchorManager != null) _anchorManager.enabled = false;
        _anchorManager = null;
        if (_depthDetector != null)
        {
            _depthDetector.SetArena(null);
            _depthDetector.enabled = false;
            _depthDetector = null;
        }

        if (_arCamera != null)
        {
            if (_arCamera.TryGetComponent<ARCameraBackground>(out var background)) background.enabled = false;
            if (_arCamera.TryGetComponent<AROcclusionManager>(out var occlusion)) occlusion.enabled = false;
            if (_arCamera.TryGetComponent<ARCameraManager>(out var cameraManager)) cameraManager.enabled = false;
            ARRigSetup.SetPoseDriverEnabled(_arCamera, false);
            _arCamera.clearFlags = CameraClearFlags.SolidColor;
            _arCamera.backgroundColor = new Color(0.56f, 0.70f, 0.80f);
        }

        if (_session != null) _session.enabled = false;

        CreateVirtualTable();
        _arena = new GameObject("Bounded Play Area").transform;
        _arena.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        CreateArenaVisuals();
        StartRound();
        _message = reason + " Press GO to start.";
    }

    /// <summary>A small landscaped island around the track for players without AR.</summary>
    private void CreateVirtualTable()
    {
        _virtualTable = new GameObject("Virtual Table").transform;
        MakeScenery("Meadow", new Vector3(0f, -0.015f, 0f), new Vector3(2.3f, 0.03f, 2.3f),
            new Color(0.40f, 0.70f, 0.33f));
        MakeScenery("Island Edge", new Vector3(0f, -0.09f, 0f), new Vector3(2.36f, 0.14f, 2.36f),
            new Color(0.58f, 0.40f, 0.25f));
        MakeScenery("Distant Ground", new Vector3(0f, -0.8f, 0f), new Vector3(40f, 0.02f, 40f),
            new Color(0.48f, 0.72f, 0.56f));

        var treeSpots = new[]
        {
            new Vector2(-0.95f, -0.92f), new Vector2(-0.78f, -1.02f), new Vector2(0.96f, -0.90f),
            new Vector2(1.01f, 0.72f), new Vector2(0.84f, 0.99f), new Vector2(-0.98f, 0.88f),
            new Vector2(-1.03f, 0.05f), new Vector2(1.04f, -0.08f), new Vector2(0.02f, 1.04f),
            new Vector2(-0.06f, -1.05f)
        };
        for (var i = 0; i < treeSpots.Length; i++)
            MakeTree(treeSpots[i], 0.8f + (i * 37 % 10) * 0.05f, i % 3);
    }

    private void MakeTree(Vector2 spot, float size, int tint)
    {
        var foliage = tint == 0 ? new Color(0.20f, 0.56f, 0.28f)
            : tint == 1 ? new Color(0.28f, 0.64f, 0.26f) : new Color(0.16f, 0.48f, 0.30f);
        var trunkHeight = 0.09f * size;
        MakeScenery("Tree Trunk", new Vector3(spot.x, trunkHeight, spot.y),
            new Vector3(0.04f, trunkHeight, 0.04f) * size, new Color(0.42f, 0.27f, 0.16f), PrimitiveType.Cylinder);
        MakeScenery("Tree Crown", new Vector3(spot.x, 0.25f * size, spot.y),
            Vector3.one * 0.25f * size, foliage, PrimitiveType.Sphere);
        MakeScenery("Tree Crown Top", new Vector3(spot.x + 0.025f * size, 0.36f * size, spot.y - 0.015f * size),
            Vector3.one * 0.16f * size, Color.Lerp(foliage, Color.white, 0.10f), PrimitiveType.Sphere);
    }

    private void MakeScenery(string objectName, Vector3 position, Vector3 scale, Color color,
        PrimitiveType shape = PrimitiveType.Cube)
    {
        var part = GameObject.CreatePrimitive(shape);
        part.name = objectName;
        part.transform.SetParent(_virtualTable, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        var material = ProceduralCarFactory.GetMaterial(color);
        if (material != null) part.GetComponent<Renderer>().sharedMaterial = material;
        part.GetComponent<Collider>().enabled = false;
    }

    /// <summary>Chase camera for the virtual table; in AR the phone itself is the camera.</summary>
    private void LateUpdate()
    {
        if (!_virtualMode || _arCamera == null || _car == null) return;

        var forward2 = HeadingToForward(_carHeading);
        var forward = _arena.TransformDirection(new Vector3(forward2.x, 0f, forward2.y));
        var up = _arena.up;
        var carPosition = _car.position;
        var desiredPosition = carPosition - forward * 0.56f + up * 0.40f;
        var lookTarget = carPosition + forward * 0.3f;
        var cameraTransform = _arCamera.transform;

        if (_snapChaseCamera)
        {
            _snapChaseCamera = false;
            _chaseVelocity = Vector3.zero;
            cameraTransform.position = desiredPosition;
            cameraTransform.rotation = Quaternion.LookRotation(lookTarget - desiredPosition, up);
            return;
        }

        cameraTransform.position = Vector3.SmoothDamp(cameraTransform.position, desiredPosition,
            ref _chaseVelocity, 0.28f);
        var targetRotation = Quaternion.LookRotation(lookTarget - cameraTransform.position, up);
        cameraTransform.rotation = Quaternion.Slerp(cameraTransform.rotation, targetRotation,
            1f - Mathf.Exp(-8f * Time.deltaTime));
    }

    private void OnDestroy()
    {
        if (_cameraManager != null)
            _cameraManager.frameReceived -= OnCameraFrameReceived;
    }

    private void OnCameraFrameReceived(ARCameraFrameEventArgs args)
    {
        _cameraFrameCount++;
    }

    /// <summary>
    /// Lit procedural models render almost black without a light; older generated scenes have none.
    /// </summary>
    private static void EnsureSceneLight()
    {
        if (FindAnyObjectByType<Light>() != null) return;

        var lightObject = new GameObject("AR Key Light");
        lightObject.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
        var keyLight = lightObject.AddComponent<Light>();
        keyLight.type = LightType.Directional;
        keyLight.intensity = 1.05f;
        keyLight.shadows = LightShadows.None;
    }

    private void Update()
    {
        UpdateLayout();
        ARRigSetup.EnsurePlaneVisuals(_planeManager);
        SetPlaneVisualsHidden(_arena != null);

        if (_arena == null)
        {
            ReadPlacementTap();
            return;
        }

        var action = ReadControls();
        if (action == ControlAction.Reset)
        {
            // The same touch must not also place a new arena this frame.
            // The virtual table has nothing to re-scan, so RESET just starts a new round there.
            if (_virtualMode) StartRound();
            else ResetGame();
            return;
        }

        if (action == ControlAction.Again)
        {
            StartRound();
            return;
        }

        if (action == ControlAction.Mode)
        {
            _mode = _mode == GameMode.Race ? GameMode.Battle : GameMode.Race;
            SetModeVisuals();
            StartRound();
            return;
        }

        var dt = Time.deltaTime;
        if (_state == GameState.Ready && _driveInput > 0.01f)
        {
            _state = GameState.Countdown;
            _countdown = CountdownSeconds;
            _message = "Get ready...";
        }

        if (_state == GameState.Countdown)
        {
            _countdown -= dt;
            if (_countdown <= 0f)
            {
                _state = GameState.Racing;
                _goFlashUntil = Time.time + 0.9f;
                _message = _mode == GameMode.Race
                    ? LapCount + " laps! Drive through coins for a speed boost."
                    : "Push your rival out of the ring! The ring keeps shrinking.";
            }
        }

        if (_state == GameState.Racing && _knockoutUntil <= 0f)
            _raceTime += dt;

        if (_state != GameState.Racing || _knockoutUntil > 0f)
        {
            _driveInput = 0f;
            _steerInput = 0f;
        }

        DriveCar();
        DriveRival(dt);
        ResolveCarContact();
        ApplyCarTransform();
        ApplyRivalTransform();
        if (_carRig != null)
            _carRig.UpdateVisual(_speed, _steerInput, dt);
        if (_rival != null && _rival.Rig != null)
            _rival.Rig.UpdateVisual(_rival.Speed, _rival.Steer, dt);
        if (_mode == GameMode.Race)
        {
            UpdateRaceProgress();
            AnimateAndCollectCoins();
        }
        else
        {
            UpdateBattle(dt);
        }
    }

    // ---------------------------------------------------------------- placement

    private void ReadPlacementTap()
    {
        for (var i = 0; i < Input.touchCount; i++)
        {
            var touch = Input.GetTouch(i);
            if (touch.phase != TouchPhase.Began) continue;
            HandlePlacementPress(touch.position);
            return;
        }

#if UNITY_EDITOR
        if (Input.GetMouseButtonDown(0))
            HandlePlacementPress(Input.mousePosition);
#endif
    }

    private void HandlePlacementPress(Vector2 screenPosition)
    {
        var guiPoint = ToGuiPoint(screenPosition);
        if (Expand(_noArRect).Contains(guiPoint))
        {
            EnterVirtualMode("Playing on a virtual table without AR.");
            return;
        }

        if (!_hudRect.Contains(guiPoint))
            TryPlaceArena(screenPosition);
    }

    /// <summary>Explains AR start-up problems while scanning instead of leaving the player stuck.</summary>
    private string GetPlacingMessage()
    {
        switch (ARSession.state)
        {
            case ARSessionState.NeedsInstall:
            case ARSessionState.Installing:
                return "Google Play Services for AR is needed. Install it, or tap PLAY WITHOUT AR.";
            case ARSessionState.Unsupported:
                return "AR is not available on this device. Tap PLAY WITHOUT AR.";
        }

        if (_cameraFrameCount == 0 && Time.timeSinceLevelLoad > ARStartTimeout)
            return "The AR camera is not starting. Check camera permission, or tap PLAY WITHOUT AR.";

        return _message;
    }

    private void TryPlaceArena(Vector2 screenPosition)
    {
        if (_raycastManager == null)
        {
            _message = "AR setup is missing. Use Tools > AR Coin Drive > Create Demo Scene.";
            return;
        }

        if (!_raycastManager.Raycast(screenPosition, RaycastHits, TrackableType.PlaneWithinPolygon))
        {
            _message = "No floor found yet. Scan a textured, well-lit floor and tap it.";
            return;
        }

        var floorHitIndex = -1;
        for (var i = 0; i < RaycastHits.Count; i++)
        {
            var hitPlane = RaycastHits[i].trackable as ARPlane;
            if (hitPlane != null && hitPlane.alignment == PlaneAlignment.HorizontalUp)
            {
                floorHitIndex = i;
                break;
            }
        }

        if (floorHitIndex < 0)
        {
            _message = "Tap a detected horizontal surface, such as the floor or a table.";
            return;
        }

        var floorPlane = RaycastHits[floorHitIndex].trackable as ARPlane;
        var hitPose = RaycastHits[floorHitIndex].pose;
        var arenaPose = new Pose(hitPose.position + hitPose.up * 0.008f, hitPose.rotation);

        _anchor = null;
        var anchorDescriptor = _anchorManager != null ? _anchorManager.descriptor : null;
        if (_anchorManager != null && anchorDescriptor != null
            && anchorDescriptor.supportsTrackableAttachments)
        {
            _anchor = _anchorManager.AttachAnchor(floorPlane, arenaPose);
        }

        _arena = new GameObject("Bounded Play Area").transform;
        if (_anchor != null)
        {
            _arena.SetParent(_anchor.transform, false);
            _arena.localPosition = Vector3.zero;
            _arena.localRotation = Quaternion.identity;
        }
        else
        {
            _arena.SetPositionAndRotation(arenaPose.position, arenaPose.rotation);
        }

        CreateArenaVisuals();
        if (_depthDetector != null)
            _depthDetector.SetArena(_arena);

        StartRound();
        if (_anchor == null)
            _message = "Placed without an AR anchor. Avoid moving too far from this spot. Press GO to start.";
    }

    // ---------------------------------------------------------------- rounds

    private void StartRound()
    {
        if (_roundRoot != null) Destroy(_roundRoot.gameObject);
        _coins.Clear();
        _coinRespawnAt.Clear();
        _obstaclePositions.Clear();
        _score = 0;
        _speed = 0f;
        _newBest = false;
        _raceTime = 0f;
        _boostUntil = 0f;
        _goFlashUntil = 0f;
        _hint = null;

        _roundRoot = new GameObject("Round").transform;
        _roundRoot.SetParent(_arena, false);

        var track = RaceTrack.Shared;
        _carPosition = track.GridPosition(0);
        _carHeading = track.StartHeading;
        _car = ProceduralCarFactory.CreateCar(_roundRoot).transform;
        _carRig = _car.GetComponent<ProceduralCarRig>();
        StartLapCounter(_carPosition, out _playerLaps, out _playerLastProgress);
        ApplyCarTransform();

        var rivalCar = ProceduralCarFactory.CreateCar(_roundRoot, new Color(0.10f, 0.42f, 0.92f)).transform;
        _rival = new Rival
        {
            Car = rivalCar,
            Rig = rivalCar.GetComponent<ProceduralCarRig>(),
            Position = track.GridPosition(1),
            Heading = track.StartHeading
        };
        StartLapCounter(_rival.Position, out _rival.Laps, out _rival.LastProgress);
        ApplyRivalTransform();

        _playerWins = 0;
        _rivalWins = 0;
        _knockoutUntil = 0f;
        if (_mode == GameMode.Battle)
        {
            ResetBattleRound();
            _snapChaseCamera = true;
            _state = GameState.Ready;
            _message = "Battle! First to " + BattleWinsNeeded + " knockouts wins. Hold GO to start.";
            return;
        }

        GenerateLayout();
        for (var i = 0; i < _obstaclePositions.Count; i++)
        {
            var position = _obstaclePositions[i];
            ProceduralCarFactory.CreateObstacle(_roundRoot,
                new Vector3(position.x, 0f, position.y), ObstacleColors[i % ObstacleColors.Length]);
        }

        for (var i = 0; i < _coinLayout.Count; i++)
        {
            var point = _coinLayout[i];
            var coin = ProceduralCarFactory.CreateCoin(_roundRoot, new Vector3(point.x, 0.06f, point.y));
            _coins.Add(coin.transform);
            _coinRespawnAt.Add(0f);
        }

        _snapChaseCamera = true;
        _state = GameState.Ready;
        _message = "On the grid. Hold GO to start the countdown.";
    }

    private void FinishRace()
    {
        _state = GameState.Finished;
        var place = _rival != null && _rival.Finished ? 2 : 1;
        _finishPlace = place;
        _newBest = _bestTime <= 0f || _raceTime < _bestTime;
        if (_newBest)
        {
            _bestTime = _raceTime;
            PlayerPrefs.SetFloat(BestTimeKey, _bestTime);
            PlayerPrefs.Save();
        }

        _message = (place == 1 ? "1st place! " : "2nd place. ") + "Time " + FormatTime(_raceTime)
            + (_newBest ? "  NEW BEST!" : "") + "  Tap AGAIN to race again.";
    }

    // ---------------------------------------------------------------- battle

    private void CreateArenaVisuals()
    {
        _trackVisual = RaceTrack.Shared.CreateVisual(_arena);
        _battleRing = BattleArena.Create(_arena);
        SetModeVisuals();
    }

    private void SetModeVisuals()
    {
        if (_trackVisual != null) _trackVisual.SetActive(_mode == GameMode.Race);
        if (_battleRing != null) _battleRing.gameObject.SetActive(_mode == GameMode.Battle);
        BattleArena.SetRadius(_battleRing, BattleStartRadius);
    }

    /// <summary>Puts both cars back on their start lines facing each other and restores the ring.</summary>
    private void ResetBattleRound()
    {
        _battleRadius = BattleStartRadius;
        BattleArena.SetRadius(_battleRing, _battleRadius);
        _raceTime = 0f;
        _carPosition = new Vector2(0f, -BattleStartOffset);
        _carHeading = 0f;
        _speed = 0f;
        _playerKnock = Vector2.zero;
        _playerFall = 0f;
        if (_rival != null)
        {
            _rival.Position = new Vector2(0f, BattleStartOffset);
            _rival.Heading = 180f;
            _rival.Speed = 0f;
            _rival.Knock = Vector2.zero;
            _rival.Fall = 0f;
        }

        ApplyCarTransform();
        ApplyRivalTransform();
    }

    private void UpdateBattle(float dt)
    {
        if (_state != GameState.Racing || _rival == null) return;

        if (_knockoutUntil > 0f)
        {
            // The loser slides off the edge and drops while the score sinks in.
            if (_playerFall > 0f) _playerFall += dt;
            if (_rival.Fall > 0f) _rival.Fall += dt;
            if (Time.time < _knockoutUntil) return;

            _knockoutUntil = 0f;
            if (_playerWins >= BattleWinsNeeded || _rivalWins >= BattleWinsNeeded)
            {
                FinishBattle();
                return;
            }

            ResetBattleRound();
            _state = GameState.Countdown;
            _countdown = 2f;
            _message = "Next round! YOU " + _playerWins + " : " + _rivalWins + " RIVAL";
            return;
        }

        _battleRadius = Mathf.Lerp(BattleStartRadius, BattleEndRadius, _raceTime / BattleShrinkSeconds);
        BattleArena.SetRadius(_battleRing, _battleRadius);

        var playerOut = _carPosition.magnitude > _battleRadius + 0.015f;
        var rivalOut = _rival.Position.magnitude > _battleRadius + 0.015f;
        if (!playerOut && !rivalOut) return;

        // If both slip out on the same frame, whoever went further loses.
        if (playerOut && rivalOut)
        {
            playerOut = _carPosition.magnitude >= _rival.Position.magnitude;
            rivalOut = !playerOut;
        }

        if (playerOut)
        {
            _rivalWins++;
            _playerFall = 0.0001f;
            _knockoutText = "OUT!";
        }
        else
        {
            _playerWins++;
            _rival.Fall = 0.0001f;
            _knockoutText = "KNOCKOUT!";
        }

        _knockoutUntil = Time.time + 1.6f;
        _message = "YOU " + _playerWins + " : " + _rivalWins + " RIVAL";
    }

    private void FinishBattle()
    {
        _state = GameState.Finished;
        _finishPlace = _playerWins >= BattleWinsNeeded ? 1 : 2;
        _message = (_finishPlace == 1 ? "You win the battle " : "Your rival wins ")
            + _playerWins + " : " + _rivalWins + ". Tap AGAIN for a rematch.";
    }

    /// <summary>
    /// Battle bot: rams the player toward the nearest edge, but backs toward the centre
    /// when it is close to falling off itself.
    /// </summary>
    private void BattleRivalInput(out float drive, out float steer)
    {
        var target = _carPosition;
        var rivalRadius = _rival.Position.magnitude;
        if (rivalRadius > _battleRadius - 0.11f)
            target = Vector2.zero;
        else if (_carPosition.sqrMagnitude > 0.0001f)
            target = _carPosition + _carPosition.normalized * 0.08f;

        var toTarget = target - _rival.Position;
        var desiredHeading = Mathf.Atan2(toTarget.x, toTarget.y) * Mathf.Rad2Deg;
        var error = Mathf.DeltaAngle(_rival.Heading, desiredHeading);
        steer = Mathf.Clamp(error / 25f, -1f, 1f);
        drive = Mathf.Abs(error) < 75f ? 0.9f : 0.5f;
    }

    private static string FormatTime(float seconds)
    {
        var minutes = Mathf.FloorToInt(seconds / 60f);
        return minutes + ":" + (seconds - minutes * 60f).ToString("00.0");
    }

    /// <summary>Spreads boost coins around the lap with a little sideways variety each round.</summary>
    private void GenerateLayout()
    {
        _obstaclePositions.Clear();
        _coinLayout.Clear();
        for (var i = 0; i < CoinCount; i++)
        {
            var lapFraction = (i + 0.5f) / CoinCount;
            _coinLayout.Add(RaceTrack.Shared.PointAt(lapFraction, Random.Range(-0.07f, 0.07f)));
        }
    }

    // ---------------------------------------------------------------- race logic

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
        if (_state == GameState.Racing)
        {
            var lapBefore = Mathf.FloorToInt(_playerLaps);
            AdvanceLapCounter(_carPosition, ref _playerLaps, ref _playerLastProgress);
            var lapAfter = Mathf.FloorToInt(_playerLaps);
            if (_playerLaps >= LapCount)
                FinishRace();
            else if (lapAfter > lapBefore && lapAfter > 0)
                ShowHint(lapAfter == LapCount - 1 ? "FINAL LAP!" : "LAP " + (lapAfter + 1) + "/" + LapCount);
        }

        if (_rival == null || _rival.Finished) return;
        if (_state != GameState.Racing && _state != GameState.Finished) return;

        AdvanceLapCounter(_rival.Position, ref _rival.Laps, ref _rival.LastProgress);
        if (_rival.Laps < LapCount) return;
        _rival.Finished = true;
        if (_state == GameState.Racing)
            ShowHint("Your rival has finished. Bring it home!");
    }

    private int PlayerPosition()
    {
        if (_state == GameState.Finished) return _finishPlace;
        if (_rival == null) return 1;
        return _rival.Laps > _playerLaps ? 2 : 1;
    }

    /// <summary>
    /// Simple racing line follower: aim at a point a little further around the lap, ease off in
    /// corners, and rubber-band slightly so races stay close.
    /// </summary>
    private void DriveRival(float dt)
    {
        if (_rival == null) return;

        var drive = 0f;
        var steer = 0f;
        if (_mode == GameMode.Battle)
        {
            if (_rival.Fall > 0f)
            {
                _rival.Position += _rival.Position.normalized * (0.2f * dt);
                return;
            }

            if (_state == GameState.Racing && _knockoutUntil <= 0f)
                BattleRivalInput(out drive, out steer);
            _rival.Steer = steer;
            StepCar(ref _rival.Position, ref _rival.Heading, ref _rival.Speed, drive, steer, 1f, dt, false);
            ApplyKnock(ref _rival.Position, ref _rival.Knock, dt);
            return;
        }

        var racing = _state == GameState.Racing || (_state == GameState.Finished && !_rival.Finished);
        if (racing)
        {
            var track = RaceTrack.Shared;
            var lane = Mathf.Sin(_raceTime * 0.45f) * 0.045f;
            var target = track.PointAt(track.Progress(_rival.Position) + 0.055f, lane);
            var toTarget = target - _rival.Position;
            var desiredHeading = Mathf.Atan2(toTarget.x, toTarget.y) * Mathf.Rad2Deg;
            steer = Mathf.Clamp(Mathf.DeltaAngle(_rival.Heading, desiredHeading) / 20f, -1f, 1f);

            var gap = _rival.Laps - _playerLaps;
            var skill = gap > 0.15f ? 0.88f : gap < -0.08f ? 1.10f : 0.98f;
            drive = skill * (1f - 0.35f * Mathf.Abs(steer));
        }

        _rival.Steer = steer;
        StepCar(ref _rival.Position, ref _rival.Heading, ref _rival.Speed, drive, steer, 1f, dt, true);
    }

    /// <summary>Cars bump and push each other apart instead of driving through one another.</summary>
    private void ResolveCarContact()
    {
        if (_rival == null || _car == null || _playerFall > 0f || _rival.Fall > 0f) return;

        var delta = _rival.Position - _carPosition;
        var distance = delta.magnitude;
        if (distance >= CarContactDistance || distance < 0.0001f) return;

        var normal = delta / distance;
        var push = (CarContactDistance - distance) * 0.5f;
        _carPosition -= normal * push;
        _rival.Position += normal * push;

        if (_mode == GameMode.Battle)
        {
            // Ramming transfers momentum: the car that hits harder sends the other one flying.
            var playerVelocity = HeadingToForward(_carHeading) * _speed + _playerKnock;
            var rivalVelocity = HeadingToForward(_rival.Heading) * _rival.Speed + _rival.Knock;
            var playerAttack = Mathf.Max(0f, Vector2.Dot(playerVelocity, normal));
            var rivalAttack = Mathf.Max(0f, Vector2.Dot(rivalVelocity, -normal));
            if (playerAttack + rivalAttack < 0.02f) return;

            _rival.Knock += normal * (playerAttack * 1.45f + rivalAttack * 0.2f);
            _playerKnock -= normal * (rivalAttack * 1.45f + playerAttack * 0.2f);
            _speed *= 0.55f;
            _rival.Speed *= 0.55f;
            if (playerAttack > 0.15f || rivalAttack > 0.15f)
                ShowHint(playerAttack >= rivalAttack ? "SMASH!" : "Ouch!");
            return;
        }

        RaceTrack.Shared.ConstrainToRails(ref _carPosition, ProceduralCarFactory.CarHalfWidth);
        RaceTrack.Shared.ConstrainToRails(ref _rival.Position, ProceduralCarFactory.CarHalfWidth);
        _speed *= 0.93f;
        _rival.Speed *= 0.93f;
    }

    /// <summary>Knock-back from hits slides a car and fades out quickly.</summary>
    private static void ApplyKnock(ref Vector2 position, ref Vector2 knock, float dt)
    {
        position += knock * dt;
        knock *= Mathf.Exp(-3.2f * dt);
        position = ClampInsideArena(position, 0f);
    }

    private void ApplyRivalTransform()
    {
        if (_rival == null || _rival.Car == null) return;
        var drop = _rival.Fall * _rival.Fall * 1.4f;
        _rival.Car.localPosition = new Vector3(_rival.Position.x, 0.002f - drop, _rival.Position.y);
        _rival.Car.localRotation = Quaternion.Euler(0f, _rival.Heading, 0f) * Quaternion.Euler(_rival.Fall * 70f, 0f, 0f);
    }

    // ---------------------------------------------------------------- driving

    private void DriveCar()
    {
        if (_car == null) return;

        var dt = Time.deltaTime;
        if (_playerFall > 0f)
        {
            _carPosition += _carPosition.normalized * (0.2f * dt);
            return;
        }

        var battle = _mode == GameMode.Battle;
        var position = _carPosition;
        var heading = _carHeading;
        var boost = Time.time < _boostUntil ? BoostMultiplier : 1f;
        StepCar(ref position, ref heading, ref _speed, _driveInput, _steerInput, boost, dt, !battle);
        if (battle)
            ApplyKnock(ref position, ref _playerKnock, dt);
        if (Mathf.Abs(_speed) < 0.0001f && _playerKnock.sqrMagnitude < 0.0001f) return;

        // In AR, real walls and objects seen by the depth sensor can still stop the car.
        var travelDirection = HeadingToForward(heading) * Mathf.Sign(_speed);
        if (IsBlocked(position, heading, travelDirection))
        {
            _speed = 0f;
            ShowHint("Bump! Back up and steer around it.");
            return;
        }

        _carPosition = position;
        _carHeading = heading;
    }

    /// <summary>Shared toy-car physics for the player and the rival.</summary>
    private static void StepCar(ref Vector2 position, ref float heading, ref float speed,
        float drive, float steer, float speedMultiplier, float dt, bool onTrack)
    {
        // Leaving the asphalt onto the grass costs speed (the battle ring has no grass).
        var onRoad = !onTrack
            || Mathf.Abs(RaceTrack.Shared.LateralOffset(position)) <= RaceTrack.RoadHalfWidth + 0.016f;
        var targetSpeed = drive * MaxSpeed * speedMultiplier * (onRoad ? 1f : 0.58f);
        float rate;
        if (Mathf.Abs(targetSpeed) < 0.001f)
            rate = RollingFriction;
        else if (Mathf.Abs(speed) > 0.01f && Mathf.Sign(targetSpeed) != Mathf.Sign(speed))
            rate = Braking;
        else
            rate = Acceleration;
        speed = Mathf.MoveTowards(speed, targetSpeed, rate * dt);

        // Only settle to a stop when coasting; a tiny speed under throttle is the car pulling away.
        if (Mathf.Abs(speed) < 0.0005f && Mathf.Abs(targetSpeed) < 0.001f)
        {
            speed = 0f;
            return;
        }

        // Steering authority grows with speed and flips when reversing, like a real car.
        var steerAuthority = Mathf.Clamp01(Mathf.Abs(speed) / FullSteerSpeed) * Mathf.Sign(speed);
        heading += steer * TurnSpeed * steerAuthority * dt;
        position += HeadingToForward(heading) * (speed * dt);
        if (onTrack && RaceTrack.Shared.ConstrainToRails(ref position, ProceduralCarFactory.CarHalfWidth))
        {
            // Scrape along the guard rail, but keep enough speed to steer back onto the road.
            var scrapeSpeed = Mathf.Sign(speed) * Mathf.Min(Mathf.Abs(speed), MaxSpeed * 0.4f);
            speed = Mathf.MoveTowards(speed, scrapeSpeed, 2.5f * dt);
        }

        position = ClampInsideArena(position, heading);
    }

    private void ApplyCarTransform()
    {
        // The wheels now touch y = 0, so the car sits on the arena instead of hovering.
        var drop = _playerFall * _playerFall * 1.4f;
        _car.localPosition = new Vector3(_carPosition.x, 0.002f - drop, _carPosition.y);
        _car.localRotation = Quaternion.Euler(0f, _carHeading, 0f) * Quaternion.Euler(_playerFall * 70f, 0f, 0f);
    }

    private static Vector2 HeadingToForward(float headingDegrees)
    {
        var radians = headingDegrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
    }

    private static Vector2 HeadingToRight(float headingDegrees)
    {
        var radians = headingDegrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(radians), -Mathf.Sin(radians));
    }

    /// <summary>Keeps the whole rotated car body inside the boundary lines.</summary>
    private static Vector2 ClampInsideArena(Vector2 position, float heading)
    {
        var forward = HeadingToForward(heading);
        var right = HeadingToRight(heading);
        var extentX = ProceduralCarFactory.CarHalfWidth * Mathf.Abs(right.x)
            + ProceduralCarFactory.CarHalfLength * Mathf.Abs(forward.x);
        var extentZ = ProceduralCarFactory.CarHalfWidth * Mathf.Abs(right.y)
            + ProceduralCarFactory.CarHalfLength * Mathf.Abs(forward.y);
        const float limit = HalfArena - 0.012f;
        position.x = Mathf.Clamp(position.x, -limit + extentX, limit - extentX);
        position.y = Mathf.Clamp(position.y, -limit + extentZ, limit - extentZ);
        return position;
    }

    private bool IsBlocked(Vector2 position, float heading, Vector2 travelDirection)
    {
        if (OverlapsVirtualObstacle(position, heading))
            return true;

        var localPosition = new Vector3(position.x, 0f, position.y);
        var localTravel = new Vector3(travelDirection.x, 0f, travelDirection.y);
        if (IsBlockedByDetectedVerticalSurface(localPosition, localTravel))
            return true;

        return _depthDetector != null
            && _depthDetector.IsPathBlocked(_arena.TransformPoint(localPosition),
                _arena.TransformDirection(localTravel), ProceduralCarFactory.CarHalfLength + 0.03f, ProceduralCarFactory.CarHalfWidth + 0.01f);
    }

    /// <summary>Separating-axis test between the car's oriented box and each square barrier.</summary>
    private bool OverlapsVirtualObstacle(Vector2 carCenter, float heading)
    {
        var forward = HeadingToForward(heading);
        var right = HeadingToRight(heading);
        const float halfWidth = ProceduralCarFactory.CarHalfWidth;
        const float halfLength = ProceduralCarFactory.CarHalfLength;
        const float box = ProceduralCarFactory.ObstacleHalfSize;

        for (var i = 0; i < _obstaclePositions.Count; i++)
        {
            var delta = _obstaclePositions[i] - carCenter;
            if (delta.sqrMagnitude > 0.16f) continue; // farther than any possible contact

            if (Separated(delta, Vector2.right, halfWidth, halfLength, right, forward, box)) continue;
            if (Separated(delta, Vector2.up, halfWidth, halfLength, right, forward, box)) continue;
            if (Separated(delta, right, halfWidth, halfLength, right, forward, box)) continue;
            if (Separated(delta, forward, halfWidth, halfLength, right, forward, box)) continue;
            return true;
        }

        return false;
    }

    private static bool Separated(Vector2 delta, Vector2 axis, float halfWidth, float halfLength,
        Vector2 right, Vector2 forward, float boxHalf)
    {
        var carRadius = halfWidth * Mathf.Abs(Vector2.Dot(right, axis))
            + halfLength * Mathf.Abs(Vector2.Dot(forward, axis));
        var boxRadius = boxHalf * (Mathf.Abs(axis.x) + Mathf.Abs(axis.y));
        return Mathf.Abs(Vector2.Dot(delta, axis)) > carRadius + boxRadius;
    }

    private bool IsBlockedByDetectedVerticalSurface(Vector3 candidateCarPosition, Vector3 localTravelDirection)
    {
        if (_planeManager == null || _arena == null) return false;

        // Probe slightly ahead of the car at bumper height. AR planes are geometric
        // observations, so this only guards against flat surfaces the device has tracked.
        var probeLocal = candidateCarPosition + localTravelDirection * (ProceduralCarFactory.CarHalfLength + 0.012f) + Vector3.up * 0.05f;
        var probeWorld = _arena.TransformPoint(probeLocal);

        foreach (var plane in _planeManager.trackables)
        {
            if (plane == null || plane.alignment != PlaneAlignment.Vertical)
                continue;

            var planeLocal = plane.transform.InverseTransformPoint(probeWorld);
            // ARPlane boundaries lie in the plane's local XY plane; local Z is
            // distance from the detected vertical surface.
            if (Mathf.Abs(planeLocal.z) > 0.12f)
                continue;

            if (IsPointInsidePlaneBoundary(plane, new Vector2(planeLocal.x, planeLocal.y)))
                return true;
        }

        return false;
    }

    private static bool IsPointInsidePlaneBoundary(ARPlane plane, Vector2 point)
    {
        var boundary = plane.boundary;
        if (!boundary.IsCreated || boundary.Length < 3)
            return false;

        var inside = false;
        var previous = boundary.Length - 1;
        for (var current = 0; current < boundary.Length; current++)
        {
            var a = boundary[current];
            var b = boundary[previous];
            var crosses = (a.y > point.y) != (b.y > point.y)
                && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x;
            if (crosses) inside = !inside;
            previous = current;
        }

        return inside;
    }

    private void ShowHint(string text)
    {
        _hint = text;
        _hintUntil = Time.time + 1.4f;
    }

    // ---------------------------------------------------------------- coins

    private void AnimateAndCollectCoins()
    {
        var forward = HeadingToForward(_carHeading);
        var right = HeadingToRight(_carHeading);

        for (var i = 0; i < _coins.Count; i++)
        {
            var coin = _coins[i];
            if (coin == null) continue;
            if (!coin.gameObject.activeSelf)
            {
                if (Time.time >= _coinRespawnAt[i]) coin.gameObject.SetActive(true);
                continue;
            }

            coin.Rotate(0f, 100f * Time.deltaTime, 0f, Space.Self);
            var basePosition = coin.localPosition;
            basePosition.y = 0.06f + Mathf.Sin(Time.time * 3f + i) * 0.01f;
            coin.localPosition = basePosition;

            if (_state != GameState.Racing) continue;

            // Collect when any part of the car body touches the coin, not just its centre.
            var delta = new Vector2(basePosition.x, basePosition.z) - _carPosition;
            var alongRight = Mathf.Clamp(Vector2.Dot(delta, right),
                -ProceduralCarFactory.CarHalfWidth, ProceduralCarFactory.CarHalfWidth);
            var alongForward = Mathf.Clamp(Vector2.Dot(delta, forward),
                -ProceduralCarFactory.CarHalfLength, ProceduralCarFactory.CarHalfLength);
            var closest = right * alongRight + forward * alongForward;
            if ((delta - closest).magnitude > ProceduralCarFactory.CoinRadius + 0.02f)
                continue;

            // Coins are boost pads: they vanish for a few seconds and then come back.
            coin.gameObject.SetActive(false);
            _coinRespawnAt[i] = Time.time + CoinRespawnSeconds;
            _score++;
            _boostUntil = Time.time + BoostSeconds;
            ShowHint("BOOST!");
        }
    }

    // ---------------------------------------------------------------- arena visuals

    /// <summary>
    /// Hides the teal plane tint while playing so the arena stays readable; planes keep
    /// tracking because vertical planes still feed obstacle detection.
    /// </summary>
    private void SetPlaneVisualsHidden(bool hidden)
    {
        if (_planeManager == null) return;
        foreach (var plane in _planeManager.trackables)
        {
            if (plane == null) continue;
            if (plane.TryGetComponent<MeshRenderer>(out var meshRenderer))
                meshRenderer.forceRenderingOff = hidden;
            if (plane.TryGetComponent<LineRenderer>(out var lineRenderer))
                lineRenderer.forceRenderingOff = hidden;
        }
    }

    private void ResetGame()
    {
        if (_arena != null) Destroy(_arena.gameObject);
        if (_anchor != null) Destroy(_anchor.gameObject);
        _arena = null;
        _roundRoot = null;
        _anchor = null;
        _car = null;
        _rival = null;
        _trackVisual = null;
        _battleRing = null;
        _knockoutUntil = 0f;
        _playerFall = 0f;
        _playerKnock = Vector2.zero;
        _depthDetector?.SetArena(null);
        _coins.Clear();
        _coinRespawnAt.Clear();
        _obstaclePositions.Clear();
        _score = 0;
        _speed = 0f;
        _steerInput = 0f;
        _driveInput = 0f;
        _hint = null;
        _state = GameState.Placing;
        _message = "Move your phone slowly to scan the floor, then tap the floor.";
    }

    // ---------------------------------------------------------------- input

    /// <summary>
    /// Reads every active touch independently so steering and throttle can be held
    /// with two fingers at once (IMGUI buttons only track a single pointer).
    /// </summary>
    private ControlAction ReadControls()
    {
        _leftHeld = _rightHeld = _goHeld = _backHeld = false;
        var action = ControlAction.None;
        var roundOver = _state == GameState.Finished;

        for (var i = 0; i < Input.touchCount; i++)
        {
            var touch = Input.GetTouch(i);
            if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                continue;

            var point = ToGuiPoint(touch.position);
            if (touch.phase == TouchPhase.Began)
            {
                if (_resetRect.Contains(point)) action = ControlAction.Reset;
                else if (roundOver && Expand(_againRect).Contains(point)) action = ControlAction.Again;
                else if (CanChangeMode() && _modeRect.Contains(point)) action = ControlAction.Mode;
            }

            if (!roundOver) ApplyHeldPoint(point);
        }

#if UNITY_EDITOR
        var mousePoint = ToGuiPoint(Input.mousePosition);
        if (Input.GetMouseButtonDown(0))
        {
            if (_resetRect.Contains(mousePoint)) action = ControlAction.Reset;
            else if (roundOver && Expand(_againRect).Contains(mousePoint)) action = ControlAction.Again;
            else if (CanChangeMode() && _modeRect.Contains(mousePoint)) action = ControlAction.Mode;
        }

        if (!roundOver)
        {
            if (Input.GetMouseButton(0)) ApplyHeldPoint(mousePoint);
            var keySteer = Input.GetAxisRaw("Horizontal");
            var keyDrive = Input.GetAxisRaw("Vertical");
            if (keySteer < -0.1f) _leftHeld = true;
            if (keySteer > 0.1f) _rightHeld = true;
            if (keyDrive > 0.1f) _goHeld = true;
            if (keyDrive < -0.1f) _backHeld = true;
        }
        else if (Input.GetKeyDown(KeyCode.Space))
        {
            action = ControlAction.Again;
        }
#endif

        _steerInput = (_rightHeld ? 1f : 0f) - (_leftHeld ? 1f : 0f);
        _driveInput = _goHeld == _backHeld ? 0f : _goHeld ? 1f : ReverseInput;
        return action;
    }

    // The mode can be switched on the starting grid or after a finish, never mid-race.
    private bool CanChangeMode() => _state == GameState.Ready || _state == GameState.Finished;

    private void ApplyHeldPoint(Vector2 guiPoint)
    {
        if (Expand(_leftRect).Contains(guiPoint)) _leftHeld = true;
        else if (Expand(_rightRect).Contains(guiPoint)) _rightHeld = true;
        else if (Expand(_goRect).Contains(guiPoint)) _goHeld = true;
        else if (Expand(_backRect).Contains(guiPoint)) _backHeld = true;
    }

    // Slightly larger hit areas than the drawn buttons make thumb control forgiving.
    private Rect Expand(Rect rect)
    {
        var pad = 6f * _uiScale;
        return new Rect(rect.x - pad, rect.y - pad, rect.width + pad * 2f, rect.height + pad * 2f);
    }

    private static Vector2 ToGuiPoint(Vector2 screenPoint)
    {
        // Input uses a bottom-left origin; IMGUI rects use top-left.
        return new Vector2(screenPoint.x, Screen.height - screenPoint.y);
    }

    private void UpdateLayout()
    {
        // Density-independent scale (1 unit ≈ 1 dp) so buttons keep a usable physical size
        // on high-resolution phones; fall back to width-based scaling if DPI is unknown.
        _uiScale = Screen.dpi > 0f
            ? Mathf.Clamp(Screen.dpi / 160f, 0.8f, 4f)
            : Mathf.Clamp(Screen.width / 420f, 0.8f, 3f);

        var s = _uiScale;
        var margin = 16f * s;
        _hudRect = new Rect(margin, margin, Screen.width - margin * 2f, 120f * s);

        var buttonWidth = 82f * s;
        var buttonHeight = 70f * s;
        var gap = 8f * s;
        var y = Screen.height - buttonHeight - 22f * s;
        _leftRect = new Rect(margin, y, buttonWidth, buttonHeight);
        _rightRect = new Rect(margin + buttonWidth + gap, y, buttonWidth, buttonHeight);
        _goRect = new Rect(Screen.width - buttonWidth - margin, y, buttonWidth, buttonHeight);
        _backRect = new Rect(_goRect.x - buttonWidth - gap, y, buttonWidth, buttonHeight);
        _resetRect = new Rect(_goRect.x, y - 42f * s - 10f * s, buttonWidth, 42f * s);
        _modeRect = new Rect(margin, y - 42f * s - 10f * s, buttonWidth * 2f + gap, 42f * s);

        var noArWidth = 220f * s;
        var noArHeight = 54f * s;
        _noArRect = new Rect((Screen.width - noArWidth) * 0.5f, Screen.height - noArHeight - 28f * s,
            noArWidth, noArHeight);

        var againWidth = 190f * s;
        var againHeight = 70f * s;
        _againRect = new Rect((Screen.width - againWidth) * 0.5f, y - againHeight - 70f * s, againWidth, againHeight);
    }

    // ---------------------------------------------------------------- HUD

    private void OnGUI()
    {
        // Only draw here; all input is read in Update via Input.touches (multi-touch).
        if (Event.current.type != EventType.Repaint) return;

        EnsureStyles();
        var scale = _uiScale;
        var margin = 16f * scale;
        var textX = margin * 1.8f;
        var textWidth = Screen.width - margin * 3f;
        var previousGuiColor = GUI.color;
        var previousContentColor = GUI.contentColor;
        GUI.color = new Color(0.025f, 0.075f, 0.11f, 0.88f);
        GUI.Box(_hudRect, "");
        GUI.color = previousGuiColor;
        GUI.contentColor = Color.white;

        var title = "AR RACER";
        if (_arena != null && _mode == GameMode.Battle)
        {
            title = "BATTLE  •  YOU " + _playerWins + " : " + _rivalWins + " RIVAL  •  RING "
                + Mathf.RoundToInt(_battleRadius * 200f) + " cm";
        }
        else
        {
            if (_arena != null)
            {
                var lap = Mathf.Clamp(Mathf.FloorToInt(_playerLaps) + 1, 1, LapCount);
                title = "LAP " + lap + "/" + LapCount + "  •  P" + PlayerPosition() + "/2  •  " + FormatTime(_raceTime);
            }

            title += "  •  BEST " + (_bestTime > 0f ? FormatTime(_bestTime) : "--");
        }
        GUI.Label(new Rect(textX, margin * 1.35f, textWidth, 24f * scale), title, _titleStyle);

        var message = _arena == null ? GetPlacingMessage()
            : _hint != null && Time.time < _hintUntil && _state == GameState.Racing ? _hint : _message;
        GUI.Label(new Rect(textX, margin * 1.35f + 27f * scale, textWidth, 32f * scale), message, _messageStyle);

        var planeCount = _planeManager != null ? _planeManager.trackables.count : 0;
        var depthState = _depthDetector == null ? "off"
            : !_depthDetector.DepthSupported ? "not supported"
            : _depthDetector.HasDepthFrame ? "ready" : "waiting";
        var anchorState = _anchor == null ? "none" : _anchor.trackingState.ToString();
        var trackingStatus = "AR: " + ARSession.state + " (" + ARSession.notTrackingReason
            + ")  •  camera frames: " + _cameraFrameCount;
        var featureStatus = "Planes: " + planeCount + "  •  anchor: " + anchorState
            + "  •  depth: " + depthState;
        if (_virtualMode)
        {
            trackingStatus = "Mode: virtual table (no AR)";
            featureStatus = "RESET starts a new layout";
        }

        GUI.Label(new Rect(textX, margin * 1.35f + 64f * scale, textWidth, 18f * scale), trackingStatus, _statusStyle);
        GUI.Label(new Rect(textX, margin * 1.35f + 82f * scale, textWidth, 18f * scale), featureStatus, _statusStyle);

        if (_arena == null)
        {
            DrawControl(_noArRect, "PLAY WITHOUT AR", new Color(0.12f, 0.18f, 0.23f, 0.9f), false, _smallButtonStyle);
            GUI.color = previousGuiColor;
            GUI.contentColor = previousContentColor;
            return;
        }

        var battle = _mode == GameMode.Battle;
        var bigText = _state == GameState.Countdown ? Mathf.CeilToInt(_countdown).ToString()
            : _state == GameState.Racing && _knockoutUntil > 0f ? _knockoutText
            : _state == GameState.Racing && Time.time < _goFlashUntil ? (battle ? "PUSH!" : "GO!")
            : _state == GameState.Finished
                ? (battle ? (_finishPlace == 1 ? "YOU WIN" : "YOU LOSE") : (_finishPlace == 1 ? "1ST" : "2ND"))
            : null;
        if (bigText != null)
        {
            GUI.color = Color.white;
            var bigRect = new Rect(0f, Screen.height * 0.30f, Screen.width, 110f * scale);
            GUI.contentColor = new Color(0f, 0f, 0f, 0.55f);
            GUI.Label(new Rect(bigRect.x + 3f * scale, bigRect.y + 3f * scale, bigRect.width, bigRect.height), bigText, _bigStyle);
            GUI.contentColor = _state == GameState.Finished && _finishPlace == 1
                ? new Color(1f, 0.82f, 0.18f) : Color.white;
            GUI.Label(bigRect, bigText, _bigStyle);
            GUI.contentColor = Color.white;
        }

        if (_state == GameState.Finished)
        {
            var againColor = _finishPlace == 1
                ? new Color(0.95f, 0.66f, 0.10f, 0.96f)
                : new Color(0.08f, 0.67f, 0.34f, 0.96f);
            DrawControl(_againRect, "AGAIN", againColor, false, _buttonStyle);
        }
        else
        {
            var steerColor = new Color(0.04f, 0.62f, 0.72f, 0.92f);
            DrawControl(_leftRect, "◀", steerColor, _leftHeld, _buttonStyle);
            DrawControl(_rightRect, "▶", steerColor, _rightHeld, _buttonStyle);
            DrawControl(_goRect, "GO", new Color(0.08f, 0.67f, 0.34f, 0.96f), _goHeld, _buttonStyle);
            DrawControl(_backRect, "BACK", new Color(0.86f, 0.48f, 0.12f, 0.94f), _backHeld, _buttonStyle);
        }

        DrawControl(_resetRect, "RESET", new Color(0.12f, 0.18f, 0.23f, 0.88f), false, _smallButtonStyle);
        if (CanChangeMode())
        {
            DrawControl(_modeRect, battle ? "MODE: BATTLE  ⇄" : "MODE: RACE  ⇄",
                new Color(0.42f, 0.20f, 0.70f, 0.92f), false, _smallButtonStyle);
        }

        GUI.color = previousGuiColor;
        GUI.contentColor = previousContentColor;
    }

    private static void DrawControl(Rect rect, string label, Color color, bool held, GUIStyle style)
    {
        // Brighten a held button so the player sees each finger register.
        GUI.color = held ? Color.Lerp(color, Color.white, 0.45f) : color;
        GUI.Box(rect, label, style);
    }

    private void EnsureStyles()
    {
        if (Mathf.Approximately(_cachedStyleScale, _uiScale) && _titleStyle != null)
            return;

        _cachedStyleScale = _uiScale;
        _titleStyle = MakeLabelStyle(18f * _uiScale, true);
        _messageStyle = MakeLabelStyle(12f * _uiScale, false);
        _statusStyle = MakeLabelStyle(9f * _uiScale, false);
        _buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = Mathf.RoundToInt(22f * _uiScale),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };
        _smallButtonStyle = new GUIStyle(_buttonStyle) { fontSize = Mathf.RoundToInt(14f * _uiScale) };
        _bigStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(76f * _uiScale),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };
    }

    private static GUIStyle MakeLabelStyle(float fontSize, bool bold)
    {
        return new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(fontSize),
            fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
            normal = { textColor = Color.white },
            wordWrap = true
        };
    }
}
