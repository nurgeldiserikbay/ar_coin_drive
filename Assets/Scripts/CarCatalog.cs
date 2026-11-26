using UnityEngine;

public enum CarBody { Buggy, Tuner, Muscle, Super, Rally, Offroad, GT, Proto }

/// <summary>
/// One car type: how it looks (an original procedural design) and how it drives. Shapes are
/// authored at real-car size in metres and built at 1:18 scale. Stats feed the physics; the
/// 0-10 ratings are only for the garage screen.
/// </summary>
public sealed class CarSpec
{
    public string Id;
    public string Name;
    public string Class;
    public CarBody Body;
    public int DefaultColor;

    // Driving
    public float TopSpeed;      // m/s at toy scale on the race track
    public float Acceleration;  // m/s²
    public float TurnRate;      // degrees per second at full lock
    public float Grip;          // how fast sideways slide is killed (1/s)
    public float Drift;         // 0..1: how easily the rear lets go at speed
    public float Mass;          // battle pushing weight
    public float OffRoad;       // speed kept on astroturf / grass (0..1)

    // Footprint at toy scale (half sizes, metres)
    public float HalfWidth;
    public float HalfLength;

    // Garage ratings 0..10
    public int SpeedRating => Mathf.RoundToInt(Mathf.InverseLerp(0.40f, 0.70f, TopSpeed) * 9f + 1f);
    public int AccelRating => Mathf.RoundToInt(Mathf.InverseLerp(0.80f, 1.35f, Acceleration) * 9f + 1f);
    public int HandlingRating => Mathf.RoundToInt(Mathf.InverseLerp(105f, 170f, TurnRate) * 5f + Mathf.InverseLerp(7f, 14f, Grip) * 4f + 1f);
    public int WeightRating => Mathf.RoundToInt(Mathf.InverseLerp(0.8f, 1.7f, Mass) * 9f + 1f);
}

public static class CarCatalog
{
    public const float Scale = 1f / 18f;

    /// <summary>
    /// Cars in races, battles and free drive are this much smaller than in the garage, so several fit
    /// side by side on the track and can overtake. Applies to the model and the collision footprint.
    /// </summary>
    public const float GameScale = 0.7f;

    public static readonly CarSpec[] All =
    {
        new CarSpec
        {
            Id = "buggy", Name = "Mudlark RC", Class = "RC buggy", Body = CarBody.Buggy, DefaultColor = 0,
            TopSpeed = 0.48f, Acceleration = 1.0f, TurnRate = 140f, Grip = 12f, Drift = 0.2f, Mass = 0.9f, OffRoad = 0.85f,
            HalfWidth = 0.069f, HalfLength = 0.115f
        },
        new CarSpec
        {
            Id = "kaze", Name = "Kaze R", Class = "Tuner coupe", Body = CarBody.Tuner, DefaultColor = 6,
            TopSpeed = 0.56f, Acceleration = 1.05f, TurnRate = 152f, Grip = 10.5f, Drift = 0.7f, Mass = 1.0f, OffRoad = 0.55f,
            HalfWidth = 0.052f, HalfLength = 0.124f
        },
        new CarSpec
        {
            Id = "brute", Name = "Brute V8", Class = "Muscle", Body = CarBody.Muscle, DefaultColor = 7,
            TopSpeed = 0.63f, Acceleration = 1.28f, TurnRate = 118f, Grip = 8.5f, Drift = 0.55f, Mass = 1.35f, OffRoad = 0.5f,
            HalfWidth = 0.054f, HalfLength = 0.133f
        },
        new CarSpec
        {
            Id = "vortex", Name = "Vortex S", Class = "Supercar", Body = CarBody.Super, DefaultColor = 2,
            TopSpeed = 0.69f, Acceleration = 1.22f, TurnRate = 146f, Grip = 13.5f, Drift = 0.25f, Mass = 1.05f, OffRoad = 0.4f,
            HalfWidth = 0.056f, HalfLength = 0.127f
        },
        new CarSpec
        {
            Id = "dune", Name = "Dune R", Class = "Rally hatch", Body = CarBody.Rally, DefaultColor = 1,
            TopSpeed = 0.54f, Acceleration = 1.15f, TurnRate = 166f, Grip = 10f, Drift = 0.45f, Mass = 0.95f, OffRoad = 0.9f,
            HalfWidth = 0.050f, HalfLength = 0.113f
        },
        new CarSpec
        {
            Id = "titan", Name = "Titan X", Class = "Off-roader", Body = CarBody.Offroad, DefaultColor = 3,
            TopSpeed = 0.50f, Acceleration = 0.92f, TurnRate = 112f, Grip = 10f, Drift = 0.3f, Mass = 1.65f, OffRoad = 1f,
            HalfWidth = 0.058f, HalfLength = 0.130f
        },
        // Premium cars (bought in the store for real money).
        new CarSpec
        {
            Id = "stinger", Name = "Stinger GT", Class = "Grand tourer", Body = CarBody.GT, DefaultColor = 5,
            TopSpeed = 0.68f, Acceleration = 1.25f, TurnRate = 142f, Grip = 12f, Drift = 0.5f, Mass = 1.25f, OffRoad = 0.5f,
            HalfWidth = 0.055f, HalfLength = 0.131f
        },
        new CarSpec
        {
            Id = "apex", Name = "Apex LM", Class = "Endurance prototype", Body = CarBody.Proto, DefaultColor = 8,
            TopSpeed = 0.73f, Acceleration = 1.30f, TurnRate = 150f, Grip = 14.5f, Drift = 0.15f, Mass = 1.0f, OffRoad = 0.35f,
            HalfWidth = 0.056f, HalfLength = 0.133f
        }
    };

