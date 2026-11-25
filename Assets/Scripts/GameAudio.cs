using UnityEngine;

/// <summary>
/// All game sounds, synthesised at start-up (no audio files): an electric RC motor whine that
/// follows the car's speed, tyre scrub, impacts, coins, start beeps, jingles and UI clicks.
/// Also short haptic pulses on impacts.
/// </summary>
public sealed class GameAudio : MonoBehaviour
{
    public enum Sfx { Click, Coin, Bump, Crash, Beep, Go, Lap, Win, Lose, Place, Remove, Knockout }

    private const int Rate = 44100;
    private static GameAudio _instance;

    private AudioClip _engine;
    private AudioClip _tyres;
    private AudioClip[] _sfx;
    private AudioSource _ui;

    public static GameAudio Instance
    {
        get
        {
            if (_instance != null) return _instance;
            var host = Camera.main != null ? Camera.main.gameObject : new GameObject("Game Audio");
            _instance = host.AddComponent<GameAudio>();
            return _instance;
        }
    }

    private void Awake()
    {
        _instance = this;
        if (FindAnyObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
        _ui = gameObject.AddComponent<AudioSource>();
        _ui.playOnAwake = false;
        _ui.spatialBlend = 0f;

        _engine = Clip("Motor", 1f, MotorSample);
        _tyres = Clip("Tyres", 1f, TyreSample);
        _sfx = new[]
        {
            Clip("Click", 0.05f, t => Tone(t, 1800f) * Decay(t, 60f) * 0.5f),
            Clip("Coin", 0.42f, t => (Tone(t, t < 0.08f ? 1319f : 1976f) + 0.4f * Tone(t, t < 0.08f ? 2638f : 3952f)) * Decay(t < 0.08f ? t : t - 0.08f, 9f) * 0.45f),
            Clip("Bump", 0.18f, t => (Noise(t) * 0.5f + Tone(t, 140f - t * 300f)) * Decay(t, 28f) * 0.8f),
            Clip("Crash", 0.45f, t => (Noise(t) * 0.7f + Tone(t, 95f) * 0.6f + Tone(t, 2300f) * 0.15f * Decay(t, 60f)) * Decay(t, 9f) * 0.9f),
            Clip("Beep", 0.22f, t => Square(t, 660f) * Envelope(t, 0.18f) * 0.28f),
            Clip("Go", 0.55f, t => Square(t, 1320f) * Envelope(t, 0.5f) * 0.3f),
            Clip("Lap", 0.35f, t => Square(t, t < 0.12f ? 880f : 1175f) * Envelope(t, 0.33f) * 0.25f),
            Clip("Win", 1.1f, t => Arpeggio(t, new[] { 523f, 659f, 784f, 1047f, 1319f }, 0.14f) * 0.35f),
            Clip("Lose", 0.9f, t => Arpeggio(t, new[] { 392f, 330f, 262f }, 0.24f) * 0.3f),
            Clip("Place", 0.12f, t => Tone(t, 520f + t * 2400f) * Decay(t, 30f) * 0.5f),
            Clip("Remove", 0.12f, t => Tone(t, 820f - t * 2400f) * Decay(t, 30f) * 0.5f),
            Clip("Knockout", 0.8f, t => (Tone(t, 600f - t * 520f) * 0.6f + Noise(t) * 0.2f) * Decay(t, 3.5f) * 0.6f)
        };
    }

    public void Play(Sfx sound, float volume = 1f)
    {
        var clip = _sfx[(int)sound];
        _ui.PlayOneShot(clip, Mathf.Clamp01(volume) * GameSettings.EffectsVolume * GameSettings.MasterVolume);
    }

    /// <summary>Motor and tyre loops attached to a car.</summary>
    public EngineVoice AttachEngine(Transform car, bool local)
    {
        return new EngineVoice(MakeLoop(car, _engine, local), MakeLoop(car, _tyres, local));
    }

    private static AudioSource MakeLoop(Transform parent, AudioClip clip, bool local)
    {
        var source = parent.gameObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.playOnAwake = false;
        source.volume = 0f;
        source.dopplerLevel = 0f;
        // The player's own car stays mostly centred; other cars are placed in 3D.
        source.spatialBlend = local ? 0.55f : 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 0.4f;
        source.maxDistance = 7f;
        source.time = Random.Range(0f, clip.length * 0.9f);
        source.Play();
        return source;
    }

    public sealed class EngineVoice
    {
        private readonly AudioSource _motor;
        private readonly AudioSource _tyres;
        private float _rev;

        public EngineVoice(AudioSource motor, AudioSource tyres)
        {
            _motor = motor;
            _tyres = tyres;
        }

        public void Update(float speedRatio, float throttle, float steer, float dt, bool active)
        {
            if (_motor == null) return;
            var target = active ? Mathf.Clamp01(Mathf.Abs(speedRatio) * 0.85f + Mathf.Abs(throttle) * 0.25f) : 0f;
            _rev = Mathf.Lerp(_rev, target, 1f - Mathf.Exp(-9f * dt));
            var master = GameSettings.MasterVolume;
            _motor.pitch = 0.55f + _rev * 1.55f;
            _motor.volume = active ? (0.10f + _rev * 0.55f) * GameSettings.EngineVolume * master : 0f;
            if (_tyres == null) return;
            var scrub = Mathf.Clamp01(Mathf.Abs(speedRatio)) * (0.25f + Mathf.Abs(steer) * 0.75f);
            _tyres.pitch = 0.8f + Mathf.Abs(speedRatio) * 0.5f;
            _tyres.volume = active ? scrub * 0.32f * GameSettings.EffectsVolume * master : 0f;
        }
    }

    // ------------------------------------------------------------------ haptics

    /// <summary>A short vibration (Android), when enabled in settings.</summary>
    public static void Haptic(long milliseconds)
    {
        if (!GameSettings.Vibration) return;
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator"))
            {
                if (vibrator == null) return;
                using (var effect = new AndroidJavaClass("android.os.VibrationEffect"))
                using (var shot = effect.CallStatic<AndroidJavaObject>("createOneShot", milliseconds, -1))
                    vibrator.Call("vibrate", shot);
            }
        }
        catch (System.Exception)
        {
            // Referencing Handheld.Vibrate also makes Unity add the VIBRATE permission.
            if (milliseconds > 400) Handheld.Vibrate();
        }
#endif
    }

