using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Playable loop: scan and tap the floor (or play in the virtual living room), then drive an RC
/// buggy. FREE DRIVE lets it roam the room, bump into real obstacles seen by the depth sensor
/// and into cones, tyres and walls the player places; RACE is five laps of an RC track against
/// a rival; BATTLE pushes the rival out of a shrinking ring.
/// Real obstacle sensing is approximate and device-dependent.
/// </summary>
public sealed class ARDemoController : MonoBehaviour
{
    private enum GameState { Placing, Ready, Countdown, Racing, Finished }
    // Values are sent over the network: keep Race = 0 and Battle = 1.
    private enum GameMode { Race, Battle, Free }
    private enum ControlAction
    {
        None, Reset, Again, Mode, Players, Host, Join,
        Camera, Spot, Scan, Build, PlaceCone, PlaceTyres, PlaceWall, ClearObstacles
    }

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
        public float BoostUntil;
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

    // Free drive: the car is quicker than on the track and roams the room.
    private const float FreeSpeedMultiplier = 1.5f;
    private const float FreeARHalfExtent = ARDepthObstacleDetector.HalfExtent - 0.15f;
    private const int FreeCoinCount = 8;
    private const float RcScale = 18f;

    // Seconds without a camera frame before the HUD suggests the virtual room.
    private const float ARStartTimeout = 12f;

    private ARSession _session;
    private Camera _arCamera;
    private bool _virtualMode;
    private Transform _virtualTable;
    private VirtualRoom.Info _room;
    private PhoneViewCamera _phoneView;
    private bool _chaseCamera;
    private Light _keyLight;
    private Vector3 _chaseVelocity;
    private bool _snapChaseCamera;
    private Rect _bounds = Rect.MinMaxRect(-HalfArena, -HalfArena, HalfArena, HalfArena);

    // Obstacles the player places in free drive (they stay until cleared or the floor is re-placed).
    private readonly List<WorldObstacle> _placed = new List<WorldObstacle>();
    private readonly List<GameObject> _placedObjects = new List<GameObject>();
    private Transform _placedRoot;
    private bool _buildMode;
    private ObstacleKind _buildKind = ObstacleKind.Cone;
    private Rect _cameraRect;
    private Rect _spotRect;
    private Rect _scanRect;
    private Rect _buildRect;
    private Rect _coneRect;
    private Rect _tyresRect;
    private Rect _wallRect;
    private Rect _clearRect;
    private float _pinchDistance;
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
    private readonly List<Vector2> _coinLayout = new List<Vector2>();
    private Rival _rival;

    // Push-out battle: a shrinking ring, first to three knockouts wins the match.
    private const int BattleWinsNeeded = 3;
    private const float BattleStartRadius = 0.62f;
    private const float BattleEndRadius = 0.30f;
    private const float BattleShrinkSeconds = 40f;
    private const float BattleStartOffset = 0.29f;
    private GameMode _mode = GameMode.Free;
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

    // Two players on the local network (see LanSession).
    private LanSession _net;
    private bool _netWasConnected;
    private bool _snapNetworkCars;
    private bool _mpPanelOpen;
    private Rect _mpRect;
    private Rect _hostRect;
    private Rect _joinRect;

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

    // Title logo on the start screen: fully visible for a few seconds, then fades so the
    // camera view is clear while the player looks for a floor.
    private const float LogoHoldSeconds = 3f;
    private const float LogoFadeSeconds = 1f;
    private Texture2D _logo;

    private void Awake()
    {
        _logo = Resources.Load<Texture2D>("ARCoinDriveLogo");
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
        _keyLight = EnsureSceneLight();
        ConfigureShadows(4f);
        SetAmbient(new Color(0.62f, 0.63f, 0.66f), new Color(0.48f, 0.46f, 0.44f), new Color(0.26f, 0.24f, 0.22f));

        // Match the virtual light to the real room on AR devices.
        if (_cameraManager != null && !_cameraManager.TryGetComponent<ARLightEstimation>(out _))
            _cameraManager.gameObject.AddComponent<ARLightEstimation>().Configure(_keyLight);
        if (_arCamera != null) _arCamera.nearClipPlane = 0.03f;
    }

    /// <summary>Real-time soft shadows sized for a car a few metres from the camera.</summary>
    private void ConfigureShadows(float distance)
    {
        QualitySettings.shadows = ShadowQuality.All;
        QualitySettings.shadowResolution = ShadowResolution.High;
        QualitySettings.shadowCascades = 1;
        QualitySettings.shadowDistance = distance;
        QualitySettings.shadowProjection = ShadowProjection.StableFit;
        if (_keyLight == null) return;
        _keyLight.shadows = LightShadows.Soft;
        _keyLight.shadowStrength = 0.85f;
        // Default biases are tuned for metre-sized objects and detach a 20 cm car's shadow.
        _keyLight.shadowBias = 0.01f;
        _keyLight.shadowNormalBias = 0.08f;
        _keyLight.shadowNearPlane = 0.1f;
    }

    /// <summary>Sky / horizon / ground ambient written straight into the ambient probe.</summary>
    private static void SetAmbient(Color sky, Color equator, Color ground)
    {
        var sh = new UnityEngine.Rendering.SphericalHarmonicsL2();
        sh.AddAmbientLight(equator);
        sh.AddDirectionalLight(Vector3.up, (sky - equator) * 1.3f, 1f);
        sh.AddDirectionalLight(Vector3.down, (ground - equator) * 1.3f, 1f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
        RenderSettings.ambientProbe = sh;
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
            EnterVirtualMode("This device does not support AR, so you are playing in a virtual room.");
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
            _arCamera.backgroundColor = new Color(0.80f, 0.80f, 0.78f);
            _arCamera.farClipPlane = 20f;
        }

        if (_session != null) _session.enabled = false;

        // The living room: sunlight through the window, warm indoor ambient, longer shadows.
        _virtualTable = new GameObject("Virtual Room").transform;
        _room = VirtualRoom.Build(_virtualTable);
        _phoneView = new PhoneViewCamera(_room.CameraSpots);
        if (_keyLight != null)
        {
            _keyLight.transform.rotation = Quaternion.LookRotation(_room.SunDirection);
            _keyLight.color = new Color(1f, 0.95f, 0.86f);
            _keyLight.intensity = 0.88f;
        }

        ConfigureShadows(4.5f);
        SetAmbient(new Color(0.50f, 0.52f, 0.56f), new Color(0.40f, 0.37f, 0.34f), new Color(0.24f, 0.20f, 0.16f));

        _arena = new GameObject("Bounded Play Area").transform;
        _arena.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        CreateArenaVisuals();
        StartRound();
        _message = reason + " " + _message;
    }

