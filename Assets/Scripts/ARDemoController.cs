using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Playable loop: scan and tap a horizontal surface, then race the clock to collect every
/// coin inside a bounded 1.5 m square while steering around toy barriers. Each round gets a
/// fresh random layout; the fastest full clear is saved as the best time.
/// Real obstacle sensing is approximate and device-dependent.
/// </summary>
public sealed class ARDemoController : MonoBehaviour
{
    private enum GameState { Placing, Ready, Playing, Won, Lost }
    private enum ControlAction { None, Reset, Again }

    private const float HalfArena = 0.75f;
    private const int CoinCount = 8;
    private const int ObstacleCount = 3;
    private const float RoundDuration = 45f;
    private const string BestTimeKey = "ARCoinDrive.BestTime";

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
    private readonly List<Transform> _coins = new List<Transform>();
    private readonly List<Vector2> _obstaclePositions = new List<Vector2>();
    private readonly List<Vector2> _coinLayout = new List<Vector2>();
    private Material _guideMaterial;

    private GameState _state = GameState.Placing;
    private Vector2 _carPosition;
    private float _carHeading;
    private float _speed;
    private int _score;
    private float _timeLeft;
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

    private void Awake()
    {
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

    private void OnDestroy()
    {
        if (_cameraManager != null)
            _cameraManager.frameReceived -= OnCameraFrameReceived;
        if (_guideMaterial != null)
            Destroy(_guideMaterial);
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
            ResetGame();
            return;
        }

        if (action == ControlAction.Again)
        {
            StartRound();
            return;
        }

        if (_state == GameState.Ready && (Mathf.Abs(_driveInput) > 0.01f || Mathf.Abs(_steerInput) > 0.01f))
        {
            _state = GameState.Playing;
            _message = "Collect all " + CoinCount + " coins before the time runs out!";
        }

        if (_state == GameState.Playing)
        {
            _timeLeft -= Time.deltaTime;
            if (_timeLeft <= 0f)
            {
                _timeLeft = 0f;
                FinishRound(false);
            }
        }

        if (_state != GameState.Playing)
        {
            _driveInput = 0f;
            _steerInput = 0f;
        }

        DriveCar();
        AnimateAndCollectCoins();
    }

    // ---------------------------------------------------------------- placement

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