    // ------------------------------------------------------------------ synthesis

    private delegate float SampleFunction(float time);

    private static AudioClip Clip(string name, float seconds, SampleFunction function)
    {
        var count = Mathf.CeilToInt(seconds * Rate);
        var data = new float[count];
        for (var i = 0; i < count; i++)
            data[i] = Mathf.Clamp(function(i / (float)Rate), -1f, 1f);
        var clip = AudioClip.Create(name, count, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Integer frequencies over a 1 s loop join seamlessly.
    private static float MotorSample(float t)
    {
        var whine = Tone(t, 220f) * 0.38f + Tone(t, 440f) * 0.22f + Tone(t, 660f) * 0.12f;
        var gear = Tone(t, 1320f) * 0.07f * (0.6f + 0.4f * Tone(t, 7f));
        var buzz = Square(t, 110f) * 0.06f;
        return (whine + gear + buzz + LoopNoise(t) * 0.05f) * 0.7f;
    }

    private static float TyreSample(float t)
    {
        // Band-limited hiss with a slow surface texture.
        return (LoopNoise(t) * 0.6f + LoopNoise(t * 0.5f) * 0.4f) * (0.7f + 0.3f * Tone(t, 3f));
    }

    private static float Tone(float t, float frequency) => Mathf.Sin(2f * Mathf.PI * frequency * t);
    private static float Square(float t, float frequency) => Mathf.Sign(Tone(t, frequency)) * 0.7f + Tone(t, frequency) * 0.3f;
    private static float Decay(float t, float rate) => Mathf.Exp(-rate * Mathf.Max(0f, t));
    private static float Envelope(float t, float length) => Mathf.Clamp01(t * 200f) * Mathf.Clamp01((length - t) * 30f);

    private static float Noise(float t)
    {
        var h = (uint)(t * Rate) * 2654435761u;
        h ^= h >> 15;
        h *= 2246822519u;
        h ^= h >> 13;
        return (h & 0xFFFF) / 32767.5f - 1f;
    }

    // Smoothed noise that repeats every second (for loops).
    private static float LoopNoise(float t)
    {
        var x = Mathf.Repeat(t, 1f) * 4000f;
        var i = Mathf.FloorToInt(x);
        var f = x - i;
        return Mathf.Lerp(Noise(i / (float)Rate), Noise(((i + 1) % 4000) / (float)Rate), f);
    }

    private static float Arpeggio(float t, float[] notes, float step)
    {
        var index = Mathf.Min(notes.Length - 1, Mathf.FloorToInt(t / step));
        var local = t - index * step;
        var last = index == notes.Length - 1;
        return (Tone(t, notes[index]) * 0.7f + Tone(t, notes[index] * 2f) * 0.2f) * Decay(local, last ? 3f : 7f);
    }
}
