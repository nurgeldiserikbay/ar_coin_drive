using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds an original stylized toy sports car from generated meshes. Meshes are created
/// once and shared, so starting new rounds adds no geometry and the car costs two meshes.
/// </summary>
public static class ProceduralCarModel
{
    public const float WheelRadius = 0.038f;
    private const float WheelCenterX = 0.099f;
    private const float WheelCenterZ = 0.105f;

    private const int Paint = 0;
    private const int Dark = 1;
    private const int Glass = 2;
    private const int Headlight = 3;
    private const int Taillight = 4;
    private const int Chrome = 5;
    private const int Stripe = 6;

    private static readonly Station[] BodyStations =
    {
        new Station(-0.177f, 0.046f, 0.060f, 0.084f),
        new Station(-0.172f, 0.074f, 0.048f, 0.097f),
        new Station(-0.160f, 0.090f, 0.039f, 0.107f),
        new Station(-0.140f, 0.097f, 0.034f, 0.112f),
        new Station(-0.100f, 0.101f, 0.032f, 0.115f),
        new Station(-0.040f, 0.098f, 0.032f, 0.110f),
        new Station(0.030f, 0.098f, 0.032f, 0.104f),
        new Station(0.090f, 0.101f, 0.032f, 0.095f),
        new Station(0.130f, 0.097f, 0.034f, 0.085f),
        new Station(0.160f, 0.086f, 0.038f, 0.073f),
        new Station(0.175f, 0.064f, 0.046f, 0.060f)
    };

    private static readonly Station[] CabinStations =
    {
        new Station(-0.098f, 0.058f, 0.098f, 0.107f),
        new Station(-0.076f, 0.075f, 0.098f, 0.140f),
        new Station(-0.045f, 0.079f, 0.098f, 0.154f),
        new Station(0.000f, 0.079f, 0.098f, 0.157f),
        new Station(0.035f, 0.077f, 0.098f, 0.146f),
        new Station(0.070f, 0.070f, 0.094f, 0.118f),
        new Station(0.094f, 0.056f, 0.090f, 0.097f)
    };

    private static readonly Station[] RoofStations =
    {
        new Station(-0.058f, 0.030f, 0.146f, 0.150f),
        new Station(-0.048f, 0.052f, 0.148f, 0.1575f),
        new Station(-0.010f, 0.058f, 0.150f, 0.1602f),
        new Station(0.016f, 0.054f, 0.147f, 0.1555f),
        new Station(0.028f, 0.032f, 0.144f, 0.149f)
    };

    private static Mesh _bodyMesh;
    private static Mesh _wheelMesh;
    private static Mesh _shadowMesh;
    private static Texture2D _shadowTexture;
    private static Material _shadowMaterial;

    public static GameObject Create(Transform parent, Color paint)
    {
        var root = new GameObject("Player Car");
        root.transform.SetParent(parent, false);
        CreateContactShadow(root.transform);

        var body = new GameObject("Body");
        body.transform.SetParent(root.transform, false);
        body.AddComponent<MeshFilter>().sharedMesh = GetBodyMesh();
        body.AddComponent<MeshRenderer>().sharedMaterials = new[]
        {
            ProceduralCarFactory.GetMaterial(paint, 0.82f, 0.12f),
            ProceduralCarFactory.GetMaterial(new Color(0.035f, 0.04f, 0.05f), 0.38f),
            ProceduralCarFactory.GetMaterial(new Color(0.05f, 0.13f, 0.20f), 0.94f, 0.25f),
            ProceduralCarFactory.GetMaterial(new Color(1.00f, 0.95f, 0.72f), 0.9f),
            ProceduralCarFactory.GetMaterial(new Color(0.95f, 0.05f, 0.08f), 0.86f),
            ProceduralCarFactory.GetMaterial(new Color(0.78f, 0.80f, 0.83f), 0.78f, 0.45f),
            ProceduralCarFactory.GetMaterial(new Color(0.97f, 0.95f, 0.90f), 0.82f, 0.05f)
        };

        var frontLeft = CreateWheel(root.transform, "Front Left Wheel", -1f, WheelCenterZ, out var spinFrontLeft);
        var frontRight = CreateWheel(root.transform, "Front Right Wheel", 1f, WheelCenterZ, out var spinFrontRight);
        CreateWheel(root.transform, "Rear Left Wheel", -1f, -WheelCenterZ, out var spinRearLeft);
        CreateWheel(root.transform, "Rear Right Wheel", 1f, -WheelCenterZ, out var spinRearRight);

        root.AddComponent<ProceduralCarRig>().Configure(body.transform,
            new[] { frontLeft, frontRight },
            new[] { spinFrontLeft, spinFrontRight, spinRearLeft, spinRearRight },
            WheelRadius * ProceduralCarFactory.CarScale);
        root.transform.localScale = Vector3.one * ProceduralCarFactory.CarScale;
        return root;
    }

