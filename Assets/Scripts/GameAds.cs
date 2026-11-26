using System;
using GoogleMobileAds.Api;
using GoogleMobileAds.Ump.Api;
using UnityEngine;

/// <summary>
/// AdMob ads for a 13+ audience: a rewarded ad that doubles a round's coins (only when the player
/// asks for it) and an interstitial after every few finished races or battles. Before the SDK starts,
/// Google's consent form (UMP) asks players in the EEA, UK and Switzerland for consent; ads are only
/// requested once UMP says they may be. VIP players never see ads, and ads never run in LAN games,
/// because a full-screen ad pauses the phone that runs the session.
/// </summary>
public static class GameAds
{
    // AdMob app "AR Coin Drive" (publisher ca-app-pub-9702825788968948). Never click your own
    // live ads: test on a phone added under AdMob > Settings > Test devices.
    public const string AndroidAppId = "ca-app-pub-9702825788968948~7895231377";
    public const string InterstitialUnitId = "ca-app-pub-9702825788968948/1220856863";
    public const string RewardedUnitId = "ca-app-pub-9702825788968948/8784807060";
    public static bool UsesTestIds => AndroidAppId.StartsWith("ca-app-pub-3940256099942544");

    /// <summary>The published privacy policy (the same URL as in Play Console). Empty hides the link.</summary>
    public const string PrivacyPolicyUrl = "https://docs.google.com/document/d/e/2PACX-1vQC9oA5-f4n4TVwz0G8_WTuk_a2wLmmRno5B0NewYHIg4PUXv_c3BSojrKTB8_GQOxlbXIIXtYoDhcB/pub";

    /// <summary>One interstitial for every this many finished races or battles.</summary>
    public const int RoundsPerInterstitial = 2;

    // No interstitial sooner than this after any other full-screen ad.
    private const float MinSecondsBetweenAds = 90f;
    private const float RetrySeconds = 30f;

    private static bool _started, _sdkStarted, _sdkReady;
    private static InterstitialAd _interstitial;
    private static RewardedAd _rewarded;
    private static bool _loadingInterstitial, _loadingRewarded;
    private static float _interstitialRetryAt, _rewardedRetryAt;
    private static float _lastAdClosedAt = -1000f;
    private static int _roundsSinceAd;

    /// <summary>True while a full-screen ad covers the game.</summary>
    public static bool Showing { get; private set; }

    private static bool Allowed => !PlayerProfile.Vip;

    /// <summary>Asks for consent where the law needs it, then starts the SDK. Call once at start-up.</summary>
    public static void Init()
    {
        if (_started || !Allowed) return;
        _started = true;
        MobileAds.RaiseAdEventsOnUnityMainThread = true;
#if UNITY_EDITOR
        // UMP has no consent form in the Editor; the SDK shows placeholder ads there.
        StartSdk();
#else
        try
        {
            ConsentInformation.Update(new ConsentRequestParameters { TagForUnderAgeOfConsent = false }, updateError =>
            {
                if (updateError != null) Debug.LogWarning("Consent update failed: " + updateError.Message);
                ConsentForm.LoadAndShowConsentFormIfRequired(formError =>
                {
                    if (formError != null) Debug.LogWarning("Consent form failed: " + formError.Message);
                    if (ConsentInformation.CanRequestAds()) StartSdk();
                });
            });
            // Consent given in an earlier session: start right away, without waiting for the update.
            if (ConsentInformation.CanRequestAds()) StartSdk();
        }
        catch (Exception e)
        {
            Debug.LogWarning("Ads unavailable: " + e.Message);
        }
#endif
    }

    /// <summary>Whether the settings screen must offer a way to change the consent choice.</summary>
    public static bool PrivacyOptionsRequired
    {
        get
        {
#if UNITY_EDITOR
            return false;
#else
            try { return ConsentInformation.PrivacyOptionsRequirementStatus == PrivacyOptionsRequirementStatus.Required; }
            catch (Exception) { return false; }
#endif
        }
    }

    public static void ShowPrivacyOptions()
    {
        ConsentForm.ShowPrivacyOptionsForm(error =>
        {
            if (error != null) Debug.LogWarning("Privacy options failed: " + error.Message);
            if (ConsentInformation.CanRequestAds()) StartSdk();
        });
    }

