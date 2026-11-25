using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds an original 1:10-style electric RC buggy from generated meshes: an aluminium chassis
/// plate with arms, gearbox, motor, shock towers and coil-over shocks; knobby off-road tyres on
/// dish rims; and a painted lexan cab-forward shell with side pods, rear wing and a receiver
/// antenna. Meshes are created once and shared between cars; only materials differ.
/// Model units: the car is 0.37 long and 0.23 wide, then scaled by ProceduralCarFactory.CarScale.
/// </summary>
public static class ProceduralCarModel
{
    public const float WheelRadius = 0.046f;
    private const float FrontAxleZ = 0.120f;
    private const float RearAxleZ = -0.115f;
    private const float FrontTyreWidth = 0.036f;
    private const float RearTyreWidth = 0.044f;
    private const float FrontWheelX = 0.115f - FrontTyreWidth * 0.5f;
    private const float RearWheelX = 0.115f - RearTyreWidth * 0.5f;

    // Shell submeshes
    private const int Paint = 0;
    private const int Secondary = 1;
    private const int Window = 2;
    private const int Black = 3;
    private const int Accent = 4;

    // Chassis submeshes
    private const int Aluminium = 0;
    private const int Plastic = 1;
    private const int Gold = 2;
    private const int Spring = 3;
    private const int Steel = 4;
    private const int MotorCan = 5;

    private static Mesh _shellMesh;
    private static Mesh _chassisMesh;
    private static Mesh _frontWheelMesh;
    private static Mesh _rearWheelMesh;
    private static Mesh _shadowMesh;
    private static Material _shadowMaterial;

    public static GameObject Create(Transform parent, Color paint, Color secondary)
    {
        var root = new GameObject("RC Buggy");
        root.transform.SetParent(parent, false);
        CreateContactShadow(root.transform);

        var chassis = new GameObject("Chassis");
        chassis.transform.SetParent(root.transform, false);
        chassis.AddComponent<MeshFilter>().sharedMesh = GetChassisMesh();
        chassis.AddComponent<MeshRenderer>().sharedMaterials = new[]
        {
            ProceduralCarFactory.GetMaterial(new Color(0.42f, 0.44f, 0.48f), 0.62f, 0.85f),
            ProceduralCarFactory.GetMaterial(new Color(0.045f, 0.047f, 0.05f), 0.32f),
            ProceduralCarFactory.GetMaterial(new Color(0.85f, 0.62f, 0.22f), 0.7f, 0.9f),
            ProceduralCarFactory.GetMaterial(new Color(0.78f, 0.10f, 0.08f), 0.55f, 0.3f),
            ProceduralCarFactory.GetMaterial(new Color(0.72f, 0.73f, 0.75f), 0.72f, 0.95f),
            ProceduralCarFactory.GetMaterial(new Color(0.10f, 0.28f, 0.70f), 0.66f, 0.8f)
        };

        // The shell is the sprung part: the rig rolls and pitches it over the chassis.
        var body = new GameObject("Shell");
        body.transform.SetParent(root.transform, false);
        body.AddComponent<MeshFilter>().sharedMesh = GetShellMesh();
        body.AddComponent<MeshRenderer>().sharedMaterials = new[]
        {
            ProceduralCarFactory.GetMaterial(paint, 0.86f, 0.05f),
            ProceduralCarFactory.GetMaterial(secondary, 0.84f, 0.02f),
            ProceduralCarFactory.GetMaterial(new Color(0.03f, 0.035f, 0.045f), 0.92f),
            ProceduralCarFactory.GetMaterial(new Color(0.04f, 0.04f, 0.045f), 0.35f),
            ProceduralCarFactory.GetMaterial(new Color(0.95f, 0.18f, 0.10f), 0.5f)
        };

        var frontLeft = CreateWheel(root.transform, "Front Left Wheel", -1f, FrontWheelX, FrontAxleZ, GetWheelMesh(true), out var spinFrontLeft);
        var frontRight = CreateWheel(root.transform, "Front Right Wheel", 1f, FrontWheelX, FrontAxleZ, GetWheelMesh(true), out var spinFrontRight);
        CreateWheel(root.transform, "Rear Left Wheel", -1f, RearWheelX, RearAxleZ, GetWheelMesh(false), out var spinRearLeft);
        CreateWheel(root.transform, "Rear Right Wheel", 1f, RearWheelX, RearAxleZ, GetWheelMesh(false), out var spinRearRight);

        root.AddComponent<ProceduralCarRig>().Configure(body.transform,
            new[] { frontLeft, frontRight },
            new[] { spinFrontLeft, spinFrontRight, spinRearLeft, spinRearRight },
            WheelRadius * ProceduralCarFactory.CarScale);
        root.transform.localScale = Vector3.one * ProceduralCarFactory.CarScale;
        return root;
    }

