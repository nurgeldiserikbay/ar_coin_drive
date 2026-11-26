using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds the road cars (original designs, no real-world models or brands) as one body mesh
/// plus one shared wheel mesh per car type. Every part's colour, gloss, metalness and glow is
/// baked into the vertices, and paintable panels are flagged, so a whole car draws with one
/// material ("AR Coin Drive/Car") and the four wheels instance together: two draw calls per car.
/// Shapes are authored in real-car metres (z forward, y up) and scaled to 1:18.
/// </summary>
public static class CarModelBuilder
{
    private sealed class Shape
    {
        public float Length;
        public float FrontAxle;
        public float RearAxle;
        public float WheelRadius;
        public float TyreWidth;
        public float Track;          // wheel centre x
        public float Exponent;
        public Station[] Body;
        public Station[] Cabin;
        public float RoofFront;
        public float RoofRear;
        public Color RimColor;
        public int Spokes;
        public bool Knobby;
    }

    private static readonly Dictionary<(CarBody, float, int, bool), Mesh> BodyMeshes = new Dictionary<(CarBody, float, int, bool), Mesh>();
    private static readonly Dictionary<(CarBody, float, int), Mesh> WheelMeshes = new Dictionary<(CarBody, float, int), Mesh>();
    private static readonly Dictionary<CarBody, Shape> Shapes = new Dictionary<CarBody, Shape>();
    private static readonly Dictionary<(Color, int), Material> Paints = new Dictionary<(Color, int), Material>();
    private static Material _template;
    private static Mesh _shadowMesh;
    private static Material _shadowMaterial;

    // Common finishes
    private static readonly Color Rubber = new Color(0.035f, 0.035f, 0.038f);
    private static readonly Color Trim = new Color(0.03f, 0.03f, 0.035f);
    private static readonly Color Glass = new Color(0.03f, 0.04f, 0.05f);
    private static readonly Color Chrome = new Color(0.78f, 0.79f, 0.81f);
    private static readonly Color Lamp = new Color(1f, 0.96f, 0.86f);
    private static readonly Color TailLamp = new Color(0.85f, 0.04f, 0.04f);

    public static GameObject Create(Transform parent, CarSpec spec, Color paint) => Create(parent, spec, paint, CarLook.Stock(0));

    public static GameObject Create(Transform parent, CarSpec spec, Color paint, CarLook look)
    {
        var shape = GetShape(spec.Body);
        var root = new GameObject(spec.Name);
        root.transform.SetParent(parent, false);
        CreateContactShadow(root.transform, shape);
        var material = PaintMaterial(paint, look.Finish);

        var body = new GameObject("Body");
        body.transform.SetParent(root.transform, false);
        body.AddComponent<MeshFilter>().sharedMesh = GetBodyMesh(spec.Body, CarCatalog.Scale, look.Spoiler, look.Stripes);
        body.AddComponent<MeshRenderer>().sharedMaterial = material;

        var wheelMesh = GetWheelMesh(spec.Body, CarCatalog.Scale, look.Rim);
        var s = CarCatalog.Scale;
        var pivots = new List<Transform>();
        var spinners = new List<Transform>();
        foreach (var z in new[] { shape.FrontAxle, shape.RearAxle })
        {
            foreach (var side in new[] { -1f, 1f })
            {
                var pivot = new GameObject("Wheel").transform;
                pivot.SetParent(root.transform, false);
                pivot.localPosition = new Vector3(side * shape.Track, shape.WheelRadius, z) * s;
                var spin = new GameObject("Spin").transform;
                spin.SetParent(pivot, false);
                var visual = new GameObject("Wheel Mesh");
                visual.transform.SetParent(spin, false);
                if (side < 0f) visual.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                visual.AddComponent<MeshFilter>().sharedMesh = wheelMesh;
                visual.AddComponent<MeshRenderer>().sharedMaterial = material;
                spinners.Add(spin);
                if (z == shape.FrontAxle) pivots.Add(pivot);
            }
        }

        root.AddComponent<ProceduralCarRig>().Configure(body.transform, pivots.ToArray(), spinners.ToArray(), shape.WheelRadius * s);
        return root;
    }

    /// <summary>
    /// A parked full-size car (scenery): body and wheels at the given scale, no rig or sound.
    /// Returns the wheel contact points (floor x, z) so they can be made solid.
    /// </summary>
    public static Vector2[] CreateParked(Transform parent, CarBody body, Color paint, Vector2 position, float heading, float scale)
    {
        var shape = GetShape(body);
        var root = new GameObject(body + " (parked)").transform;
        root.SetParent(parent, false);
        root.localPosition = new Vector3(position.x, 0f, position.y);
        root.localRotation = Quaternion.Euler(0f, heading, 0f);
        var material = PaintMaterial(paint);
        var bodyObject = new GameObject("Body");
        bodyObject.transform.SetParent(root, false);
        bodyObject.AddComponent<MeshFilter>().sharedMesh = GetBodyMesh(body, scale);
        bodyObject.AddComponent<MeshRenderer>().sharedMaterial = material;
        var contacts = new Vector2[4];
        var k = 0;
        foreach (var z in new[] { shape.FrontAxle, shape.RearAxle })
        {
            foreach (var side in new[] { -1f, 1f })
            {
                var wheel = new GameObject("Wheel");
                wheel.transform.SetParent(root, false);
                wheel.transform.localPosition = new Vector3(side * shape.Track, shape.WheelRadius, z) * scale;
                if (side < 0f) wheel.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                wheel.AddComponent<MeshFilter>().sharedMesh = GetWheelMesh(body, scale);
                wheel.AddComponent<MeshRenderer>().sharedMaterial = material;
                var world = root.TransformPoint(new Vector3(side * shape.Track, 0f, z) * scale) - parent.position;
                contacts[k++] = new Vector2(world.x, world.z);
            }
        }

        return contacts;
    }

    public static float WheelRadiusAt(CarBody body, float scale) => GetShape(body).WheelRadius * scale;
    public static float TyreWidthAt(CarBody body, float scale) => GetShape(body).TyreWidth * scale;

    /// <summary>Driver camera position in the body's space (just above the windscreen base).</summary>
    public static Vector3 DriverEye(CarBody body)
    {
        var shape = GetShape(body);
        var front = shape.Cabin[0];
        return new Vector3(0f, front.Top + 0.12f, front.Z + 0.05f) * CarCatalog.Scale;
    }

