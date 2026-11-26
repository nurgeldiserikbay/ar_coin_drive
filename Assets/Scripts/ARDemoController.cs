using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// AR Coin Drive: scan and tap the floor (or play in the virtual living room), then drive an RC
/// buggy. FREE DRIVE lets it roam the room, bump into real obstacles seen by the depth sensor
/// and into cones, tyres and walls the player places; RACE is five laps of an RC track; BATTLE
/// pushes the others out of a shrinking ring. Up to four players on the local network.
///
/// This file: start-up, screens, AR placement, the virtual room, camera, building and world
/// collisions. See the .Cars, .Network and .UI parts for the rest.
/// Real obstacle sensing is approximate and device-dependent.
/// </summary>
public sealed partial class ARDemoController : MonoBehaviour
{
    private enum GameState { Placing, Ready, Countdown, Racing, Finished }
    // Values are sent over the network: keep Race = 0 and Battle = 1.
    private enum GameMode { Race, Battle, Free }
    private enum UiScreen { Menu, Lobby, Settings, Game, Pause, Garage, Tracks, Locations }

    private static readonly List<ARRaycastHit> RaycastHits = new List<ARRaycastHit>();
    private const float FreeARHalfExtent = ARDepthObstacleDetector.HalfExtent - 0.15f;
    private const float ARStartTimeout = 12f;
    private const float RcScale = 18f;

    // AR rig
    private ARSession _session;
    private Camera _arCamera;
    private ARRaycastManager _raycastManager;
    private ARPlaneManager _planeManager;
    private ARCameraManager _cameraManager;
    private ARAnchorManager _anchorManager;
    private ARAnchor _anchor;
    private ARDepthObstacleDetector _depthDetector;
    private int _cameraFrameCount;
    private bool _arChecked;
    private bool _arSupported = true;
    private float _scanStartTime;

    // Virtual room
    private bool _virtualMode;
    private Transform _virtualTable;
    private VirtualHouse.Info _room;
    private PhoneViewCamera _phoneView;
    private enum ViewMode { Phone, Chase, Driver }
    private ViewMode _view = ViewMode.Phone;
    private Vector3 _chaseVelocity;
    private bool _snapChaseCamera;

    // Play area and round
    private Light _keyLight;
    private Transform _arena;
    private Transform _roundRoot;
    private GameObject _trackVisual;
    private Transform _battleRing;
    private Rect _bounds = Rect.MinMaxRect(-HalfArena, -HalfArena, HalfArena, HalfArena);
    private GameMode _mode = GameMode.Free;
    private GameState _state = GameState.Placing;
    private float _countdown;
    private float _raceTime;
    private float _goFlashUntil;
    private float _bestTime;
    private bool _newBest;

    // Garage coins earned this round.
    private int _roundEarned;
    private int _adBonus; // coins added by the rewarded ad this round (0 = not watched)
    private int _placePrize;
    private int _coinsRewarded;
    private bool _prizeGiven;
    private float _earnFlashAt = -10f;

    // Free-drive building
    private readonly List<WorldObstacle> _placed = new List<WorldObstacle>();
    private readonly List<GameObject> _placedObjects = new List<GameObject>();
    private Transform _placedRoot;
    private bool _buildMode;
    private ObstacleKind _buildKind = ObstacleKind.Cone;

    // Input
    private float _driveInput;
    private float _steerInput;
    private float _pinchDistance;
    private Vector2 _lastLook;
    private bool _looking;

    // Messages
    private string _message = "";
    private float _messageTime;
    private string _hint;
    private float _hintUntil;
    private string _toast;
    private float _toastUntil;

