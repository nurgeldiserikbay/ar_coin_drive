using System.Collections.Generic;
using UnityEngine;

/// <summary>The places you can drive without AR.</summary>
public enum GameLocation { House, Backyard, Workshop }

/// <summary>
/// The other virtual locations. They share the house's 3 x 3 zone grid (5 m zones; the middle
/// one stays clear for the track and the battle arena), its materials and its builders, so each
/// zone is again one shell mesh plus one furniture mesh and the camera culls zones out of view.
/// Unlike the house they are open plan: no interior walls, one boundary around the outside.
/// </summary>
public static partial class VirtualHouse
{
    public static readonly string[] LocationNames = { "HOUSE", "BACKYARD", "WORKSHOP" };
    public static readonly string[] LocationBlurbs =
    {
        "Nine rooms: living room, kitchen, garage, kids' room and more.",
        "Lawn, patio, pool, sandbox and a trampoline under open sky.",
        "A big garage: car lifts, tyre racks, a forklift and tool walls."
    };

    private delegate void ZoneBuilder(Room room, Transform parent);

    /// <summary>Builds each zone's meshes and camera spots, plus the shared site mesh and overlay.</summary>
    private static void BuildZones(Transform parent, Info info, Room site, ZoneBuilder[] zones, string[] names, float eyeHeight,
        List<(Vector3, Vector3, Vector3, Vector3, Color, Color)> overlay)
    {
        var materials = Materials();
        for (var index = 0; index < 9; index++)
        {
            var room = new Room(RoomCenter(index), info.Obstacles);
            zones[index]?.Invoke(room, parent);
            CreateRenderer(parent, names[index], room.B, materials, false);
            CreateRenderer(parent, names[index] + " Props", room.F, materials, true);

            var center = RoomCenter(index);
            var corner = RoomSize * 0.5f - 0.45f;
            info.CameraSpots.Add((new Vector3(center.x - corner, eyeHeight, center.y - corner), new Vector3(center.x, 0f, center.y), index));
            info.CameraSpots.Add((new Vector3(center.x + corner, eyeHeight, center.y + corner), new Vector3(center.x, 0f, center.y), index));
        }

        CreateRenderer(parent, "Site", site.B, materials, false);
        CreateRenderer(parent, "Site Props", site.F, materials, true);
        CreateOverlay(parent, overlay);
    }

    // ------------------------------------------------------------------ backyard

