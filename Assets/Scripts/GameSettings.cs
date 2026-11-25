using UnityEngine;

public enum OrientationSetting { Landscape, Portrait, Auto }
public enum SteeringSetting { Buttons, Tilt }

/// <summary>Player preferences, saved with PlayerPrefs.</summary>
public static class GameSettings
{
    public static readonly Color[] CarColors =
    {
        new Color(0.80f, 0.06f, 0.05f), new Color(0.06f, 0.24f, 0.72f), new Color(0.98f, 0.70f, 0.06f),
        new Color(0.08f, 0.55f, 0.22f), new Color(0.95f, 0.42f, 0.06f), new Color(0.45f, 0.14f, 0.62f),
        new Color(0.92f, 0.92f, 0.90f), new Color(0.10f, 0.10f, 0.11f)
    };

    public static readonly string[] CarColorNames = { "Red", "Blue", "Yellow", "Green", "Orange", "Purple", "White", "Black" };

    /// <summary>Shell trim colour that reads well against each paint.</summary>
    public static Color TrimFor(int colorIndex) =>
        colorIndex == 6 ? new Color(0.80f, 0.06f, 0.05f)
        : colorIndex == 2 ? new Color(0.06f, 0.06f, 0.08f)
        : colorIndex == 1 ? new Color(0.98f, 0.78f, 0.12f)
        : new Color(0.94f, 0.94f, 0.92f);

    private const string Prefix = "ARCoinDrive.";

    public static float MasterVolume { get => Get("Master", 0.9f); set => Set("Master", value); }
    public static float EngineVolume { get => Get("Engine", 0.7f); set => Set("Engine", value); }
    public static float EffectsVolume { get => Get("Effects", 0.8f); set => Set("Effects", value); }
    public static bool Vibration { get => GetInt("Vibration", 1) == 1; set => SetInt("Vibration", value ? 1 : 0); }
    public static bool ShowDebug { get => GetInt("Debug", 0) == 1; set => SetInt("Debug", value ? 1 : 0); }
    public static OrientationSetting Orientation { get => (OrientationSetting)GetInt("Orientation", 0); set => SetInt("Orientation", (int)value); }
    public static SteeringSetting Steering { get => (SteeringSetting)GetInt("Steering", 0); set => SetInt("Steering", (int)value); }
    public static int Bots { get => Mathf.Clamp(GetInt("Bots", 1), 0, 3); set => SetInt("Bots", Mathf.Clamp(value, 0, 3)); }
    public static int CarColor { get => Mathf.Clamp(GetInt("CarColor", 0), 0, CarColors.Length - 1); set => SetInt("CarColor", value); }
    public static TrackLayout Track { get => (TrackLayout)Mathf.Clamp(GetInt("Track", 0), 0, RaceTrack.LayoutNames.Length - 1); set => SetInt("Track", (int)value); }
    public static TrackSize TrackSize { get => (TrackSize)Mathf.Clamp(GetInt("TrackSize", 1), 0, 2); set => SetInt("TrackSize", (int)value); }
    public static ArenaShape Arena { get => (ArenaShape)Mathf.Clamp(GetInt("Arena", 0), 0, 1); set => SetInt("Arena", (int)value); }
    public static int ArenaSize { get => Mathf.Clamp(GetInt("ArenaSize", 1), 0, 2); set => SetInt("ArenaSize", value); }
    public static int CarType { get => Mathf.Clamp(GetInt("CarType", 0), 0, CarCatalog.All.Length - 1); set => SetInt("CarType", value); }
    /// <summary>Battery saver caps the game at 30 FPS instead of 60.</summary>
    public static bool BatterySaver { get => GetInt("BatterySaver", 0) == 1; set => SetInt("BatterySaver", value ? 1 : 0); }

    public static void ApplyFrameRate()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = BatterySaver ? 30 : 60;
    }

    public static string PlayerName
    {
        get
        {
            var name = PlayerPrefs.GetString(Prefix + "Name", "");
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "Driver " + Random.Range(10, 99);
                PlayerPrefs.SetString(Prefix + "Name", name);
            }

            return name;
        }
        set => PlayerPrefs.SetString(Prefix + "Name", Clean(value));
    }

    public static float BestTime(string key) => Get("Best." + key, 0f);
    public static void SetBestTime(string key, float seconds) { Set("Best." + key, seconds); Save(); }

    public static void Save() => PlayerPrefs.Save();

    /// <summary>Applies the orientation choice: landscape (either way up), portrait, or free rotation.</summary>
    public static void ApplyOrientation()
    {
        var landscape = Orientation != OrientationSetting.Portrait;
        var portrait = Orientation != OrientationSetting.Landscape;
        Screen.autorotateToLandscapeLeft = landscape;
        Screen.autorotateToLandscapeRight = landscape;
        Screen.autorotateToPortrait = portrait;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.orientation = ScreenOrientation.AutoRotation;
    }

    /// <summary>Names travel over the network: printable, at most 14 characters.</summary>
    public static string Clean(string name)
    {
        if (string.IsNullOrEmpty(name)) return "Driver";
        var builder = new System.Text.StringBuilder();
        foreach (var c in name.Trim())
        {
            if (char.IsControl(c)) continue;
            builder.Append(c);
            if (builder.Length >= 14) break;
        }

        return builder.Length > 0 ? builder.ToString() : "Driver";
    }

    private static float Get(string key, float fallback) => PlayerPrefs.GetFloat(Prefix + key, fallback);
    private static void Set(string key, float value) => PlayerPrefs.SetFloat(Prefix + key, value);
    private static int GetInt(string key, int fallback) => PlayerPrefs.GetInt(Prefix + key, fallback);
    private static void SetInt(string key, int value) => PlayerPrefs.SetInt(Prefix + key, value);
}