    private void Awake()
    {
        GameSettings.ApplyOrientation();
        GameSettings.ApplyFrameRate();
        Input.compensateSensors = true;
        _ui = new UiKit();
        _logo = Resources.Load<Texture2D>("ARCoinDriveLogo");
        _arCamera = Camera.main;
        ARRigSetup.EnsureRig(_arCamera);
        _session = FindAnyObjectByType<ARSession>();
        _raycastManager = FindAnyObjectByType<ARRaycastManager>();
        _planeManager = FindAnyObjectByType<ARPlaneManager>();
        _cameraManager = FindAnyObjectByType<ARCameraManager>();
        if (_cameraManager != null) _cameraManager.frameReceived += OnCameraFrameReceived;
        _anchorManager = FindAnyObjectByType<ARAnchorManager>();
        _depthDetector = FindAnyObjectByType<ARDepthObstacleDetector>();
        _bestTime = GameSettings.BestTime("Race");
        _keyLight = EnsureSceneLight();
        ConfigureShadows(4f);
        SetAmbient(new Color(0.62f, 0.63f, 0.66f), new Color(0.48f, 0.46f, 0.44f), new Color(0.26f, 0.24f, 0.22f));

        // Match the virtual light to the real room on AR devices.
        if (_cameraManager != null && !_cameraManager.TryGetComponent<ARLightEstimation>(out _))
            _cameraManager.gameObject.AddComponent<ARLightEstimation>().Configure(_keyLight);
        if (_arCamera != null)
        {
            _arCamera.nearClipPlane = 0.03f;
            _arCamera.cullingMask &= ~(1 << GaragePreview.Layer);
        }
        _ = GameAudio.Instance;
        _screen = UiScreen.Menu;
    }

    private void Start()
    {
        GameStore.Init();
        GameAds.Init();
        StartCoroutine(CheckARSupport());
    }

    /// <summary>Learns whether AR can run; the menu disables AR play if it cannot.</summary>
    private IEnumerator CheckARSupport()
    {
        if (ARSession.state == ARSessionState.None || ARSession.state == ARSessionState.CheckingAvailability)
            yield return ARSession.CheckAvailability();
        _arSupported = ARSession.state != ARSessionState.Unsupported;
        _arChecked = true;
    }

    private void OnApplicationPause(bool paused)
    {
        // A full-screen ad also pauses the app; that must not open the pause menu.
        if (paused && _screen == UiScreen.Game && _arena != null && !IsOnline && !GameAds.Showing) OpenPause();
    }

    private void OnDestroy()
    {
        if (_cameraManager != null) _cameraManager.frameReceived -= OnCameraFrameReceived;
        _net?.Dispose();
        _net = null;
        Time.timeScale = 1f;
        AudioListener.pause = false;
    }

    private void OnCameraFrameReceived(ARCameraFrameEventArgs args) => _cameraFrameCount++;

    // ------------------------------------------------------------------ lighting

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

    // ------------------------------------------------------------------ frame

    private void Update()
    {
        var dt = Time.deltaTime;
        _ui.BeginInput();
        ARRigSetup.EnsurePlaneVisuals(_planeManager);
        SetPlaneVisualsHidden(_arena != null || _screen != UiScreen.Game);

        UpdateNetwork();
        _leftHeld = _rightHeld = _goHeld = _backHeld = false;
        RunUi();
        HandleFreeTouches();
        ReadDriveInput();

        if (_arena != null)
        {
            if (IsClient) UpdateClient(dt);
            else SimulateGame(dt);
            UpdateCarVisuals(dt);
            UpdateRewards();
        }
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        _ui.BeginDraw();
        RunUi();
    }

    /// <summary>Taps and drags that no button used: placing the floor, building, looking around.</summary>
    private void HandleFreeTouches()
    {
        if (_screen != UiScreen.Game) return;

        foreach (var tap in _ui.FreeTaps())
        {
            if (_arena == null && !_virtualMode) TryPlaceArena(tap);
            else if (_buildMode) HandleBuildTap(tap);
        }

        // One finger drags the view around; two fingers pinch to zoom (virtual room, phone camera).
        var count = _ui.FreeFingers(out var a, out var b);
        if (count < 2) _pinchDistance = 0f;
        if (count == 0) _looking = false;
        if (_phoneView == null || _view != ViewMode.Phone || _buildMode || _arena == null) return;

        if (count >= 2)
        {
            var distance = (a - b).magnitude;
            if (_pinchDistance > 0f && distance > 1f)
                _phoneView.FieldOfView = Mathf.Clamp(_phoneView.FieldOfView * (_pinchDistance / distance), 18f, 70f);
            _pinchDistance = distance;
            _looking = false;
            return;
        }

        if (count == 1)
        {
            if (_looking)
            {
                var degreesPerPixel = _phoneView.FieldOfView / Mathf.Max(1f, Screen.height);
                _phoneView.Drag(-(a - _lastLook) * degreesPerPixel * new Vector2(1f, -1f));
            }

            _lastLook = a;
            _looking = true;
        }
    }