    private static Info BuildBackyard(Transform parent)
    {
        var edge = HalfExtent;
        var info = new Info
        {
            Bounds = Rect.MinMaxRect(-edge, -edge, edge, edge),
            SunDirection = new Vector3(0.35f, -0.78f, 0.52f).normalized,
            OpenPlan = true,
            Background = new Color(0.62f, 0.78f, 0.95f),
            SunColor = new Color(1f, 0.97f, 0.90f),
            SunIntensity = 1.05f,
            AmbientSky = new Color(0.62f, 0.72f, 0.86f),
            AmbientEquator = new Color(0.55f, 0.58f, 0.52f),
            AmbientGround = new Color(0.26f, 0.32f, 0.18f),
            FarClip = 90f,
            ShadowDistance = 6f
        };

        var overlay = new List<(Vector3, Vector3, Vector3, Vector3, Color, Color)>();
        var site = new Room(Vector2.zero, info.Obstacles);
        AddSky(overlay, new Color(0.86f, 0.92f, 0.97f), new Color(0.40f, 0.62f, 0.92f));

        // Lawn everywhere (beyond the fence too, so the horizon is green).
        site.Ground(M.Grass, 0f, 0f, 60f, 60f, 0f);

        // Back of the house along the south side: brick wall, roof edge, windows and a glass door.
        site.ShellBox(M.Brick, 0f, -edge - 4f, 0f, 0f, 3.2f, edge + 4f, 4f);
        site.ShellBox(M.Stone, 0f, -edge - 3.6f, 0f, 3.2f, 3.4f, edge + 4.4f, 4.1f, false);
        var wallFace = new Vector3(0f, 0f, -edge + 0.004f);
        foreach (var x in new[] { -5f, 5f })
        {
            Window(overlay, wallFace + new Vector3(x, 0f, 0f), Vector3.right, 0.8f, 1.0f, 2.2f, new Color(0.62f, 0.74f, 0.84f), new Color(0.40f, 0.52f, 0.66f));
            site.ShellBox(M.Trim, x, -edge + 0.03f, 0f, 0.96f, 1.0f, 0.86f, 0.05f, false);
        }

        Window(overlay, wallFace, Vector3.right, 1.0f, 0.02f, 2.2f, new Color(0.55f, 0.66f, 0.76f), new Color(0.38f, 0.48f, 0.60f));
        site.ShellBox(M.Trim, 0f, -edge + 0.02f, 0f, 2.2f, 2.28f, 1.06f, 0.04f, false);
        foreach (var x in new[] { -1.04f, 0f, 1.04f })
            site.ShellBox(M.Trim, x, -edge + 0.02f, 0f, 0f, 2.2f, 0.03f, 0.04f, false);

        // Wooden fence on the other three sides, with posts every couple of metres.
        foreach (var side in new[] { -1f, 1f })
        {
            site.ShellBox(M.Fence, side * edge, 0f, 0f, 0f, 1.6f, 0.03f, edge);
            for (var z = -edge + 0.4f; z <= edge; z += 2.1f) site.ShellBox(M.Bark, side * (edge - 0.05f), z, 0f, 0f, 1.7f, 0.05f, 0.05f, false);
        }

        site.ShellBox(M.Fence, 0f, edge, 0f, 0f, 1.6f, edge, 0.03f);
        for (var x = -edge + 0.4f; x <= edge; x += 2.1f) site.ShellBox(M.Bark, x, edge - 0.05f, 0f, 0f, 1.7f, 0.05f, 0.05f, false);

        // Neighbours' trees beyond the fence give the view some depth.
        var random = new System.Random(5);
        for (var i = 0; i < 18; i++)
        {
            var angle = i / 18f * Mathf.PI * 2f;
            var distance = 13f + (float)random.NextDouble() * 9f;
            var at = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
            if (at.y < -edge - 2f) continue; // behind the house
            var size = 1.6f + (float)random.NextDouble() * 1.6f;
            site.Cyl(M.Bark, at.x, at.y, 0f, size * 1.4f, 0.18f, 0.12f, false, 8);
            site.Ball(M.Hedge, at.x, size * 1.6f, at.y, size, size * 1.1f, size);
        }

        var zones = new ZoneBuilder[]
        {
            (r, p) => Shed(r), (r, p) => Patio(r), (r, p) => VeggieGarden(r),
            (r, p) => Sandbox(r), (r, p) => r.Patch(M.Grass, 0f, 0f, 0.01f, 0.01f), (r, p) => Pool(r, overlay),
            (r, p) => Trampoline(r), (r, p) => GardenTree(r), (r, p) => SwingSet(r)
        };
        BuildZones(parent, info, site, zones, new[] { "Shed", "Patio", "Vegetables", "Sandbox", "Lawn", "Pool", "Trampoline", "Tree", "Swings" }, 1.5f, overlay);
        return info;
    }

    private static void Shed(Room r)
    {
        // Garden shed in the corner, a wheelbarrow and a stack of plant pots.
        r.Ground(M.Paving, -1.3f, -1.3f, 1.3f, 1.3f, 0.004f);
        r.Box(M.Fence, -1.4f, -1.5f, 0f, 0f, 2.1f, 1.0f, 0.85f);
        r.Box(M.Stone, -1.4f, -1.5f, 0f, 2.1f, 2.18f, 1.12f, 0.98f, false);
        r.Box(M.DarkWood, -1.4f, -0.63f, 0f, 0.02f, 1.85f, 0.38f, 0.02f, false);
        r.Box(M.Machine, 0.8f, 0.6f, 30f, 0.30f, 0.55f, 0.30f, 0.45f, false);
        r.Cyl(M.Rubber, 0.8f + 0.25f, 0.6f + 0.42f, 0.0f, 0.30f, 0.15f, 0.15f, true, 12);
        foreach (var (x, z) in new[] { (0.6f, 0.4f), (1.0f, 0.2f) }) r.Box(M.Steel, x, z, 30f, 0f, 0.3f, 0.025f, 0.025f);
        for (var i = 0; i < 3; i++) r.Cyl(M.Terracotta, 1.8f, -1.8f, i * 0.16f, i * 0.16f + 0.2f, 0.16f, 0.19f, i == 0);
    }