    private static Transform CreateWheel(Transform parent, string wheelName, float side, float z, out Transform spinner)
    {
        var pivot = new GameObject(wheelName).transform;
        pivot.SetParent(parent, false);
        pivot.localPosition = new Vector3(side * WheelCenterX, WheelRadius, z);

        spinner = new GameObject("Spin").transform;
        spinner.SetParent(pivot, false);

        // The wheel mesh faces +X; the left side is turned around so its rim faces outward.
        var visual = new GameObject("Wheel Mesh");
        visual.transform.SetParent(spinner, false);
        if (side < 0f) visual.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        visual.AddComponent<MeshFilter>().sharedMesh = GetWheelMesh();
        visual.AddComponent<MeshRenderer>().sharedMaterials = new[]
        {
            ProceduralCarFactory.GetMaterial(new Color(0.025f, 0.027f, 0.03f), 0.22f),
            ProceduralCarFactory.GetMaterial(new Color(0.82f, 0.84f, 0.87f), 0.8f, 0.5f)
        };
        return pivot;
    }

    private static Mesh GetBodyMesh()
    {
        if (_bodyMesh != null) return _bodyMesh;

        var builder = new MeshBuilder(7);
        builder.AddLoft(Paint, Resample(BodyStations, 5), 3.2f, 32, Matrix4x4.identity);
        builder.AddLoft(Glass, Resample(CabinStations, 5), 3.4f, 28, Matrix4x4.identity);
        builder.AddLoft(Paint, Resample(RoofStations, 4), 2.6f, 24, Matrix4x4.identity);

        AddCenterStripes(builder);
        builder.AddBox(Dark, new Vector3(0f, 0.031f, 0f), new Vector3(0.17f, 0.012f, 0.30f));

        foreach (var side in new[] { -1f, 1f })
        {
            AddWheelArch(builder, side, WheelCenterZ);
            AddWheelArch(builder, side, -WheelCenterZ);
            builder.AddEllipsoid(Chrome, new Vector3(side * 0.058f, 0.0690f, 0.1460f), new Vector3(0.0245f, 0.0124f, 0.0175f));
            builder.AddEllipsoid(Headlight, new Vector3(side * 0.058f, 0.0695f, 0.1550f), new Vector3(0.0222f, 0.0110f, 0.0175f));
            builder.AddEllipsoid(Taillight, new Vector3(side * 0.062f, 0.091f, -0.166f), new Vector3(0.020f, 0.011f, 0.008f));
            builder.AddEllipsoid(Paint, new Vector3(side * 0.091f, 0.112f, 0.058f), new Vector3(0.014f, 0.007f, 0.010f));
            builder.AddBox(Dark, new Vector3(side * 0.101f, 0.035f, 0f), new Vector3(0.006f, 0.008f, 0.118f));
            builder.AddEllipsoid(Chrome, new Vector3(side * 0.034f, 0.047f, -0.170f), new Vector3(0.009f, 0.006f, 0.008f));
        }

        // Radiator grille with a thin chrome surround and slat.
        builder.AddEllipsoid(Chrome, new Vector3(0f, 0.0525f, 0.1690f), new Vector3(0.0375f, 0.0088f, 0.0072f));
        builder.AddEllipsoid(Dark, new Vector3(0f, 0.0525f, 0.1712f), new Vector3(0.0340f, 0.0068f, 0.0060f));
        builder.AddEllipsoid(Chrome, new Vector3(0f, 0.0525f, 0.1764f), new Vector3(0.0280f, 0.0011f, 0.0013f));

        builder.AddEllipsoid(Dark, new Vector3(0f, 0.036f, 0.158f), new Vector3(0.086f, 0.008f, 0.020f));
        builder.AddEllipsoid(Dark, new Vector3(0f, 0.042f, -0.160f), new Vector3(0.086f, 0.010f, 0.016f));
        builder.AddEllipsoid(Paint, new Vector3(0f, 0.111f, -0.153f), new Vector3(0.082f, 0.009f, 0.025f));

        _bodyMesh = builder.Build("Toy Car Body");
        return _bodyMesh;
    }