        StartRound();
        if (_anchor == null)
            _message = "Placed without an AR anchor. Avoid moving too far from this spot. Press GO to start.";
    }

    // ---------------------------------------------------------------- rounds

    private void StartRound()
    {
        if (_roundRoot != null) Destroy(_roundRoot.gameObject);
        _coins.Clear();
        _obstaclePositions.Clear();
        _score = 0;
        _speed = 0f;
        _newBest = false;
        _timeLeft = RoundDuration;
        _hint = null;

        _roundRoot = new GameObject("Round").transform;
        _roundRoot.SetParent(_arena, false);

        _carPosition = Vector2.zero;
        _carHeading = InitialHeadingTowardsCamera();
        _car = ProceduralCarFactory.CreateCar(_roundRoot).transform;
        ApplyCarTransform();

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
            var coin = ProceduralCarFactory.CreateCoin(_roundRoot, new Vector3(point.x, 0.075f, point.y));
            _coins.Add(coin.transform);
        }

        CreateGuideRoute();
        _state = GameState.Ready;
        _message = "Track locked. Press GO to start the " + Mathf.RoundToInt(RoundDuration) + " s timer.";
    }

    private void FinishRound(bool won)
    {
        _state = won ? GameState.Won : GameState.Lost;
        _speed = 0f;
        if (won)
        {
            var clearTime = RoundDuration - _timeLeft;
            _newBest = _bestTime <= 0f || clearTime < _bestTime;
            if (_newBest)
            {
                _bestTime = clearTime;
                PlayerPrefs.SetFloat(BestTimeKey, _bestTime);
                PlayerPrefs.Save();
            }

            _message = (_newBest ? "NEW BEST! " : "All coins collected! ")
                + "Time " + clearTime.ToString("0.0") + " s. Tap AGAIN for a new layout.";
        }
        else
        {
            _message = "Time's up! " + _score + "/" + CoinCount + " coins. Tap AGAIN to retry.";
        }
    }

    private float InitialHeadingTowardsCamera()
    {
        var cameraTransform = Camera.main != null ? Camera.main.transform : null;
        if (cameraTransform == null) return 0f;

        // Face the car away from the player so GO drives into the scene.
        var localForward = _arena.InverseTransformDirection(cameraTransform.forward);
        localForward.y = 0f;
        if (localForward.sqrMagnitude < 0.0001f) return 0f;
        return Mathf.Atan2(localForward.x, localForward.z) * Mathf.Rad2Deg;
    }

    /// <summary>
    /// Random layout with spacing rules that keep the start area clear, leave car-sized gaps
    /// between barriers and keep every coin reachable. Falls back to a known-good layout.
    /// </summary>
    private void GenerateLayout()
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (TryRandomLayout()) return;
        }

        _obstaclePositions.Clear();
        _obstaclePositions.Add(new Vector2(-0.18f, 0.12f));
        _obstaclePositions.Add(new Vector2(0.20f, -0.10f));
        _obstaclePositions.Add(new Vector2(0.43f, 0.40f));
        _coinLayout.Clear();
        _coinLayout.AddRange(new[]
        {
            new Vector2(-0.42f, -0.36f), new Vector2(-0.12f, -0.36f),
            new Vector2(0.22f, -0.34f), new Vector2(0.43f, -0.10f),
            new Vector2(0.24f, 0.16f), new Vector2(-0.08f, 0.34f),
            new Vector2(-0.40f, 0.28f), new Vector2(-0.47f, -0.02f)
        });
        OrderCoinsAsRoute();
    }

    private bool TryRandomLayout()
    {
        _obstaclePositions.Clear();
        _coinLayout.Clear();

        for (var i = 0; i < ObstacleCount; i++)
        {
            if (!TryPlacePoint(0.45f, candidate =>
                    candidate.magnitude >= 0.36f
                    && MinDistance(candidate, _obstaclePositions) >= 0.40f,
                    out var point))
                return false;
            _obstaclePositions.Add(point);
        }

        for (var i = 0; i < CoinCount; i++)
        {
            if (!TryPlacePoint(0.50f, candidate =>
                    candidate.magnitude >= 0.24f
                    && MinDistance(candidate, _obstaclePositions) >= 0.25f
                    && MinDistance(candidate, _coinLayout) >= 0.20f,
                    out var point))
                return false;
            _coinLayout.Add(point);
        }

        OrderCoinsAsRoute();
        return true;
    }

    private static bool TryPlacePoint(float extent, System.Func<Vector2, bool> isValid, out Vector2 point)
    {
        for (var i = 0; i < 60; i++)
        {
            point = new Vector2(Random.Range(-extent, extent), Random.Range(-extent, extent));
            if (isValid(point)) return true;
        }

        point = Vector2.zero;
        return false;
    }

    private static float MinDistance(Vector2 point, List<Vector2> others)
    {
        var min = float.MaxValue;
        for (var i = 0; i < others.Count; i++)
            min = Mathf.Min(min, Vector2.Distance(point, others[i]));
        return min;
    }

    // Nearest-neighbour order from the start gives the guide line a sensible driving route.
    private void OrderCoinsAsRoute()
    {
        var remaining = new List<Vector2>(_coinLayout);
        _coinLayout.Clear();
        var current = Vector2.zero;
        while (remaining.Count > 0)
        {
            var bestIndex = 0;
            for (var i = 1; i < remaining.Count; i++)
            {
                if ((remaining[i] - current).sqrMagnitude < (remaining[bestIndex] - current).sqrMagnitude)
                    bestIndex = i;
            }

            current = remaining[bestIndex];
            _coinLayout.Add(current);
            remaining.RemoveAt(bestIndex);
        }
    }

    // ---------------------------------------------------------------- driving

    private void DriveCar()
    {
        if (_car == null) return;

        var dt = Time.deltaTime;
        var targetSpeed = _driveInput * MaxSpeed;
        float rate;
        if (Mathf.Abs(targetSpeed) < 0.001f)
            rate = RollingFriction;
        else if (Mathf.Abs(_speed) > 0.01f && Mathf.Sign(targetSpeed) != Mathf.Sign(_speed))
            rate = Braking;
        else
            rate = Acceleration;
        _speed = Mathf.MoveTowards(_speed, targetSpeed, rate * dt);

        if (Mathf.Abs(_speed) < 0.0005f)
        {
            _speed = 0f;
            ApplyCarTransform();
            return;
        }

        // Steering authority grows with speed and flips when reversing, like a real car.
        var steerAuthority = Mathf.Clamp01(Mathf.Abs(_speed) / FullSteerSpeed) * Mathf.Sign(_speed);
        var heading = _carHeading + _steerInput * TurnSpeed * steerAuthority * dt;
        var forward = HeadingToForward(heading);
        var position = ClampInsideArena(_carPosition + forward * (_speed * dt), heading);
        var travelDirection = forward * Mathf.Sign(_speed);

        if (IsBlocked(position, heading, travelDirection))
        {
            _speed = 0f;
            ShowHint("Bump! Back up and steer around it.");
        }
        else
        {
            _carPosition = position;
            _carHeading = heading;
        }

        ApplyCarTransform();
    }

    private void ApplyCarTransform()
    {
        _car.localPosition = new Vector3(_carPosition.x, 0.01f, _carPosition.y);
        _car.localRotation = Quaternion.Euler(0f, _carHeading, 0f);
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
                _arena.TransformDirection(localTravel), 0.19f, 0.12f);
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

            if (_state != GameState.Playing) continue;

            // Collect when any part of the car body touches the coin, not just its centre.
            var delta = new Vector2(basePosition.x, basePosition.z) - _carPosition;
            var alongRight = Mathf.Clamp(Vector2.Dot(delta, right),
                -ProceduralCarFactory.CarHalfWidth, ProceduralCarFactory.CarHalfWidth);
            var alongForward = Mathf.Clamp(Vector2.Dot(delta, forward),
                -ProceduralCarFactory.CarHalfLength, ProceduralCarFactory.CarHalfLength);
            var closest = right * alongRight + forward * alongForward;
            if ((delta - closest).magnitude > ProceduralCarFactory.CoinRadius + 0.02f)
                continue;

            Destroy(coin.gameObject);
            _coins.RemoveAt(i);
            _score++;
            if (_score >= CoinCount)
                FinishRound(true);
            else
                ShowHint("Coin! " + (CoinCount - _score) + " to go.");
        }
    }

    // ---------------------------------------------------------------- arena visuals

    private void CreateGuideRoute()
    {
        if (_guideMaterial == null)
        {
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) return;
            _guideMaterial = new Material(shader) { color = new Color(0.12f, 0.88f, 0.78f, 0.62f) };
        }

        var routeObject = new GameObject("Coin Guide Route");
        routeObject.transform.SetParent(_roundRoot, false);
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
        var lineMaterial = ProceduralCarFactory.GetMaterial(new Color(0.18f, 0.82f, 0.72f));
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
        if (material != null)
            line.GetComponent<Renderer>().sharedMaterial = material;
        line.GetComponent<Collider>().enabled = false;
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
        _depthDetector?.SetArena(null);
        _coins.Clear();
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
        var roundOver = _state == GameState.Won || _state == GameState.Lost;

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
            }

            if (!roundOver) ApplyHeldPoint(point);
        }

