using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The house used when AR is unavailable: nine furnished rooms on a 3 x 3 grid, linked by
/// doorways. Each room has its own floor, wall colour, lighting fixture and furniture, and is a
/// single mesh holding only the materials it uses, so the camera culls rooms out of view and a
/// visible room costs a handful of draw calls. Furniture registers solid footprints (table and
/// bed legs only, so the car can drive underneath), and walls block everywhere but doorways.
/// The living room in the middle stays clear enough for the race track and battle arena.
/// </summary>
public static partial class VirtualHouse
{
    public const float RoomSize = 5f;
    public const float Wall = 0.12f;
    public const float Pitch = RoomSize + Wall;
    public const float Height = 2.6f;
    private const float Door = 1.1f;
    private const float DoorHeight = 2.1f;
    public static float HalfExtent => Pitch * 1.5f - Wall * 0.5f;

    public static readonly string[] RoomNames =
    {
        "Garage", "Hall", "Laundry",
        "Office", "Living room", "Kitchen",
        "Bedroom", "Bathroom", "Kids' room"
    };

    public sealed class Info
    {
        public readonly List<WorldObstacle> Obstacles = new List<WorldObstacle>();
        public readonly List<(Vector3 position, Vector3 lookAt, int room)> CameraSpots = new List<(Vector3, Vector3, int)>();
        public Rect Bounds;
        public Vector3 SunDirection;

        // How the location is lit and framed (indoor defaults: the house).
        public bool OpenPlan;   // no interior walls: the chase camera may roam the whole area
        public Color Background = new Color(0.80f, 0.80f, 0.78f);
        public Color SunColor = new Color(1f, 0.95f, 0.86f);
        public float SunIntensity = 0.88f;
        public Color AmbientSky = new Color(0.50f, 0.52f, 0.56f);
        public Color AmbientEquator = new Color(0.40f, 0.37f, 0.34f);
        public Color AmbientGround = new Color(0.24f, 0.20f, 0.16f);
        public float FarClip = 20f;
        public float ShadowDistance = 4.5f;

        /// <summary>Room index 0..8 (column + 3 * row) for a floor point.</summary>
        public int RoomAt(Vector2 point)
        {
            var column = Mathf.Clamp(Mathf.FloorToInt((point.x + Pitch * 1.5f) / Pitch), 0, 2);
            var row = Mathf.Clamp(Mathf.FloorToInt((point.y + Pitch * 1.5f) / Pitch), 0, 2);
            return column + row * 3;
        }

        public Rect RoomRect(int room)
        {
            if (OpenPlan) return Bounds;
            var center = RoomCenter(room);
            return new Rect(center.x - RoomSize * 0.5f, center.y - RoomSize * 0.5f, RoomSize, RoomSize);
        }
    }

    public static Vector2 RoomCenter(int room) => new Vector2((room % 3 - 1) * Pitch, (room / 3 - 1) * Pitch);

    // ------------------------------------------------------------------ materials

    private enum M
    {
        Oak, DarkFloor, Checker, SmallTile, CarpetBeige, CarpetBlue, Concrete,
        WallWarm, WallYellow, WallBlue, WallMint, WallGreen, WallGrey, WallTile, Ceiling, Trim,
        Wood, DarkWood, White, Black, Steel, Chrome, FabricBlue, FabricMustard, FabricBeige, FabricGreen,
        Cardboard, Terracotta, Leaves, Porcelain, Stone, ToyRed, ToyBlue, ToyYellow, ToyGreen, Rubber, Orange, Bedding,
        Grass, Paving, Brick, Sand, Water, Fence, Hedge, Bark, Tarmac, LinePaint, Machine, Pallet, Glass,
        Count
    }

    private static Material[] _materials;