    private void ReadDriveInput()
    {
        var steer = (_rightHeld ? 1f : 0f) - (_leftHeld ? 1f : 0f);
        if (GameSettings.Steering == SteeringSetting.Tilt)
        {
            // Tilt like a steering wheel: gravity along the screen's x axis.
            var tilt = Input.acceleration.x;
            steer = Mathf.Abs(tilt) < 0.06f ? 0f : Mathf.Clamp((tilt - Mathf.Sign(tilt) * 0.06f) * 2.6f, -1f, 1f);
        }

#if UNITY_EDITOR
        var keySteer = Input.GetAxisRaw("Horizontal");
        var keyDrive = Input.GetAxisRaw("Vertical");
        if (Mathf.Abs(keySteer) > 0.1f) steer = Mathf.Sign(keySteer);
        if (keyDrive > 0.1f) _goHeld = true;
        if (keyDrive < -0.1f) _backHeld = true;
#endif
        _steerInput = steer;
        _driveInput = _goHeld == _backHeld ? 0f : _goHeld ? 1f : ReverseInput;
    }

    // ------------------------------------------------------------------ messages

    private void SetMessage(string text)
    {
        if (text == _message) return;
        _message = text;
        _messageTime = Time.unscaledTime;
    }

    private void ShowHint(string text)
    {
        _hint = text;
        _hintUntil = Time.time + 1.4f;
    }

    private void ShowToast(string text)
    {
        _toast = text;
        _toastUntil = Time.unscaledTime + 3.5f;
    }

    // ------------------------------------------------------------------ entering play

    /// <summary>Starts scanning the real floor (AR) for single player or a network game.</summary>
    private void BeginAR()
    {
        _garage?.Release();
        _screen = UiScreen.Game;
        _scanStartTime = Time.unscaledTime;
        _state = GameState.Placing;
        SetMessage("Move your phone slowly over the floor, then tap it to place your play area.");
    }

    private GameLocation _location;

    /// <summary>Builds (or rebuilds) the virtual location: geometry, camera spots, light and sky.</summary>
    private void BuildLocation(GameLocation location)
    {
        if (_virtualTable != null)
        {
            _virtualTable.gameObject.SetActive(false);
            Destroy(_virtualTable.gameObject);
            VirtualHouse.Release();
        }

        _location = location;
        _virtualTable = new GameObject("Virtual " + location).transform;
        _room = VirtualHouse.Build(_virtualTable, location);
        _phoneView = new PhoneViewCamera(_room.CameraSpots);
        _phoneView.FollowRoom(4, Vector3.zero); // the middle zone holds the track and arena
        if (_arCamera != null)
        {
            _arCamera.backgroundColor = _room.Background;
            _arCamera.farClipPlane = _room.FarClip;
        }

        if (_keyLight != null)
        {
            _keyLight.transform.rotation = Quaternion.LookRotation(_room.SunDirection);
            _keyLight.color = _room.SunColor;
            _keyLight.intensity = _room.SunIntensity;
        }

        ConfigureShadows(_room.ShadowDistance);
        SetAmbient(_room.AmbientSky, _room.AmbientEquator, _room.AmbientGround);
    }

    private void EnterVirtualMode()
    {
        _garage?.Release();
        _screen = UiScreen.Game;
        if (_virtualMode)
        {
            if (GameSettings.Location != _location)
            {
                BuildLocation(GameSettings.Location);
                StartRound();
            }

            return;
        }

        if (_arena != null) ResetToScanning();
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
            if (_arCamera.TryGetComponent<ARLightEstimation>(out var estimation)) estimation.enabled = false;
            if (_arCamera.TryGetComponent<ARCameraManager>(out var cameraManager)) cameraManager.enabled = false;
            ARRigSetup.SetPoseDriverEnabled(_arCamera, false);
            _arCamera.clearFlags = CameraClearFlags.SolidColor;
        }

        if (_session != null) _session.enabled = false;

