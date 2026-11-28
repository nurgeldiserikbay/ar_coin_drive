using System;
using UnityEngine;

/// <summary>
/// Rewarded ads that pay coins, with a pause between them and a daily cap, so watching an ad is the
/// quick way to coins without being endless. Every rewarded ad counts: the free-coins button and the
/// "double this round" offer on the results screen share the same cooldown and limit.
/// Times are stored on the device (UTC seconds; the day resets at local midnight).
/// </summary>
public static class AdRewards
{
    public const int CoinReward = 150;
    public const int CooldownSeconds = 240;
    public const int DailyLimit = 8;

    private const string LastKey = "ACD.AdLast";
    private const string DayKey = "ACD.AdDay";
    private const string CountKey = "ACD.AdCount";

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private static int Today => DateTime.Now.Year * 10000 + DateTime.Now.Month * 100 + DateTime.Now.Day;

    public static int WatchedToday => PlayerPrefs.GetInt(DayKey, 0) == Today ? PlayerPrefs.GetInt(CountKey, 0) : 0;
    public static bool LimitReached => WatchedToday >= DailyLimit;

    /// <summary>Seconds until the next rewarded ad may be offered (0 = now).</summary>
    public static int SecondsUntilNext
    {
        get
        {
            if (LimitReached) return (int)(DateTime.Today.AddDays(1) - DateTime.Now).TotalSeconds;
            var last = long.TryParse(PlayerPrefs.GetString(LastKey, "0"), out var value) ? value : 0L;
            // A clock moved backwards must not lock the button for ever.
            var wait = last + CooldownSeconds - Now;
            return wait > CooldownSeconds ? 0 : (int)Math.Max(0L, wait);
        }
    }

    /// <summary>Not cooling down, under the daily cap, not VIP.</summary>
    public static bool Allowed => !PlayerProfile.Vip && !LimitReached && SecondsUntilNext == 0;

    /// <summary>Allowed and an ad is loaded.</summary>
    public static bool Ready => Allowed && GameAds.RewardedReady;

    /// <summary>"3:12" style wait for a button label.</summary>
    public static string WaitLabel
    {
        get
        {
            var seconds = SecondsUntilNext;
            return seconds >= 3600 ? (seconds / 3600) + "h " + (seconds / 60 % 60) + "m" : (seconds / 60) + ":" + (seconds % 60).ToString("00");
        }
    }

    /// <summary>Shows a rewarded ad; on a completed view counts it and runs the reward.</summary>
    public static bool Show(Action onRewarded)
    {
        if (!Ready) return false;
        return GameAds.ShowRewarded(() =>
        {
            Record();
            onRewarded?.Invoke();
        });
    }

    /// <summary>The free-coins button: CoinReward coins per watched ad.</summary>
    public static bool ShowForCoins(Action onGranted)
    {
        return Show(() =>
        {
            PlayerProfile.Earn(CoinReward);
            onGranted?.Invoke();
        });
    }

    private static void Record()
    {
        var count = WatchedToday + 1;
        PlayerPrefs.SetInt(DayKey, Today);
        PlayerPrefs.SetInt(CountKey, count);
        PlayerPrefs.SetString(LastKey, Now.ToString());
        PlayerPrefs.Save();
    }
}
