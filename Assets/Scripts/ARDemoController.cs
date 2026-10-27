using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// First playable loop: scan and tap a horizontal surface, drive inside a bounded
/// 1.5 m square, and collect coins. Real-room obstacle mapping is intentionally a later step.
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
    private Transform _arena;
    private Transform _car;
    private readonly List<Transform> _coins = new List<Transform>();
    private int _score;
    private float _forwardInput;
    private float _steerInput;
    private string _message = "Move your phone slowly to scan the floor, then tap the floor.";

    private void Awake()
    {
        _raycastManager = FindFirstObjectByType<ARRaycastManager>();
        _planeManager = FindFirstObjectByType<ARPlaneManager>();
    }

    private void Update()
    {
        if (_arena == null)
        {
            ReadPlacementTap();
            return;
        }

        DriveCar();
        AnimateAndCollectCoins();
    }

    private void ReadPlacementTap()
    {
        if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
        {
            TryPlaceArena(Input.GetTouch(0).position);
            return;
        }

#if UNITY_EDITOR
        if (Input.GetMouseButtonDown(0))
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

        var pose = RaycastHits[0].pose;
        _arena = new GameObject("Bounded Play Area").transform;
        _arena.SetPositionAndRotation(pose.position + pose.up * 0.008f, pose.rotation);

        CreateBoundary();
        var car = ProceduralCarFactory.CreateCar(_arena);
        _car = car.transform;
        _car.localPosition = Vector3.up * 0.01f;

        var cameraTransform = Camera.main != null ? Camera.main.transform : null;
        var forward = cameraTransform != null
            ? Vector3.ProjectOnPlane(cameraTransform.forward, pose.up)
            : Vector3.forward;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        var worldRotation = Quaternion.LookRotation(forward.normalized, pose.up);
        _car.localRotation = Quaternion.Inverse(_arena.rotation) * worldRotation;

        SpawnCoins();
        _message = "Drive with the arrows and collect the gold coins.";
        if (_planeManager != null)
            _planeManager.enabled = false;
    }

    private void DriveCar()
    {
        if (_car == null) return;

        var dt = Time.deltaTime;
        var localPosition = _car.localPosition;
        var localForward = _car.localRotation * Vector3.forward;
        localPosition += localForward * (_forwardInput * DriveSpeed * dt);
        localPosition.x = Mathf.Clamp(localPosition.x, -HalfArena + 0.12f, HalfArena - 0.12f);
        localPosition.z = Mathf.Clamp(localPosition.z, -HalfArena + 0.12f, HalfArena - 0.12f);
        _car.localPosition = localPosition;
        _car.localRotation = Quaternion.AngleAxis(_steerInput * TurnSpeed * dt, Vector3.up) * _car.localRotation;
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

    private void SpawnCoins()
    {
        _coins.Clear();
        var random = new System.Random(37);
        for (var i = 0; i < CoinCount; i++)
        {
            var x = Mathf.Lerp(-0.55f, 0.55f, (float)random.NextDouble());
            var z = Mathf.Lerp(-0.55f, 0.55f, (float)random.NextDouble());
            if (Mathf.Abs(x) < 0.18f && Mathf.Abs(z) < 0.18f) z = 0.42f;
            var coin = ProceduralCarFactory.CreateCoin(_arena, new Vector3(x, 0.075f, z));
            _coins.Add(coin.transform);
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
        _arena = null;
        _car = null;
        _coins.Clear();
        _score = 0;
        if (_planeManager != null) _planeManager.enabled = true;
        _message = "Move your phone slowly to scan the floor, then tap the floor.";
    }

    private void OnGUI()
    {
        var scale = Mathf.Clamp(Screen.width / 420f, 0.8f, 1.35f);
        var margin = 16f * scale;
        var topHeight = 74f * scale;
        GUI.Box(new Rect(margin, margin, Screen.width - margin * 2f, topHeight), "");
        GUI.Label(new Rect(margin * 1.8f, margin * 1.35f, Screen.width - margin * 3f, 24f * scale),
            "AR COIN DRIVE  •  COINS " + _score + "/" + CoinCount, MakeLabelStyle(20f * scale, true));
        GUI.Label(new Rect(margin * 1.8f, margin * 1.35f + 27f * scale, Screen.width - margin * 3f, 34f * scale),
            _message, MakeLabelStyle(13f * scale, false));

        if (_arena == null) return;

        var buttonWidth = 82f * scale;
        var buttonHeight = 70f * scale;
        var y = Screen.height - buttonHeight - 22f * scale;
        _steerInput = 0f;
        _forwardInput = 0f;

        if (GUI.RepeatButton(new Rect(margin, y, buttonWidth, buttonHeight), "◀"))
            _steerInput = -1f;
        if (GUI.RepeatButton(new Rect(margin + buttonWidth + 8f * scale, y, buttonWidth, buttonHeight), "▶"))
            _steerInput = 1f;
        if (GUI.RepeatButton(new Rect(Screen.width - buttonWidth - margin, y, buttonWidth, buttonHeight), "GO"))
            _forwardInput = 1f;
        if (GUI.Button(new Rect(Screen.width - buttonWidth - margin, y - buttonHeight - 10f * scale, buttonWidth, 42f * scale), "RESET"))
            ResetGame();
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