    private static void AddCenterStripes(MeshBuilder builder)
    {
        foreach (var offset in new[] { -0.011f, 0.011f })
        {
            var matrix = Matrix4x4.Translate(new Vector3(offset, 0f, 0f));
            builder.AddLoft(Stripe, StripeStations(0.096f, 0.150f), 2.4f, 12, matrix);
            builder.AddLoft(Stripe, new List<Station>
            {
                new Station(-0.040f, 0.0062f, 0.150f, 0.1590f),
                new Station(-0.010f, 0.0062f, 0.152f, 0.1612f),
                new Station(0.012f, 0.0062f, 0.150f, 0.1572f)
            }, 2.4f, 12, matrix);
        }
    }

    private static List<Station> StripeStations(float fromZ, float toZ)
    {
        var dense = Resample(BodyStations, 8);
        var result = new List<Station>();
        foreach (var station in dense)
        {
            if (station.Z < fromZ || station.Z > toZ) continue;
            result.Add(new Station(station.Z, 0.0062f, station.Top - 0.006f, station.Top + 0.0012f));
        }

        return result;
    }

    private static void AddWheelArch(MeshBuilder builder, float side, float z)
    {
        // Only the upper arc: a painted fender flare around a dark wheel well.
        var center = new Vector3(side * 0.1005f, WheelRadius + 0.002f, z);
        var matrix = Matrix4x4.Translate(center);
        var mirrored = side < 0f ? Matrix4x4.Rotate(Quaternion.Euler(0f, 180f, 0f)) : Matrix4x4.identity;
        builder.AddLatheArc(Dark, new[]
        {
            new Vector2(0.0035f, 0.0435f), new Vector2(0.0035f, 0f)
        }, 20, 66f, matrix * mirrored);
        builder.AddLatheArc(Paint, new[]
        {
            new Vector2(-0.024f, 0.0505f), new Vector2(0.004f, 0.0505f), new Vector2(0.0075f, 0.0475f),
            new Vector2(0.0075f, 0.0435f), new Vector2(-0.024f, 0.0435f)
        }, 20, 66f, matrix * mirrored);
    }

    private static Mesh GetWheelMesh()
    {
        if (_wheelMesh != null) return _wheelMesh;

        const int tire = 0;
        const int rim = 1;
        var builder = new MeshBuilder(2);
        builder.AddLathe(tire, new[]
        {
            new Vector2(-0.019f, 0f), new Vector2(-0.019f, 0.030f), new Vector2(-0.017f, 0.0352f),
            new Vector2(-0.012f, 0.038f), new Vector2(0.012f, 0.038f), new Vector2(0.017f, 0.0352f),
            new Vector2(0.019f, 0.030f), new Vector2(0.019f, 0.026f), new Vector2(0.012f, 0.024f),
            new Vector2(0.012f, 0f)
        }, 32, Matrix4x4.identity);
        builder.AddLathe(rim, new[]
        {
            new Vector2(0.012f, 0.0245f), new Vector2(0.0185f, 0.0245f),
            new Vector2(0.0185f, 0.0205f), new Vector2(0.012f, 0.0205f)
        }, 32, Matrix4x4.identity);
        builder.AddLathe(rim, new[]
        {
            new Vector2(0.012f, 0.0075f), new Vector2(0.018f, 0.0075f),
            new Vector2(0.019f, 0.005f), new Vector2(0.019f, 0f)
        }, 18, Matrix4x4.identity);

        for (var i = 0; i < 5; i++)
        {
            var matrix = Matrix4x4.Rotate(Quaternion.AngleAxis(72f * i, Vector3.right));
            builder.AddBox(rim, new Vector3(0.0155f, 0.0138f, 0f), new Vector3(0.004f, 0.0175f, 0.0062f), matrix);
        }

        _wheelMesh = builder.Build("Toy Car Wheel");
        return _wheelMesh;
    }