    private static void StartSdk()
    {
        if (_sdkStarted) return;
        _sdkStarted = true;
        MobileAds.Initialize(status =>
        {
            _sdkReady = true;
            LoadInterstitial();
            LoadRewarded();
        });
    }

    // ------------------------------------------------------------------ rewarded

    /// <summary>A rewarded ad is loaded and may be offered now.</summary>
    public static bool RewardedReady
    {
        get
        {
            if (!Allowed || !_sdkReady) return false;
            if (_rewarded == null || !_rewarded.CanShowAd())
            {
                if (Time.unscaledTime >= _rewardedRetryAt) LoadRewarded();
                return false;
            }
            return true;
        }
    }

    /// <summary>Shows the rewarded ad; <paramref name="onRewarded"/> runs after it closes, only if it was watched.</summary>
    public static bool ShowRewarded(Action onRewarded)
    {
        if (!RewardedReady) return false;
        var ad = _rewarded;
        _rewarded = null;
        var earned = false;
        ad.OnAdFullScreenContentClosed += () =>
        {
            Closed();
            ad.Destroy();
            if (earned) onRewarded?.Invoke();
            LoadRewarded();
        };
        ad.OnAdFullScreenContentFailed += error =>
        {
            Debug.LogWarning("Rewarded ad failed to show: " + error.GetMessage());
            Closed();
            ad.Destroy();
            LoadRewarded();
        };
        Showing = true;
        ad.Show(reward => earned = true);
        return true;
    }

    private static void LoadRewarded()
    {
        if (!Allowed || !_sdkReady || _loadingRewarded) return;
        _loadingRewarded = true;
        RewardedAd.Load(RewardedUnitId, new AdRequest(), (ad, error) =>
        {
            _loadingRewarded = false;
            if (error != null || ad == null)
            {
                _rewardedRetryAt = Time.unscaledTime + RetrySeconds;
                return;
            }
            _rewarded?.Destroy();
            _rewarded = ad;
        });
    }

    // ------------------------------------------------------------------ interstitial

    /// <summary>
    /// Counts a finished race or battle and shows an interstitial when one is due, then runs
    /// <paramref name="next"/> (straight away when no ad is shown). A rewarded ad watched in the
    /// round counts as that round's ad.
    /// </summary>
    public static void AfterRound(bool watchedRewarded, Action next)
    {
        if (watchedRewarded) _roundsSinceAd = 0;
        else _roundsSinceAd++;

        var due = Allowed && _sdkReady && _roundsSinceAd >= RoundsPerInterstitial
                  && Time.unscaledTime - _lastAdClosedAt >= MinSecondsBetweenAds;
        if (!due || _interstitial == null || !_interstitial.CanShowAd())
        {
            if (due && Time.unscaledTime >= _interstitialRetryAt) LoadInterstitial();
            next?.Invoke();
            return;
        }

        var ad = _interstitial;
        _interstitial = null;
        _roundsSinceAd = 0;
        ad.OnAdFullScreenContentClosed += () =>
        {
            Closed();
            ad.Destroy();
            next?.Invoke();
            LoadInterstitial();
        };
        ad.OnAdFullScreenContentFailed += error =>
        {
            Debug.LogWarning("Interstitial failed to show: " + error.GetMessage());
            Closed();
            ad.Destroy();
            next?.Invoke();
            LoadInterstitial();
        };
        Showing = true;
        ad.Show();
    }

    private static void LoadInterstitial()
    {
        if (!Allowed || !_sdkReady || _loadingInterstitial) return;
        _loadingInterstitial = true;
        InterstitialAd.Load(InterstitialUnitId, new AdRequest(), (ad, error) =>
        {
            _loadingInterstitial = false;
            if (error != null || ad == null)
            {
                _interstitialRetryAt = Time.unscaledTime + RetrySeconds;
                return;
            }
            _interstitial?.Destroy();
            _interstitial = ad;
        });
    }

    private static void Closed()
    {
        Showing = false;
        _lastAdClosedAt = Time.unscaledTime;
    }
}
