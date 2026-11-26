using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Writes GameAds.AndroidAppId into the Google Mobile Ads plugin settings before every Android build;
/// without an app ID the plugin stops the build, and an app ID that does not match the ad units
/// shows no ads.
/// </summary>
public class AdMobSetup : IPreprocessBuildWithReport
{
    public int callbackOrder => -100;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform == BuildTarget.Android) Apply();
    }

    [MenuItem("AR Coin Drive/Apply AdMob Settings")]
    public static void Apply()
    {
        // The plugin keeps its settings class internal, so reach it by name.
        var settingsType = Type.GetType("GoogleMobileAds.Editor.GoogleMobileAdsSettings, GoogleMobileAds.Editor");
        var load = settingsType?.GetMethod("LoadInstance", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (load == null) throw new BuildFailedException("Google Mobile Ads plugin is missing.");
        var settings = (ScriptableObject)load.Invoke(null, null);
        var serialized = new SerializedObject(settings);
        var appId = serialized.FindProperty("adMobAndroidAppId");
        if (appId == null) throw new BuildFailedException("Google Mobile Ads settings have no adMobAndroidAppId field.");
        appId.stringValue = GameAds.AndroidAppId;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        if (GameAds.UsesTestIds) Debug.LogWarning("AR Coin Drive: AdMob uses Google's TEST IDs; replace them in GameAds.cs before release.");
    }
}