    private static void Patio(Room r)
    {
        // Stone patio by the back door, table with chairs, parasol, barbecue, plant pots.
        r.Ground(M.Paving, 0f, -0.4f, 2.5f, 2.1f, 0.004f);
        r.Table(M.White, M.Steel, -0.9f, 0.2f, 0f, 0.55f, 0.55f, 0.74f, 0.025f);
        r.Cyl(M.Steel, -0.9f, 0.2f, 0.74f, 2.2f, 0.02f, 0.02f, false, 8);
        r.Cyl(M.ToyYellow, -0.9f, 0.2f, 2.0f, 2.25f, 1.25f, 0.05f, false, 16);
        foreach (var (x, z, a) in new[] { (-1.65f, 0.2f, 90f), (-0.15f, 0.2f, 270f), (-0.9f, 0.95f, 180f) })
        {
            r.Table(M.White, M.Steel, x, z, a, 0.21f, 0.21f, 0.44f, 0.015f);
            var back = CarCollision.Forward(a) * -0.19f;
            r.Box(M.White, x + back.x, z + back.y, a, 0.44f, 0.90f, 0.21f, 0.02f, false);
        }

        r.Box(M.Black, 1.7f, -1.4f, 0f, 0.75f, 1.0f, 0.38f, 0.25f, false);
        r.Ball(M.Black, 1.7f, 1.0f, -1.4f, 0.38f, 0.16f, 0.25f);
        foreach (var x in new[] { 1.40f, 2.0f }) r.Box(M.Steel, x, -1.4f, 0f, 0f, 0.75f, 0.02f, 0.2f);
        foreach (var x in new[] { -2.1f, 2.1f })
        {
            r.Cyl(M.Terracotta, x, -2.1f, 0f, 0.42f, 0.18f, 0.22f);
            r.Ball(M.Leaves, x, 0.75f, -2.1f, 0.30f, 0.38f, 0.30f);
        }
    }

    private static void VeggieGarden(Room r)
    {
        // Raised beds with rows of plants, a watering can and a compost bin.
        foreach (var x in new[] { -1.2f, 0.6f })
        {
            r.Box(M.Fence, x, -0.4f, 0f, 0f, 0.30f, 0.55f, 1.4f);
            r.Box(M.Bark, x, -0.4f, 0f, 0.30f, 0.31f, 0.50f, 1.35f, false);
            for (var i = 0; i < 6; i++)
                r.Ball(i % 2 == 0 ? M.Leaves : M.ToyGreen, x + (i % 2 == 0 ? -0.2f : 0.2f), 0.42f, -1.5f + i * 0.45f, 0.16f, 0.14f, 0.16f);
        }

        r.Cyl(M.ToyGreen, 1.9f, 1.8f, 0f, 0.25f, 0.13f, 0.11f);
        r.Box(M.Black, 2.0f, -2.0f, 0f, 0f, 0.9f, 0.35f, 0.35f);
        r.Patch(M.Bark, -0.3f, -0.4f, 0.3f, 1.8f, 0.003f);
    }

    private static void Sandbox(Room r)
    {
        // Sand pit with a low wooden border (a gap lets cars drive in), bucket, spade and toys.
        r.Ground(M.Sand, 0f, 0f, 1.3f, 1.3f, 0.006f);
        r.Box(M.Fence, 0f, 1.33f, 0f, 0f, 0.12f, 1.36f, 0.04f);
        r.Box(M.Fence, -1.33f, 0f, 0f, 0f, 0.12f, 0.04f, 1.36f);
        r.Box(M.Fence, 1.33f, 0.55f, 0f, 0f, 0.12f, 0.04f, 0.81f);
        r.Box(M.Fence, 0f, -1.33f, 0f, 0f, 0.12f, 1.36f, 0.04f);
        r.Cyl(M.ToyRed, -0.6f, 0.5f, 0f, 0.20f, 0.12f, 0.15f);
        r.Box(M.ToyBlue, 0.4f, -0.5f, 25f, 0.006f, 0.03f, 0.06f, 0.22f, false);
        r.Box(M.ToyYellow, 0.6f, 0.7f, -20f, 0f, 0.16f, 0.16f, 0.22f);
        r.Ball(M.ToyGreen, -2.0f, 0.12f, -1.9f, 0.12f, 0.12f, 0.12f, true);
    }

