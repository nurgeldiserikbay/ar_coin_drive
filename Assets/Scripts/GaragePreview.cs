using UnityEngine;

/// <summary>
/// The garage turntable: the selected car on a podium, rendered by its own camera into a
/// texture that the garage screen shows. Lives on its own layer far from the play area, and the
/// camera only renders while the garage is open, so it costs nothing during play.
/// </summary>
public sealed class GaragePreview
{
    public const int Layer = 30;
    private static readonly Vector3 Origin = new Vector3(0f, -60f, 0f);

    private readonly GameObject _root;
    private readonly Camera _camera;
    private RenderTexture _texture;
    private GameObject _car;
    private int _shownSpec = -1;
    private int _shownColor = -1;
    private int _shownBuild = -1;
    private float _angle = 210f;

    public Texture Texture => _texture;

    public GaragePreview()
    {
        _root = new GameObject("Garage Preview");
        _root.transform.position = Origin;

        // Glossy dark podium with a lighter rim.
        var podium = new GameObject("Podium");
        podium.transform.SetParent(_root.transform, false);
        podium.AddComponent<MeshFilter>().sharedMesh = PodiumMesh();
        var podiumRenderer = podium.AddComponent<MeshRenderer>();
        podiumRenderer.sharedMaterials = new[]
        {
            ProceduralCarFactory.GetMaterial(new Color(0.08f, 0.09f, 0.11f), 0.8f, 0.2f),
            ProceduralCarFactory.GetMaterial(new Color(1f, 0.56f, 0.10f), 0.6f, 0.3f)
        };
        podium.layer = Layer;

        var cameraObject = new GameObject("Garage Camera");
        cameraObject.transform.SetParent(_root.transform, false);
        _camera = cameraObject.AddComponent<Camera>();
        _camera.cullingMask = 1 << Layer;
        _camera.clearFlags = CameraClearFlags.SolidColor;
        // Transparent: the car and podium sit straight on the menu backdrop, with no box around them.
        _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        _camera.fieldOfView = 26f;
        _camera.nearClipPlane = 0.05f;
        _camera.farClipPlane = 5f;
        _camera.enabled = false;
        cameraObject.transform.localPosition = new Vector3(0f, 0.17f, -0.62f);
        cameraObject.transform.LookAt(Origin + new Vector3(0f, 0.025f, 0f));
    }

    /// <summary>Renders the given car into the preview texture for a frame of the given size.</summary>
    public void Show(int specIndex, int colorIndex, Vector2 size, float dt, float dragDegrees) =>
        Show(specIndex, colorIndex, PlayerProfile.BuildOf(specIndex), size, dt, dragDegrees);

    /// <summary>Renders a car with the given upgrades/look code (see PlayerProfile.PackBuild).</summary>
    public void Show(int specIndex, int colorIndex, int build, Vector2 size, float dt, float dragDegrees)
    {
        var width = Mathf.Clamp(Mathf.RoundToInt(size.x), 64, 1024);
        var height = Mathf.Clamp(Mathf.RoundToInt(size.y), 64, 1024);
        if (_texture == null || _texture.width != width || _texture.height != height)
        {
            if (_texture != null) _texture.Release();
            _texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2, name = "Garage Preview" };
            _camera.targetTexture = _texture;
        }

        if (specIndex != _shownSpec || colorIndex != _shownColor || build != _shownBuild)
        {
            if (_car != null)
            {
                // Hide first: Destroy only happens at the end of the frame, after this render.
                _car.SetActive(false);
                Object.Destroy(_car);
            }
            var spec = CarCatalog.Get(specIndex);
            PlayerProfile.UnpackBuild(build, colorIndex, out _, out var look);
            _car = ProceduralCarFactory.CreateCar(_root.transform, spec, GameSettings.CarColors[colorIndex], GameSettings.TrimFor(colorIndex), look);
            foreach (var t in _car.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            foreach (var source in _car.GetComponentsInChildren<AudioSource>()) Object.Destroy(source);
            _shownSpec = specIndex;
            _shownColor = colorIndex;
            _shownBuild = build;
        }

        // Slow turntable; a drag spins it by hand.
        _angle += dragDegrees + (Mathf.Abs(dragDegrees) > 0.01f ? 0f : 18f * dt);
        _car.transform.localRotation = Quaternion.Euler(0f, _angle, 0f);
        _camera.aspect = width / (float)height;
        // Keep at least ~40 degrees across, so the whole car and turntable fit in narrow (portrait)
        // frames instead of the wing and wheels being cut off at the sides.
        var minVertical = 2f * Mathf.Atan(Mathf.Tan(20f * Mathf.Deg2Rad) / _camera.aspect) * Mathf.Rad2Deg;
        _camera.fieldOfView = Mathf.Clamp(minVertical, 26f, 60f);

        // Studio lighting for the shot: the scene's key light comes from above the camera's
        // shoulder while the preview renders, then goes back to lighting the game.
        var key = RenderSettings.sun != null ? RenderSettings.sun : FindKeyLight();
        if (key != null)
        {
            var rotation = key.transform.rotation;
            var intensity = key.intensity;
            var color = key.color;
            key.transform.rotation = Quaternion.LookRotation(new Vector3(0.45f, -0.70f, 0.55f));
            key.intensity = 1.0f;
            key.color = new Color(1f, 0.98f, 0.95f);
            // Bright studio fill so panels facing away from the key light still read.
            var ambient = RenderSettings.ambientProbe;
            var studio = new UnityEngine.Rendering.SphericalHarmonicsL2();
            studio.AddAmbientLight(new Color(0.62f, 0.62f, 0.66f));
            studio.AddDirectionalLight(Vector3.up, new Color(0.35f, 0.35f, 0.38f), 1f);
            RenderSettings.ambientProbe = studio;
            _camera.Render();
            RenderSettings.ambientProbe = ambient;
            key.transform.rotation = rotation;
            key.intensity = intensity;
            key.color = color;
        }
        else
        {
            _camera.Render();
        }
    }

    private static Light FindKeyLight()
    {
        foreach (var light in Object.FindObjectsByType<Light>())
            if (light.type == LightType.Directional) return light;
        return null;
    }

    public void Release()
    {
        if (_texture != null) _texture.Release();
        _texture = null;
    }

    private static Mesh PodiumMesh()
    {
        var builder = new RaceTrack.FlatMeshBuilder(2);
        const int segments = 64;
        for (var i = 0; i < segments; i++)
        {
            var a0 = i / (float)segments * Mathf.PI * 2f;
            var a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
            var p0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
            var p1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            builder.AddTriangle(0, Vector3.zero, p0 * 0.20f, p1 * 0.20f, Vector3.up);
            builder.AddQuad(1, p0 * 0.20f, p0 * 0.212f, p1 * 0.212f, p1 * 0.20f, Vector3.up);
            builder.AddQuad(0, p0 * 0.212f + Vector3.down * 0.02f, p1 * 0.212f + Vector3.down * 0.02f, p1 * 0.212f, p0 * 0.212f, p0);
        }

        return builder.Build("Podium");
    }
}