    private static Material[] Materials()
    {
        if (_materials != null) return _materials;
        var m = new Material[(int)M.Count];
        Material Mat(Color c, float gloss = 0.3f, float metal = 0f, Texture tex = null) => ProceduralCarFactory.GetMaterial(c, gloss, metal, tex, 1f);
        m[(int)M.Oak] = Mat(new Color(0.82f, 0.80f, 0.78f), 0.45f, 0f, ProceduralTextures.Wood);
        m[(int)M.DarkFloor] = Mat(new Color(0.48f, 0.40f, 0.36f), 0.5f, 0f, ProceduralTextures.Wood);
        m[(int)M.Checker] = Mat(new Color(0.95f, 0.93f, 0.90f), 0.55f, 0f, ProceduralTextures.CheckerTiles);
        m[(int)M.SmallTile] = Mat(new Color(0.80f, 0.82f, 0.84f), 0.35f, 0f, ProceduralTextures.SmallTiles);
        m[(int)M.CarpetBeige] = Mat(new Color(0.80f, 0.72f, 0.62f), 0.04f, 0f, ProceduralTextures.Carpet);
        m[(int)M.CarpetBlue] = Mat(new Color(0.46f, 0.54f, 0.66f), 0.04f, 0f, ProceduralTextures.Carpet);
        m[(int)M.Concrete] = Mat(new Color(0.85f, 0.85f, 0.85f), 0.15f, 0f, ProceduralTextures.Concrete);
        m[(int)M.WallWarm] = Mat(new Color(0.86f, 0.83f, 0.78f), 0.08f);
        m[(int)M.WallYellow] = Mat(new Color(0.93f, 0.86f, 0.64f), 0.08f);
        m[(int)M.WallBlue] = Mat(new Color(0.66f, 0.75f, 0.85f), 0.08f);
        m[(int)M.WallMint] = Mat(new Color(0.70f, 0.86f, 0.78f), 0.08f);
        m[(int)M.WallGreen] = Mat(new Color(0.62f, 0.68f, 0.60f), 0.08f);
        m[(int)M.WallGrey] = Mat(new Color(0.66f, 0.67f, 0.68f), 0.08f);
        m[(int)M.WallTile] = Mat(new Color(0.86f, 0.89f, 0.92f), 0.45f, 0f, ProceduralTextures.SmallTiles);
        m[(int)M.Ceiling] = Mat(new Color(0.92f, 0.92f, 0.91f), 0.05f);
        m[(int)M.Trim] = Mat(new Color(0.94f, 0.94f, 0.93f), 0.35f);
        m[(int)M.Wood] = Mat(new Color(0.66f, 0.50f, 0.36f), 0.5f, 0f, ProceduralTextures.Wood);
        m[(int)M.DarkWood] = Mat(new Color(0.40f, 0.30f, 0.24f), 0.55f, 0f, ProceduralTextures.Wood);
        m[(int)M.White] = Mat(new Color(0.93f, 0.93f, 0.92f), 0.6f);
        m[(int)M.Black] = Mat(new Color(0.06f, 0.06f, 0.07f), 0.7f);
        m[(int)M.Steel] = Mat(new Color(0.55f, 0.56f, 0.58f), 0.6f, 0.85f);
        m[(int)M.Chrome] = Mat(new Color(0.80f, 0.81f, 0.83f), 0.85f, 1f);
        m[(int)M.FabricBlue] = Mat(new Color(0.34f, 0.40f, 0.48f), 0.08f, 0f, ProceduralTextures.Fabric);
        m[(int)M.FabricMustard] = Mat(new Color(0.72f, 0.52f, 0.20f), 0.08f, 0f, ProceduralTextures.Fabric);
        m[(int)M.FabricBeige] = Mat(new Color(0.78f, 0.72f, 0.62f), 0.08f, 0f, ProceduralTextures.Fabric);
        m[(int)M.FabricGreen] = Mat(new Color(0.30f, 0.46f, 0.36f), 0.08f, 0f, ProceduralTextures.Fabric);
        m[(int)M.Cardboard] = Mat(new Color(1f, 1f, 1f), 0.08f, 0f, ProceduralTextures.Cardboard);
        m[(int)M.Terracotta] = Mat(new Color(0.70f, 0.36f, 0.22f), 0.15f);
        m[(int)M.Leaves] = Mat(new Color(0.18f, 0.42f, 0.17f), 0.25f);
        m[(int)M.Porcelain] = Mat(new Color(0.96f, 0.96f, 0.97f), 0.85f);
        m[(int)M.Stone] = Mat(new Color(0.20f, 0.20f, 0.22f), 0.7f);
        m[(int)M.ToyRed] = Mat(new Color(0.85f, 0.12f, 0.10f), 0.5f);
        m[(int)M.ToyBlue] = Mat(new Color(0.12f, 0.35f, 0.85f), 0.5f);
        m[(int)M.ToyYellow] = Mat(new Color(0.98f, 0.80f, 0.12f), 0.5f);
        m[(int)M.ToyGreen] = Mat(new Color(0.16f, 0.65f, 0.25f), 0.5f);
        m[(int)M.Rubber] = Mat(new Color(0.06f, 0.06f, 0.065f), 0.2f);
        m[(int)M.Orange] = Mat(new Color(0.92f, 0.42f, 0.08f), 0.45f, 0.3f);
        m[(int)M.Bedding] = Mat(new Color(0.94f, 0.94f, 0.96f), 0.1f, 0f, ProceduralTextures.Fabric);
        m[(int)M.Grass] = Mat(new Color(0.92f, 0.95f, 0.90f), 0.12f, 0f, ProceduralTextures.Grass);
        m[(int)M.Paving] = Mat(new Color(0.74f, 0.72f, 0.68f), 0.2f, 0f, ProceduralTextures.Paving);
        m[(int)M.Brick] = Mat(Color.white, 0.1f, 0f, ProceduralTextures.Brick);
        m[(int)M.Sand] = Mat(new Color(0.85f, 0.82f, 0.74f), 0.05f, 0f, ProceduralTextures.Sand);
        m[(int)M.Water] = Mat(new Color(0.20f, 0.62f, 0.80f), 0.95f, 0.1f);
        m[(int)M.Fence] = Mat(new Color(0.72f, 0.60f, 0.46f), 0.25f, 0f, ProceduralTextures.Wood);
        m[(int)M.Hedge] = Mat(new Color(0.14f, 0.34f, 0.14f), 0.15f);
        m[(int)M.Bark] = Mat(new Color(0.36f, 0.26f, 0.18f), 0.1f);
        m[(int)M.Tarmac] = Mat(new Color(0.56f, 0.56f, 0.57f), 0.25f, 0f, ProceduralTextures.Concrete);
        m[(int)M.LinePaint] = Mat(new Color(0.98f, 0.80f, 0.12f), 0.4f);
        m[(int)M.Machine] = Mat(new Color(0.14f, 0.36f, 0.62f), 0.55f, 0.4f);
        m[(int)M.Pallet] = Mat(new Color(0.80f, 0.68f, 0.50f), 0.15f, 0f, ProceduralTextures.Wood);
        m[(int)M.Glass] = Mat(new Color(0.30f, 0.38f, 0.44f), 0.92f, 0.2f);
        _materials = m;
        return m;
    }

    // ------------------------------------------------------------------ room context