    private static void Pool(Room r, List<(Vector3, Vector3, Vector3, Vector3, Color, Color)> overlay)
    {
        // Pool with a stone edge (cars stay out), sun loungers and a towel.
        r.Ground(M.Paving, 0.2f, 0f, 2.3f, 1.9f, 0.004f);
        r.Box(M.Paving, 0.3f, 0f, 0f, 0f, 0.10f, 1.7f, 1.3f);
        r.Box(M.Water, 0.3f, 0f, 0f, 0.10f, 0.105f, 1.55f, 1.15f, false);
        r.Box(M.Steel, 1.85f, 0.9f, 0f, 0.10f, 0.9f, 0.02f, 0.02f, false);
        foreach (var z in new[] { -1.4f, 1.4f })
        {
            r.Box(M.White, -2.0f, z, 90f, 0.18f, 0.24f, 0.30f, 0.85f, false);
            r.Box(M.White, -2.0f - 0.0f, z, 90f, 0.24f, 0.55f, 0.30f, 0.06f, false);
            foreach (var (dx, dz) in new[] { (-0.25f, -0.7f), (0.25f, -0.7f), (-0.25f, 0.7f), (0.25f, 0.7f) })
                r.Box(M.Steel, -2.0f + dz, z + dx, 0f, 0f, 0.18f, 0.02f, 0.02f);
        }

        r.Patch(M.ToyRed, -2.0f, 0f, 0.35f, 0.18f, 0.008f);
    }

    private static void Trampoline(Room r)
    {
        // Round trampoline on legs (drive underneath), a garden bench and a hedge.
        r.Cyl(M.Black, -0.4f, 0.3f, 0.70f, 0.72f, 1.25f, 1.25f, false, 24);
        r.Cyl(M.ToyBlue, -0.4f, 0.3f, 0.66f, 0.74f, 1.35f, 1.35f, false, 24);
        for (var i = 0; i < 6; i++)
        {
            var a = i / 6f * Mathf.PI * 2f;
            r.Cyl(M.Steel, -0.4f + Mathf.Cos(a) * 1.3f, 0.3f + Mathf.Sin(a) * 1.3f, 0f, 0.68f, 0.03f, 0.03f, true, 8);
        }

        r.Box(M.Fence, -2.0f, -1.6f, 90f, 0.40f, 0.45f, 0.75f, 0.22f, false);
        foreach (var x in new[] { -0.6f, 0.6f }) r.Box(M.Bark, -2.0f, -1.6f + x, 90f, 0f, 0.40f, 0.18f, 0.04f);
        r.Box(M.Hedge, -2.25f, 1.6f, 0f, 0f, 1.1f, 0.25f, 0.85f);
    }

    private static void GardenTree(Room r)
    {
        // A big tree with a stepping-stone path and a flower bed around it.
        r.Cyl(M.Bark, 0.6f, 0.8f, 0f, 2.6f, 0.24f, 0.16f, true, 12);
        r.Ball(M.Leaves, 0.6f, 3.3f, 0.8f, 1.9f, 1.5f, 1.9f);
        r.Ball(M.Hedge, 1.3f, 2.9f, 0.3f, 1.2f, 1.0f, 1.2f);
        for (var i = 0; i < 7; i++) r.Ground(M.Paving, -1.6f + i * 0.08f, -2.2f + i * 0.62f, 0.24f, 0.2f, 0.005f);
        r.Ground(M.Bark, 1.9f, -1.6f, 0.5f, 0.8f, 0.004f);
        for (var i = 0; i < 6; i++)
            r.Ball(i % 3 == 0 ? M.ToyRed : i % 3 == 1 ? M.ToyYellow : M.FabricBeige, 1.7f + (i % 2) * 0.35f, 0.18f, -2.2f + i * 0.24f, 0.09f, 0.09f, 0.09f);
    }