    public static Material PaintMaterial(Color paint, int finish = 0)
    {
        if (Paints.TryGetValue((paint, finish), out var cached) && cached != null) return cached;
        if (_template == null) _template = Resources.Load<Material>("ARCoinDriveCar");
        Material material;
        if (_template != null) material = new Material(_template);
        else
        {
            var shader = Shader.Find("AR Coin Drive/Car");
            if (shader == null) return ProceduralCarFactory.GetMaterial(paint);
            material = new Material(shader);
        }

        material.name = "Car Paint " + ColorUtility.ToHtmlStringRGB(paint);
        material.enableInstancing = true;
        material.SetColor("_PaintColor", paint);
        // Finish: gloss clear coat, metallic flake, or flat matte.
        material.SetFloat("_PaintGloss", finish == 2 ? 0.22f : finish == 1 ? 0.80f : 0.86f);
        material.SetFloat("_PaintMetal", finish == 2 ? 0.02f : finish == 1 ? 0.85f : 0.35f);
        Paints[(paint, finish)] = material;
        return material;
    }

    // ------------------------------------------------------------------ shapes (real metres)

    private static Shape GetShape(CarBody body)
    {
        if (Shapes.TryGetValue(body, out var shape)) return shape;
        switch (body)
        {
            case CarBody.Muscle:
                shape = new Shape
                {
                    Length = 4.75f, FrontAxle = 1.45f, RearAxle = -1.40f, WheelRadius = 0.35f, TyreWidth = 0.28f, Track = 0.80f,
                    Exponent = 6f, RoofFront = -0.05f, RoofRear = -1.30f, RimColor = new Color(0.12f, 0.12f, 0.13f), Spokes = 5,
                    Body = new[]
                    {
                        St(2.37f, 0.80f, 0.30f, 0.70f), St(2.25f, 0.92f, 0.22f, 0.79f), St(1.60f, 0.95f, 0.20f, 0.83f),
                        St(0.40f, 0.95f, 0.20f, 0.85f), St(-1.00f, 0.95f, 0.20f, 0.87f), St(-1.80f, 0.95f, 0.24f, 0.89f),
                        St(-2.30f, 0.90f, 0.32f, 0.87f), St(-2.38f, 0.82f, 0.40f, 0.81f)
                    },
                    Cabin = new[]
                    {
                        St(0.32f, 0.80f, 0.82f, 0.88f), St(-0.05f, 0.76f, 0.84f, 1.27f), St(-0.70f, 0.74f, 0.85f, 1.30f),
                        St(-1.30f, 0.74f, 0.86f, 1.12f), St(-1.85f, 0.72f, 0.86f, 0.92f)
                    }
                };
                break;
            case CarBody.Super:
                shape = new Shape
                {
                    Length = 4.55f, FrontAxle = 1.38f, RearAxle = -1.35f, WheelRadius = 0.34f, TyreWidth = 0.30f, Track = 0.84f,
                    Exponent = 3.4f, RoofFront = 0.30f, RoofRear = -0.55f, RimColor = new Color(0.72f, 0.73f, 0.76f), Spokes = 10,
                    Body = new[]
                    {
                        St(2.27f, 0.62f, 0.20f, 0.36f), St(2.10f, 0.90f, 0.14f, 0.49f), St(1.60f, 1.00f, 0.12f, 0.64f),
                        St(1.00f, 0.97f, 0.12f, 0.58f), St(0.20f, 0.95f, 0.12f, 0.70f), St(-0.60f, 1.00f, 0.12f, 0.80f),
                        St(-1.40f, 1.00f, 0.14f, 0.87f), St(-2.00f, 0.96f, 0.20f, 0.84f), St(-2.27f, 0.86f, 0.30f, 0.78f)
                    },
                    Cabin = new[]
                    {
                        St(1.05f, 0.70f, 0.55f, 0.60f), St(0.55f, 0.70f, 0.60f, 1.00f), St(0.00f, 0.67f, 0.64f, 1.12f),
                        St(-0.55f, 0.63f, 0.70f, 1.06f), St(-1.15f, 0.55f, 0.78f, 0.88f)
                    }
                };
                break;
            case CarBody.Rally:
                shape = new Shape
                {
                    Length = 4.05f, FrontAxle = 1.28f, RearAxle = -1.27f, WheelRadius = 0.32f, TyreWidth = 0.24f, Track = 0.76f,
                    Exponent = 5f, RoofFront = 0.32f, RoofRear = -1.85f, RimColor = new Color(0.92f, 0.92f, 0.90f), Spokes = 6,
                    Body = new[]
                    {
                        St(2.02f, 0.70f, 0.34f, 0.63f), St(1.90f, 0.84f, 0.28f, 0.79f), St(1.40f, 0.89f, 0.26f, 0.87f),
                        St(0.70f, 0.89f, 0.26f, 0.91f), St(-0.60f, 0.89f, 0.26f, 0.95f), St(-1.50f, 0.88f, 0.28f, 0.99f),
                        St(-1.95f, 0.82f, 0.34f, 1.00f), St(-2.02f, 0.76f, 0.42f, 0.96f)
                    },
                    Cabin = new[]
                    {
                        St(0.80f, 0.78f, 0.88f, 0.93f), St(0.40f, 0.76f, 0.92f, 1.36f), St(-0.40f, 0.74f, 0.94f, 1.46f),
                        St(-1.50f, 0.72f, 0.97f, 1.44f), St(-1.95f, 0.70f, 0.97f, 1.30f)
                    }
                };
                break;
            case CarBody.Offroad:
                shape = new Shape
                {
                    Length = 4.60f, FrontAxle = 1.42f, RearAxle = -1.42f, WheelRadius = 0.42f, TyreWidth = 0.32f, Track = 0.84f,
                    Exponent = 8f, RoofFront = 0.50f, RoofRear = -1.95f, RimColor = new Color(0.10f, 0.10f, 0.11f), Spokes = 6, Knobby = true,
                    Body = new[]
                    {
                        St(2.30f, 0.85f, 0.48f, 0.98f), St(2.15f, 0.95f, 0.44f, 1.10f), St(1.50f, 0.97f, 0.42f, 1.14f),
                        St(0.00f, 0.97f, 0.42f, 1.17f), St(-1.80f, 0.97f, 0.44f, 1.18f), St(-2.30f, 0.92f, 0.48f, 1.12f)
                    },
                    Cabin = new[]
                    {
                        St(0.95f, 0.86f, 1.12f, 1.17f), St(0.55f, 0.84f, 1.14f, 1.70f), St(-0.20f, 0.84f, 1.14f, 1.78f),
                        St(-1.20f, 0.84f, 1.16f, 1.77f), St(-2.05f, 0.82f, 1.16f, 1.70f)
                    }
                };
                break;
            case CarBody.GT:
                // Long-bonnet front-engined grand tourer with a fastback roof.
                shape = new Shape
                {
                    Length = 4.70f, FrontAxle = 1.50f, RearAxle = -1.25f, WheelRadius = 0.35f, TyreWidth = 0.28f, Track = 0.80f,
                    Exponent = 4.2f, RoofFront = -0.25f, RoofRear = -1.20f, RimColor = new Color(0.22f, 0.22f, 0.24f), Spokes = 5,
                    Body = new[]
                    {
                        St(2.35f, 0.60f, 0.26f, 0.50f), St(2.22f, 0.88f, 0.20f, 0.62f), St(1.70f, 0.96f, 0.18f, 0.70f),
                        St(0.80f, 0.94f, 0.18f, 0.74f), St(0.00f, 0.94f, 0.18f, 0.78f), St(-0.90f, 0.98f, 0.20f, 0.84f),
                        St(-1.60f, 0.98f, 0.22f, 0.86f), St(-2.15f, 0.90f, 0.28f, 0.84f), St(-2.35f, 0.78f, 0.36f, 0.78f)
                    },
                    Cabin = new[]
                    {
                        St(0.05f, 0.76f, 0.74f, 0.78f), St(-0.30f, 0.72f, 0.77f, 1.18f), St(-0.85f, 0.68f, 0.80f, 1.22f),
                        St(-1.50f, 0.64f, 0.84f, 1.02f), St(-2.05f, 0.60f, 0.84f, 0.88f)
                    }
                };
                break;
            case CarBody.Proto:
                // Endurance prototype: low tub, tall fenders and a narrow bubble canopy.
                shape = new Shape
                {
                    Length = 4.80f, FrontAxle = 1.55f, RearAxle = -1.45f, WheelRadius = 0.36f, TyreWidth = 0.32f, Track = 0.84f,
                    Exponent = 2.8f, RoofFront = 0.20f, RoofRear = -0.45f, RimColor = new Color(0.08f, 0.08f, 0.09f), Spokes = 10,
                    Body = new[]
                    {
                        St(2.40f, 0.70f, 0.10f, 0.24f), St(2.25f, 0.98f, 0.08f, 0.38f), St(1.55f, 1.00f, 0.08f, 0.64f),
                        St(0.80f, 0.98f, 0.08f, 0.52f), St(0.00f, 0.98f, 0.08f, 0.58f), St(-0.80f, 1.00f, 0.08f, 0.66f),
                        St(-1.45f, 1.00f, 0.10f, 0.74f), St(-2.10f, 0.98f, 0.14f, 0.68f), St(-2.40f, 0.90f, 0.18f, 0.62f)
                    },
                    Cabin = new[]
                    {
                        St(0.95f, 0.48f, 0.45f, 0.52f), St(0.50f, 0.52f, 0.50f, 0.98f), St(0.00f, 0.52f, 0.52f, 1.04f),
                        St(-0.45f, 0.48f, 0.56f, 0.98f), St(-1.10f, 0.40f, 0.60f, 0.80f)
                    }
                };
                break;
            default: // Tuner
                shape = new Shape
                {
                    Length = 4.45f, FrontAxle = 1.30f, RearAxle = -1.27f, WheelRadius = 0.32f, TyreWidth = 0.24f, Track = 0.77f,
                    Exponent = 4.6f, RoofFront = 0.20f, RoofRear = -0.95f, RimColor = new Color(0.80f, 0.64f, 0.22f), Spokes = 6,
                    Body = new[]
                    {
                        St(2.22f, 0.58f, 0.28f, 0.55f), St(2.10f, 0.80f, 0.22f, 0.66f), St(1.80f, 0.88f, 0.18f, 0.74f),
                        St(1.30f, 0.90f, 0.18f, 0.80f), St(0.60f, 0.90f, 0.18f, 0.82f), St(-0.30f, 0.90f, 0.18f, 0.84f),
                        St(-1.00f, 0.90f, 0.20f, 0.88f), St(-1.50f, 0.89f, 0.22f, 0.92f), St(-1.95f, 0.84f, 0.28f, 0.92f),
                        St(-2.20f, 0.70f, 0.38f, 0.86f)
                    },
                    Cabin = new[]
                    {
                        St(0.75f, 0.78f, 0.76f, 0.82f), St(0.35f, 0.74f, 0.79f, 1.22f), St(-0.20f, 0.70f, 0.82f, 1.28f),
                        St(-0.80f, 0.68f, 0.84f, 1.20f), St(-1.40f, 0.66f, 0.88f, 0.95f)
                    }
                };
                break;
        }

        // Wheels sit with their outer face just proud of the body side, like a real car.
        shape.Track = WidthAt(shape.Body, shape.FrontAxle) - shape.TyreWidth * 0.5f + 0.04f;
        Shapes[body] = shape;
        return shape;
    }