    /// <summary>Geometry and footprints for one room, in room-local coordinates.</summary>
    private sealed class Room
    {
        // Shell (floor, walls, rugs): receives shadows only. Furniture: casts them too.
        public readonly RaceTrack.FlatMeshBuilder B = new RaceTrack.FlatMeshBuilder((int)M.Count, 1.2f);
        public readonly RaceTrack.FlatMeshBuilder F = new RaceTrack.FlatMeshBuilder((int)M.Count, 1.2f);
        public readonly Vector2 Center;
        public readonly List<WorldObstacle> Obstacles;

        public Room(Vector2 center, List<WorldObstacle> obstacles)
        {
            Center = center;
            Obstacles = obstacles;
        }

        private Vector3 P(float x, float y, float z) => new Vector3(Center.x + x, y, Center.y + z);

        /// <summary>Large structure (walls, fences): drawn with the shell, so it receives but does not cast shadows.</summary>
        public void ShellBox(M m, float x, float z, float angle, float y0, float y1, float hx, float hz, bool solid = true)
        {
            var right = CarCollision.Right(angle);
            var forward = CarCollision.Forward(angle);
            B.AddBoxAxes((int)m, P(x, (y0 + y1) * 0.5f, z), new Vector3(right.x, 0f, right.y) * hx,
                Vector3.up * ((y1 - y0) * 0.5f), new Vector3(forward.x, 0f, forward.y) * hz);
            if (solid && y0 < 0.08f) Obstacles.Add(WorldObstacle.Box(Center + new Vector2(x, z), new Vector2(hx, hz), angle));
        }

        /// <summary>Flat floor area with its own material (lawn, patio, sand), slightly above the ground.</summary>
        public void Ground(M m, float x, float z, float hx, float hz, float y)
        {
            B.AddQuad((int)m, P(x - hx, y, z - hz), P(x - hx, y, z + hz), P(x + hx, y, z + hz), P(x + hx, y, z - hz), Vector3.up);
        }

        public void Box(M m, float x, float z, float angle, float y0, float y1, float hx, float hz, bool solid = true)
        {
            var right = CarCollision.Right(angle);
            var forward = CarCollision.Forward(angle);
            F.AddBoxAxes((int)m, P(x, (y0 + y1) * 0.5f, z), new Vector3(right.x, 0f, right.y) * hx,
                Vector3.up * ((y1 - y0) * 0.5f), new Vector3(forward.x, 0f, forward.y) * hz);
            if (solid && y0 < 0.08f) Obstacles.Add(WorldObstacle.Box(Center + new Vector2(x, z), new Vector2(hx, hz), angle));
        }

        public void Cyl(M m, float x, float z, float y0, float y1, float r0, float r1, bool solid = true, int segments = 16)
        {
            F.AddUprightCylinder((int)m, Center + new Vector2(x, z), y0, y1, r0, r1, segments);
            if (solid && y0 < 0.08f) Obstacles.Add(WorldObstacle.Circle(Center + new Vector2(x, z), Mathf.Max(r0, r1)));
        }

        public void Ball(M m, float x, float y, float z, float rx, float ry, float rz, bool solid = false)
        {
            F.AddSphere((int)m, P(x, y, z), new Vector3(rx, ry, rz));
            if (solid) Obstacles.Add(WorldObstacle.Circle(Center + new Vector2(x, z), Mathf.Max(rx, rz)));
        }

        /// <summary>Table top on four solid legs (the car drives between them).</summary>
        public void Table(M top, M legs, float x, float z, float angle, float hx, float hz, float height, float leg = 0.025f)
        {
            Box(top, x, z, angle, height - 0.04f, height, hx, hz, false);
            var right = CarCollision.Right(angle);
            var forward = CarCollision.Forward(angle);
            foreach (var sx in new[] { -1f, 1f })
            foreach (var sz in new[] { -1f, 1f })
            {
                var offset = right * (sx * (hx - leg * 2f)) + forward * (sz * (hz - leg * 2f));
                Box(legs, x + offset.x, z + offset.y, angle, 0f, height - 0.04f, leg, leg);
            }
        }

        /// <summary>Flat floor-level patch (rugs, mats) — no collision.</summary>
        public void Patch(M m, float x, float z, float hx, float hz, float y = 0.003f)
        {
            B.AddQuad((int)m, P(x - hx, y, z - hz), P(x - hx, y, z + hz), P(x + hx, y, z + hz), P(x + hx, y, z - hz), Vector3.up);
        }
    }

    // ------------------------------------------------------------------ build

    public static Info Build(Transform parent, GameLocation location)
    {
        switch (location)
        {
            case GameLocation.Backyard: return BuildBackyard(parent);
            case GameLocation.Workshop: return BuildWorkshop(parent);
            default: return Build(parent);
        }
    }