    private static void SwingSet(Room r)
    {
        // Swing frame (legs solid), two swings, a slide and a paddling pool.
        foreach (var x in new[] { -1.6f, 0.6f })
        foreach (var z in new[] { 0.9f, 1.9f })
            r.Box(M.Machine, x, z, 0f, 0f, 2.0f, 0.04f, 0.04f);
        r.Cyl(M.Machine, -0.5f, 1.4f, 2.0f, 2.06f, 0.06f, 0.06f, false, 8);
        r.Box(M.Machine, -0.5f, 1.4f, 0f, 2.0f, 2.06f, 1.15f, 0.04f, false);
        foreach (var x in new[] { -1.0f, 0.1f })
        {
            r.Box(M.Black, x, 1.4f, 0f, 0.45f, 0.48f, 0.22f, 0.10f, false);
            foreach (var dx in new[] { -0.2f, 0.2f }) r.Box(M.Steel, x + dx, 1.4f, 0f, 0.48f, 2.0f, 0.008f, 0.008f, false);
        }

        r.Box(M.ToyYellow, 1.8f, -1.2f, 0f, 0f, 1.2f, 0.25f, 0.25f);
        r.Box(M.ToyRed, 1.8f, -0.35f, 0f, 0.3f, 0.36f, 0.25f, 0.65f, false);
        r.Ground(M.Water, -1.2f, -1.4f, 0.6f, 0.6f, 0.15f);
        r.Cyl(M.ToyBlue, -1.2f, -1.4f, 0f, 0.16f, 0.85f, 0.85f, true, 20);
    }

    // ------------------------------------------------------------------ workshop

    private static Info BuildWorkshop(Transform parent)
    {
        var edge = HalfExtent;
        const float height = 5.2f;
        var info = new Info
        {
            Bounds = Rect.MinMaxRect(-edge, -edge, edge, edge),
            SunDirection = new Vector3(0.30f, -0.85f, 0.42f).normalized,
            OpenPlan = true,
            Background = new Color(0.20f, 0.21f, 0.23f),
            SunColor = new Color(0.96f, 0.97f, 1f),
            SunIntensity = 0.80f,
            AmbientSky = new Color(0.52f, 0.54f, 0.58f),
            AmbientEquator = new Color(0.40f, 0.40f, 0.42f),
            AmbientGround = new Color(0.22f, 0.22f, 0.22f),
            FarClip = 30f,
            ShadowDistance = 6f
        };

        var overlay = new List<(Vector3, Vector3, Vector3, Vector3, Color, Color)>();
        var site = new Room(Vector2.zero, info.Obstacles);
        site.Ground(M.Tarmac, 0f, 0f, edge, edge, 0f);

        // Painted floor: a ring in the middle and lane lines along the zone edges.
        for (var i = 0; i < 2; i++)
        {
            var line = (i - 0.5f) * Pitch;
            site.Ground(M.LinePaint, line, 0f, 0.05f, edge - 0.2f, 0.002f);
            site.Ground(M.LinePaint, 0f, line, edge - 0.2f, 0.05f, 0.002f);
        }

        // Walls: steel lower panels, grey upper, a strip of high windows and two roller doors.
        var walls = new[] { (0f, -edge, 0f), (0f, edge, 0f), (-edge, 0f, 90f), (edge, 0f, 90f) };
        foreach (var (x, z, a) in walls)
        {
            site.ShellBox(M.WallGrey, x, z, a, 0f, height, edge + 0.1f, 0.1f);
            var inward = new Vector3(-x, 0f, -z).normalized;
            site.ShellBox(M.Machine, x + inward.x * 0.11f, z + inward.z * 0.11f, a, 0f, 1.2f, edge, 0.01f, false);
            var along = a == 0f ? Vector3.right : Vector3.forward;
            var face = new Vector3(x, 0f, z) + inward * 0.12f;
            for (var w = -2; w <= 2; w++)
                Window(overlay, face + along * (w * 2.9f), along, 1.1f, 3.6f, 4.6f, new Color(0.80f, 0.88f, 0.95f), new Color(0.70f, 0.82f, 0.95f));
        }

        foreach (var x in new[] { -Pitch, Pitch })
        {
            Window(overlay, new Vector3(x, 0f, edge - 0.13f), Vector3.right, 1.7f, 0.0f, 3.4f, new Color(0.46f, 0.48f, 0.50f), new Color(0.56f, 0.58f, 0.60f));
            for (var y = 0.3f; y < 3.4f; y += 0.3f)
                site.ShellBox(M.Steel, x, edge - 0.14f, 0f, y, y + 0.03f, 1.7f, 0.01f, false);
        }

        // Roof and light strips (unlit, like the house ceilings).
        var roof = new Color(0.30f, 0.31f, 0.33f);
        overlay.Add((new Vector3(-edge, height, -edge), new Vector3(edge, height, -edge), new Vector3(edge, height, edge), new Vector3(-edge, height, edge), roof, roof));
        for (var i = -1; i <= 1; i++)
        for (var j = -1; j <= 1; j++)
        {
            var c = new Vector3(i * Pitch, height - 0.01f, j * Pitch);
            var lamp = new Color(1f, 0.99f, 0.95f);
            overlay.Add((c + new Vector3(-1.2f, 0f, -0.12f), c + new Vector3(1.2f, 0f, -0.12f), c + new Vector3(1.2f, 0f, 0.12f), c + new Vector3(-1.2f, 0f, 0.12f), lamp, lamp));
        }

        // Steel columns at the four inner grid crossings.
        foreach (var x in new[] { -0.5f, 0.5f })
        foreach (var z in new[] { -0.5f, 0.5f })
        {
            site.ShellBox(M.LinePaint, x * Pitch, z * Pitch, 0f, 0f, 1.0f, 0.13f, 0.13f);
            site.ShellBox(M.Steel, x * Pitch, z * Pitch, 0f, 1.0f, height, 0.12f, 0.12f, false);
        }

        var zones = new ZoneBuilder[]
        {
            CarLift, (r, p) => ToolWall(r), (r, p) => TyreRacks(r),
            ParkedMuscle, (r, p) => r.Patch(M.Tarmac, 0f, 0f, 0.01f, 0.01f), (r, p) => Pallets(r),
            (r, p) => PaintBay(r), (r, p) => WorkshopOffice(r), (r, p) => Crates(r)
        };
        BuildZones(parent, info, site, zones, new[] { "Lift", "Tools", "Tyres", "Bay", "Floor", "Pallets", "Paint", "Office", "Crates" }, 1.55f, overlay);
        return info;
    }