    /// <summary>A soft blob under the car is what visually seats it on the real floor in AR.</summary>
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

        const float halfWidth = 0.165f;
        const float halfLength = 0.245f;
        _shadowMesh = new Mesh { name = "Toy Car Contact Shadow" };
        _shadowMesh.SetVertices(new[]
        {
            new Vector3(-halfWidth, 0f, -halfLength), new Vector3(-halfWidth, 0f, halfLength),
            new Vector3(halfWidth, 0f, halfLength), new Vector3(halfWidth, 0f, -halfLength)
        });
        _shadowMesh.SetUVs(0, new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right });
        _shadowMesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
        _shadowMesh.RecalculateNormals();
        _shadowMesh.RecalculateBounds();
        return _shadowMesh;
    }

    private static Material GetShadowMaterial()
    {
        if (_shadowMaterial != null) return _shadowMaterial;

        var shader = Shader.Find("Sprites/Default");
        if (shader == null) return null;

        const int size = 64;
        _shadowTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Toy Car Shadow",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        var pixels = new Color32[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var u = (x + 0.5f) / size * 2f - 1f;
                var v = (y + 0.5f) / size * 2f - 1f;
                var distance = Mathf.Pow(Mathf.Pow(Mathf.Abs(u), 3f) + Mathf.Pow(Mathf.Abs(v), 3f), 1f / 3f);
                var alpha = 1f - Mathf.SmoothStep(0.52f, 1f, distance);
                pixels[y * size + x] = new Color32(0, 0, 0, (byte)(alpha * 255f));
            }
        }

        _shadowTexture.SetPixels32(pixels);
        _shadowTexture.Apply(false, true);
        _shadowMaterial = new Material(shader)
        {
            name = "Toy Car Shadow",
            mainTexture = _shadowTexture,
            color = new Color(0f, 0f, 0f, 0.62f)
        };
        return _shadowMaterial;
    }

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

    private readonly struct Station
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

    private sealed class MeshBuilder
    {
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<int>[] _triangles;

        public MeshBuilder(int submeshCount)
        {
            _triangles = new List<int>[submeshCount];
            for (var i = 0; i < submeshCount; i++)
                _triangles[i] = new List<int>();
        }

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

        /// <summary>Lathe limited to ±halfAngle degrees around local +Y (an open arc).</summary>
        public void AddLatheArc(int submesh, IReadOnlyList<Vector2> profile, int segments, float halfAngle, Matrix4x4 matrix)
        {
            var start = _vertices.Count;
            var columns = segments + 1;
            for (var i = 0; i < profile.Count; i++)
            {
                for (var j = 0; j < columns; j++)
                {
                    var angle = Mathf.Lerp(-halfAngle, halfAngle, j / (float)segments) * Mathf.Deg2Rad;
                    var point = new Vector3(profile[i].x, Mathf.Cos(angle) * profile[i].y, Mathf.Sin(angle) * profile[i].y);
                    _vertices.Add(matrix.MultiplyPoint3x4(point));
                }
            }

            var triangles = _triangles[submesh];
            for (var i = 0; i < profile.Count - 1; i++)
            {
                var ringA = start + i * columns;
                var ringB = start + (i + 1) * columns;
                for (var j = 0; j < segments; j++)
                {
                    triangles.Add(ringA + j);
                    triangles.Add(ringA + j + 1);
                    triangles.Add(ringB + j + 1);
                    triangles.Add(ringA + j);
                    triangles.Add(ringB + j + 1);
                    triangles.Add(ringB + j);
                }
            }
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

            // Unity front faces wind so that Cross(b - a, c - a) points outward.
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
            // Duplicate the rim so the cap gets a crisp edge instead of smeared normals.
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