    /// <summary>
    /// Virtual room camera: the phone films the car from where the player stands (or a chase
    /// camera if chosen). In AR the phone itself is the camera.
    /// </summary>
    private void LateUpdate()
    {
        if (!_virtualMode || _arCamera == null || _car == null) return;

        if (!_chaseCamera && _phoneView != null)
        {
            _phoneView.Update(_arCamera.transform, _arCamera, _car.position + Vector3.up * 0.04f, Time.deltaTime);
            return;
        }

        if (Mathf.Abs(_arCamera.fieldOfView - 60f) > 0.01f) _arCamera.fieldOfView = 60f;
        var forward2 = HeadingToForward(_carHeading);
        var forward = _arena.TransformDirection(new Vector3(forward2.x, 0f, forward2.y));
        var up = _arena.up;
        var carPosition = _car.position;
        var desiredPosition = carPosition - forward * 0.56f + up * 0.40f;
        if (_room != null)
        {
            // Keep the chase camera inside the room walls.
            desiredPosition.x = Mathf.Clamp(desiredPosition.x, -VirtualRoom.HalfWidth + 0.12f, VirtualRoom.HalfWidth - 0.12f);
            desiredPosition.z = Mathf.Clamp(desiredPosition.z, -VirtualRoom.HalfDepth + 0.12f, VirtualRoom.HalfDepth - 0.12f);
        }

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
    private static Light EnsureSceneLight()
    {
        foreach (var existing in FindObjectsByType<Light>())
            if (existing.type == LightType.Directional) return existing;

        var lightObject = new GameObject("AR Key Light");
        lightObject.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
        var keyLight = lightObject.AddComponent<Light>();
        keyLight.type = LightType.Directional;
        keyLight.intensity = 1.05f;
        return keyLight;
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

        UpdateNetwork();
        var action = ReadControls();
        if (action == ControlAction.Players)
        {
            if (_net != null) StopNetwork("Left the two-player game. Racing the computer again.");
            else _mpPanelOpen = !_mpPanelOpen;
            return;
        }

        if (action == ControlAction.Host || action == ControlAction.Join)
        {
            StartNetwork(action == ControlAction.Host);
            return;
        }

        if (HandleViewAndBuildAction(action)) return;

        if (IsClient)
        {
            // The host runs the game; this phone only sends controls and shows the result.
            if (action == ControlAction.Reset)
            {
                StopNetwork("Left the two-player game.");
                if (!_virtualMode) ResetGame();
                return;
            }

            UpdateClient(Time.deltaTime);
            return;
        }

        if (action == ControlAction.Reset)
        {
            // The same touch must not also place a new arena this frame.
            // The virtual table has nothing to re-scan, so RESET just starts a new round there.
            if (_virtualMode || IsHostOnline) StartRound();
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
            // FREE -> RACE -> BATTLE; free drive is single-player only.
            _mode = _mode == GameMode.Free ? GameMode.Race
                : _mode == GameMode.Race ? GameMode.Battle
                : _net != null ? GameMode.Race : GameMode.Free;
            _buildMode = false;
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

        if (_state != GameState.Racing || _knockoutUntil > 0f || _buildMode)
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
        else if (_mode == GameMode.Battle)
        {
            UpdateBattle(dt);
        }
        else
        {
            AnimateAndCollectCoins();
        }

        SendStateToClient();
    }

    // ---------------------------------------------------------------- camera, scan and build

    /// <summary>View buttons (any player) and obstacle building (free drive). True when handled.</summary>
    private bool HandleViewAndBuildAction(ControlAction action)
    {
        switch (action)
        {
            case ControlAction.Camera:
                _chaseCamera = !_chaseCamera;
                _snapChaseCamera = true;
                _phoneView?.Recenter();
                ShowHint(_chaseCamera ? "Chase camera" : "Phone camera: turn your phone to look around");
                return true;
            case ControlAction.Spot:
                _phoneView?.NextSpot();
                ShowHint(_phoneView != null ? "Standing at " + _phoneView.SpotName : null);
                return true;
            case ControlAction.Scan:
                if (_depthDetector != null) _depthDetector.ShowOverlay = !_depthDetector.ShowOverlay;
                return true;
            case ControlAction.Build:
                _buildMode = !_buildMode;
                _message = _buildMode ? BuildMessage() : FreeDriveMessage();
                return true;
            case ControlAction.PlaceCone:
                _buildKind = ObstacleKind.Cone;
                _message = BuildMessage();
                return true;
            case ControlAction.PlaceTyres:
                _buildKind = ObstacleKind.Tyres;
                _message = BuildMessage();
                return true;
            case ControlAction.PlaceWall:
                _buildKind = ObstacleKind.Wall;
                _message = BuildMessage();
                return true;
            case ControlAction.ClearObstacles:
                ClearPlacedObstacles();
                _message = "All placed obstacles removed. " + BuildMessage();
                return true;
            default:
                return false;
        }
    }

    private string BuildMessage()
    {
        var name = _buildKind == ObstacleKind.Cone ? "a cone" : _buildKind == ObstacleKind.Tyres ? "a tyre stack" : "a wall";
        return "BUILD: tap the floor to place " + name + ", tap an obstacle to remove it. DONE to drive.";
    }

    private string FreeDriveMessage() => _virtualMode
        ? "Free drive. Turn your phone to follow the car; hit the furniture, collect coins."
        : "Free drive. Real objects stop the car (orange = what depth sees). BUILD adds obstacles.";

    /// <summary>Screen tap in build mode: remove the obstacle under the finger or place a new one.</summary>
    private void HandleBuildTap(Vector2 screenPosition)
    {
        if (_arena == null || _arCamera == null) return;
        var ray = _arCamera.ScreenPointToRay(screenPosition);
        var plane = new Plane(_arena.up, _arena.position);
        if (!plane.Raycast(ray, out var distance) || distance > 8f)
        {
            ShowHint("Point at the floor");
            return;
        }

        var local = _arena.InverseTransformPoint(ray.GetPoint(distance));
        var point = new Vector2(local.x, local.z);

        for (var i = _placed.Count - 1; i >= 0; i--)
        {
            if (!_placed[i].Contains(point, 0.03f)) continue;
            if (_placedObjects[i] != null) Destroy(_placedObjects[i]);
            _placed.RemoveAt(i);
            _placedObjects.RemoveAt(i);
            ShowHint("Removed");
            return;
        }

        if (!_bounds.Contains(point))
        {
            ShowHint("Too far away");
            return;
        }

        // Walls face the phone: their long side runs across the view.
        var cameraRight = _arena.InverseTransformDirection(_arCamera.transform.right);
        var angle = _buildKind == ObstacleKind.Wall ? Mathf.Atan2(-cameraRight.z, cameraRight.x) * Mathf.Rad2Deg : 0f;
        var footprint = ProceduralCarFactory.Footprint(_buildKind, point, angle);
        if (CarCollision.Overlap(_carPosition, _carHeading, ProceduralCarFactory.CarHalfWidth,
                ProceduralCarFactory.CarHalfLength, footprint, out _, out _))
        {
            ShowHint("The car is in the way");
            return;
        }

        if (_placedRoot == null)
        {
            _placedRoot = new GameObject("Placed Obstacles").transform;
            _placedRoot.SetParent(_arena, false);
        }

        _placed.Add(footprint);
        _placedObjects.Add(ProceduralCarFactory.CreateObstacle(_placedRoot, _buildKind, point, angle));
        ShowHint(_placed.Count + " placed");
    }

    private void ClearPlacedObstacles()
    {
        foreach (var placed in _placedObjects)
            if (placed != null) Destroy(placed);
        _placed.Clear();
        _placedObjects.Clear();
    }

    /// <summary>
    /// Everything solid around the car: placed obstacles (free drive), the room's furniture
    /// (virtual room), and in AR the depth map and tracked vertical planes. The normal points
    /// away from what was hit.
    /// </summary>
    private bool CollidesWithWorld(Vector2 position, float heading, Vector2 travel, out Vector2 normal, out float depth)
    {
        const float halfWidth = ProceduralCarFactory.CarHalfWidth;
        const float halfLength = ProceduralCarFactory.CarHalfLength;
        var push = Vector2.zero;
        var deepest = 0f;

        if (_mode == GameMode.Free)
        {
            for (var i = 0; i < _placed.Count; i++)
                Accumulate(CarCollision.Overlap(position, heading, halfWidth, halfLength, _placed[i], out var n, out var d), n, d);
        }

        if (_room != null)
        {
            for (var i = 0; i < _room.Obstacles.Count; i++)
                Accumulate(CarCollision.Overlap(position, heading, halfWidth, halfLength, _room.Obstacles[i], out var n, out var d), n, d);
        }

        if (!_virtualMode)
        {
            if (_depthDetector != null)
                Accumulate(_depthDetector.Overlap(position, heading, halfWidth, halfLength, out var n, out var d), n, d);

            if (travel.sqrMagnitude > 0.0000001f
                && IsBlockedByDetectedVerticalSurface(new Vector3(position.x, 0f, position.y), new Vector3(travel.x, 0f, travel.y).normalized))
                Accumulate(true, -travel.normalized, 0.01f);
        }

        normal = push.sqrMagnitude > 0.0000001f ? push.normalized : Vector2.zero;
        depth = deepest;
        return deepest > 0f;

        void Accumulate(bool hit, Vector2 n, float d)
        {
            if (!hit) return;
            push += n * Mathf.Max(d, 0.001f);
            deepest = Mathf.Max(deepest, Mathf.Max(d, 0.001f));
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
            EnterVirtualMode("Playing in a virtual room without AR.");
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
        CreateShadowCatcher();
        if (_depthDetector != null)
            _depthDetector.SetArena(_arena);

        StartRound();
        if (_anchor == null)
            _message = "Placed without an AR anchor. Avoid moving too far from this spot. " + _message;
    }

    /// <summary>Invisible floor that lets virtual shadows fall on the real floor.</summary>
    private void CreateShadowCatcher()
    {
        var shader = Shader.Find("AR Coin Drive/Shadow Catcher");
        if (shader == null || _arena == null) return;
        const float half = ARDepthObstacleDetector.HalfExtent;
        var mesh = new Mesh { name = "Shadow Catcher" };
        mesh.SetVertices(new[]
        {
            new Vector3(-half, 0f, -half), new Vector3(-half, 0f, half), new Vector3(half, 0f, half), new Vector3(half, 0f, -half)
        });
        mesh.SetNormals(new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
        mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
        mesh.RecalculateBounds();
        var catcher = new GameObject("Shadow Catcher");
        catcher.transform.SetParent(_arena, false);
        catcher.transform.localPosition = Vector3.up * 0.0005f;
        catcher.AddComponent<MeshFilter>().sharedMesh = mesh;
        var meshRenderer = catcher.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = new Material(shader) { name = "Shadow Catcher" };
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = true;
    }

    // ---------------------------------------------------------------- rounds

    private void StartRound()
    {
        if (_roundRoot != null) Destroy(_roundRoot.gameObject);
        _coins.Clear();
        _coinRespawnAt.Clear();
        _score = 0;
        _speed = 0f;
        _newBest = false;
        _raceTime = 0f;
        _boostUntil = 0f;
        _goFlashUntil = 0f;
        _hint = null;
        _playerKnock = Vector2.zero;
        _playerFall = 0f;

        _roundRoot = new GameObject("Round").transform;
        _roundRoot.SetParent(_arena, false);
        UpdateBounds();

        if (_mode == GameMode.Free)
        {
            StartFreeDrive();
            return;
        }

        var track = RaceTrack.Shared;
        _carPosition = track.GridPosition(0);
        _carHeading = track.StartHeading;
        _car = ProceduralCarFactory.CreateCar(_roundRoot).transform;
        _carRig = _car.GetComponent<ProceduralCarRig>();
        StartLapCounter(_carPosition, out _playerLaps, out _playerLastProgress);
        ApplyCarTransform();

        var rivalCar = ProceduralCarFactory.CreateCar(_roundRoot, new Color(0.06f, 0.24f, 0.72f),
            new Color(0.98f, 0.78f, 0.12f)).transform;
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

    /// <summary>Where the car may drive: the room in VR, the depth map area in AR free drive, else the arena.</summary>
    private void UpdateBounds()
    {
        if (_mode != GameMode.Free)
            _bounds = Rect.MinMaxRect(-HalfArena, -HalfArena, HalfArena, HalfArena);
        else if (_room != null)
            _bounds = _room.Bounds;
        else
            _bounds = Rect.MinMaxRect(-FreeARHalfExtent, -FreeARHalfExtent, FreeARHalfExtent, FreeARHalfExtent);
    }

    /// <summary>Free drive: just the buggy (facing away from the phone) and coins around the room.</summary>
    private void StartFreeDrive()
    {
        _rival = null;
        _playerWins = 0;
        _rivalWins = 0;
        _knockoutUntil = 0f;

        _carPosition = _virtualMode ? new Vector2(0f, -0.5f) : Vector2.zero;
        _carHeading = 0f;
        if (!_virtualMode && _arCamera != null)
        {
            var forward = _arena.InverseTransformDirection(_arCamera.transform.forward);
            if (forward.x * forward.x + forward.z * forward.z > 0.0001f)
                _carHeading = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }

        _car = ProceduralCarFactory.CreateCar(_roundRoot).transform;
        _carRig = _car.GetComponent<ProceduralCarRig>();
        if (_carRig != null) _carRig.ReferenceSpeed = MaxSpeed * FreeSpeedMultiplier;
        ApplyCarTransform();

        var random = new System.Random();
        var spread = _virtualMode ? 1.75f : 1.3f;
        for (var attempt = 0; attempt < 200 && _coins.Count < FreeCoinCount; attempt++)
        {
            var angle = (float)random.NextDouble() * Mathf.PI * 2f;
            var distance = Mathf.Sqrt((float)random.NextDouble()) * spread;
            var point = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
            if (!IsGoodCoinSpot(point)) continue;
            var coin = ProceduralCarFactory.CreateCoin(_roundRoot, new Vector3(point.x, 0.06f, point.y));
            _coins.Add(coin.transform);
            _coinRespawnAt.Add(0f);
        }

        _snapChaseCamera = true;
        _phoneView?.Recenter();
        _state = GameState.Racing;
        _message = FreeDriveMessage();
    }

    private bool IsGoodCoinSpot(Vector2 point)
    {
        var inner = new Rect(_bounds.xMin + 0.15f, _bounds.yMin + 0.15f, _bounds.width - 0.3f, _bounds.height - 0.3f);
        if (!inner.Contains(point) || (point - _carPosition).magnitude < 0.4f) return false;
        foreach (var coin in _coins)
            if ((new Vector2(coin.localPosition.x, coin.localPosition.z) - point).magnitude < 0.3f) return false;
        foreach (var obstacle in _placed)
            if (obstacle.Contains(point, 0.12f)) return false;
        if (_room != null)
            foreach (var obstacle in _room.Obstacles)
                if (obstacle.Contains(point, 0.12f)) return false;
        return _depthDetector == null || !_depthDetector.IsSolid(point);
    }

    private void FinishRace()
    {
        _state = GameState.Finished;
        var place = _rival != null && _rival.Finished ? 2 : 1;
        _finishPlace = place;
        var playerFinished = _playerLaps >= LapCount;
        _newBest = playerFinished && (_bestTime <= 0f || _raceTime < _bestTime);
        if (_newBest)
        {
            _bestTime = _raceTime;
            PlayerPrefs.SetFloat(BestTimeKey, _bestTime);
            PlayerPrefs.Save();
        }

        _message = (place == 1 ? "1st place! " : "2nd place. ") + "Time " + FormatTime(_raceTime)
            + (_newBest ? "  NEW BEST!" : "") + "  Tap AGAIN to race again.";
    }

    // ---------------------------------------------------------------- two players

    private bool IsHostOnline => _net != null && _net.Role == LanSession.SessionRole.Host && _net.Connected;
    private bool IsClient => _net != null && _net.Role == LanSession.SessionRole.Client;

    private void ReadRemoteInput(out float drive, out float steer)
    {
        drive = Mathf.Clamp(_net.RemoteDrive, ReverseInput, 1f);
        steer = Mathf.Clamp(_net.RemoteSteer, -1f, 1f);
    }

    private void StartNetwork(bool host)
    {
        _mpPanelOpen = false;
        try
        {
            _net = host ? LanSession.Host() : LanSession.Join();
            _netWasConnected = false;
            _message = host
                ? "Hosting. On the other phone tap 2 PLAYERS > JOIN (same Wi-Fi or your hotspot)."
                : "Looking for a host on this Wi-Fi or hotspot...";
        }
        catch (System.Exception exception)
        {
            _net = null;
            _message = "Could not open the network: " + exception.Message;
        }
    }

    private void StopNetwork(string message)
    {
        _net?.Dispose();
        _net = null;
        _netWasConnected = false;
        _mpPanelOpen = false;
        StartRound();
        _message = message;
    }

    /// <summary>Polls the link and reacts to players joining or dropping out.</summary>
    private void UpdateNetwork()
    {
        if (_net == null) return;
        _net.Poll(Time.unscaledTime);

        if (_net.LostConnection)
        {
            StopNetwork("The other player disconnected. Back to racing the computer.");
            return;
        }

        if (!_net.Connected || _netWasConnected) return;
        _netWasConnected = true;
        if (_mode == GameMode.Free)
        {
            // Two players race or battle; free drive is single-player.
            _mode = GameMode.Race;
            _buildMode = false;
            SetModeVisuals();
        }

        StartRound();
        _snapNetworkCars = true;
        _message = _net.Role == LanSession.SessionRole.Host
            ? "Player 2 joined! Pick a MODE, then hold GO to start."
            : "Connected! Waiting for the host to start.";
    }

    private const int StatePacketSize = 128;
    private readonly byte[] _statePacket = new byte[StatePacketSize];
    private float _nextStateSend;

    /// <summary>Host side: sends the whole game state to the client about 30 times a second.</summary>
    private void SendStateToClient()
    {
        if (!IsHostOnline || _rival == null || Time.unscaledTime < _nextStateSend) return;
        _nextStateSend = Time.unscaledTime + 1f / 30f;

        using (var stream = new System.IO.MemoryStream(_statePacket))
        using (var writer = new System.IO.BinaryWriter(stream))
        {
            writer.Write((byte)0); // packet type, filled in by LanSession
            writer.Write((byte)_mode);
            writer.Write((byte)_state);
            writer.Write(_countdown);
            writer.Write(_raceTime);
            writer.Write(_battleRadius);
            writer.Write((byte)(_knockoutUntil <= 0f ? 0 : _playerFall > 0f ? 1 : 2));
            WriteCar(writer, _carPosition, _carHeading, _speed, _steerInput, _playerFall, _playerLaps);
            WriteCar(writer, _rival.Position, _rival.Heading, _rival.Speed, _rival.Steer, _rival.Fall, _rival.Laps);
            writer.Write((byte)_playerWins);
            writer.Write((byte)_rivalWins);
            writer.Write((byte)_finishPlace);
            writer.Write(Mathf.Max(0f, _goFlashUntil - Time.time));
            var coinBits = 0;
            for (var i = 0; i < _coins.Count && i < 16; i++)
                if (_coins[i] != null && _coins[i].gameObject.activeSelf) coinBits |= 1 << i;
            writer.Write((ushort)coinBits);
            writer.Write(Time.time < _rival.BoostUntil);
            _net.SendState(_statePacket, (int)stream.Position);
        }
    }

    private static void WriteCar(System.IO.BinaryWriter writer, Vector2 position, float heading, float speed,
        float steer, float fall, float laps)
    {
        writer.Write(position.x);
        writer.Write(position.y);
        writer.Write(heading);
        writer.Write(speed);
        writer.Write(steer);
        writer.Write(fall);
        writer.Write(laps);
    }

    private struct NetCar
    {
        public Vector2 Position;
        public float Heading;
        public float Speed;
        public float Steer;
        public float Fall;
        public float Laps;
    }

    private static NetCar ReadCar(System.IO.BinaryReader reader) => new NetCar
    {
        Position = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
        Heading = reader.ReadSingle(),
        Speed = reader.ReadSingle(),
        Steer = reader.ReadSingle(),
        Fall = reader.ReadSingle(),
        Laps = reader.ReadSingle()
    };

    private NetCar _netMine;
    private NetCar _netOther;

    /// <summary>
    /// Client side: sends controls and shows the host's simulation. The host's "rival" car is
    /// this player, so everything is mirrored into this phone's point of view.
    /// </summary>
    private void UpdateClient(float dt)
    {
        _net.SendInput(_driveInput, _steerInput);

        var packet = _net.TakeState();
        if (packet != null)
            ApplyHostState(packet);

        // Smoothly follow the latest reported positions between network updates.
        var follow = _snapNetworkCars ? 1f : 1f - Mathf.Exp(-18f * dt);
        _snapNetworkCars = false;
        _carPosition = Vector2.Lerp(_carPosition, _netMine.Position, follow);
        _carHeading = Mathf.LerpAngle(_carHeading, _netMine.Heading, follow);
        _speed = _netMine.Speed;
        if (_rival != null)
        {
            _rival.Position = Vector2.Lerp(_rival.Position, _netOther.Position, follow);
            _rival.Heading = Mathf.LerpAngle(_rival.Heading, _netOther.Heading, follow);
            _rival.Speed = _netOther.Speed;
            _rival.Steer = _netOther.Steer;
        }

        ApplyCarTransform();
        ApplyRivalTransform();
        if (_carRig != null) _carRig.UpdateVisual(_speed, _netMine.Steer, dt);
        if (_rival != null && _rival.Rig != null) _rival.Rig.UpdateVisual(_rival.Speed, _rival.Steer, dt);
        if (_mode == GameMode.Race) AnimateAndCollectCoins(false);
    }

    private void ApplyHostState(byte[] packet)
    {
        using (var reader = new System.IO.BinaryReader(new System.IO.MemoryStream(packet)))
        {
            reader.ReadByte();
            var mode = (GameMode)reader.ReadByte();
            var state = (GameState)reader.ReadByte();
            var countdown = reader.ReadSingle();
            var raceTime = reader.ReadSingle();
            var battleRadius = reader.ReadSingle();
            var knockout = reader.ReadByte();
            var host = ReadCar(reader);
            var me = ReadCar(reader);
            var hostWins = reader.ReadByte();
            var myWins = reader.ReadByte();
            var hostPlace = reader.ReadByte();
            var goFlash = reader.ReadSingle();
            var coinBits = reader.ReadUInt16();
            var myBoost = reader.ReadBoolean();

            if (mode != _mode)
            {
                _mode = mode;
                SetModeVisuals();
                StartRound();
                _snapNetworkCars = true;
            }

            var previousState = _state;
            if (state == GameState.Countdown && previousState != GameState.Countdown)
                _snapNetworkCars = true;

            _state = state;
            _countdown = countdown;
            _raceTime = raceTime;
            _battleRadius = battleRadius;
            BattleArena.SetRadius(_battleRing, battleRadius);
            _netMine = me;
            _netOther = host;
            _playerFall = me.Fall;
            _playerLaps = me.Laps;
            if (_rival != null)
            {
                _rival.Fall = host.Fall;
                _rival.Laps = host.Laps;
            }

            _playerWins = myWins;
            _rivalWins = hostWins;
            _finishPlace = hostPlace == 1 ? 2 : 1;
            _knockoutUntil = knockout == 0 ? 0f : Time.time + 0.5f;
            _knockoutText = knockout == 2 ? "OUT!" : "KNOCKOUT!";
            _goFlashUntil = Time.time + goFlash;
            if (myBoost && Time.time >= _boostUntil) ShowHint("BOOST!");
            _boostUntil = myBoost ? Time.time + 0.1f : 0f;
            for (var i = 0; i < _coins.Count && i < 16; i++)
                if (_coins[i] != null) _coins[i].gameObject.SetActive((coinBits & (1 << i)) != 0);

            if (state != previousState)
                _message = ClientMessageFor(state);
        }
    }

    private string ClientMessageFor(GameState state)
    {
        switch (state)
        {
            case GameState.Ready:
                return "Connected! Waiting for the host to start.";
            case GameState.Countdown:
                return _mode == GameMode.Battle
                    ? "Round! YOU " + _playerWins + " : " + _rivalWins + " HOST"
                    : "Get ready...";
            case GameState.Racing:
                return _mode == GameMode.Race
                    ? LapCount + " laps! Drive through coins for a speed boost."
                    : "Push the host out of the ring! The ring keeps shrinking.";
            case GameState.Finished:
                if (_mode == GameMode.Battle)
                    return (_finishPlace == 1 ? "You win the battle " : "The host wins ")
                        + _playerWins + " : " + _rivalWins + ". The host can start a rematch.";
                return (_finishPlace == 1 ? "1st place! " : "2nd place. ") + "Time " + FormatTime(_raceTime)
                    + "  The host can start a rematch.";
            default:
                return _message;
        }
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
        if (_placedRoot != null) _placedRoot.gameObject.SetActive(_mode == GameMode.Free);
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
        _coinLayout.Clear();
        for (var i = 0; i < CoinCount; i++)
        {
            var lapFraction = (i + 0.5f) / CoinCount;
            // Deterministic offsets so both phones in a two-player game see the same coins.
            _coinLayout.Add(RaceTrack.Shared.PointAt(lapFraction, Mathf.Sin(i * 2.3f + 0.7f) * 0.07f));
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
        if (_state != GameState.Racing) return;

        // Against a person the race ends at the first finish; against the bot you can still finish.
        if (IsHostOnline) FinishRace();
        else ShowHint("Your rival has finished. Bring it home!");
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
            {
                if (IsHostOnline) ReadRemoteInput(out drive, out steer);
                else BattleRivalInput(out drive, out steer);
            }

            _rival.Steer = steer;
            StepCar(ref _rival.Position, ref _rival.Heading, ref _rival.Speed, drive, steer, 1f, dt, false);
            ApplyKnock(ref _rival.Position, ref _rival.Knock, dt);
            return;
        }

        var racing = _state == GameState.Racing || (_state == GameState.Finished && !_rival.Finished);
        if (IsHostOnline)
        {
            if (_state == GameState.Racing) ReadRemoteInput(out drive, out steer);
        }
        else if (racing)
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
        var rivalBoost = Time.time < _rival.BoostUntil ? BoostMultiplier : 1f;
        StepCar(ref _rival.Position, ref _rival.Heading, ref _rival.Speed, drive, steer, rivalBoost, dt, true);
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
    private void ApplyKnock(ref Vector2 position, ref Vector2 knock, float dt)
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
        var start = _carPosition;
        var startHeading = _carHeading;
        var position = start;
        var heading = startHeading;
        var boost = (Time.time < _boostUntil ? BoostMultiplier : 1f) * (_mode == GameMode.Free ? FreeSpeedMultiplier : 1f);
        StepCar(ref position, ref heading, ref _speed, _driveInput, _steerInput, boost, dt, _mode == GameMode.Race);
        if (battle)
            ApplyKnock(ref position, ref _playerKnock, dt);
        if (Mathf.Abs(_speed) < 0.0001f && _playerKnock.sqrMagnitude < 0.0001f && Mathf.Approximately(heading, startHeading))
            return;

        var travel = position - start;
        if (CollidesWithWorld(position, heading, travel, out var normal, out var depth))
        {
            if (CollidesWithWorld(start, startHeading, travel, out var startNormal, out var startDepth))
            {
                // Already touching (a new obstacle appeared in the depth map, or one was placed):
                // let the car ease out instead of freezing it.
                position += startNormal * Mathf.Min(startDepth, 0.6f * dt);
            }
            else
            {
                // Slide along the surface: drop the part of the move that goes into it.
                var into = Vector2.Dot(travel, normal);
                var slide = start + (into < 0f ? travel - normal * into : travel);
                if (!CollidesWithWorld(slide, heading, travel, out _, out _)) position = slide;
                else if (!CollidesWithWorld(start, heading, travel, out _, out _)) position = start;
                else
                {
                    position = start;
                    heading = startHeading;
                }

                // Hitting it head-on bounces the car back and rocks the shell; a glancing hit scrapes.
                var approach = -Vector2.Dot(HeadingToForward(startHeading) * _speed, normal);
                if (approach > 0.3f)
                {
                    if (_carRig != null) _carRig.Bump(Mathf.Sign(_speed) * approach / (MaxSpeed * FreeSpeedMultiplier));
                    _speed = -_speed * 0.25f;
                    ShowHint(approach > 0.45f ? "CRASH!" : "Bump!");
                }
                else
                {
                    _speed *= Mathf.Exp(-(4f + 10f * Mathf.Clamp01(-Vector2.Dot(HeadingToForward(startHeading), normal))) * dt);
                }
            }
        }

        _carPosition = position;
        _carHeading = heading;
    }

    /// <summary>Shared RC-car physics for the player and the rival.</summary>
    private void StepCar(ref Vector2 position, ref float heading, ref float speed,
        float drive, float steer, float speedMultiplier, float dt, bool onTrack)
    {
        // Leaving the asphalt onto the astroturf run-off costs speed.
        var onRoad = !onTrack
            || Mathf.Abs(RaceTrack.Shared.LateralOffset(position)) <= RaceTrack.RoadHalfWidth + 0.016f;
        var targetSpeed = drive * MaxSpeed * speedMultiplier * (onRoad ? 1f : 0.58f);
        float rate;
        if (Mathf.Abs(targetSpeed) < 0.001f)
            rate = RollingFriction;
        else if (Mathf.Abs(speed) > 0.01f && Mathf.Sign(targetSpeed) != Mathf.Sign(speed))
            rate = Braking;
        else
            rate = Acceleration * Mathf.Max(1f, speedMultiplier * 0.8f);
        speed = Mathf.MoveTowards(speed, targetSpeed, rate * dt);

        // Only settle to a stop when coasting; a tiny speed under throttle is the car pulling away.
        if (Mathf.Abs(speed) < 0.0005f && Mathf.Abs(targetSpeed) < 0.001f)
        {
            speed = 0f;
            return;
        }

        // Steering authority grows with speed and flips when reversing, like a real car; at high
        // speed the turn rate eases off so the car does not spin on the spot.
        var steerAuthority = Mathf.Clamp01(Mathf.Abs(speed) / FullSteerSpeed) * Mathf.Sign(speed);
        var highSpeedEase = Mathf.Lerp(1f, 0.72f, Mathf.Clamp01((Mathf.Abs(speed) - MaxSpeed) / MaxSpeed));
        heading += steer * TurnSpeed * steerAuthority * highSpeedEase * dt;
        position += HeadingToForward(heading) * (speed * dt);
        if (onTrack && RaceTrack.Shared.ConstrainToRails(ref position, ProceduralCarFactory.CarHalfWidth))
        {
            // Scrape along the barrier, but keep enough speed to steer back onto the road.
            var scrapeSpeed = Mathf.Sign(speed) * Mathf.Min(Mathf.Abs(speed), MaxSpeed * 0.4f);
            speed = Mathf.MoveTowards(speed, scrapeSpeed, 2.5f * dt);
        }

        var clamped = ClampInsideArena(position, heading);
        if ((clamped - position).sqrMagnitude > 0.0000001f && _mode == GameMode.Free)
            speed *= Mathf.Exp(-8f * dt); // the edge of the mapped area / the room wall
        position = clamped;
    }

    private void ApplyCarTransform()
    {
        // The wheels now touch y = 0, so the car sits on the arena instead of hovering.
        var drop = _playerFall * _playerFall * 1.4f;
        _car.localPosition = new Vector3(_carPosition.x, 0.002f - drop, _carPosition.y);
        _car.localRotation = Quaternion.Euler(0f, _carHeading, 0f) * Quaternion.Euler(_playerFall * 70f, 0f, 0f);
    }

    private static Vector2 HeadingToForward(float headingDegrees) => CarCollision.Forward(headingDegrees);

    private static Vector2 HeadingToRight(float headingDegrees) => CarCollision.Right(headingDegrees);

    /// <summary>Keeps the whole rotated car body inside the current bounds.</summary>
    private Vector2 ClampInsideArena(Vector2 position, float heading)
    {
        var forward = HeadingToForward(heading);
        var right = HeadingToRight(heading);
        var extentX = ProceduralCarFactory.CarHalfWidth * Mathf.Abs(right.x)
            + ProceduralCarFactory.CarHalfLength * Mathf.Abs(forward.x);
        var extentZ = ProceduralCarFactory.CarHalfWidth * Mathf.Abs(right.y)
            + ProceduralCarFactory.CarHalfLength * Mathf.Abs(forward.y);
        const float inset = 0.012f;
        position.x = Mathf.Clamp(position.x, _bounds.xMin + inset + extentX, _bounds.xMax - inset - extentX);
        position.y = Mathf.Clamp(position.y, _bounds.yMin + inset + extentZ, _bounds.yMax - inset - extentZ);
        return position;
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
            // ARPlane boundaries lie in the plane's local XZ plane; local Y (the plane normal) is
            // the distance from the detected vertical surface.
            if (Mathf.Abs(planeLocal.y) > 0.03f)
                continue;

            if (IsPointInsidePlaneBoundary(plane, new Vector2(planeLocal.x, planeLocal.z)))
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

    /// <summary>
    /// Spins the coins and, when this device runs the simulation, lets either car pick them up.
    /// A network client only animates; the host tells it which coins are present.
    /// </summary>
    private void AnimateAndCollectCoins(bool simulate = true)
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
            var playerTouch = TouchesCoin(_carPosition, _carHeading, coinPoint);
            var rivalTouch = _rival != null && TouchesCoin(_rival.Position, _rival.Heading, coinPoint);
            if (!playerTouch && !rivalTouch) continue;

            // Coins are boost pads: they vanish for a few seconds and then come back.
            coin.gameObject.SetActive(false);
            _coinRespawnAt[i] = Time.time + CoinRespawnSeconds;
            if (playerTouch)
            {
                _score++;
                _boostUntil = Time.time + BoostSeconds;
                ShowHint("BOOST!");
            }
            else
            {
                _rival.BoostUntil = Time.time + BoostSeconds;
            }
        }
    }

    // Collect when any part of the car body touches the coin, not just its centre.
    private static bool TouchesCoin(Vector2 carPosition, float carHeading, Vector2 coinPoint)
    {
        var forward = HeadingToForward(carHeading);
        var right = HeadingToRight(carHeading);
        var delta = coinPoint - carPosition;
        var alongRight = Mathf.Clamp(Vector2.Dot(delta, right),
            -ProceduralCarFactory.CarHalfWidth, ProceduralCarFactory.CarHalfWidth);
        var alongForward = Mathf.Clamp(Vector2.Dot(delta, forward),
            -ProceduralCarFactory.CarHalfLength, ProceduralCarFactory.CarHalfLength);
        var closest = right * alongRight + forward * alongForward;
        return (delta - closest).magnitude <= ProceduralCarFactory.CoinRadius + 0.02f;
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
        // Placed obstacles lived under the old play area, which is gone now.
        _placed.Clear();
        _placedObjects.Clear();
        _placedRoot = null;
        _buildMode = false;
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
        var freeTouches = 0;
        var freeA = Vector2.zero;
        var freeB = Vector2.zero;
        var freeDelta = Vector2.zero;

        for (var i = 0; i < Input.touchCount; i++)
        {
            var touch = Input.GetTouch(i);
            if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                continue;

            var point = ToGuiPoint(touch.position);
            var onControl = IsOnControl(point, roundOver);
            if (touch.phase == TouchPhase.Began)
            {
                action = ClassifyTap(point, roundOver, action);
                if (!onControl && _buildMode && !_hudRect.Contains(point)) HandleBuildTap(touch.position);
            }

            if (!roundOver && !_buildMode) ApplyHeldPoint(point);
            if (onControl || _hudRect.Contains(point)) continue;

            // Fingers on empty screen turn and zoom the virtual-room view.
            if (freeTouches == 0)
            {
                freeA = touch.position;
                freeDelta = touch.phase == TouchPhase.Moved ? touch.deltaPosition : Vector2.zero;
            }
            else if (freeTouches == 1)
            {
                freeB = touch.position;
            }

            freeTouches++;
        }

#if UNITY_EDITOR
        var mousePoint = ToGuiPoint(Input.mousePosition);
        if (Input.GetMouseButtonDown(0))
        {
            action = ClassifyTap(mousePoint, roundOver, action);
            if (!IsOnControl(mousePoint, roundOver) && _buildMode && !_hudRect.Contains(mousePoint))
                HandleBuildTap(Input.mousePosition);
        }

        if (!roundOver)
        {
            if (Input.GetMouseButton(0) && !_buildMode) ApplyHeldPoint(mousePoint);
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

        UpdateLookGestures(freeTouches, freeA, freeB, freeDelta);
        _steerInput = (_rightHeld ? 1f : 0f) - (_leftHeld ? 1f : 0f);
        _driveInput = _goHeld == _backHeld ? 0f : _goHeld ? 1f : ReverseInput;
        return action;
    }

    /// <summary>One finger drags the view around; two fingers pinch to zoom (virtual room, phone camera).</summary>
    private void UpdateLookGestures(int count, Vector2 a, Vector2 b, Vector2 delta)
    {
        if (count < 2) _pinchDistance = 0f;
        if (_phoneView == null || _chaseCamera || _buildMode) return;

        if (count >= 2)
        {
            var distance = (a - b).magnitude;
            if (_pinchDistance > 0f && distance > 1f)
                _phoneView.FieldOfView = Mathf.Clamp(_phoneView.FieldOfView * (_pinchDistance / distance), 18f, 70f);
            _pinchDistance = distance;
            return;
        }

        if (count == 1 && delta.sqrMagnitude > 0f)
        {
            var degreesPerPixel = _phoneView.FieldOfView / Mathf.Max(1f, Screen.height);
            _phoneView.Drag(-delta * degreesPerPixel);
        }
    }

    private bool IsOnControl(Vector2 point, bool roundOver)
    {
        if (ClassifyTap(point, roundOver, ControlAction.None) != ControlAction.None) return true;
        if (roundOver || _buildMode) return false;
        return Expand(_leftRect).Contains(point) || Expand(_rightRect).Contains(point)
            || Expand(_goRect).Contains(point) || Expand(_backRect).Contains(point);
    }

    // FREE drive can switch any time; races only on the grid or after a finish.
    private bool CanChangeMode() => !IsClient && !_buildMode
        && (_mode == GameMode.Free || _state == GameState.Ready || _state == GameState.Finished);

    private bool ShowPlayersButton() => !_buildMode && (_net != null || CanChangeMode());
    private bool ShowCameraButtons() => _virtualMode && !_buildMode;
    private bool ShowScanButton() => !_virtualMode && _depthDetector != null && _depthDetector.DepthSupported && !_buildMode;
    private bool ShowBuildButton() => _mode == GameMode.Free && _net == null;

    private ControlAction ClassifyTap(Vector2 point, bool roundOver, ControlAction current)
    {
        if (_buildMode)
        {
            if (Expand(_buildRect).Contains(point)) return ControlAction.Build;
            if (Expand(_coneRect).Contains(point)) return ControlAction.PlaceCone;
            if (Expand(_tyresRect).Contains(point)) return ControlAction.PlaceTyres;
            if (Expand(_wallRect).Contains(point)) return ControlAction.PlaceWall;
            if (Expand(_clearRect).Contains(point)) return ControlAction.ClearObstacles;
            return current;
        }

        if (_resetRect.Contains(point)) return ControlAction.Reset;
        if (_mpPanelOpen && Expand(_hostRect).Contains(point)) return ControlAction.Host;
        if (_mpPanelOpen && Expand(_joinRect).Contains(point)) return ControlAction.Join;
        if (ShowPlayersButton() && _mpRect.Contains(point)) return ControlAction.Players;
        if (roundOver && !IsClient && Expand(_againRect).Contains(point)) return ControlAction.Again;
        if (CanChangeMode() && _modeRect.Contains(point)) return ControlAction.Mode;
        if (ShowCameraButtons() && _cameraRect.Contains(point)) return ControlAction.Camera;
        if (ShowCameraButtons() && !_chaseCamera && _spotRect.Contains(point)) return ControlAction.Spot;
        if (ShowScanButton() && _scanRect.Contains(point)) return ControlAction.Scan;
        if (ShowBuildButton() && _buildRect.Contains(point)) return ControlAction.Build;
        return current;
    }

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
        _mpRect = new Rect(Screen.width - margin - 150f * s, _hudRect.yMax + 8f * s, 150f * s, 40f * s);
        var panelWidth = 150f * s;
        var panelY = Screen.height * 0.45f;
        _hostRect = new Rect(Screen.width * 0.5f - panelWidth - gap, panelY, panelWidth, 64f * s);
        _joinRect = new Rect(Screen.width * 0.5f + gap, panelY, panelWidth, 64f * s);

        var noArWidth = 220f * s;
        var noArHeight = 54f * s;
        _noArRect = new Rect((Screen.width - noArWidth) * 0.5f, Screen.height - noArHeight - 28f * s,
            noArWidth, noArHeight);

        var againWidth = 190f * s;
        var againHeight = 70f * s;
        _againRect = new Rect((Screen.width - againWidth) * 0.5f, y - againHeight - 70f * s, againWidth, againHeight);

        // Tool column under the HUD on the left: view buttons, depth scan, build.
        var toolWidth = 96f * s;
        var toolHeight = 38f * s;
        var toolY = _hudRect.yMax + 8f * s;
        var hidden = new Rect(-1000f, -1000f, 0f, 0f);
        _cameraRect = _spotRect = _scanRect = hidden;
        if (ShowCameraButtons())
        {
            _cameraRect = new Rect(margin, toolY, toolWidth, toolHeight);
            toolY += toolHeight + 6f * s;
            if (!_chaseCamera)
            {
                _spotRect = new Rect(margin, toolY, toolWidth, toolHeight);
                toolY += toolHeight + 6f * s;
            }
        }

        if (ShowScanButton())
        {
            _scanRect = new Rect(margin, toolY, toolWidth, toolHeight);
            toolY += toolHeight + 6f * s;
        }

        _buildRect = ShowBuildButton() ? new Rect(margin, toolY, toolWidth, toolHeight) : hidden;

        // Build palette replaces the driving buttons.
        var paletteWidth = (Screen.width - margin * 2f - gap * 3f) / 4f;
        _coneRect = new Rect(margin, y, paletteWidth, buttonHeight);
        _tyresRect = new Rect(margin + (paletteWidth + gap), y, paletteWidth, buttonHeight);
        _wallRect = new Rect(margin + (paletteWidth + gap) * 2f, y, paletteWidth, buttonHeight);
        _clearRect = new Rect(margin + (paletteWidth + gap) * 3f, y, paletteWidth, buttonHeight);
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

        var title = "AR COIN DRIVE";
        if (_arena != null && _mode == GameMode.Free)
        {
            // Speed shown as the full-size equivalent of a 1:18 RC car.
            var scaleSpeed = Mathf.RoundToInt(Mathf.Abs(_speed) * 3.6f * RcScale);
            title = "FREE DRIVE  •  COINS " + _score + "  •  " + scaleSpeed + " km/h";
        }
        else if (_arena != null && _mode == GameMode.Battle)
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
        if (_depthDetector != null && _depthDetector.HasDepthFrame)
            depthState += ", " + _depthDetector.SolidCellCount + " obstacle cells";
        var featureStatus = "Planes: " + planeCount + "  •  anchor: " + anchorState
            + "  •  depth: " + depthState;
        if (_virtualMode)
        {
            trackingStatus = "Mode: virtual room (no AR)  •  "
                + (_chaseCamera ? "chase camera" : _phoneView != null && _phoneView.HasGyro ? "gyroscope camera" : "auto camera");
            featureStatus = "Drag the screen to look around, pinch to zoom  •  RESET restarts";
        }

        if (_net != null)
            featureStatus = "2 players: " + _net.Status;

        GUI.Label(new Rect(textX, margin * 1.35f + 64f * scale, textWidth, 18f * scale), trackingStatus, _statusStyle);
        GUI.Label(new Rect(textX, margin * 1.35f + 82f * scale, textWidth, 18f * scale), featureStatus, _statusStyle);

        if (_arena == null)
        {
            DrawLogo();
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

        var toolColor = new Color(0.12f, 0.18f, 0.23f, 0.88f);
        var activeColor = new Color(0.95f, 0.55f, 0.10f, 0.94f);
        if (ShowCameraButtons())
        {
            DrawControl(_cameraRect, _chaseCamera ? "CAM: CHASE" : "CAM: PHONE", toolColor, false, _smallButtonStyle);
            if (!_chaseCamera && _phoneView != null)
                DrawControl(_spotRect, "MOVE: " + _phoneView.SpotName, toolColor, false, _smallButtonStyle);
        }

        if (ShowScanButton())
            DrawControl(_scanRect, _depthDetector.ShowOverlay ? "SCAN: ON" : "SCAN: OFF", toolColor, false, _smallButtonStyle);
        if (ShowBuildButton())
            DrawControl(_buildRect, _buildMode ? "DONE" : "BUILD", _buildMode ? activeColor : toolColor, false, _smallButtonStyle);

        if (_buildMode)
        {
            DrawControl(_coneRect, "CONE", _buildKind == ObstacleKind.Cone ? activeColor : toolColor, false, _smallButtonStyle);
            DrawControl(_tyresRect, "TYRES", _buildKind == ObstacleKind.Tyres ? activeColor : toolColor, false, _smallButtonStyle);
            DrawControl(_wallRect, "WALL", _buildKind == ObstacleKind.Wall ? activeColor : toolColor, false, _smallButtonStyle);
            DrawControl(_clearRect, "CLEAR", new Color(0.55f, 0.12f, 0.10f, 0.9f), false, _smallButtonStyle);
            GUI.color = previousGuiColor;
            GUI.contentColor = previousContentColor;
            return;
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

        DrawControl(_resetRect, "RESET", toolColor, false, _smallButtonStyle);
        if (CanChangeMode())
        {
            var modeLabel = _mode == GameMode.Free ? "MODE: FREE  ⇄" : battle ? "MODE: BATTLE  ⇄" : "MODE: RACE  ⇄";
            DrawControl(_modeRect, modeLabel, new Color(0.42f, 0.20f, 0.70f, 0.92f), false, _smallButtonStyle);
        }

        if (ShowPlayersButton())
        {
            var playersLabel = _net != null ? "LEAVE 2P" : _mpPanelOpen ? "CLOSE" : "2 PLAYERS";
            DrawControl(_mpRect, playersLabel, new Color(0.10f, 0.45f, 0.85f, 0.92f), false, _smallButtonStyle);
        }

        if (_mpPanelOpen && _net == null)
        {
            DrawControl(_hostRect, "HOST", new Color(0.08f, 0.67f, 0.34f, 0.96f), false, _buttonStyle);
            DrawControl(_joinRect, "JOIN", new Color(0.10f, 0.45f, 0.85f, 0.96f), false, _buttonStyle);
        }

        GUI.color = previousGuiColor;
        GUI.contentColor = previousContentColor;
    }

    private void DrawLogo()
    {
        if (_logo == null) return;
        var alpha = 1f - Mathf.Clamp01((Time.timeSinceLevelLoad - LogoHoldSeconds) / LogoFadeSeconds);
        if (alpha <= 0f) return;

        var aspect = _logo.width / (float)_logo.height;
        var width = Mathf.Min(Screen.width * 0.78f, Screen.height * 0.42f * aspect);
        var height = width / aspect;
        var rect = new Rect((Screen.width - width) * 0.5f, Screen.height * 0.47f - height * 0.5f, width, height);
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.DrawTexture(rect, _logo, ScaleMode.ScaleToFit, true);
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