    private static Station St(float z, float halfWidth, float bottom, float top) => new Station(z, halfWidth, bottom, top);

    // ------------------------------------------------------------------ body mesh

    private static Mesh GetBodyMesh(CarBody body, float scale = CarCatalog.Scale, int spoiler = 1, bool stripes = false)
    {
        if (BodyMeshes.TryGetValue((body, scale, spoiler, stripes), out var mesh)) return mesh;
        var shape = GetShape(body);
        var b = new PartMeshBuilder();

        // Painted body shell and the glasshouse.
        b.Part(Color.white, 0.86f, 0.35f, 0f, true);
        b.Loft(Resample(shape.Body, 4), shape.Exponent, 40, Matrix4x4.identity);
        b.Part(Glass, 0.95f, 0.1f);
        b.Loft(Resample(shape.Cabin, 4), 3.2f, 32, Matrix4x4.identity);

        // Roof panel over the glass leaves the windscreen, side windows and rear glass showing.
        var roof = new List<Station>();
        foreach (var station in Resample(shape.Cabin, 8))
        {
            if (station.Z > shape.RoofFront || station.Z < shape.RoofRear) continue;
            roof.Add(new Station(station.Z, station.HalfWidth * 0.95f, station.Top - 0.14f, station.Top + 0.012f));
        }

        if (roof.Count >= 2)
        {
            b.Part(Color.white, 0.86f, 0.35f, 0f, true);
            b.Loft(roof, 3.2f, 28, Matrix4x4.identity);
        }

        AddWheelArches(b, shape);
        AddLights(b, shape);
        AddCommonDetails(b, shape);
        AddCharacter(b, body, shape, spoiler == 1, stripes);
        if (spoiler == 2) AddRaceWing(b, shape);
        if (stripes && body != CarBody.Muscle) AddStripes(b, shape);

        mesh = b.Build(body + " Body", scale);
        BodyMeshes[(body, scale, spoiler, stripes)] = mesh;
        return mesh;
    }