    public static Info Build(Transform parent)
    {
        var info = new Info
        {
            Bounds = Rect.MinMaxRect(-HalfExtent, -HalfExtent, HalfExtent, HalfExtent),
            SunDirection = new Vector3(0.45f, -0.68f, 0.58f).normalized
        };

        var materials = Materials();
        var structure = new RaceTrack.FlatMeshBuilder((int)M.Count, 1.2f);
        var overlay = new List<(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color low, Color high)>();
        BuildWalls(structure, info, overlay);

        var floors = new[] { M.Concrete, M.Checker, M.SmallTile, M.DarkFloor, M.Oak, M.Checker, M.CarpetBlue, M.SmallTile, M.CarpetBeige };
        var walls = new[] { M.WallGrey, M.WallWarm, M.WallGrey, M.WallGreen, M.WallWarm, M.WallYellow, M.WallBlue, M.WallTile, M.WallMint };
        for (var index = 0; index < 9; index++)
        {
            var room = new Room(RoomCenter(index), info.Obstacles);
            BuildShell(room, index, floors[index], walls[index], overlay);
            switch (index)
            {
                case 0: Garage(room, parent); break;
                case 1: Hall(room); break;
                case 2: Laundry(room); break;
                case 3: Office(room); break;
                case 4: Living(room); break;
                case 5: Kitchen(room); break;
                case 6: Bedroom(room); break;
                case 7: Bathroom(room); break;
                default: KidsRoom(room); break;
            }

            CreateRenderer(parent, RoomNames[index], room.B, materials, false);
            CreateRenderer(parent, RoomNames[index] + " Furniture", room.F, materials, true);

            // Two standing spots per room, in opposite corners, looking at the middle.
            var center = RoomCenter(index);
            var corner = RoomSize * 0.5f - 0.45f;
            info.CameraSpots.Add((new Vector3(center.x - corner, 1.35f, center.y - corner), new Vector3(center.x, 0f, center.y), index));
            info.CameraSpots.Add((new Vector3(center.x + corner, 1.35f, center.y + corner), new Vector3(center.x, 0f, center.y), index));
        }

        CreateRenderer(parent, "Walls", structure, materials, false);
        CreateOverlay(parent, overlay);
        CreateMat(parent, RoomCenter(8) + new Vector2(-0.3f, -0.3f), new Vector2(1.7f, 1.7f), ProceduralCarFactory.GetMaterial(Color.white, 0.04f, 0f, ProceduralTextures.KidsCarpet, 1f));
        return info;
    }

    /// <summary>Wall cores between and around the rooms, with doorways and lintels.</summary>
    private static void BuildWalls(RaceTrack.FlatMeshBuilder b, Info info, List<(Vector3, Vector3, Vector3, Vector3, Color, Color)> overlay)
    {
        for (var line = 0; line < 4; line++)
        {
            var interior = line == 1 || line == 2;
            var position = (line - 1.5f) * Pitch;
            for (var cell = 0; cell < 3; cell++)
            {
                var from = (cell - 1.5f) * Pitch - Wall * 0.5f;
                var to = (cell - 0.5f) * Pitch + Wall * 0.5f;
                var mid = (cell - 1f) * Pitch;
                // Vertical line (constant x) and horizontal line (constant z).
                AddWallRun(b, info, true, position, from, to, mid, interior);
                AddWallRun(b, info, false, position, from, to, mid, interior);
            }
        }
    }

    private static void AddWallRun(RaceTrack.FlatMeshBuilder b, Info info, bool alongZ, float line, float from, float to, float mid, bool door)
    {
        void Piece(float a, float c, float y0, float y1, bool solid)
        {
            if (c - a < 0.001f) return;
            var center = (a + c) * 0.5f;
            var half = (c - a) * 0.5f;
            var position = alongZ ? new Vector3(line, (y0 + y1) * 0.5f, center) : new Vector3(center, (y0 + y1) * 0.5f, line);
            var x = alongZ ? new Vector3(Wall * 0.5f, 0f, 0f) : new Vector3(half, 0f, 0f);
            var z = alongZ ? new Vector3(0f, 0f, half) : new Vector3(0f, 0f, Wall * 0.5f);
            b.AddBoxAxes((int)M.Trim, position, x, Vector3.up * ((y1 - y0) * 0.5f), z);
            if (!solid) return;
            var floor = alongZ ? new Vector2(line, center) : new Vector2(center, line);
            info.Obstacles.Add(WorldObstacle.Box(floor, alongZ ? new Vector2(Wall * 0.5f, half) : new Vector2(half, Wall * 0.5f)));
        }

        if (!door)
        {
            Piece(from, to, 0f, Height, true);
            return;
        }

        Piece(from, mid - Door * 0.5f, 0f, Height, true);
        Piece(mid + Door * 0.5f, to, 0f, Height, true);
        Piece(mid - Door * 0.5f, mid + Door * 0.5f, DoorHeight, Height, false);
    }

