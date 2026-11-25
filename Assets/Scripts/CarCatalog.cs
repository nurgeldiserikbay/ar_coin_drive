using UnityEngine;

public enum CarBody { Buggy, Tuner, Muscle, Super, Rally, Offroad }

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