    private static void AddWheelArches(PartMeshBuilder b, Shape shape)
    {
        foreach (var z in new[] { shape.FrontAxle, shape.RearAxle })
        {
            foreach (var side in new[] { -1f, 1f })
            {
                var halfWidth = WidthAt(shape.Body, z);
                var center = new Vector3(side * (halfWidth - 0.10f), shape.WheelRadius + 0.03f, z);
                var mirrored = side < 0f ? Matrix4x4.Rotate(Quaternion.Euler(0f, 180f, 0f)) : Matrix4x4.identity;
                var matrix = Matrix4x4.Translate(center) * mirrored;
                var outer = 0.10f + 0.03f;
                // Painted fender lip around the top of the wheel.
                b.Part(Color.white, 0.86f, 0.35f, 0f, true);
                b.LatheArc(new[]
                {
                    new Vector2(-0.05f, shape.WheelRadius + 0.12f), new Vector2(outer, shape.WheelRadius + 0.12f),
                    new Vector2(outer + 0.012f, shape.WheelRadius + 0.09f), new Vector2(outer, shape.WheelRadius + 0.07f)
                }, 20, 72f, matrix);
            }
        }
    }

    private static void AddLights(PartMeshBuilder b, Shape shape)
    {
        var nose = shape.Body[1];
        var tail = shape.Body[shape.Body.Length - 2];
        foreach (var side in new[] { -1f, 1f })
        {
            b.Part(Lamp, 0.95f, 0.1f, 0.6f);
            b.Ellipsoid(new Vector3(side * nose.HalfWidth * 0.70f, nose.Top - 0.07f, nose.Z + 0.03f), new Vector3(0.16f, 0.045f, 0.07f));
            b.Part(TailLamp, 0.9f, 0.1f, 0.6f);
            b.Ellipsoid(new Vector3(side * tail.HalfWidth * 0.74f, tail.Top - 0.10f, tail.Z - 0.05f), new Vector3(0.20f, 0.05f, 0.06f));
        }
    }

    private static void AddCommonDetails(PartMeshBuilder b, Shape shape)
    {
        var front = shape.Body[0];
        var back = shape.Body[shape.Body.Length - 1];

        // Grille / lower intake, number plates, underbody and sills.
        b.Part(Trim, 0.4f, 0.2f);
        b.Ellipsoid(new Vector3(0f, front.Bottom + 0.10f, front.Z - 0.02f), new Vector3(front.HalfWidth * 0.8f, 0.07f, 0.05f));
        b.Box(new Vector3(0f, shape.Body[2].Bottom + 0.02f, 0f), new Vector3(shape.Track * 2f - 0.1f, 0.06f, shape.Length * 0.82f));
        foreach (var side in new[] { -1f, 1f })
            b.Box(new Vector3(side * (WidthAt(shape.Body, 0f) - 0.01f), shape.Body[3].Bottom + 0.04f, (shape.FrontAxle + shape.RearAxle) * 0.5f),
                new Vector3(0.05f, 0.08f, (shape.FrontAxle - shape.RearAxle) - shape.WheelRadius * 2.4f));

        b.Part(new Color(0.92f, 0.92f, 0.9f), 0.5f, 0f);
        b.Box(new Vector3(0f, back.Bottom + 0.16f, back.Z - 0.01f), new Vector3(0.52f, 0.11f, 0.02f));

        // Exhaust tips.
        b.Part(Chrome, 0.85f, 0.95f);
        b.Cylinder(new Vector3(back.HalfWidth * 0.45f, back.Bottom + 0.02f, back.Z + 0.15f), new Vector3(back.HalfWidth * 0.45f, back.Bottom + 0.02f, back.Z - 0.04f), 0.05f, 12);

        // Door mirrors at the base of the windscreen.
        var windscreen = shape.Cabin[0];
        foreach (var side in new[] { -1f, 1f })
        {
            b.Part(Color.white, 0.86f, 0.35f, 0f, true);
            b.Ellipsoid(new Vector3(side * (windscreen.HalfWidth + 0.13f), windscreen.Bottom + 0.10f, windscreen.Z - 0.15f), new Vector3(0.09f, 0.055f, 0.07f));
            b.Part(Trim, 0.3f, 0f);
            b.Box(new Vector3(side * (windscreen.HalfWidth + 0.05f), windscreen.Bottom + 0.08f, windscreen.Z - 0.15f), new Vector3(0.08f, 0.02f, 0.04f));
        }
    }

