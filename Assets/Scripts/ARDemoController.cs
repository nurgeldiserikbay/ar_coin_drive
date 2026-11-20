using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// First playable loop: scan and tap a horizontal surface, drive inside a bounded
/// 1.5 m square, and collect coins. Real obstacle sensing is approximate and device-dependent.
/// </summary>
public sealed class ARDemoController : MonoBehaviour
{
    private const float HalfArena = 0.75f;
    private const float DriveSpeed = 0.48f;
    private const float TurnSpeed = 120f;
    private const int CoinCount = 8;

    private static readonly List<ARRaycastHit> RaycastHits = new List<ARRaycastHit>();

    private ARRaycastManager _raycastManager;
    private ARPlaneManager _planeManager;
    private ARCameraManager _cameraManager;
    private int _cameraFrameCount;
    private ARAnchorManager _anchorManager;
    private ARAnchor _anchor;
    private ARDepthObstacleDetector _depthDetector;
    private Transform _arena;
    private Transform _car;
    private readonly List<Transform> _coins = new List<Transform>();
    private readonly List<Vector2> _obstaclePositions = new List<Vector2>();
    private Material _guideMaterial;
    private int _score;
    private float _driveInput;
    private float _steerInput;
    private bool _leftHeld;
    private bool _rightHeld;
    private bool _goHeld;
    private bool _backHeld;
    private float _uiScale = 1f;
    private Rect _hudRect;
    private Rect _leftRect;
    private Rect _rightRect;
    private Rect _goRect;
    private Rect _backRect;
    private Rect _resetRect;
    private float _cachedStyleScale = -1f;
    private GUIStyle _titleStyle;
    private GUIStyle _messageStyle;
    private GUIStyle _statusStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _smallButtonStyle;
    private string _message = "Move your phone slowly to scan the floor, then tap the floor.";