    private static Transform CreateWheel(Transform parent, string wheelName, float side, float x, float z,
        Mesh mesh, out Transform spinner)
    {
        var pivot = new GameObject(wheelName).transform;
        pivot.SetParent(parent, false);
        pivot.localPosition = new Vector3(side * x, WheelRadius, z);

        spinner = new GameObject("Spin").transform;
        spinner.SetParent(pivot, false);

        // The wheel mesh's rim faces +X; the left side is turned around so its rim faces outward.
        var visual = new GameObject("Wheel Mesh");
        visual.transform.SetParent(spinner, false);
        if (side < 0f) visual.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        visual.AddComponent<MeshFilter>().sharedMesh = mesh;
        visual.AddComponent<MeshRenderer>().sharedMaterials = new[]
        {
            ProceduralCarFactory.GetMaterial(new Color(0.045f, 0.045f, 0.048f), 0.18f),
            ProceduralCarFactory.GetMaterial(new Color(0.90f, 0.90f, 0.88f), 0.5f)
        };
        return pivot;
    }

    // ------------------------------------------------------------------ chassis

    private static Mesh GetChassisMesh()
    {
        if (_chassisMesh != null) return _chassisMesh;
        var b = new MeshBuilder(6);

        // Chassis plate with raised side rails, front skid and bumpers.
        b.AddBox(Aluminium, new Vector3(0f, 0.022f, -0.005f), new Vector3(0.088f, 0.006f, 0.30f));
        b.AddBox(Aluminium, new Vector3(0f, 0.028f, 0.152f), new Vector3(0.070f, 0.005f, 0.05f),
            Matrix4x4.Rotate(Quaternion.Euler(-18f, 0f, 0f)));
        b.AddBox(Plastic, new Vector3(0f, 0.031f, 0.182f), new Vector3(0.100f, 0.012f, 0.010f));
        b.AddBox(Plastic, new Vector3(0f, 0.037f, 0.172f), new Vector3(0.066f, 0.006f, 0.016f));
        b.AddBox(Plastic, new Vector3(0f, 0.046f, -0.180f), new Vector3(0.096f, 0.022f, 0.014f));

        // Front differential, rear gearbox with spur cover, mid motor, battery and ESC.
        b.AddBox(Plastic, new Vector3(0f, 0.044f, FrontAxleZ), new Vector3(0.050f, 0.032f, 0.042f));
        b.AddBox(Plastic, new Vector3(0f, 0.052f, RearAxleZ), new Vector3(0.064f, 0.050f, 0.052f));
        b.AddCylinder(Plastic, new Vector3(-0.034f, 0.062f, -0.090f), new Vector3(-0.024f, 0.062f, -0.090f), 0.026f, 20);
        b.AddCylinder(MotorCan, new Vector3(0f, 0.050f, -0.083f), new Vector3(0f, 0.050f, -0.030f), 0.0175f, 20);
        b.AddCylinder(Steel, new Vector3(0f, 0.050f, -0.030f), new Vector3(0f, 0.050f, -0.024f), 0.012f, 16);
        b.AddBox(Plastic, new Vector3(0f, 0.039f, 0.040f), new Vector3(0.052f, 0.028f, 0.105f));
        b.AddBox(Aluminium, new Vector3(0f, 0.054f, 0.040f), new Vector3(0.056f, 0.003f, 0.012f));
        b.AddBox(Plastic, new Vector3(0.036f, 0.040f, -0.035f), new Vector3(0.022f, 0.016f, 0.028f));

        foreach (var side in new[] { -1f, 1f })
        {
            AddCorner(b, side, FrontAxleZ, FrontWheelX - FrontTyreWidth * 0.5f, 0.105f, 0.082f, 0.046f);
            AddCorner(b, side, RearAxleZ, RearWheelX - RearTyreWidth * 0.5f, -0.098f, 0.098f, 0.054f);

            // Rear drive shafts from the gearbox to the hubs.
            b.AddCylinder(Steel, new Vector3(side * 0.030f, WheelRadius, RearAxleZ),
                new Vector3(side * (RearWheelX - RearTyreWidth * 0.5f), WheelRadius, RearAxleZ), 0.0042f, 10);
            b.AddCylinder(Steel, new Vector3(side * 0.024f, WheelRadius, FrontAxleZ),
                new Vector3(side * (FrontWheelX - FrontTyreWidth * 0.5f), WheelRadius, FrontAxleZ), 0.0036f, 10);
        }

        // Shock towers (carbon plates).
        b.AddBox(Plastic, new Vector3(0f, 0.066f, 0.104f), new Vector3(0.100f, 0.030f, 0.004f));
        b.AddBox(Plastic, new Vector3(0f, 0.078f, -0.098f), new Vector3(0.116f, 0.040f, 0.004f));

        _chassisMesh = b.Build("RC Buggy Chassis");
        return _chassisMesh;
    }