    /// <summary>The details that give each class its own character.</summary>
    private static void AddCharacter(PartMeshBuilder b, CarBody body, Shape shape, bool stockSpoiler, bool stripes)
    {
        var tail = shape.Body[shape.Body.Length - 2];
        switch (body)
        {
            case CarBody.Tuner:
                if (stockSpoiler)
                {
                    // Tall rear wing on two struts.
                    b.Part(Trim, 0.5f, 0.2f);
                    foreach (var side in new[] { -0.45f, 0.45f })
                        b.Box(new Vector3(side, tail.Top + 0.10f, tail.Z + 0.10f), new Vector3(0.05f, 0.22f, 0.10f));
                    b.Part(Color.white, 0.86f, 0.35f, 0f, true);
                    b.Box(new Vector3(0f, tail.Top + 0.22f, tail.Z + 0.06f), new Vector3(1.62f, 0.035f, 0.30f), Matrix4x4.Rotate(Quaternion.Euler(-8f, 0f, 0f)));
                    b.Part(Trim, 0.5f, 0.2f);
                    foreach (var side in new[] { -0.81f, 0.81f })
                        b.Box(new Vector3(side, tail.Top + 0.20f, tail.Z + 0.06f), new Vector3(0.02f, 0.14f, 0.34f));
                }

                b.Part(Trim, 0.5f, 0.2f);
                // Front lip spoiler.
                b.Box(new Vector3(0f, shape.Body[1].Bottom - 0.02f, shape.Body[1].Z - 0.02f), new Vector3(1.4f, 0.03f, 0.18f));
                break;

            case CarBody.Muscle:
                // Hood scoop, ducktail and twin stripes.
                b.Part(Trim, 0.6f, 0.2f);
                b.Box(new Vector3(0f, 0.88f, 1.05f), new Vector3(0.55f, 0.10f, 0.70f));
                if (stockSpoiler)
                {
                    b.Part(Color.white, 0.86f, 0.35f, 0f, true);
                    b.Box(new Vector3(0f, tail.Top + 0.03f, tail.Z + 0.05f), new Vector3(1.6f, 0.05f, 0.22f), Matrix4x4.Rotate(Quaternion.Euler(-12f, 0f, 0f)));
                }

                b.Part(new Color(0.95f, 0.95f, 0.93f), 0.8f, 0.1f);
                foreach (var x in stripes ? new[] { -0.16f, 0.16f } : new float[0])
                {
                    var stripe = new List<Station>();
                    foreach (var s in Resample(shape.Body, 6))
                        if (s.Z < 2.2f && s.Z > 0.45f) stripe.Add(new Station(s.Z, 0.07f, s.Top - 0.01f, s.Top + 0.006f));
                    b.Loft(stripe, 2.5f, 10, Matrix4x4.Translate(new Vector3(x, 0f, 0f)));
                }

                break;

            case CarBody.Super:
                // Side intakes, a big wing and a centre quad exhaust.
                b.Part(Trim, 0.3f, 0.1f);
                foreach (var side in new[] { -1f, 1f })
                    b.Ellipsoid(new Vector3(side * 0.96f, 0.52f, -0.55f), new Vector3(0.05f, 0.16f, 0.45f));
                b.Box(new Vector3(0f, 0.22f, -2.20f), new Vector3(1.4f, 0.12f, 0.12f));
                if (stockSpoiler)
                {
                    foreach (var side in new[] { -0.4f, 0.4f })
                        b.Box(new Vector3(side, tail.Top + 0.12f, tail.Z + 0.2f), new Vector3(0.04f, 0.26f, 0.12f));
                    b.Part(Color.white, 0.86f, 0.35f, 0f, true);
                    b.Box(new Vector3(0f, tail.Top + 0.26f, tail.Z + 0.12f), new Vector3(1.85f, 0.035f, 0.36f), Matrix4x4.Rotate(Quaternion.Euler(-6f, 0f, 0f)));
                }

                b.Part(Chrome, 0.85f, 0.95f);
                foreach (var x in new[] { -0.12f, 0.12f })
                    b.Cylinder(new Vector3(x, 0.32f, -2.10f), new Vector3(x, 0.32f, -2.30f), 0.055f, 12);
                break;

            case CarBody.Rally:
                // Roof scoop, light pod, roof spoiler and mud flaps.
                var nose = shape.Body[1];
                b.Part(Trim, 0.5f, 0.2f);
                b.Box(new Vector3(0f, 1.47f, -0.10f), new Vector3(0.35f, 0.06f, 0.40f));
                b.Box(new Vector3(0f, nose.Top - 0.02f, nose.Z + 0.07f), new Vector3(1.10f, 0.20f, 0.06f));
                b.Part(Lamp, 0.95f, 0.1f, 0.9f);
                for (var i = 0; i < 4; i++)
                    b.Ellipsoid(new Vector3(-0.42f + i * 0.28f, nose.Top - 0.02f, nose.Z + 0.10f), new Vector3(0.11f, 0.11f, 0.03f));
                if (stockSpoiler)
                {
                    b.Part(Color.white, 0.86f, 0.35f, 0f, true);
                    b.Box(new Vector3(0f, 1.44f, -1.92f), new Vector3(1.40f, 0.05f, 0.22f), Matrix4x4.Rotate(Quaternion.Euler(10f, 0f, 0f)));
                }

                b.Part(Trim, 0.2f, 0f);
                foreach (var side in new[] { -1f, 1f })
                    b.Box(new Vector3(side * shape.Track, 0.20f, shape.RearAxle - shape.WheelRadius - 0.06f), new Vector3(0.26f, 0.28f, 0.02f));
                break;

            case CarBody.GT:
            {
                // Bonnet vents, side gills behind the front wheels, a ducktail and quad exhausts.
                b.Part(Trim, 0.4f, 0.2f);
                foreach (var x in new[] { -0.30f, 0.30f })
                    b.Box(new Vector3(x, 0.735f, 1.05f), new Vector3(0.22f, 0.03f, 0.50f));
                foreach (var side in new[] { -1f, 1f })
                    for (var i = 0; i < 3; i++)
                        b.Box(new Vector3(side * 0.95f, 0.50f, shape.FrontAxle - 0.55f - i * 0.12f), new Vector3(0.03f, 0.16f, 0.05f));
                if (stockSpoiler)
                {
                    b.Part(Color.white, 0.86f, 0.35f, 0f, true);
                    b.Box(new Vector3(0f, tail.Top + 0.03f, tail.Z + 0.06f), new Vector3(1.5f, 0.04f, 0.18f), Matrix4x4.Rotate(Quaternion.Euler(-14f, 0f, 0f)));
                }

                b.Part(Chrome, 0.85f, 0.95f);
                var back = shape.Body[shape.Body.Length - 1];
                foreach (var x in new[] { -0.62f, -0.48f, 0.48f, 0.62f })
                    b.Cylinder(new Vector3(x, back.Bottom + 0.02f, back.Z + 0.15f), new Vector3(x, back.Bottom + 0.02f, back.Z - 0.04f), 0.045f, 12);
                break;
            }

            case CarBody.Proto:
            {
                // Shark fin, front splitter, side intakes and a tall endurance wing.
                b.Part(Color.white, 0.86f, 0.35f, 0f, true);
                b.Box(new Vector3(0f, 0.80f, -1.05f), new Vector3(0.025f, 0.22f, 1.00f));
                b.Part(Trim, 0.3f, 0.2f);
                b.Box(new Vector3(0f, 0.06f, 2.30f), new Vector3(1.90f, 0.03f, 0.30f));
                foreach (var side in new[] { -1f, 1f })
                    b.Ellipsoid(new Vector3(side * 0.88f, 0.38f, -0.20f), new Vector3(0.10f, 0.14f, 0.40f));
                if (stockSpoiler)
                {
                    var top = tail.Top + 0.30f;
                    foreach (var side in new[] { -0.30f, 0.30f })
                        b.Box(new Vector3(side, tail.Top + 0.15f, tail.Z + 0.10f), new Vector3(0.04f, 0.30f, 0.14f));
                    b.Box(new Vector3(0f, top, tail.Z + 0.04f), new Vector3(1.90f, 0.04f, 0.40f), Matrix4x4.Rotate(Quaternion.Euler(-8f, 0f, 0f)));
                    b.Part(Color.white, 0.86f, 0.35f, 0f, true);
                    foreach (var side in new[] { -0.96f, 0.96f })
                        b.Box(new Vector3(side, top - 0.05f, tail.Z + 0.04f), new Vector3(0.025f, 0.26f, 0.50f));
                }

                break;
            }

            case CarBody.Offroad:
                // Bull bar, roof rack, fender flares, rear spare wheel.
                var front = shape.Body[0];
                b.Part(new Color(0.12f, 0.12f, 0.13f), 0.5f, 0.6f);
                b.Box(new Vector3(0f, front.Bottom + 0.10f, front.Z + 0.10f), new Vector3(1.6f, 0.10f, 0.10f));
                foreach (var side in new[] { -0.55f, 0.55f })
                    b.Box(new Vector3(side, front.Bottom + 0.30f, front.Z + 0.10f), new Vector3(0.08f, 0.50f, 0.08f));
                b.Box(new Vector3(0f, front.Bottom + 0.55f, front.Z + 0.10f), new Vector3(1.18f, 0.08f, 0.08f));
                b.Box(new Vector3(0f, 1.84f, -0.70f), new Vector3(1.40f, 0.05f, 1.90f));
                foreach (var side in new[] { -1f, 1f })
                    b.Box(new Vector3(side * 0.68f, 1.80f, -0.70f), new Vector3(0.05f, 0.08f, 1.90f));
                foreach (var z in new[] { shape.FrontAxle, shape.RearAxle })
                    foreach (var side in new[] { -1f, 1f })
                        b.Box(new Vector3(side * 0.98f, shape.WheelRadius + 0.40f, z), new Vector3(0.08f, 0.06f, shape.WheelRadius * 2.6f));
                b.Part(Rubber, 0.2f, 0f);
                b.Cylinder(new Vector3(0f, 0.85f, -2.30f), new Vector3(0f, 0.85f, -2.52f), 0.36f, 20);
                break;
        }
    }