        BuildLocation(GameSettings.Location);
        _arena = new GameObject("Bounded Play Area").transform;
        _arena.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        CreateArenaVisuals();
        StartRound();
    }

    /// <summary>Back to the main menu: reloading the scene restarts AR cleanly.</summary>
    private void ReturnToMenu()
    {
        _net?.Dispose();
        _net = null;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        VirtualHouse.Release(); // runtime meshes outlive the scene
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void OpenPause()
    {
        _screen = UiScreen.Pause;
        _buildMode = false;
        if (!IsOnline)
        {
            Time.timeScale = 0f;
            AudioListener.pause = true;
        }
    }

    private void ClosePause()
    {
        _screen = UiScreen.Game;
        Time.timeScale = 1f;
        AudioListener.pause = false;
    }

    // ------------------------------------------------------------------ AR placement

    /// <summary>Explains AR start-up problems while scanning instead of leaving the player stuck.</summary>
    private string GetPlacingMessage()
    {
        switch (ARSession.state)
        {
            case ARSessionState.NeedsInstall:
            case ARSessionState.Installing:
                return "Google Play Services for AR is needed. Install it, or play in the virtual room.";
            case ARSessionState.Unsupported:
                return "AR is not available on this phone. Play in the virtual room instead.";
        }

        if (_cameraFrameCount == 0 && Time.unscaledTime - _scanStartTime > ARStartTimeout)
            return "The AR camera is not starting. Check the camera permission, or play in the virtual room.";
        var planes = _planeManager != null ? _planeManager.trackables.count : 0;
        return planes == 0 ? "Move your phone slowly over the floor..." : "Tap the highlighted floor to place your play area.";
    }

    private void TryPlaceArena(Vector2 screenPosition)
    {
        if (_raycastManager == null)
        {
            ShowToast("AR setup is missing. Use Tools > AR Coin Drive > Create Demo Scene.");
            return;
        }

        if (!_raycastManager.Raycast(screenPosition, RaycastHits, TrackableType.PlaneWithinPolygon))
        {
            ShowToast("No floor found there yet. Scan a textured, well-lit floor and tap it.");
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
            ShowToast("Tap a horizontal surface, such as the floor or a table.");
            return;
        }

        var floorPlane = RaycastHits[floorHitIndex].trackable as ARPlane;
        var hitPose = RaycastHits[floorHitIndex].pose;
        var arenaPose = new Pose(hitPose.position + hitPose.up * 0.008f, hitPose.rotation);

        _anchor = null;
        var anchorDescriptor = _anchorManager != null ? _anchorManager.descriptor : null;
        if (_anchorManager != null && anchorDescriptor != null && anchorDescriptor.supportsTrackableAttachments)
            _anchor = _anchorManager.AttachAnchor(floorPlane, arenaPose);

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
        if (_depthDetector != null) _depthDetector.SetArena(_arena);
        GameAudio.Instance.Play(GameAudio.Sfx.Place);
        StartRound();
        if (_anchor == null) ShowToast("Placed without an AR anchor. Avoid walking far from this spot.");
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

    private void CreateArenaVisuals()
    {
        // Guests use the host's track (set from state packets), not their own saved choice.
        if (!IsClient) LoadTrackChoice();
        _trackVisual = RaceTrack.Shared.CreateVisual(_arena);
        _battleRing = BattleArena.Create(_arena, _arenaShape);
        SetModeVisuals();
    }

    private void LoadTrackChoice()
    {
        RaceTrack.Select(GameSettings.Track, GameSettings.TrackSize);
        _arenaShape = GameSettings.Arena;
        _arenaSize = GameSettings.ArenaSize;
    }

    /// <summary>Swaps the track and arena for the ones picked in the TRACKS screen (or sent by the host).</summary>
    private void ApplyTrackChoice()
    {
        if (_arena == null) return;
        if (_trackVisual != null) Destroy(_trackVisual);
        if (_battleRing != null) Destroy(_battleRing.gameObject);
        CreateArenaVisuals();
    }

    private void SetModeVisuals()
    {
        if (_trackVisual != null) _trackVisual.SetActive(_mode == GameMode.Race);
        if (_battleRing != null) _battleRing.gameObject.SetActive(_mode == GameMode.Battle);
        if (_placedRoot != null) _placedRoot.gameObject.SetActive(_mode == GameMode.Free);
        BattleArena.SetRadius(_battleRing, BattleStartRadius);
    }

    private void ChangeMode(GameMode mode)
    {
        if (mode == _mode || IsClient) return;
        if (mode == GameMode.Free && IsOnline) return;
        _mode = mode;
        _buildMode = false;
        SetModeVisuals();
        if (_arena != null) StartRound();
    }

    /// <summary>AR: forget the placed play area and scan for a new spot.</summary>
    private void ResetToScanning()
    {
        ClearRound();
        if (_arena != null) Destroy(_arena.gameObject);
        if (_anchor != null) Destroy(_anchor.gameObject);
        _arena = null;
        _anchor = null;
        _trackVisual = null;
        _battleRing = null;
        _depthDetector?.SetArena(null);
        // Placed obstacles lived under the old play area, which is gone now.
        _placed.Clear();
        _placedObjects.Clear();
        _placedRoot = null;
        _buildMode = false;
        _state = GameState.Placing;
        _scanStartTime = Time.unscaledTime;
    }

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
            if (plane.TryGetComponent<MeshRenderer>(out var meshRenderer)) meshRenderer.forceRenderingOff = hidden;
            if (plane.TryGetComponent<LineRenderer>(out var lineRenderer)) lineRenderer.forceRenderingOff = hidden;
        }
    }

    // ------------------------------------------------------------------ camera

    /// <summary>
    /// Virtual room camera: the phone films the car from where the player stands (or a chase
    /// camera if chosen). In AR the phone itself is the camera.
    /// </summary>
    private void LateUpdate()
    {
        if (!_virtualMode || _arCamera == null || _player == null || _player.Root == null) return;
        var car = _player.Root;
        var dt = Time.unscaledDeltaTime;

        if (_view == ViewMode.Phone && _phoneView != null)
        {
            // The player walks with the car from room to room.
            _phoneView.FollowRoom(_room.RoomAt(_player.Position), car.position);
            _phoneView.Update(_arCamera.transform, _arCamera, car.position + Vector3.up * 0.04f, dt);
            return;
        }

        if (_view == ViewMode.Driver)
        {
            // Driver's eye just above the windscreen, looking slightly down the road. The view
            // follows the shell, so it leans with body roll and rocks after bumps.
            // Vertical FOV: wide enough in portrait, not fish-eyed in landscape.
            _arCamera.fieldOfView = Screen.width > Screen.height ? 58f : 80f;
            var eyeAnchor = _player.Rig != null ? _player.Rig.Body : car;
            var eyeLocal = _player.Spec.Body == CarBody.Buggy ? new Vector3(0f, 0.108f, 0.09f) : CarModelBuilder.DriverEye(_player.Spec.Body);
            var eye = eyeAnchor.TransformPoint(eyeLocal);
            var look = eyeAnchor.rotation * Quaternion.Euler(5f, 0f, 0f);
            var eyeTransform = _arCamera.transform;
            eyeTransform.position = eye;
            eyeTransform.rotation = _snapChaseCamera ? look : Quaternion.Slerp(eyeTransform.rotation, look, 1f - Mathf.Exp(-22f * dt));
            _snapChaseCamera = false;
            return;
        }

        if (Mathf.Abs(_arCamera.fieldOfView - 60f) > 0.01f) _arCamera.fieldOfView = 60f;
        var forward2 = CarCollision.Forward(_player.Heading);
        var forward = _arena.TransformDirection(new Vector3(forward2.x, 0f, forward2.y));
        var up = _arena.up;
        var desiredPosition = car.position - forward * 0.56f + up * 0.40f;
        if (_room != null)
        {
            // Keep the chase camera inside the walls of the room the car is in.
            var roomRect = _room.RoomRect(_room.RoomAt(_player.Position));
            desiredPosition.x = Mathf.Clamp(desiredPosition.x, roomRect.xMin + 0.15f, roomRect.xMax - 0.15f);
            desiredPosition.z = Mathf.Clamp(desiredPosition.z, roomRect.yMin + 0.15f, roomRect.yMax - 0.15f);
        }

        var lookTarget = car.position + forward * 0.3f;
        var cameraTransform = _arCamera.transform;
        if (_snapChaseCamera)
        {
            _snapChaseCamera = false;
            _chaseVelocity = Vector3.zero;
            cameraTransform.position = desiredPosition;
            cameraTransform.rotation = Quaternion.LookRotation(lookTarget - desiredPosition, up);
            return;
        }

        cameraTransform.position = Vector3.SmoothDamp(cameraTransform.position, desiredPosition, ref _chaseVelocity, 0.28f,
            Mathf.Infinity, Mathf.Max(dt, 0.0001f));
        var targetRotation = Quaternion.LookRotation(lookTarget - cameraTransform.position, up);
        cameraTransform.rotation = Quaternion.Slerp(cameraTransform.rotation, targetRotation, 1f - Mathf.Exp(-8f * dt));
    }

    // ------------------------------------------------------------------ building

    private string BuildMessage()
    {
        var name = _buildKind == ObstacleKind.Cone ? "a cone" : _buildKind == ObstacleKind.Tyres ? "a tyre stack" : "a wall";
        return "Tap the floor to place " + name + ". Tap an obstacle to remove it.";
    }

    private string FreeDriveMessage() => _virtualMode
        ? "Free drive. Turn your phone to follow the car. Collect the coins!"
        : "Free drive. Real objects stop the car. BUILD adds cones, tyres and walls.";

    /// <summary>Screen tap in build mode: remove the obstacle under the finger or place a new one.</summary>
    private void HandleBuildTap(Vector2 screenPosition)
    {
        if (_arena == null || _arCamera == null || _player == null) return;
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
            GameAudio.Instance.Play(GameAudio.Sfx.Remove);
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
        if (CarCollision.Overlap(_player.Position, _player.Heading, _player.Spec.HalfWidth,
                _player.Spec.HalfLength, footprint, out _, out _))
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
        GameAudio.Instance.Play(GameAudio.Sfx.Place);
        ShowHint(_placed.Count + " placed");
    }

    private void ClearPlacedObstacles()
    {
        foreach (var placed in _placedObjects)
            if (placed != null) Destroy(placed);
        _placed.Clear();
        _placedObjects.Clear();
        GameAudio.Instance.Play(GameAudio.Sfx.Remove);
    }

    // ------------------------------------------------------------------ world collisions

    /// <summary>
    /// Everything solid around the car: placed obstacles (free drive), the room's furniture
    /// (virtual room), and in AR the depth map and tracked vertical planes. The normal points
    /// away from what was hit.
    /// </summary>
    private bool CollidesWithWorld(Car car, Vector2 position, float heading, Vector2 travel, out Vector2 normal, out float depth)
    {
        var halfWidth = car.Spec.HalfWidth;
        var halfLength = car.Spec.HalfLength;
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
                && IsBlockedByDetectedVerticalSurface(new Vector3(position.x, 0f, position.y), new Vector3(travel.x, 0f, travel.y).normalized, halfLength))
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

    private bool IsBlockedByDetectedVerticalSurface(Vector3 candidateCarPosition, Vector3 localTravelDirection, float halfLength)
    {
        if (_planeManager == null || _arena == null) return false;

        // Probe slightly ahead of the car at bumper height. AR planes are geometric
        // observations, so this only guards against flat surfaces the device has tracked.
        var probeLocal = candidateCarPosition + localTravelDirection * (halfLength + 0.012f) + Vector3.up * 0.05f;
        var probeWorld = _arena.TransformPoint(probeLocal);

        foreach (var plane in _planeManager.trackables)
        {
            if (plane == null || plane.alignment != PlaneAlignment.Vertical) continue;
            var planeLocal = plane.transform.InverseTransformPoint(probeWorld);
            // ARPlane boundaries lie in the plane's local XZ plane; local Y (the plane normal) is
            // the distance from the detected vertical surface.
            if (Mathf.Abs(planeLocal.y) > 0.03f) continue;
            if (IsPointInsidePlaneBoundary(plane, new Vector2(planeLocal.x, planeLocal.z))) return true;
        }

        return false;
    }

    private static bool IsPointInsidePlaneBoundary(ARPlane plane, Vector2 point)
    {
        var boundary = plane.boundary;
        if (!boundary.IsCreated || boundary.Length < 3) return false;

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
}