    /// <summary>Floor, ceiling, painted wall faces with doorways, skirting, windows and a lamp.</summary>
    private static void BuildShell(Room room, int index, M floor, M wall, List<(Vector3, Vector3, Vector3, Vector3, Color, Color)> overlay)
    {
        var h = RoomSize * 0.5f;
        var c = room.Center;
        var b = room.B;
        b.AddQuad((int)floor, new Vector3(c.x - h, 0f, c.y - h), new Vector3(c.x - h, 0f, c.y + h), new Vector3(c.x + h, 0f, c.y + h), new Vector3(c.x + h, 0f, c.y - h), Vector3.up);
        // Ceiling: unlit (it only sees bounce light in reality), drawn with the windows and lamps.
        var ceilingColor = new Color(0.80f, 0.80f, 0.79f);
        overlay.Add((new Vector3(c.x - h, Height, c.y - h), new Vector3(c.x + h, Height, c.y - h), new Vector3(c.x + h, Height, c.y + h),
            new Vector3(c.x - h, Height, c.y + h), ceilingColor, ceilingColor));

        var column = index % 3;
        var row = index / 3;
        // Sides: 0 west, 1 east, 2 south, 3 north. Interior sides have a doorway.
        var hasDoor = new[] { column > 0, column < 2, row > 0, row < 2 };
        for (var side = 0; side < 4; side++)
        {
            var normal = side == 0 ? Vector3.right : side == 1 ? Vector3.left : side == 2 ? Vector3.forward : Vector3.back;
            var along = side < 2 ? Vector3.forward : Vector3.right;
            var origin = new Vector3(c.x, 0f, c.y) - normal * (h - 0.001f);
            void Face(float a, float d, float y0, float y1)
            {
                if (d - a < 0.001f) return;
                b.AddQuad((int)wall, origin + along * a + Vector3.up * y0, origin + along * d + Vector3.up * y0,
                    origin + along * d + Vector3.up * y1, origin + along * a + Vector3.up * y1, normal);
                if (y0 < 0.01f)
                {
                    // Skirting board along the bottom of this stretch.
                    var center = origin + along * ((a + d) * 0.5f) + normal * 0.006f + Vector3.up * 0.04f;
                    b.AddBoxAxes((int)M.Trim, center, along * ((d - a) * 0.5f), Vector3.up * 0.04f, normal * 0.006f);
                }
            }

            if (hasDoor[side])
            {
                Face(-h, -Door * 0.5f, 0f, Height);
                Face(Door * 0.5f, h, 0f, Height);
                Face(-Door * 0.5f, Door * 0.5f, DoorHeight, Height);
            }
            else
            {
                Face(-h, h, 0f, Height);
                var inset = origin + normal * 0.004f;
                if (index == 1 && side == 2)
                {
                    // Front door of the house.
                    room.Box(M.DarkWood, 0f, -h + 0.03f, 0f, 0f, DoorHeight, 0.5f, 0.02f, false);
                    room.Box(M.Chrome, 0.38f, -h + 0.06f, 0f, 1.0f, 1.04f, 0.06f, 0.02f, false);
                }
                else
                {
                    // Window: glass glow plus a white frame.
                    overlay.Add((inset + along * -0.75f + Vector3.up * 0.95f, inset + along * 0.75f + Vector3.up * 0.95f,
                        inset + along * 0.75f + Vector3.up * 2.05f, inset + along * -0.75f + Vector3.up * 2.05f,
                        new Color(0.86f, 0.93f, 0.98f), new Color(0.66f, 0.82f, 0.98f)));
                    var frame = inset + normal * 0.02f;
                    b.AddBoxAxes((int)M.Trim, frame + Vector3.up * 0.93f, along * 0.8f, Vector3.up * 0.03f, normal * 0.04f);
                    b.AddBoxAxes((int)M.Trim, frame + Vector3.up * 2.07f, along * 0.8f, Vector3.up * 0.03f, normal * 0.02f);
                    b.AddBoxAxes((int)M.Trim, frame + Vector3.up * 1.5f, along * 0.02f, Vector3.up * 0.55f, normal * 0.02f);
                }
            }
        }

        // Ceiling light.
        var lamp = new Vector3(c.x, Height - 0.006f, c.y);
        overlay.Add((lamp + new Vector3(-0.3f, 0f, -0.3f), lamp + new Vector3(0.3f, 0f, -0.3f), lamp + new Vector3(0.3f, 0f, 0.3f), lamp + new Vector3(-0.3f, 0f, 0.3f),
            new Color(1f, 0.97f, 0.88f), new Color(1f, 0.97f, 0.88f)));
    }

    // ------------------------------------------------------------------ rooms (room-local metres; doors at wall centres)

    private static void Living(Room r)
    {
        // Sofa (north-west), armchair (north-east corner), TV (south-east), shelf, plant, lamp.
        r.Box(M.FabricBlue, -1.40f, 2.10f, 0f, 0.08f, 0.42f, 0.85f, 0.36f);
        r.Box(M.FabricBlue, -1.40f, 2.40f, 0f, 0.42f, 0.84f, 0.85f, 0.08f, false);
        r.Box(M.FabricBlue, -2.20f, 2.10f, 0f, 0.42f, 0.62f, 0.07f, 0.36f, false);
        r.Box(M.FabricBlue, -0.60f, 2.10f, 0f, 0.42f, 0.62f, 0.07f, 0.36f, false);
        r.Box(M.Black, -1.40f, 2.10f, 0f, 0f, 0.08f, 0.82f, 0.33f, false);
        r.Box(M.FabricMustard, 1.95f, 1.95f, 225f, 0.06f, 0.42f, 0.38f, 0.38f);
        r.Box(M.FabricMustard, 2.20f, 2.20f, 225f, 0.42f, 0.86f, 0.38f, 0.08f, false);
        r.Box(M.Wood, 1.45f, -2.28f, 0f, 0.04f, 0.50f, 0.70f, 0.20f);
        r.Box(M.Black, 1.45f, -2.40f, 0f, 0.56f, 1.12f, 0.52f, 0.02f, false);
        r.Box(M.Black, 1.45f, -2.30f, 0f, 0.50f, 0.56f, 0.10f, 0.06f, false);
        r.Box(M.Wood, -2.32f, -1.55f, 0f, 0f, 1.8f, 0.15f, 0.60f);
        for (var i = 0; i < 4; i++)
            r.Box(i % 2 == 0 ? M.Cardboard : M.ToyBlue, -2.25f, -1.75f + (i % 3) * 0.2f, 0f, 0.1f + i * 0.42f, 0.32f + i * 0.42f, 0.04f, 0.12f, false);
        r.Cyl(M.Terracotta, -2.05f, -2.05f, 0f, 0.34f, 0.15f, 0.18f);
        r.Ball(M.Leaves, -2.05f, 0.62f, -2.05f, 0.26f, 0.32f, 0.26f);
        r.Cyl(M.Steel, 1.10f, 2.25f, 0f, 0.03f, 0.15f, 0.15f);
        r.Cyl(M.Steel, 1.10f, 2.25f, 0.03f, 1.45f, 0.012f, 0.012f, false, 8);
        r.Cyl(M.FabricBeige, 1.10f, 2.25f, 1.40f, 1.70f, 0.20f, 0.13f, false);
    }