    /// <summary>Big GT wing on swan-neck mounts (the GT WING option, any car).</summary>
    private static void AddRaceWing(PartMeshBuilder b, Shape shape)
    {
        var tail = shape.Body[shape.Body.Length - 2];
        var top = tail.Top + 0.34f;
        b.Part(Trim, 0.5f, 0.3f);
        foreach (var side in new[] { -0.38f, 0.38f })
            b.Box(new Vector3(side, tail.Top + 0.17f, tail.Z + 0.12f), new Vector3(0.04f, 0.36f, 0.14f), Matrix4x4.Rotate(Quaternion.Euler(-10f, 0f, 0f)));
        b.Box(new Vector3(0f, top, tail.Z + 0.04f), new Vector3(1.95f, 0.04f, 0.38f), Matrix4x4.Rotate(Quaternion.Euler(-10f, 0f, 0f)));
        b.Part(Color.white, 0.86f, 0.35f, 0f, true);
        foreach (var side in new[] { -0.98f, 0.98f })
            b.Box(new Vector3(side, top - 0.04f, tail.Z + 0.04f), new Vector3(0.025f, 0.22f, 0.46f));
    }

    /// <summary>Twin white racing stripes over the bonnet, roof and boot.</summary>
    private static void AddStripes(PartMeshBuilder b, Shape shape)
    {
        b.Part(new Color(0.95f, 0.95f, 0.93f), 0.8f, 0.1f);
        var noseZ = shape.Body[1].Z;
        var tailZ = shape.Body[shape.Body.Length - 2].Z;
        var screenZ = shape.Cabin[0].Z;
        var rearZ = shape.Cabin[shape.Cabin.Length - 1].Z;
        foreach (var x in new[] { -0.15f, 0.15f })
        {
            var offset = Matrix4x4.Translate(new Vector3(x, 0f, 0f));
            var bonnet = new List<Station>();
            var boot = new List<Station>();
            foreach (var s in Resample(shape.Body, 6))
            {
                if (s.Z < noseZ && s.Z > screenZ + 0.05f) bonnet.Add(new Station(s.Z, 0.065f, s.Top - 0.01f, s.Top + 0.006f));
                else if (s.Z < rearZ - 0.05f && s.Z > tailZ) boot.Add(new Station(s.Z, 0.065f, s.Top - 0.01f, s.Top + 0.006f));
            }

            if (bonnet.Count >= 2) b.Loft(bonnet, 2.5f, 10, offset);
            if (boot.Count >= 2) b.Loft(boot, 2.5f, 10, offset);
            var roof = new List<Station>();
            foreach (var s in Resample(shape.Cabin, 8))
                if (s.Z < shape.RoofFront && s.Z > shape.RoofRear)
                    roof.Add(new Station(s.Z, 0.065f, s.Top + 0.002f, s.Top + 0.018f));
            if (roof.Count >= 2) b.Loft(roof, 2.5f, 10, offset);
        }
    }

    // ------------------------------------------------------------------ wheels

    private static Mesh GetWheelMesh(CarBody body, float scale = CarCatalog.Scale, int rimStyle = 0)
    {
        if (WheelMeshes.TryGetValue((body, scale, rimStyle), out var mesh)) return mesh;
        var shape = GetShape(body);
        // Rim options: stock (the class's own), sport 5-spoke silver, mesh 10-spoke, black deep dish.
        var rimColor = rimStyle == 1 ? new Color(0.78f, 0.79f, 0.82f) : rimStyle == 2 ? new Color(0.70f, 0.62f, 0.40f)
            : rimStyle == 3 ? new Color(0.06f, 0.06f, 0.07f) : shape.RimColor;
        var spokes = rimStyle == 1 ? 5 : rimStyle == 2 ? 12 : rimStyle == 3 ? 6 : shape.Spokes;
        var b = new PartMeshBuilder();
        var r = shape.WheelRadius;
        var half = shape.TyreWidth * 0.5f;
        var rimRadius = r * 0.64f;

        b.Part(Rubber, 0.15f, 0f);
        b.Tyre(half, r, rimRadius, shape.Knobby ? 0.03f : 0.006f, 48, shape.Knobby);

        // Brake disc and caliper visible through the spokes.
        b.Part(new Color(0.45f, 0.45f, 0.47f), 0.6f, 0.9f);
        b.Lathe(new[] { new Vector2(half - 0.10f, 0.001f), new Vector2(half - 0.10f, rimRadius * 0.82f), new Vector2(half - 0.07f, rimRadius * 0.82f), new Vector2(half - 0.07f, 0.001f) }, 20, Matrix4x4.identity);
        b.Part(new Color(0.85f, 0.10f, 0.08f), 0.6f, 0.2f);
        b.Box(new Vector3(half - 0.07f, rimRadius * 0.62f, 0f), new Vector3(0.06f, rimRadius * 0.35f, rimRadius * 0.45f));

        // Rim: barrel lip, spokes and centre cap.
        b.Part(rimColor, rimStyle == 3 ? 0.6f : 0.75f, rimStyle == 3 ? 0.2f : 0.85f);
        b.Lathe(new[]
        {
            new Vector2(-half + 0.02f, rimRadius), new Vector2(half - 0.01f, rimRadius),
            new Vector2(half, rimRadius * 0.95f), new Vector2(half - 0.025f, rimRadius * 0.92f)
        }, 32, Matrix4x4.identity);
        // Deep dish: spokes sit back from a wide polished lip.
        var spokeX = rimStyle == 3 ? half - 0.09f : half - 0.03f;
        for (var i = 0; i < spokes; i++)
        {
            var matrix = Matrix4x4.Rotate(Quaternion.AngleAxis(360f / spokes * i, Vector3.right));
            b.Box(new Vector3(spokeX, rimRadius * 0.5f, 0f), new Vector3(0.03f, rimRadius * 0.85f, spokes > 7 ? 0.028f : 0.055f), matrix);
        }

        if (rimStyle == 3)
        {
            b.Part(Chrome, 0.9f, 0.95f);
            b.Lathe(new[] { new Vector2(half - 0.09f, rimRadius * 0.86f), new Vector2(half + 0.002f, rimRadius * 0.98f), new Vector2(half - 0.005f, rimRadius * 1.0f), new Vector2(half - 0.09f, rimRadius * 0.9f) }, 32, Matrix4x4.identity);
            b.Part(rimColor, 0.6f, 0.2f);
        }

        b.Lathe(new[] { new Vector2(half - 0.05f, 0.001f), new Vector2(half - 0.05f, rimRadius * 0.22f), new Vector2(half - 0.01f, rimRadius * 0.2f), new Vector2(half - 0.005f, 0.001f) }, 16, Matrix4x4.identity);

        mesh = b.Build(body + " Wheel", scale);
        WheelMeshes[(body, scale, rimStyle)] = mesh;
        return mesh;
    }