    private static void CarLift(Room r, Transform parent)
    {
        // A two-post lift with a car raised overhead: the RC car drives right under it.
        foreach (var x in new[] { -1.25f, 1.25f })
        {
            r.Box(M.Machine, x, 0f, 0f, 0f, 2.6f, 0.14f, 0.14f);
            r.Box(M.Steel, x * 0.55f, 0.6f, 20f, 1.25f, 1.3f, 0.55f, 0.05f, false);
            r.Box(M.Steel, x * 0.55f, -0.6f, -20f, 1.25f, 1.3f, 0.55f, 0.05f, false);
        }

        var raised = new GameObject("Lifted car").transform;
        raised.SetParent(parent, false);
        raised.localPosition = new Vector3(0f, 1.3f, 0f);
        CarModelBuilder.CreateParked(raised, CarBody.Super, new Color(0.85f, 0.10f, 0.08f), r.Center, 90f, 1f);
        r.Box(M.Black, 2.0f, -2.0f, 0f, 0f, 0.6f, 0.25f, 0.25f);
        r.Patch(M.Black, 0f, 0f, 0.8f, 0.6f, 0.002f);
    }

    private static void ToolWall(Room r)
    {
        // Red tool chests along the wall, a workbench and a pegboard of tools.
        for (var i = 0; i < 4; i++)
        {
            r.Box(M.ToyRed, -1.65f + i * 1.1f, -2.15f, 0f, 0.06f, 1.0f, 0.50f, 0.28f);
            r.Box(M.Black, -1.65f + i * 1.1f, -2.15f, 0f, 0f, 0.06f, 0.48f, 0.26f, false);
            for (var d = 0; d < 4; d++)
                r.Box(M.Chrome, -1.65f + i * 1.1f, -1.86f, 0f, 0.2f + d * 0.22f, 0.22f + d * 0.22f, 0.35f, 0.01f, false);
        }

        r.Box(M.Wood, 0f, -2.36f, 0f, 1.2f, 2.4f, 2.2f, 0.02f, false);
        for (var i = 0; i < 10; i++)
            r.Box(i % 3 == 0 ? M.ToyYellow : M.Steel, -1.9f + i * 0.42f, -2.33f, (i * 37) % 30 - 15, 1.5f + (i % 3) * 0.25f, 1.8f + (i % 3) * 0.25f, 0.03f, 0.01f, false);
        r.Table(M.Wood, M.Steel, 1.3f, 1.2f, 0f, 0.80f, 0.40f, 0.90f, 0.03f);
        r.Box(M.Machine, 1.0f, 1.2f, 0f, 0.90f, 1.2f, 0.18f, 0.15f, false);
    }