    private static void Kitchen(Room r)
    {
        // Counter run along the outside (east) wall with a sink, wall cabinets, fridge.
        r.Box(M.Black, 2.18f, -0.35f, 0f, 0f, 0.08f, 0.28f, 2.05f, false);
        r.Box(M.White, 2.15f, -0.35f, 0f, 0.0f, 0.86f, 0.33f, 2.10f);
        r.Box(M.Stone, 2.12f, -0.35f, 0f, 0.86f, 0.90f, 0.38f, 2.12f, false);
        r.Box(M.Steel, 2.10f, 0.4f, 0f, 0.90f, 0.91f, 0.22f, 0.30f, false);
        r.Box(M.White, 2.30f, -0.35f, 0f, 1.50f, 2.20f, 0.18f, 2.10f, false);
        r.Box(M.Steel, 2.10f, 2.10f, 0f, 0f, 1.85f, 0.36f, 0.36f);
        // Dining table and four chairs (legs only are solid).
        r.Table(M.Wood, M.Wood, -0.9f, -0.2f, 0f, 0.80f, 0.45f, 0.75f, 0.03f);
        foreach (var (x, z, a) in new[] { (-1.35f, -0.95f, 0f), (-0.45f, -0.95f, 0f), (-1.35f, 0.55f, 180f), (-0.45f, 0.55f, 180f) })
        {
            r.Table(M.Wood, M.DarkWood, x, z, a, 0.21f, 0.21f, 0.45f, 0.018f);
            var back = CarCollision.Forward(a) * -0.19f;
            r.Box(M.Wood, x + back.x, z + back.y, a, 0.45f, 0.95f, 0.21f, 0.02f, false);
        }

        r.Ball(M.Orange, -0.9f, 0.80f, -0.2f, 0.12f, 0.05f, 0.12f);
    }

    private static void KidsRoom(Room r)
    {
        // Bed (east wall), toy box, building blocks, a ball and a block tower.
        r.Box(M.ToyBlue, 1.95f, 1.30f, 0f, 0f, 0.30f, 0.50f, 1.00f);
        r.Box(M.Bedding, 1.95f, 1.25f, 0f, 0.30f, 0.45f, 0.48f, 0.95f, false);
        r.Box(M.ToyBlue, 1.95f, 2.32f, 0f, 0f, 0.80f, 0.50f, 0.05f, false);
        r.Ball(M.FabricBeige, 1.95f, 0.55f, 2.05f, 0.18f, 0.12f, 0.14f);
        r.Ball(M.FabricMustard, 1.80f, 0.60f, 1.40f, 0.12f, 0.14f, 0.10f);
        r.Box(M.ToyRed, -1.90f, 2.10f, 0f, 0f, 0.45f, 0.45f, 0.28f);
        r.Box(M.ToyYellow, -1.90f, 2.10f, 0f, 0.45f, 0.48f, 0.47f, 0.30f, false);
        var blocks = new[]
        {
            (0.6f, -0.8f, 20f, M.ToyRed), (0.9f, -0.5f, -15f, M.ToyBlue), (-0.5f, 0.9f, 40f, M.ToyGreen),
            (-1.2f, -0.6f, 5f, M.ToyYellow), (1.3f, 0.3f, 60f, M.ToyGreen), (-0.2f, -1.6f, 30f, M.ToyBlue)
        };
        foreach (var (x, z, a, m) in blocks) r.Box(m, x, z, a, 0f, 0.14f, 0.07f, 0.07f);
        for (var i = 0; i < 4; i++)
            r.Box(i % 2 == 0 ? M.ToyRed : M.ToyYellow, -1.6f, -1.8f, i * 25f, i * 0.12f, i * 0.12f + 0.12f, 0.08f, 0.08f, i == 0);
        r.Ball(M.ToyRed, 0.9f, 0.11f, 1.4f, 0.11f, 0.11f, 0.11f, true);
        r.Ball(M.White, 0.9f, 0.11f, 1.4f, 0.112f, 0.03f, 0.112f);
    }

    private static void Bedroom(Room r)
    {
        // Double bed on a frame, nightstands, wardrobe, dresser.
        r.Box(M.DarkWood, -0.6f, 1.40f, 0f, 0.08f, 0.32f, 0.82f, 1.05f, false);
        r.Box(M.Bedding, -0.6f, 1.35f, 0f, 0.32f, 0.50f, 0.80f, 1.00f, false);
        r.Box(M.DarkWood, -0.6f, 2.42f, 0f, 0f, 1.05f, 0.85f, 0.05f);
        foreach (var x in new[] { -0.95f, -0.25f }) r.Ball(M.Bedding, x, 0.56f, 2.10f, 0.28f, 0.07f, 0.16f);
        r.Box(M.FabricGreen, -0.6f, 0.75f, 0f, 0.50f, 0.52f, 0.81f, 0.42f, false);
        foreach (var sx in new[] { -1f, 1f })
        foreach (var sz in new[] { 0.42f, 2.35f })
            r.Box(M.DarkWood, -0.6f + sx * 0.76f, sz, 0f, 0f, 0.08f, 0.04f, 0.04f);
        foreach (var x in new[] { -1.75f, 0.55f })
        {
            r.Box(M.DarkWood, x, 2.20f, 0f, 0f, 0.55f, 0.22f, 0.20f);
            r.Cyl(M.FabricBeige, x, 2.20f, 0.55f, 0.85f, 0.10f, 0.07f, false);
        }

        r.Box(M.White, -2.20f, -0.55f, 0f, 0f, 2.05f, 0.30f, 0.75f);
        r.Box(M.DarkWood, -1.5f, -2.25f, 0f, 0f, 0.80f, 0.60f, 0.22f);
    }