    public static CarSpec Get(int index) => All[Mathf.Clamp(index, 0, All.Length - 1)];

    public static int IndexOf(string id)
    {
        for (var i = 0; i < All.Length; i++)
            if (All[i].Id == id) return i;
        return 0;
    }
}

/// <summary>How a car looks: paint, finish, rims, spoiler and stripes (all purely visual).</summary>
public struct CarLook
{
    public static readonly string[] FinishNames = { "GLOSS", "METALLIC", "MATTE" };
    public static readonly string[] RimNames = { "STOCK", "SPORT", "MESH", "DEEP DISH" };
    public static readonly string[] SpoilerNames = { "NONE", "STOCK", "GT WING" };

    public int Paint;
    public int Finish;
    public int Rim;
    public int Spoiler;
    public bool Stripes;

    public static CarLook Stock(int paint) => new CarLook { Paint = paint, Spoiler = 1 };
}

/// <summary>Performance upgrades, each 0..3.</summary>
public struct CarTune
{
    public const int MaxLevel = 3;
    public int Engine;
    public int Tyres;
    public int Weight;
}

public static class CarTuning
{
    private static readonly System.Collections.Generic.Dictionary<(int, int, int, int), CarSpec> Cache =
        new System.Collections.Generic.Dictionary<(int, int, int, int), CarSpec>();

    /// <summary>
    /// The car's stats with its upgrades applied: engine adds top speed and acceleration, tyres
    /// add grip, steering and off-road pace, weight reduction adds acceleration and steering but
    /// makes the car easier to push around in battle.
    /// </summary>
    public static CarSpec Apply(int specIndex, CarTune tune)
    {
        var key = (specIndex, tune.Engine, tune.Tyres, tune.Weight);
        if (Cache.TryGetValue(key, out var tuned)) return tuned;
        var b = CarCatalog.Get(specIndex);
        tuned = new CarSpec
        {
            Id = b.Id, Name = b.Name, Class = b.Class, Body = b.Body, DefaultColor = b.DefaultColor,
            TopSpeed = b.TopSpeed * (1f + 0.04f * tune.Engine),
            Acceleration = b.Acceleration * (1f + 0.06f * tune.Engine + 0.03f * tune.Weight),
            TurnRate = b.TurnRate * (1f + 0.03f * tune.Tyres + 0.02f * tune.Weight),
            Grip = b.Grip * (1f + 0.08f * tune.Tyres),
            Drift = b.Drift,
            Mass = b.Mass * (1f - 0.05f * tune.Weight),
            OffRoad = Mathf.Min(1f, b.OffRoad * (1f + 0.04f * tune.Tyres)),
            HalfWidth = b.HalfWidth * CarCatalog.GameScale,
            HalfLength = b.HalfLength * CarCatalog.GameScale
        };
        Cache[key] = tuned;
        return tuned;
    }
}