    private static void TyreRacks(Room r)
    {
        // Racks of tyres (each rack a solid block), a loose stack and a balancing machine.
        foreach (var x in new[] { 1.0f, 2.0f })
        {
            r.Box(M.Steel, x, 0f, 0f, 0f, 2.4f, 0.30f, 1.9f);
            for (var shelf = 0; shelf < 3; shelf++)
            for (var t = 0; t < 6; t++)
                r.Cyl(M.Rubber, x, -1.6f + t * 0.64f, 0.12f + shelf * 0.8f, 0.72f + shelf * 0.8f, 0.30f, 0.30f, false, 16);
        }

        for (var i = 0; i < 5; i++) r.Cyl(M.Rubber, -1.4f, -1.6f, i * 0.2f, i * 0.2f + 0.19f, 0.31f, 0.31f, i == 0, 18);
        r.Box(M.Machine, -1.6f, 1.6f, 0f, 0f, 1.0f, 0.35f, 0.40f);
        r.Cyl(M.Chrome, -1.6f, 1.25f, 1.0f, 1.1f, 0.2f, 0.2f, false, 12);
    }

    private static void ParkedMuscle(Room r, Transform parent)
    {
        // A muscle car on the floor (tyres solid, drive under the body) and oil drums.
        var contacts = CarModelBuilder.CreateParked(parent, CarBody.Muscle, new Color(0.08f, 0.08f, 0.09f), r.Center + new Vector2(-0.3f, 0.2f), 0f, 1f);
        var tyre = CarModelBuilder.TyreWidthAt(CarBody.Muscle, 1f) * 0.6f;
        foreach (var contact in contacts) r.Obstacles.Add(WorldObstacle.Circle(contact, tyre));
        foreach (var (x, z, m) in new[] { (-2.0f, -1.9f, M.Machine), (-1.4f, -2.0f, M.Orange), (-2.0f, -1.3f, M.ToyRed) })
            r.Cyl(m, x, z, 0f, 0.88f, 0.29f, 0.29f, true, 16);
        r.Patch(M.Black, -0.3f, 1.5f, 0.5f, 0.35f, 0.002f);
    }

    private static void Pallets(Room r)
    {
        // Pallets with shrink-wrapped boxes and a small forklift.
        foreach (var (x, z, stack) in new[] { (1.6f, -1.5f, 3), (1.6f, 0f, 2), (1.6f, 1.5f, 1) })
        {
            r.Box(M.Pallet, x, z, 0f, 0f, 0.14f, 0.60f, 0.50f);
            for (var s = 0; s < stack; s++)
                r.Box(M.Cardboard, x, z, s * 7f, 0.14f + s * 0.45f, 0.58f + s * 0.45f, 0.52f, 0.44f, false);
        }

        // Forklift: body, mast and forks raised high enough to drive under.
        r.Box(M.ToyYellow, -1.0f, 0.2f, 90f, 0f, 1.2f, 0.55f, 0.80f);
        r.Box(M.Black, -1.0f, 0.2f, 90f, 1.2f, 2.1f, 0.50f, 0.04f, false);
        r.Box(M.Steel, -0.10f, 0.2f, 90f, 0f, 2.3f, 0.45f, 0.06f);
        foreach (var dz in new[] { -0.25f, 0.25f }) r.Box(M.Steel, 0.45f, 0.2f + dz, 0f, 0.55f, 0.6f, 0.55f, 0.05f, false);
        foreach (var dz in new[] { -0.5f, 0.5f })
        foreach (var dx in new[] { -1.45f, -0.55f })
            r.Cyl(M.Rubber, dx, 0.2f + dz, 0f, 0.32f, 0.16f, 0.16f, false, 12);
    }