    private static void Bathroom(Room r)
    {
        // Bath (north), vanity with mirror, toilet, laundry basket, bath mat.
        r.Box(M.Porcelain, 0f, 2.08f, 0f, 0f, 0.56f, 0.85f, 0.38f);
        r.Box(M.SmallTile, 0f, 2.08f, 0f, 0.56f, 0.57f, 0.75f, 0.28f, false);
        r.Box(M.White, 1.75f, 2.22f, 0f, 0f, 0.85f, 0.45f, 0.25f);
        r.Box(M.Porcelain, 1.75f, 2.20f, 0f, 0.85f, 0.95f, 0.30f, 0.18f, false);
        r.Box(M.Chrome, 1.75f, 2.46f, 0f, 1.20f, 1.95f, 0.40f, 0.01f, false);
        r.Box(M.Porcelain, -1.85f, 2.25f, 0f, 0.40f, 0.80f, 0.20f, 0.10f, false);
        r.Cyl(M.Porcelain, -1.85f, 2.00f, 0f, 0.40f, 0.17f, 0.20f);
        r.Cyl(M.FabricBeige, 1.95f, -1.95f, 0f, 0.50f, 0.20f, 0.23f);
        r.Patch(M.FabricBlue, 0f, 1.35f, 0.45f, 0.28f);
    }

    private static void Office(Room r)
    {
        // Desk on legs against the outside wall, office chair, bookcases, filing cabinet, bin.
        r.Table(M.DarkWood, M.Black, -1.85f, 0f, 0f, 0.40f, 0.80f, 0.75f, 0.025f);
        r.Box(M.Black, -2.05f, 0f, 0f, 0.79f, 1.15f, 0.02f, 0.30f, false);
        r.Box(M.Black, -1.75f, 0f, 0f, 0.79f, 0.81f, 0.12f, 0.28f, false);
        r.Cyl(M.Black, -1.25f, 0f, 0f, 0.06f, 0.24f, 0.24f);
        r.Cyl(M.Chrome, -1.25f, 0f, 0.06f, 0.45f, 0.025f, 0.025f, false, 8);
        r.Box(M.Black, -1.25f, 0f, 90f, 0.45f, 0.53f, 0.24f, 0.24f, false);
        r.Box(M.Black, -1.05f, 0f, 90f, 0.53f, 1.05f, 0.22f, 0.03f, false);
        foreach (var z in new[] { 1.55f, -1.55f })
        {
            r.Box(M.Wood, -2.25f, z, 0f, 0f, 2.0f, 0.20f, 0.70f);
            for (var i = 0; i < 5; i++)
                r.Box(i % 2 == 0 ? M.ToyRed : M.Cardboard, -2.15f, z - 0.3f + (i % 3) * 0.25f, 0f, 0.1f + i * 0.38f, 0.36f + i * 0.38f, 0.06f, 0.10f, false);
        }

        r.Box(M.Steel, 1.95f, 2.10f, 0f, 0f, 1.10f, 0.25f, 0.30f);
        r.Cyl(M.Black, -1.55f, 0.95f, 0f, 0.30f, 0.11f, 0.13f);
    }

    private static void Garage(Room r, Transform parent)
    {
        // A full-size car: only its tyres are solid, so the RC car can drive underneath it.
        var contacts = CarModelBuilder.CreateParked(parent, CarBody.Tuner, new Color(0.10f, 0.25f, 0.62f), r.Center + new Vector2(0.4f, 0f), 0f, 1f);
        var tyre = CarModelBuilder.TyreWidthAt(CarBody.Tuner, 1f) * 0.6f;
        foreach (var contact in contacts) r.Obstacles.Add(WorldObstacle.Circle(contact, tyre));
        // Workbench on legs, shelving, a tyre stack, an oil drum and a toolbox.
        r.Table(M.Wood, M.Steel, -2.15f, 0.8f, 0f, 0.30f, 0.90f, 0.90f, 0.03f);
        r.Box(M.ToyRed, -2.15f, 0.5f, 0f, 0.90f, 1.10f, 0.15f, 0.25f, false);
        r.Box(M.Steel, -1.2f, -2.28f, 0f, 0f, 1.8f, 0.60f, 0.20f);
        for (var i = 0; i < 4; i++)
            r.Cyl(M.Rubber, -2.0f, -1.6f, i * 0.22f, i * 0.22f + 0.21f, 0.33f, 0.33f, i == 0, 20);
        r.Cyl(M.Orange, 2.0f, -2.0f, 0f, 0.88f, 0.29f, 0.29f);
        r.Box(M.ToyRed, 1.95f, 2.05f, 0f, 0f, 0.95f, 0.30f, 0.25f);
        r.Patch(M.Black, 0.9f, -1.2f, 0.4f, 0.3f, 0.002f);
    }

    private static void Hall(Room r)
    {
        // Runner rug, shoe rack, coat stand, umbrella stand, console table.
        r.Patch(M.FabricBlue, 0f, 0f, 0.5f, 1.8f);
        r.Box(M.DarkWood, -1.5f, -2.28f, 0f, 0f, 0.48f, 0.55f, 0.16f);
        foreach (var x in new[] { -1.8f, -1.55f, -1.25f })
            r.Ball(M.Black, x, 0.53f, -2.25f, 0.06f, 0.05f, 0.12f);
        r.Cyl(M.DarkWood, 1.85f, -2.0f, 0f, 0.04f, 0.20f, 0.20f);
        r.Cyl(M.DarkWood, 1.85f, -2.0f, 0.04f, 1.75f, 0.02f, 0.02f, false, 8);
        r.Ball(M.FabricMustard, 1.85f, 1.45f, -2.0f, 0.16f, 0.3f, 0.16f);
        r.Cyl(M.Steel, 1.35f, -2.22f, 0f, 0.55f, 0.12f, 0.12f);
        r.Table(M.Wood, M.Wood, -2.25f, 1.4f, 0f, 0.16f, 0.50f, 0.80f, 0.02f);
    }