    /// <summary>Lower and upper arms, hub carrier and a coil-over shock for one corner.</summary>
    private static void AddCorner(MeshBuilder b, float side, float axleZ, float hubX, float towerZ, float towerTop, float towerX)
    {
        var armInner = side * 0.034f;
        b.AddBox(Plastic, new Vector3((armInner + side * hubX) * 0.5f, 0.032f, axleZ),
            new Vector3(Mathf.Abs(side * hubX - armInner), 0.007f, 0.030f));
        b.AddBox(Plastic, new Vector3((armInner + side * hubX) * 0.5f, 0.064f, axleZ),
            new Vector3(Mathf.Abs(side * hubX - armInner) * 0.9f, 0.004f, 0.008f));
        b.AddBox(Plastic, new Vector3(side * (hubX - 0.006f), WheelRadius, axleZ), new Vector3(0.010f, 0.034f, 0.020f));

        var top = new Vector3(side * towerX, towerTop, towerZ);
        var bottom = new Vector3(side * (hubX - 0.016f), 0.036f, axleZ + (towerZ > axleZ ? 0.004f : -0.004f));
        var axis = (bottom - top).normalized;
        b.AddCylinder(Gold, top, top + axis * 0.040f, 0.0080f, 14);
        b.AddCylinder(Steel, top + axis * 0.038f, bottom, 0.0030f, 8);
        b.AddSpring(Spring, top + axis * 0.012f, bottom - axis * 0.004f, 0.0102f, 0.0016f, 9);
        b.AddCylinder(Plastic, top - axis * 0.004f, top + axis * 0.004f, 0.0060f, 10);
    }

    // ------------------------------------------------------------------ shell