    private void Awake()
    {
        _raycastManager = FindAnyObjectByType<ARRaycastManager>();
        _planeManager = FindAnyObjectByType<ARPlaneManager>();
        _cameraManager = FindAnyObjectByType<ARCameraManager>();
        if (_cameraManager != null)
            _cameraManager.frameReceived += OnCameraFrameReceived;
        _anchorManager = FindAnyObjectByType<ARAnchorManager>();
        _depthDetector = FindAnyObjectByType<ARDepthObstacleDetector>();
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

    private void Update()
    {
        UpdateLayout();

        if (_arena == null)
        {
            ReadPlacementTap();
            return;
        }

        if (ReadControls())
        {
            // RESET was pressed this frame; the same touch must not also place a new arena.
            ResetGame();
            return;
        }

        DriveCar();
        AnimateAndCollectCoins();
    }

    private void ReadPlacementTap()
    {
        for (var i = 0; i < Input.touchCount; i++)
        {
            var touch = Input.GetTouch(i);
            if (touch.phase != TouchPhase.Began) continue;
            if (_hudRect.Contains(ToGuiPoint(touch.position))) continue;
            TryPlaceArena(touch.position);
            return;
        }

#if UNITY_EDITOR
        if (Input.GetMouseButtonDown(0) && !_hudRect.Contains(ToGuiPoint(Input.mousePosition)))
            TryPlaceArena(Input.mousePosition);
#endif
    }

    /// <summary>
    /// Reads every active touch independently so steering and throttle can be held
    /// with two fingers at once (IMGUI buttons only track a single pointer).
    /// Returns true when RESET was tapped.
    /// </summary>
    private bool ReadControls()
    {
        _leftHeld = _rightHeld = _goHeld = _backHeld = false;
        var resetTapped = false;

        for (var i = 0; i < Input.touchCount; i++)
        {
            var touch = Input.GetTouch(i);
            if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                continue;

            var point = ToGuiPoint(touch.position);
            ApplyHeldPoint(point);
            if (touch.phase == TouchPhase.Began && _resetRect.Contains(point))
                resetTapped = true;
        }

#if UNITY_EDITOR
        if (Input.GetMouseButton(0))
            ApplyHeldPoint(ToGuiPoint(Input.mousePosition));
        if (Input.GetMouseButtonDown(0) && _resetRect.Contains(ToGuiPoint(Input.mousePosition)))
            resetTapped = true;
        var keySteer = Input.GetAxisRaw("Horizontal");
        var keyDrive = Input.GetAxisRaw("Vertical");
        if (keySteer < -0.1f) _leftHeld = true;
        if (keySteer > 0.1f) _rightHeld = true;
        if (keyDrive > 0.1f) _goHeld = true;
        if (keyDrive < -0.1f) _backHeld = true;
#endif

        _steerInput = (_rightHeld ? 1f : 0f) - (_leftHeld ? 1f : 0f);
        _driveInput = _goHeld == _backHeld ? 0f : _goHeld ? 1f : -0.65f;
        return resetTapped;
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

        CreateBoundary();
        if (_depthDetector != null)
            _depthDetector.SetArena(_arena);

        var car = ProceduralCarFactory.CreateCar(_arena);
        _car = car.transform;
        _car.localPosition = Vector3.up * 0.01f;

        var cameraTransform = Camera.main != null ? Camera.main.transform : null;
        var forward = cameraTransform != null
            ? Vector3.ProjectOnPlane(cameraTransform.forward, (arenaPose.rotation * Vector3.up))
            : Vector3.forward;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        var worldRotation = Quaternion.LookRotation(forward.normalized, (arenaPose.rotation * Vector3.up));
        _car.localRotation = Quaternion.Inverse(_arena.rotation) * worldRotation;

        SpawnObstacles();
        SpawnCoins();
        CreateGuideRoute();
        _message = _anchor != null
            ? "Track locked. Use arrows to steer, GO forward and BACK to reverse."
            : "Placed without an AR anchor. Avoid moving too far from this spot.";
    }

    private void DriveCar()
    {
        if (_car == null) return;

        var dt = Time.deltaTime;
        var localPosition = _car.localPosition;
        var localForward = _car.localRotation * Vector3.forward;
        var travelDirection = localForward * Mathf.Sign(_driveInput);
        var candidatePosition = localPosition + localForward * (_driveInput * DriveSpeed * dt);
        const float carEdgeMargin = 0.17f;
        candidatePosition.x = Mathf.Clamp(candidatePosition.x, -HalfArena + carEdgeMargin, HalfArena - carEdgeMargin);
        candidatePosition.z = Mathf.Clamp(candidatePosition.z, -HalfArena + carEdgeMargin, HalfArena - carEdgeMargin);

        var blockedByWall = false;
        if (Mathf.Abs(_driveInput) > 0.01f)
        {
            var detectedPlane = IsBlockedByDetectedVerticalSurface(candidatePosition, travelDirection);
            var virtualObstacle = IsBlockedByVirtualObstacle(candidatePosition);
            var candidateWorld = _arena.TransformPoint(candidatePosition);
            var worldTravelDirection = _arena.TransformDirection(travelDirection);
            var detectedDepthObstacle = _depthDetector != null
                && _depthDetector.IsPathBlocked(candidateWorld, worldTravelDirection, 0.19f, 0.12f);
            blockedByWall = detectedPlane || virtualObstacle || detectedDepthObstacle;
        }

        if (!blockedByWall)
        {
            _car.localPosition = candidatePosition;
        }
        else
        {
            _message = "An obstacle was sensed nearby. Turn to drive around it.";
        }

        _car.localRotation = Quaternion.AngleAxis(_steerInput * TurnSpeed * dt, Vector3.up) * _car.localRotation;
    }

    private bool IsBlockedByVirtualObstacle(Vector3 candidatePosition)
    {
        var candidate = new Vector2(candidatePosition.x, candidatePosition.z);
        for (var i = 0; i < _obstaclePositions.Count; i++)
        {
            if (Vector2.Distance(candidate, _obstaclePositions[i]) < 0.17f)
                return true;
        }

        return false;
    }

    private bool IsBlockedByDetectedVerticalSurface(Vector3 candidateCarPosition, Vector3 localTravelDirection)
    {
        if (_planeManager == null || _arena == null) return false;

        // Probe slightly ahead of the car at bumper height. AR planes are geometric
        // observations, so this only guards against flat surfaces the device has tracked.
        var probeLocal = candidateCarPosition + localTravelDirection * 0.19f + Vector3.up * 0.07f;
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

    private void AnimateAndCollectCoins()
    {
        for (var i = _coins.Count - 1; i >= 0; i--)
        {
            var coin = _coins[i];
            if (coin == null)
            {
                _coins.RemoveAt(i);
                continue;
            }

            coin.Rotate(0f, 100f * Time.deltaTime, 0f, Space.Self);
            var basePosition = coin.localPosition;
            basePosition.y = 0.075f + Mathf.Sin(Time.time * 3f + i) * 0.012f;
            coin.localPosition = basePosition;

            var carPosition = _car.localPosition;
            var coinPosition = coin.localPosition;
            carPosition.y = 0f;
            coinPosition.y = 0f;
            if (Vector3.Distance(carPosition, coinPosition) < 0.19f)
            {
                Destroy(coin.gameObject);
                _coins.RemoveAt(i);
                _score++;
                _message = _score == CoinCount ? "All coins collected! Tap RESET to play again." : "Coin collected!";
            }
        }
    }

    private void SpawnObstacles()
    {
        _obstaclePositions.Clear();
        var positions = new[]
        {
            new Vector2(-0.18f, 0.12f),
            new Vector2(0.20f, -0.10f),
            new Vector2(0.43f, 0.40f)
        };
        var colors = new[]
        {
            new Color(0.12f, 0.72f, 0.86f),
            new Color(0.98f, 0.46f, 0.18f),
            new Color(0.43f, 0.78f, 0.28f)
        };

        for (var i = 0; i < positions.Length; i++)
        {
            var position = positions[i];
            _obstaclePositions.Add(position);
            ProceduralCarFactory.CreateObstacle(_arena,
                new Vector3(position.x, 0f, position.y), colors[i]);
        }
    }

    private void SpawnCoins()
    {
        _coins.Clear();
        // A fixed route makes the first run readable and keeps coins away from the walls.
        var route = new[]
        {
            new Vector2(-0.42f, -0.36f), new Vector2(-0.12f, -0.36f),
            new Vector2(0.22f, -0.34f), new Vector2(0.43f, -0.10f),
            new Vector2(0.24f, 0.16f), new Vector2(-0.08f, 0.34f),
            new Vector2(-0.40f, 0.28f), new Vector2(-0.47f, -0.02f)
        };

        for (var i = 0; i < CoinCount; i++)
        {
            var point = route[i];
            var coin = ProceduralCarFactory.CreateCoin(_arena, new Vector3(point.x, 0.075f, point.y));
            _coins.Add(coin.transform);
        }
    }

    private void CreateGuideRoute()
    {
        var shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            return;

        _guideMaterial = new Material(shader)
        {
            color = new Color(0.12f, 0.88f, 0.78f, 0.62f)
        };

        var routeObject = new GameObject("Coin Guide Route");
        routeObject.transform.SetParent(_arena, false);
        var line = routeObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = false;
        line.positionCount = _coins.Count + 1;
        line.startWidth = 0.009f;
        line.endWidth = 0.006f;
        line.numCapVertices = 2;
        line.sharedMaterial = _guideMaterial;
        line.startColor = _guideMaterial.color;
        line.endColor = _guideMaterial.color;

        line.SetPosition(0, new Vector3(0f, 0.014f, 0f));
        for (var i = 0; i < _coins.Count; i++)
        {
            var point = _coins[i].localPosition;
            point.y = 0.014f;
            line.SetPosition(i + 1, point);
        }
    }

    private void CreateBoundary()
    {
        var lineMaterial = new Material(Shader.Find("Standard"));
        lineMaterial.color = new Color(0.18f, 0.82f, 0.72f);
        MakeBoundaryLine("North Boundary", new Vector3(0f, 0.006f, HalfArena), new Vector3(1.5f, 0.008f, 0.012f), lineMaterial);
        MakeBoundaryLine("South Boundary", new Vector3(0f, 0.006f, -HalfArena), new Vector3(1.5f, 0.008f, 0.012f), lineMaterial);
        MakeBoundaryLine("East Boundary", new Vector3(HalfArena, 0.006f, 0f), new Vector3(0.012f, 0.008f, 1.5f), lineMaterial);
        MakeBoundaryLine("West Boundary", new Vector3(-HalfArena, 0.006f, 0f), new Vector3(0.012f, 0.008f, 1.5f), lineMaterial);
    }

    private void MakeBoundaryLine(string objectName, Vector3 position, Vector3 scale, Material material)
    {
        var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
        line.name = objectName;
        line.transform.SetParent(_arena, false);
        line.transform.localPosition = position;
        line.transform.localScale = scale;
        line.GetComponent<Renderer>().sharedMaterial = material;
        line.GetComponent<Collider>().enabled = false;
    }

    private void ResetGame()
    {
        if (_arena != null) Destroy(_arena.gameObject);
        if (_anchor != null) Destroy(_anchor.gameObject);
        _arena = null;
        _anchor = null;
        _car = null;
        _depthDetector?.SetArena(null);
        _coins.Clear();
        _obstaclePositions.Clear();
        _score = 0;
        _steerInput = 0f;
        _driveInput = 0f;
        if (_guideMaterial != null)
        {
            Destroy(_guideMaterial);
            _guideMaterial = null;
        }
        if (_planeManager != null) _planeManager.enabled = true;
        _message = "Move your phone slowly to scan the floor, then tap the floor.";
    }

    private void OnGUI()
    {
        // Only draw here; all input is read in Update via Input.touches (multi-touch).
        if (Event.current.type != EventType.Repaint) return;

        EnsureStyles();
        var scale = _uiScale;
        var margin = 16f * scale;
        var previousGuiColor = GUI.color;
        var previousContentColor = GUI.contentColor;
        GUI.color = new Color(0.025f, 0.075f, 0.11f, 0.88f);
        GUI.Box(_hudRect, "");
        GUI.color = previousGuiColor;
        GUI.contentColor = Color.white;
        GUI.Label(new Rect(margin * 1.8f, margin * 1.35f, Screen.width - margin * 3f, 24f * scale),
            "AR COIN DRIVE  •  COINS " + _score + "/" + CoinCount, _titleStyle);
        GUI.Label(new Rect(margin * 1.8f, margin * 1.35f + 27f * scale, Screen.width - margin * 3f, 32f * scale),
            _message, _messageStyle);

        var planeCount = _planeManager != null ? _planeManager.trackables.count : 0;
        var depthState = _depthDetector == null ? "off"
            : !_depthDetector.DepthSupported ? "not supported"
            : _depthDetector.HasDepthFrame ? "ready" : "waiting";
        var anchorState = _anchor == null ? "none" : _anchor.trackingState.ToString();
        var trackingStatus = "AR: " + ARSession.state + " (" + ARSession.notTrackingReason
            + ")  •  camera frames: " + _cameraFrameCount;
        var featureStatus = "Planes: " + planeCount + "  •  anchor: " + anchorState
            + "  •  depth: " + depthState;
        GUI.Label(new Rect(margin * 1.8f, margin * 1.35f + 64f * scale,
            Screen.width - margin * 3f, 18f * scale), trackingStatus, _statusStyle);
        GUI.Label(new Rect(margin * 1.8f, margin * 1.35f + 82f * scale,
            Screen.width - margin * 3f, 18f * scale), featureStatus, _statusStyle);

        if (_arena == null) return;

        GUI.contentColor = Color.white;
        var steerColor = new Color(0.04f, 0.62f, 0.72f, 0.92f);
        DrawControl(_leftRect, "◀", steerColor, _leftHeld, _buttonStyle);
        DrawControl(_rightRect, "▶", steerColor, _rightHeld, _buttonStyle);
        DrawControl(_goRect, "GO", new Color(0.08f, 0.67f, 0.34f, 0.96f), _goHeld, _buttonStyle);
        DrawControl(_backRect, "BACK", new Color(0.86f, 0.48f, 0.12f, 0.94f), _backHeld, _buttonStyle);
        DrawControl(_resetRect, "RESET", new Color(0.12f, 0.18f, 0.23f, 0.88f), false, _smallButtonStyle);

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
        _titleStyle = MakeLabelStyle(20f * _uiScale, true);
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