    // ------------------------------------------------------------------ contact shadow

    private static void CreateContactShadow(Transform parent, Shape shape)
    {
        if (_shadowMaterial == null)
        {
            var shader = Shader.Find("AR Coin Drive/Overlay");
            if (shader == null) return;
            _shadowMaterial = new Material(shader)
            {
                name = "Car Contact Shadow",
                mainTexture = ProceduralTextures.SoftDot,
                color = new Color(0f, 0f, 0f, 0.6f)
            };
        }

        if (_shadowMesh == null)
        {
            _shadowMesh = new Mesh { name = "Car Contact Shadow" };
            _shadowMesh.SetVertices(new[] { new Vector3(-1f, 0f, -1f), new Vector3(-1f, 0f, 1f), new Vector3(1f, 0f, 1f), new Vector3(1f, 0f, -1f) });
            _shadowMesh.SetUVs(0, new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right });
            _shadowMesh.SetColors(new[] { Color.white, Color.white, Color.white, Color.white });
            _shadowMesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            _shadowMesh.RecalculateBounds();
        }

        var shadow = new GameObject("Contact Shadow");
        shadow.transform.SetParent(parent, false);
        shadow.transform.localPosition = Vector3.up * 0.0015f;
        shadow.transform.localScale = new Vector3(1.15f, 1f, shape.Length * 0.62f) * CarCatalog.Scale;
        shadow.AddComponent<MeshFilter>().sharedMesh = _shadowMesh;
        var renderer = shadow.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = _shadowMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // ------------------------------------------------------------------ helpers

    private static float WidthAt(Station[] stations, float z)
    {
        for (var i = 0; i < stations.Length - 1; i++)
        {
            if (z <= stations[i].Z && z >= stations[i + 1].Z)
                return Mathf.Lerp(stations[i + 1].HalfWidth, stations[i].HalfWidth, Mathf.InverseLerp(stations[i + 1].Z, stations[i].Z, z));
        }

        return stations[stations.Length / 2].HalfWidth;
    }