    private static Mesh GetShellMesh()
    {
        if (_shellMesh != null) return _shellMesh;
        var b = new MeshBuilder(5);

        // Cab-forward lexan shell: low pointed nose, cockpit hump, tapering over the motor.
        var shell = new[]
        {
            new Station(0.176f, 0.030f, 0.040f, 0.046f),
            new Station(0.160f, 0.046f, 0.036f, 0.058f),
            new Station(0.120f, 0.056f, 0.034f, 0.070f),
            new Station(0.070f, 0.060f, 0.034f, 0.092f),
            new Station(0.025f, 0.061f, 0.034f, 0.114f),
            new Station(-0.015f, 0.059f, 0.034f, 0.116f),
            new Station(-0.050f, 0.054f, 0.040f, 0.102f),
            new Station(-0.080f, 0.044f, 0.050f, 0.082f),
            new Station(-0.092f, 0.036f, 0.056f, 0.070f)
        };
        b.AddLoft(Paint, Resample(shell, 5), 3.4f, 36, Matrix4x4.identity);

        // Tinted windscreen on the front of the cockpit hump, and small side windows.
        var window = new[]
        {
            new Station(0.076f, 0.040f, 0.080f, 0.0905f),
            new Station(0.062f, 0.050f, 0.083f, 0.0995f),
            new Station(0.046f, 0.056f, 0.087f, 0.1075f),
            new Station(0.034f, 0.058f, 0.090f, 0.1120f)
        };
        b.AddLoft(Window, Resample(window, 4), 3.4f, 36, Matrix4x4.identity);
        foreach (var side in new[] { -1f, 1f })
            b.AddEllipsoid(Window, new Vector3(side * 0.0575f, 0.094f, 0.012f), new Vector3(0.0045f, 0.011f, 0.024f));

        // White livery stripe over the nose and roof.
        var stripe = new List<Station>();
        foreach (var s in Resample(shell, 6))
        {
            if (s.Z > 0.170f || s.Z < -0.060f) continue;
            stripe.Add(new Station(s.Z, 0.013f, s.Top - 0.004f, s.Top + 0.0012f));
        }

        b.AddLoft(Secondary, stripe, 3.0f, 14, Matrix4x4.identity);

        foreach (var side in new[] { -1f, 1f })
        {
            // Side pods / nerf bars between the wheels.
            b.AddLoft(Secondary, Resample(new[]
            {
                new Station(0.070f, 0.010f, 0.030f, 0.040f),
                new Station(0.050f, 0.016f, 0.028f, 0.050f),
                new Station(-0.010f, 0.017f, 0.028f, 0.054f),
                new Station(-0.060f, 0.014f, 0.030f, 0.048f),
                new Station(-0.072f, 0.008f, 0.033f, 0.040f)
            }, 4), 3.0f, 18, Matrix4x4.Translate(new Vector3(side * 0.071f, 0f, 0f)));
            // Race-number panel on the shell flank.
            b.AddEllipsoid(Secondary, new Vector3(side * 0.0585f, 0.064f, -0.004f), new Vector3(0.004f, 0.016f, 0.022f));
            b.AddEllipsoid(Accent, new Vector3(side * 0.0605f, 0.064f, -0.004f), new Vector3(0.0028f, 0.010f, 0.012f));
            // Wing endplates and struts.
            b.AddBox(Secondary, new Vector3(side * 0.088f, 0.132f, -0.170f), new Vector3(0.003f, 0.034f, 0.070f));
            b.AddBox(Black, new Vector3(side * 0.028f, 0.106f, -0.150f), new Vector3(0.004f, 0.046f, 0.018f),
                Matrix4x4.Rotate(Quaternion.Euler(-12f, 0f, 0f)));
        }

        // Rear wing with a gurney lip.
        var wingTilt = Matrix4x4.TRS(new Vector3(0f, 0.136f, -0.168f), Quaternion.Euler(-9f, 0f, 0f), Vector3.one);
        b.AddBox(Secondary, Vector3.zero, new Vector3(0.176f, 0.004f, 0.070f), wingTilt);
        b.AddBox(Paint, new Vector3(0f, 0.006f, -0.034f), new Vector3(0.176f, 0.010f, 0.003f), wingTilt);

        // Receiver antenna tube with a cap.
        b.AddCylinder(Black, new Vector3(0.036f, 0.060f, 0.004f), new Vector3(0.038f, 0.205f, -0.006f), 0.0022f, 8);
        b.AddEllipsoid(Accent, new Vector3(0.038f, 0.206f, -0.006f), new Vector3(0.0042f, 0.0050f, 0.0042f));

        // Body posts and clips poking through the shell.
        foreach (var z in new[] { 0.132f, -0.062f })
            b.AddCylinder(Black, new Vector3(0f, 0.040f, z), new Vector3(0f, z > 0f ? 0.076f : 0.106f, z), 0.0030f, 8);

        _shellMesh = b.Build("RC Buggy Shell");
        return _shellMesh;
    }

    // ------------------------------------------------------------------ wheels