#if UNITY_EDITOR
        var mousePoint = ToGuiPoint(Input.mousePosition);
        if (Input.GetMouseButtonDown(0))
        {
            if (_resetRect.Contains(mousePoint)) action = ControlAction.Reset;
            else if (roundOver && Expand(_againRect).Contains(mousePoint)) action = ControlAction.Again;
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

        var title = "COINS " + _score + "/" + CoinCount;
        if (_arena != null)
            title += "  •  TIME " + Mathf.CeilToInt(_timeLeft);
        title += "  •  BEST " + (_bestTime > 0f ? _bestTime.ToString("0.0") + " s" : "--");
        GUI.Label(new Rect(textX, margin * 1.35f, textWidth, 24f * scale), title, _titleStyle);

        var message = _hint != null && Time.time < _hintUntil && _state == GameState.Playing ? _hint : _message;
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
        GUI.Label(new Rect(textX, margin * 1.35f + 64f * scale, textWidth, 18f * scale), trackingStatus, _statusStyle);
        GUI.Label(new Rect(textX, margin * 1.35f + 82f * scale, textWidth, 18f * scale), featureStatus, _statusStyle);

        if (_arena == null)
        {
            GUI.color = previousGuiColor;
            GUI.contentColor = previousContentColor;
            return;
        }

        if (_state == GameState.Won || _state == GameState.Lost)
        {
            var againColor = _state == GameState.Won
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