    private static void Laundry(Room r)
    {
        // Washer and dryer, baskets, ironing board, detergent bottles.
        foreach (var z in new[] { -0.6f, 0.15f })
        {
            r.Box(M.White, 2.15f, z, 0f, 0f, 0.85f, 0.30f, 0.30f);
            r.Box(M.Chrome, 1.84f, z, 0f, 0.30f, 0.62f, 0.01f, 0.16f, false);
        }

        r.Cyl(M.FabricBeige, 1.2f, -2.0f, 0f, 0.45f, 0.24f, 0.27f);
        r.Cyl(M.ToyBlue, 0.6f, -2.1f, 0f, 0.35f, 0.20f, 0.22f);
        r.Box(M.FabricBlue, -1.0f, -1.5f, 20f, 0.88f, 0.90f, 0.90f, 0.18f, false);
        var right = CarCollision.Right(20f);
        foreach (var s in new[] { -0.7f, 0.7f })
            r.Box(M.Steel, -1.0f + right.x * s, -1.5f + right.y * s, 20f, 0f, 0.88f, 0.02f, 0.15f);
        for (var i = 0; i < 3; i++)
            r.Cyl(i == 1 ? M.Orange : M.ToyBlue, -2.0f + i * 0.18f, 1.9f, 0f, 0.28f, 0.07f, 0.06f);
        r.Box(M.White, 0f, 2.3f, 0f, 1.4f, 2.2f, 1.0f, 0.15f, false);
    }

    // ------------------------------------------------------------------ renderers

    private static readonly List<int> Used = new List<int>();
    private static readonly List<Object> Built = new List<Object>();

    /// <summary>Frees the meshes and materials made for the last location (before building another).</summary>
    public static void Release()
    {
        foreach (var item in Built)
            if (item != null) Object.Destroy(item);
        Built.Clear();
    }

    private static void CreateRenderer(Transform parent, string name, RaceTrack.FlatMeshBuilder builder, Material[] materials, bool castShadows)
    {
        var mesh = builder.BuildCompact(name, Used);
        Built.Add(mesh);
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        var used = new Material[Used.Count];
        for (var i = 0; i < Used.Count; i++) used[i] = materials[Used[i]];
        renderer.sharedMaterials = used;
        renderer.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = true;
    }

    /// <summary>Window glass and ceiling lights: one unlit mesh for the whole house.</summary>
    private static void CreateOverlay(Transform parent, List<(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color low, Color high)> quads)
    {
        var shader = Shader.Find("AR Coin Drive/Overlay");
        if (shader == null) return;
        var vertices = new List<Vector3>();
        var colors = new List<Color>();
        var triangles = new List<int>();
        foreach (var q in quads)
        {
            var start = vertices.Count;
            vertices.Add(q.a); vertices.Add(q.b); vertices.Add(q.c); vertices.Add(q.d);
            colors.Add(q.low); colors.Add(q.low); colors.Add(q.high); colors.Add(q.high);
            triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }

        var mesh = new Mesh { name = "Windows and Lights" };
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetUVs(0, new List<Vector2>(new Vector2[vertices.Count]));
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        var go = new GameObject("Windows and Lights");
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = new Material(shader) { name = "Windows and Lights", renderQueue = 2450 };
        Built.Add(mesh);
        Built.Add(renderer.sharedMaterial);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /// <summary>
    /// The mat the race track or battle ring sits on, sized by the caller (transform scale = half size):
    /// a rug in the house, a black rubber mat in the workshop, none on the backyard lawn.
    /// </summary>
    public static GameObject CreatePlayMat(Transform parent, GameLocation location)
    {
        if (location == GameLocation.Backyard) return null;
        var material = location == GameLocation.Workshop
            ? ProceduralCarFactory.GetMaterial(new Color(0.20f, 0.21f, 0.23f), 0.12f, 0f, ProceduralTextures.Carpet, 1f)
            : ProceduralCarFactory.GetMaterial(new Color(0.85f, 0.85f, 0.85f), 0.04f, 0f, ProceduralTextures.Rug, 1f);
        var holder = new GameObject("Play Mat").transform;
        holder.SetParent(parent, false);
        CreateMat(holder, Vector2.zero, Vector2.one, material);
        return holder.gameObject;
    }

    /// <summary>Rug or play mat with its own 0..1 texture mapping (no collision).</summary>
    private static void CreateMat(Transform parent, Vector2 center, Vector2 half, Material material)
    {
        var mesh = new Mesh { name = "Mat" };
        Built.Add(mesh);
        const float y = 0.0012f;
        mesh.SetVertices(new[]
        {
            new Vector3(center.x - half.x, y, center.y - half.y), new Vector3(center.x - half.x, y, center.y + half.y),
            new Vector3(center.x + half.x, y, center.y + half.y), new Vector3(center.x + half.x, y, center.y - half.y)
        });
        mesh.SetNormals(new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
        mesh.SetUVs(0, new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right });
        mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
        mesh.RecalculateBounds();
        var go = new GameObject("Mat");
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}