    private static Mesh GetWheelMesh(bool front)
    {
        if (front && _frontWheelMesh != null) return _frontWheelMesh;
        if (!front && _rearWheelMesh != null) return _rearWheelMesh;

        const int tyre = 0;
        const int rim = 1;
        var width = front ? FrontTyreWidth : RearTyreWidth;
        var half = width * 0.5f;
        var b = new MeshBuilder(2);

        // Knobby off-road tread: two staggered rows of blocks plus shoulder knobs.
        b.AddTyre(tyre, half, WheelRadius, WheelRadius * 0.66f, 0.0034f, 72);

        // Dish rim: barrel, face with six spokes, and a hex hub nut.
        var rimRadius = WheelRadius * 0.64f;
        b.AddLathe(rim, new[]
        {
            new Vector2(-half + 0.003f, rimRadius), new Vector2(half - 0.002f, rimRadius),
            new Vector2(half - 0.001f, rimRadius * 0.92f), new Vector2(half - 0.006f, rimRadius * 0.90f)
        }, 32, Matrix4x4.identity);
        b.AddLathe(rim, new[]
        {
            new Vector2(half - 0.005f, rimRadius * 0.92f), new Vector2(half - 0.005f, rimRadius * 0.78f),
            new Vector2(half - 0.007f, rimRadius * 0.76f)
        }, 32, Matrix4x4.identity);
        for (var i = 0; i < 6; i++)
        {
            var matrix = Matrix4x4.Rotate(Quaternion.AngleAxis(60f * i, Vector3.right));
            b.AddBox(rim, new Vector3(half - 0.008f, rimRadius * 0.48f, 0f), new Vector3(0.006f, rimRadius * 0.62f, 0.0055f), matrix);
        }

        b.AddLathe(rim, new[]
        {
            new Vector2(half - 0.012f, 0.0001f), new Vector2(half - 0.012f, 0.0105f),
            new Vector2(half - 0.004f, 0.0098f), new Vector2(half - 0.003f, 0.0001f)
        }, 16, Matrix4x4.identity);
        b.AddLathe(tyre, new[]
        {
            new Vector2(half - 0.004f, 0.0001f), new Vector2(half - 0.004f, 0.0052f),
            new Vector2(half + 0.0008f, 0.0052f), new Vector2(half + 0.0008f, 0.0001f)
        }, 6, Matrix4x4.identity);

        var mesh = b.Build(front ? "RC Front Wheel" : "RC Rear Wheel");
        if (front) _frontWheelMesh = mesh;
        else _rearWheelMesh = mesh;
        return mesh;
    }

    // ------------------------------------------------------------------ contact shadow

    /// <summary>Soft ambient-occlusion blob that seats the car on the floor under the real shadow.</summary>
    private static void CreateContactShadow(Transform parent)
    {
        var material = GetShadowMaterial();
        if (material == null) return;

        var shadow = new GameObject("Contact Shadow");
        shadow.transform.SetParent(parent, false);
        shadow.transform.localPosition = Vector3.up * 0.0015f;
        shadow.AddComponent<MeshFilter>().sharedMesh = GetShadowMesh();
        var shadowRenderer = shadow.AddComponent<MeshRenderer>();
        shadowRenderer.sharedMaterial = material;
        shadowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        shadowRenderer.receiveShadows = false;
    }