    private static List<Station> Resample(Station[] stations, int stepsPerSpan)
    {
        var result = new List<Station>();
        for (var i = 0; i < stations.Length - 1; i++)
        {
            var p0 = stations[Mathf.Max(i - 1, 0)];
            var p1 = stations[i];
            var p2 = stations[i + 1];
            var p3 = stations[Mathf.Min(i + 2, stations.Length - 1)];
            for (var step = 0; step < stepsPerSpan; step++)
            {
                var t = step / (float)stepsPerSpan;
                result.Add(new Station(Mathf.Lerp(p1.Z, p2.Z, t),
                    CatmullRom(p0.HalfWidth, p1.HalfWidth, p2.HalfWidth, p3.HalfWidth, t),
                    CatmullRom(p0.Bottom, p1.Bottom, p2.Bottom, p3.Bottom, t),
                    CatmullRom(p0.Top, p1.Top, p2.Top, p3.Top, t)));
            }
        }

        result.Add(stations[stations.Length - 1]);
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

    /// <summary>Single-submesh builder that stamps each part's finish into vertex colour and UV1.</summary>
    private sealed class PartMeshBuilder
    {
        private readonly List<Vector3> _vertices = new List<Vector3>(8192);
        private readonly List<Color32> _colors = new List<Color32>(8192);
        private readonly List<Vector4> _surface = new List<Vector4>(8192);
        private readonly List<int> _triangles = new List<int>(16384);
        private Color32 _color;
        private Vector4 _finish;

        public void Part(Color color, float gloss, float metal, float emission = 0f, bool paint = false)
        {
            _color = new Color(color.r, color.g, color.b, paint ? 1f : 0f);
            _finish = new Vector4(gloss, metal, emission, 0f);
        }

        private int Add(Vector3 position)
        {
            _vertices.Add(position);
            _colors.Add(_color);
            _surface.Add(_finish);
            return _vertices.Count - 1;
        }

        public void Loft(IReadOnlyList<Station> stations, float exponent, int segments, Matrix4x4 matrix)
        {
            var start = _vertices.Count;
            var power = 2f / exponent;
            foreach (var station in stations)
            {
                var centerY = (station.Top + station.Bottom) * 0.5f;
                var halfHeight = Mathf.Max(0.0001f, (station.Top - station.Bottom) * 0.5f);
                for (var j = 0; j < segments; j++)
                {
                    var angle = j / (float)segments * Mathf.PI * 2f;
                    var x = Signed(Mathf.Cos(angle), power) * station.HalfWidth;
                    var y = centerY + Signed(Mathf.Sin(angle), power) * halfHeight;
                    Add(matrix.MultiplyPoint3x4(new Vector3(x, y, station.Z)));
                }
            }

            // Lofts authored nose-to-tail (z decreasing) would face inward: flip their winding.
            var flip = stations[stations.Count - 1].Z < stations[0].Z;
            for (var i = 0; i < stations.Count - 1; i++)
                Strip(start + i * segments, start + (i + 1) * segments, segments, flip);
            Cap(start, segments, flip);
            Cap(start + (stations.Count - 1) * segments, segments, !flip);
        }

        public void Lathe(IReadOnlyList<Vector2> profile, int segments, Matrix4x4 matrix)
        {
            var start = _vertices.Count;
            foreach (var point in profile)
            {
                for (var j = 0; j < segments; j++)
                {
                    var angle = j / (float)segments * Mathf.PI * 2f;
                    Add(matrix.MultiplyPoint3x4(new Vector3(point.x, Mathf.Cos(angle) * point.y, Mathf.Sin(angle) * point.y)));
                }
            }

            for (var i = 0; i < profile.Count - 1; i++)
                Strip(start + i * segments, start + (i + 1) * segments, segments, false);
        }

        public void LatheArc(IReadOnlyList<Vector2> profile, int segments, float halfAngle, Matrix4x4 matrix)
        {
            var start = _vertices.Count;
            var columns = segments + 1;
            foreach (var point in profile)
            {
                for (var j = 0; j < columns; j++)
                {
                    var angle = Mathf.Lerp(-halfAngle, halfAngle, j / (float)segments) * Mathf.Deg2Rad;
                    Add(matrix.MultiplyPoint3x4(new Vector3(point.x, Mathf.Cos(angle) * point.y, Mathf.Sin(angle) * point.y)));
                }
            }

            for (var i = 0; i < profile.Count - 1; i++)
            {
                var a = start + i * columns;
                var c = start + (i + 1) * columns;
                for (var j = 0; j < segments; j++)
                {
                    _triangles.Add(a + j); _triangles.Add(a + j + 1); _triangles.Add(c + j + 1);
                    _triangles.Add(a + j); _triangles.Add(c + j + 1); _triangles.Add(c + j);
                }
            }
        }

        public void Cylinder(Vector3 from, Vector3 to, float radius, int segments)
        {
            var length = Vector3.Distance(from, to);
            if (length < 0.00001f) return;
            var matrix = Matrix4x4.TRS(from, Quaternion.FromToRotation(Vector3.right, (to - from) / length), Vector3.one);
            Lathe(new[] { new Vector2(0f, 0.0001f), new Vector2(0f, radius), new Vector2(length, radius), new Vector2(length, 0.0001f) }, segments, matrix);
        }

        /// <summary>Tyre around local X: rounded sidewalls and a tread (blocky knobs when off-road).</summary>
        public void Tyre(float halfWidth, float radius, float beadRadius, float tread, int segments, bool knobby)
        {
            var profile = new[]
            {
                new Vector2(-halfWidth + 0.01f, beadRadius), new Vector2(-halfWidth, beadRadius + (radius - beadRadius) * 0.6f),
                new Vector2(-halfWidth * 0.9f, radius - tread), new Vector2(-halfWidth * 0.5f, radius),
                new Vector2(halfWidth * 0.5f, radius), new Vector2(halfWidth * 0.9f, radius - tread),
                new Vector2(halfWidth, beadRadius + (radius - beadRadius) * 0.6f), new Vector2(halfWidth - 0.01f, beadRadius)
            };
            var start = _vertices.Count;
            for (var i = 0; i < profile.Length; i++)
            {
                for (var j = 0; j < segments; j++)
                {
                    var raised = knobby && (i == 3 || i == 4) && ((j / 2 + i) % 2 == 0);
                    var r = profile[i].y - (raised ? 0f : (i == 3 || i == 4) && knobby ? tread : 0f);
                    var angle = j / (float)segments * Mathf.PI * 2f;
                    Add(new Vector3(profile[i].x, Mathf.Cos(angle) * r, Mathf.Sin(angle) * r));
                }
            }

            for (var i = 0; i < profile.Length - 1; i++)
                Strip(start + i * segments, start + (i + 1) * segments, segments, false);
        }

        public void Ellipsoid(Vector3 center, Vector3 radii)
        {
            const int rings = 8;
            var stations = new List<Station>(rings + 1);
            for (var i = 0; i <= rings; i++)
            {
                var phi = Mathf.PI * (1f - i / (float)rings);
                var scale = Mathf.Max(0.02f, Mathf.Sin(phi));
                stations.Add(new Station(center.z + Mathf.Cos(phi) * radii.z, radii.x * scale, center.y - radii.y * scale, center.y + radii.y * scale));
            }

            Loft(stations, 2f, 12, Matrix4x4.Translate(new Vector3(center.x, 0f, 0f)));
        }

        public void Box(Vector3 center, Vector3 size) => Box(center, size, Matrix4x4.identity);

        /// <summary>Box rotated by `rotation` around its own centre.</summary>
        public void Box(Vector3 center, Vector3 size, Matrix4x4 rotation)
        {
            var matrix = Matrix4x4.Translate(center) * rotation;
            var h = size * 0.5f;
            Face(matrix, Vector3.right, Vector3.up, Vector3.forward, h);
            Face(matrix, Vector3.left, Vector3.forward, Vector3.up, h);
            Face(matrix, Vector3.up, Vector3.forward, Vector3.right, h);
            Face(matrix, Vector3.down, Vector3.right, Vector3.forward, h);
            Face(matrix, Vector3.forward, Vector3.right, Vector3.up, h);
            Face(matrix, Vector3.back, Vector3.up, Vector3.right, h);
        }

        private void Face(Matrix4x4 matrix, Vector3 normal, Vector3 u, Vector3 v, Vector3 half)
        {
            var c = Vector3.Scale(normal, half);
            var du = Vector3.Scale(u, half);
            var dv = Vector3.Scale(v, half);
            Vector3 a = c - du - dv, b = c - du + dv, cc = c + du + dv, d = c + du - dv;
            if (Vector3.Dot(Vector3.Cross(b - a, cc - a), normal) < 0f)
            {
                var swap = b;
                b = d;
                d = swap;
            }

            var i0 = Add(matrix.MultiplyPoint3x4(a));
            var i1 = Add(matrix.MultiplyPoint3x4(b));
            var i2 = Add(matrix.MultiplyPoint3x4(cc));
            var i3 = Add(matrix.MultiplyPoint3x4(d));
            _triangles.Add(i0); _triangles.Add(i1); _triangles.Add(i2);
            _triangles.Add(i0); _triangles.Add(i2); _triangles.Add(i3);
        }

        private void Strip(int ringA, int ringB, int segments, bool flip)
        {
            for (var j = 0; j < segments; j++)
            {
                var next = (j + 1) % segments;
                if (flip)
                {
                    _triangles.Add(ringA + j); _triangles.Add(ringB + next); _triangles.Add(ringA + next);
                    _triangles.Add(ringA + j); _triangles.Add(ringB + j); _triangles.Add(ringB + next);
                    continue;
                }

                _triangles.Add(ringA + j); _triangles.Add(ringA + next); _triangles.Add(ringB + next);
                _triangles.Add(ringA + j); _triangles.Add(ringB + next); _triangles.Add(ringB + j);
            }
        }

        private void Cap(int ring, int segments, bool facesForward)
        {
            var start = _vertices.Count;
            var center = Vector3.zero;
            for (var j = 0; j < segments; j++)
            {
                var vertex = _vertices[ring + j];
                Add(vertex);
                center += vertex;
            }

            var centerIndex = Add(center / segments);
            for (var j = 0; j < segments; j++)
            {
                var next = (j + 1) % segments;
                _triangles.Add(centerIndex);
                _triangles.Add(start + (facesForward ? j : next));
                _triangles.Add(start + (facesForward ? next : j));
            }
        }

        public Mesh Build(string name, float scale)
        {
            for (var i = 0; i < _vertices.Count; i++) _vertices[i] *= scale;
            var mesh = new Mesh { name = name };
            mesh.indexFormat = _vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(_vertices);
            mesh.SetColors(_colors);
            mesh.SetUVs(1, _surface);
            mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        private static float Signed(float value, float power) => Mathf.Sign(value) * Mathf.Pow(Mathf.Abs(value), power);
    }
}