    private static void PaintBay(Room r)
    {
        // Spray booth frame with plastic curtains, a compressor and paint tins.
        r.Ground(M.Concrete, 0f, 0f, 2.3f, 2.3f, 0.003f);
        foreach (var x in new[] { -2.2f, 2.2f })
        foreach (var z in new[] { -2.2f, 2.2f })
            r.Box(M.Steel, x, z, 0f, 0f, 3.0f, 0.05f, 0.05f);
        r.ShellBox(M.Glass, -2.2f, 0f, 0f, 0.4f, 2.8f, 0.01f, 2.15f, false);
        r.ShellBox(M.Glass, 0f, -2.2f, 0f, 0.4f, 2.8f, 2.15f, 0.01f, false);
        r.Box(M.Machine, 1.5f, 1.6f, 0f, 0f, 0.7f, 0.45f, 0.30f);
        r.Cyl(M.Machine, 1.5f, 1.6f, 0.7f, 0.95f, 0.22f, 0.22f, false, 12);
        for (var i = 0; i < 5; i++)
            r.Cyl(i % 2 == 0 ? M.ToyBlue : M.ToyRed, -1.8f + i * 0.25f, 1.9f, 0f, 0.25f, 0.10f, 0.10f, i == 0, 12);
    }

    private static void WorkshopOffice(Room r)
    {
        // Glass-fronted office corner: desk, chair, filing cabinet, a water cooler.
        r.ShellBox(M.Glass, 0f, 0.6f, 0f, 0f, 2.6f, 2.5f, 0.03f);
        r.ShellBox(M.Steel, 0f, 0.6f, 0f, 2.6f, 2.7f, 2.5f, 0.05f, false);
        r.Table(M.DarkWood, M.Steel, -1.0f, 1.8f, 0f, 0.75f, 0.40f, 0.75f, 0.025f);
        r.Cyl(M.Black, -1.0f, 1.2f, 0f, 0.06f, 0.24f, 0.24f);
        r.Box(M.Black, -1.0f, 1.2f, 0f, 0.45f, 0.53f, 0.24f, 0.24f, false);
        r.Box(M.Steel, 1.6f, 2.1f, 0f, 0f, 1.3f, 0.25f, 0.30f);
        r.Cyl(M.ToyBlue, 1.0f, -1.6f, 0f, 1.1f, 0.16f, 0.16f);
        r.Cyl(M.White, 1.0f, -1.6f, 1.1f, 1.4f, 0.13f, 0.13f, false, 12);
    }

    private static void Crates(Room r)
    {
        // Wooden crates in stacks and a spare engine on a stand.
        var stacks = new[] { (1.5f, 1.5f, 3), (0.5f, 1.8f, 2), (1.8f, 0.4f, 1), (-1.8f, 1.8f, 2) };
        foreach (var (x, z, n) in stacks)
            for (var s = 0; s < n; s++)
                r.Box(M.Pallet, x, z, s * 9f, s * 0.5f, s * 0.5f + 0.48f, 0.35f, 0.35f, s == 0);
        r.Table(M.Steel, M.Steel, -1.0f, -1.2f, 0f, 0.45f, 0.35f, 0.5f, 0.03f);
        r.Box(M.Steel, -1.0f, -1.2f, 0f, 0.5f, 1.0f, 0.38f, 0.28f, false);
        r.Cyl(M.Machine, -1.0f, -1.2f, 1.0f, 1.12f, 0.20f, 0.20f, false, 12);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>A glowing pane (window or door glass) facing into the space.</summary>
    private static void Window(List<(Vector3, Vector3, Vector3, Vector3, Color, Color)> overlay, Vector3 center, Vector3 along, float halfWidth,
        float y0, float y1, Color low, Color high)
    {
        overlay.Add((center - along * halfWidth + Vector3.up * y0, center + along * halfWidth + Vector3.up * y0,
            center + along * halfWidth + Vector3.up * y1, center - along * halfWidth + Vector3.up * y1, low, high));
    }

    /// <summary>Unlit sky box: a horizon-to-zenith gradient on the sides and a plain top.</summary>
    private static void AddSky(List<(Vector3, Vector3, Vector3, Vector3, Color, Color)> overlay, Color horizon, Color zenith)
    {
        const float d = 45f;
        const float top = 40f;
        var corners = new[] { new Vector3(-d, 0f, -d), new Vector3(d, 0f, -d), new Vector3(d, 0f, d), new Vector3(-d, 0f, d) };
        for (var i = 0; i < 4; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % 4];
            overlay.Add((a + Vector3.down, b + Vector3.down, b + Vector3.up * top, a + Vector3.up * top, horizon, zenith));
        }

        overlay.Add((corners[0] + Vector3.up * top, corners[1] + Vector3.up * top, corners[2] + Vector3.up * top, corners[3] + Vector3.up * top, zenith, zenith));
    }
}