    private static Mesh GetShadowMesh()
    {
        if (_shadowMesh != null) return _shadowMesh;

        const float halfWidth = 0.15f;
        const float halfLength = 0.23f;
        _shadowMesh = new Mesh { name = "RC Buggy Contact Shadow" };
        _shadowMesh.SetVertices(new[]
        {
            new Vector3(-halfWidth, 0f, -halfLength), new Vector3(-halfWidth, 0f, halfLength),
            new Vector3(halfWidth, 0f, halfLength), new Vector3(halfWidth, 0f, -halfLength)
        });
        _shadowMesh.SetUVs(0, new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right });
        _shadowMesh.SetColors(new[] { Color.white, Color.white, Color.white, Color.white });
        _shadowMesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
        _shadowMesh.RecalculateNormals();
        _shadowMesh.RecalculateBounds();
        return _shadowMesh;
    }

    private static Material GetShadowMaterial()
    {
        if (_shadowMaterial != null) return _shadowMaterial;
        var shader = Shader.Find("AR Coin Drive/Overlay");
        if (shader == null) return null;
        _shadowMaterial = new Material(shader)
        {
            name = "RC Buggy Contact Shadow",
            mainTexture = ProceduralTextures.SoftDot,
            color = new Color(0f, 0f, 0f, 0.55f)
        };
        return _shadowMaterial;
    }

    // ------------------------------------------------------------------ helpers

    private static List<Station> Resample(IReadOnlyList<Station> stations, int stepsPerSpan)
    {
        var result = new List<Station>();
        for (var i = 0; i < stations.Count - 1; i++)
        {
            var p0 = stations[Mathf.Max(i - 1, 0)];
            var p1 = stations[i];
            var p2 = stations[i + 1];
            var p3 = stations[Mathf.Min(i + 2, stations.Count - 1)];
            for (var step = 0; step < stepsPerSpan; step++)
            {
                var t = step / (float)stepsPerSpan;
                result.Add(new Station(
                    Mathf.Lerp(p1.Z, p2.Z, t),
                    CatmullRom(p0.HalfWidth, p1.HalfWidth, p2.HalfWidth, p3.HalfWidth, t),
                    CatmullRom(p0.Bottom, p1.Bottom, p2.Bottom, p3.Bottom, t),
                    CatmullRom(p0.Top, p1.Top, p2.Top, p3.Top, t)));
            }
        }

        result.Add(stations[stations.Count - 1]);
        return result;
    }

    private static float CatmullRom(float a, float b, float c, float d, float t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        return 0.5f * (2f * b + (c - a) * t + (2f * a - 5f * b + 4f * c - d) * t2 + (3f * b - a - 3f * c + d) * t3);
    }

    internal readonly struct Station
    {
        public readonly float Z;
        public readonly float HalfWidth;
        public readonly float Bottom;
        public readonly float Top;

        public Station(float z, float halfWidth, float bottom, float top)
        {
            Z = z;
            HalfWidth = halfWidth;
            Bottom = bottom;
            Top = top;
        }
    }

    /// <summary>Accumulates generated geometry into one multi-material mesh.</summary>
    internal sealed class MeshBuilder
    {
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<int>[] _triangles;

        public MeshBuilder(int submeshCount)
        {
            _triangles = new List<int>[submeshCount];
            for (var i = 0; i < submeshCount; i++)
                _triangles[i] = new List<int>();
        }

        /// <summary>Superellipse cross-sections (exponent 2 = ellipse, higher = boxier) lofted along Z.</summary>
        public void AddLoft(int submesh, IReadOnlyList<Station> stations, float exponent, int segments, Matrix4x4 matrix)
        {
            var start = _vertices.Count;
            var power = 2f / exponent;
            for (var i = 0; i < stations.Count; i++)
            {
                var station = stations[i];
                var centerY = (station.Top + station.Bottom) * 0.5f;
                var halfHeight = Mathf.Max(0.0001f, (station.Top - station.Bottom) * 0.5f);
                for (var j = 0; j < segments; j++)
                {
                    var angle = j / (float)segments * Mathf.PI * 2f;
                    var x = Signed(Mathf.Cos(angle), power) * station.HalfWidth;
                    var y = centerY + Signed(Mathf.Sin(angle), power) * halfHeight;
                    _vertices.Add(matrix.MultiplyPoint3x4(new Vector3(x, y, station.Z)));
                }
            }

            for (var i = 0; i < stations.Count - 1; i++)
                AddRingStrip(submesh, start + i * segments, start + (i + 1) * segments, segments);

            AddCap(submesh, start, segments, false);
            AddCap(submesh, start + (stations.Count - 1) * segments, segments, true);
        }

        /// <summary>Profile (x along the axis, y radius) revolved around local X.</summary>
        public void AddLathe(int submesh, IReadOnlyList<Vector2> profile, int segments, Matrix4x4 matrix)
        {
            var start = _vertices.Count;
            for (var i = 0; i < profile.Count; i++)
            {
                for (var j = 0; j < segments; j++)
                {
                    var angle = j / (float)segments * Mathf.PI * 2f;
                    var point = new Vector3(profile[i].x, Mathf.Cos(angle) * profile[i].y, Mathf.Sin(angle) * profile[i].y);
                    _vertices.Add(matrix.MultiplyPoint3x4(point));
                }
            }

            for (var i = 0; i < profile.Count - 1; i++)
                AddRingStrip(submesh, start + i * segments, start + (i + 1) * segments, segments);
        }

        /// <summary>Closed cylinder between two points.</summary>
        public void AddCylinder(int submesh, Vector3 from, Vector3 to, float radius, int segments)
        {
            var length = Vector3.Distance(from, to);
            if (length < 0.00001f) return;
            var matrix = Matrix4x4.TRS(from, Quaternion.FromToRotation(Vector3.right, (to - from) / length), Vector3.one);
            AddLathe(submesh, new[]
            {
                new Vector2(0f, 0.0001f), new Vector2(0f, radius), new Vector2(length, radius), new Vector2(length, 0.0001f)
            }, segments, matrix);
        }

        /// <summary>Coil spring look: a ribbed tube whose radius ripples once per coil.</summary>
        public void AddSpring(int submesh, Vector3 from, Vector3 to, float radius, float wire, int coils)
        {
            var length = Vector3.Distance(from, to);
            if (length < 0.00001f) return;
            var matrix = Matrix4x4.TRS(from, Quaternion.FromToRotation(Vector3.right, (to - from) / length), Vector3.one);
            var profile = new List<Vector2> { new Vector2(0f, radius - wire) };
            var steps = coils * 4;
            for (var i = 0; i <= steps; i++)
            {
                var t = i / (float)steps;
                var ripple = Mathf.Cos(t * coils * Mathf.PI * 2f) * 0.5f + 0.5f;
                profile.Add(new Vector2(t * length, radius - wire + ripple * wire * 2f));
            }

            profile.Add(new Vector2(length, radius - wire));
            AddLathe(submesh, profile, 14, matrix);
        }

        /// <summary>
        /// Off-road tyre around local X: rounded carcass from the bead to the tread, with raised
        /// knob blocks in two staggered rows across the tread.
        /// </summary>
        public void AddTyre(int submesh, float halfWidth, float radius, float beadRadius, float knob, int segments)
        {
            var profile = new[]
            {
                new Vector2(-halfWidth + 0.002f, beadRadius), new Vector2(-halfWidth, beadRadius + (radius - beadRadius) * 0.55f),
                new Vector2(-halfWidth * 0.92f, radius - knob * 0.9f), new Vector2(-halfWidth * 0.55f, radius - knob),
                new Vector2(-halfWidth * 0.08f, radius - knob), new Vector2(halfWidth * 0.08f, radius - knob),
                new Vector2(halfWidth * 0.55f, radius - knob), new Vector2(halfWidth * 0.92f, radius - knob * 0.9f),
                new Vector2(halfWidth, beadRadius + (radius - beadRadius) * 0.55f), new Vector2(halfWidth - 0.002f, beadRadius)
            };
            // Tread profile points that carry knobs: 0 = none, 1 = left row, 2 = right row, 3 = shoulder.
            int[] row = { 0, 0, 3, 1, 1, 2, 2, 3, 0, 0 };

            var start = _vertices.Count;
            for (var i = 0; i < profile.Length; i++)
            {
                for (var j = 0; j < segments; j++)
                {
                    var block = j / 2;
                    var raised = row[i] == 1 ? block % 2 == 0
                        : row[i] == 2 ? block % 2 == 1
                        : row[i] == 3 && block % 3 == 0;
                    var r = profile[i].y + (raised ? knob : 0f);
                    var angle = j / (float)segments * Mathf.PI * 2f;
                    _vertices.Add(new Vector3(profile[i].x, Mathf.Cos(angle) * r, Mathf.Sin(angle) * r));
                }
            }

            for (var i = 0; i < profile.Length - 1; i++)
                AddRingStrip(submesh, start + i * segments, start + (i + 1) * segments, segments);
        }

        public void AddEllipsoid(int submesh, Vector3 center, Vector3 radii)
        {
            const int rings = 10;
            var stations = new List<Station>(rings + 1);
            for (var i = 0; i <= rings; i++)
            {
                var phi = Mathf.PI * (1f - i / (float)rings);
                var scale = Mathf.Max(0.02f, Mathf.Sin(phi));
                stations.Add(new Station(center.z + Mathf.Cos(phi) * radii.z, radii.x * scale,
                    center.y - radii.y * scale, center.y + radii.y * scale));
            }

            AddLoft(submesh, stations, 2f, 16, Matrix4x4.Translate(new Vector3(center.x, 0f, 0f)));
        }

        public void AddBox(int submesh, Vector3 center, Vector3 size) =>
            AddBox(submesh, center, size, Matrix4x4.identity);

        public void AddBox(int submesh, Vector3 center, Vector3 size, Matrix4x4 matrix)
        {
            var half = size * 0.5f;
            AddFace(submesh, center, half, Vector3.right, Vector3.up, Vector3.forward, matrix);
            AddFace(submesh, center, half, Vector3.left, Vector3.forward, Vector3.up, matrix);
            AddFace(submesh, center, half, Vector3.up, Vector3.forward, Vector3.right, matrix);
            AddFace(submesh, center, half, Vector3.down, Vector3.right, Vector3.forward, matrix);
            AddFace(submesh, center, half, Vector3.forward, Vector3.right, Vector3.up, matrix);
            AddFace(submesh, center, half, Vector3.back, Vector3.up, Vector3.right, matrix);
        }

        private void AddFace(int submesh, Vector3 center, Vector3 half, Vector3 normal, Vector3 u, Vector3 v, Matrix4x4 matrix)
        {
            var faceCenter = center + Vector3.Scale(normal, half);
            var du = Vector3.Scale(u, half);
            var dv = Vector3.Scale(v, half);
            var a = faceCenter - du - dv;
            var b = faceCenter - du + dv;
            var c = faceCenter + du + dv;
            var d = faceCenter + du - dv;

            if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f)
            {
                var swap = b;
                b = d;
                d = swap;
            }

            var start = _vertices.Count;
            _vertices.Add(matrix.MultiplyPoint3x4(a));
            _vertices.Add(matrix.MultiplyPoint3x4(b));
            _vertices.Add(matrix.MultiplyPoint3x4(c));
            _vertices.Add(matrix.MultiplyPoint3x4(d));
            var triangles = _triangles[submesh];
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        private void AddRingStrip(int submesh, int ringA, int ringB, int segments)
        {
            var triangles = _triangles[submesh];
            for (var j = 0; j < segments; j++)
            {
                var next = (j + 1) % segments;
                triangles.Add(ringA + j);
                triangles.Add(ringA + next);
                triangles.Add(ringB + next);
                triangles.Add(ringA + j);
                triangles.Add(ringB + next);
                triangles.Add(ringB + j);
            }
        }

        private void AddCap(int submesh, int ring, int segments, bool facesForward)
        {
            var start = _vertices.Count;
            var center = Vector3.zero;
            for (var j = 0; j < segments; j++)
            {
                var vertex = _vertices[ring + j];
                _vertices.Add(vertex);
                center += vertex;
            }

            var centerIndex = _vertices.Count;
            _vertices.Add(center / segments);
            var triangles = _triangles[submesh];
            for (var j = 0; j < segments; j++)
            {
                var next = (j + 1) % segments;
                triangles.Add(centerIndex);
                triangles.Add(start + (facesForward ? j : next));
                triangles.Add(start + (facesForward ? next : j));
            }
        }

        public Mesh Build(string meshName)
        {
            var mesh = new Mesh { name = meshName };
            mesh.indexFormat = _vertices.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(_vertices);
            mesh.subMeshCount = _triangles.Length;
            for (var i = 0; i < _triangles.Length; i++)
                mesh.SetTriangles(_triangles[i], i);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        private static float Signed(float value, float power) =>
            Mathf.Sign(value) * Mathf.Pow(Mathf.Abs(value), power);
    }
}
