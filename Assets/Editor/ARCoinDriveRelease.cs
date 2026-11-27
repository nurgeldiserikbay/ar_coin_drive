using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Google Play release build: a signed Android App Bundle (.aab).
/// The upload key lives OUTSIDE the repository (never commit it). Its location and passwords are
/// read from a properties file, by default the shared TheLightCome key at
/// D:/WORK/Passwords/thelightcome/release.properties, or from the path in the
/// ACD_RELEASE_PROPERTIES environment variable. Expected keys:
///   keystore=C:/path/upload.keystore   storePassword=...   alias=upload   keyPassword=...
/// Run from the AR Coin Drive menu or in batch mode with -executeMethod
/// ARCoinDriveRelease.BuildTestingBundle (closed testing) / ARCoinDriveRelease.BuildBundle (production).
/// </summary>
public static class ARCoinDriveRelease
{
    public const string PackageName = "com.thelightcome.arcoindrive";
    public const string CompanyName = "TheLightCome";

    // Raise VersionCode for EVERY upload to Google Play (Play rejects a code it has seen).
    // Set here, not in Player Settings, because the Android build profile keeps its own copy.
    public const string VersionName = "1.0";
    public const int VersionCode = 4;

    /// <summary>Closed testing: signed .aab that shows only Google's test ads.</summary>
    [MenuItem("AR Coin Drive/Build Signed AAB - Testing (test ads)")]
    public static void BuildTestingBundle() => BuildSignedBundle(true);

    /// <summary>Production: signed .aab with the live AdMob units. Only after closed testing.</summary>
    [MenuItem("AR Coin Drive/Build Signed AAB - Production (real ads)")]
    public static void BuildBundle() => BuildSignedBundle(false);

    private static void BuildSignedBundle(bool testAds)
    {
        var ok = false;
        try
        {
            var settings = ReadProperties();
            AdMobSetup.ForceTestIds = testAds;
            ARCoinDriveSceneBuilder.CreateDemoScene();

            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = "AR Coin Drive";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageName);
            PlayerSettings.bundleVersion = VersionName;
            PlayerSettings.Android.bundleVersionCode = VersionCode;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            // Google Play requires 64-bit; ARM64 covers every ARCore phone.
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = settings["keystore"];
            PlayerSettings.Android.keystorePass = settings["storePassword"];
            PlayerSettings.Android.keyaliasName = settings["alias"];
            PlayerSettings.Android.keyaliasPass = settings["keyPassword"];
            EditorUserBuildSettings.buildAppBundle = true;
            // Native crash symbols for Play Console (it warns about a bundle without them).
            EditorUserBuildSettings.androidCreateSymbols = AndroidCreateSymbols.Public;

            Directory.CreateDirectory("build");
            var path = "build/ARCoinDrive-" + PlayerSettings.bundleVersion + "-" + PlayerSettings.Android.bundleVersionCode
                       + (testAds ? "-testing" : "") + ".aab";
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/ARCoinDriveDemo.unity" },
                locationPathName = path,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                extraScriptingDefines = testAds ? new[] { "ACD_TEST_ADS" } : null,
                options = BuildOptions.None
            });

            // Do not leave the passwords in memory-backed settings for later builds of this session.
            PlayerSettings.Android.keystorePass = "";
            PlayerSettings.Android.keyaliasPass = "";
            EditorUserBuildSettings.buildAppBundle = false;

            Debug.Log("RELEASE_BUILD " + report.summary.result + " errors=" + report.summary.totalErrors + " path=" + path
                      + " version=" + PlayerSettings.bundleVersion + " (" + PlayerSettings.Android.bundleVersionCode + ")"
                      + " targetSdk=" + PlayerSettings.Android.targetSdkVersion + " minSdk=" + PlayerSettings.Android.minSdkVersion
                      + " testAds=" + testAds);
            ok = report.summary.result == BuildResult.Succeeded;
        }
        catch (Exception e)
        {
            Debug.LogError("RELEASE_BUILD_FAILED " + e.Message);
        }

        // Put the real app ID back into the plugin settings (before Exit, which skips finally).
        AdMobSetup.ForceTestIds = false;
        AdMobSetup.Apply();
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }

    /// <summary>
    /// A phone test build (.apk, debug-signed) that shows Google's TEST ads, so the real ads can be
    /// tried without risking the AdMob account. Never upload it to Google Play.
    /// </summary>
    [MenuItem("AR Coin Drive/Build Test APK (test ads)")]
    public static void BuildTestApk()
    {
        var ok = false;
        try
        {
            ARCoinDriveSceneBuilder.CreateDemoScene();
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = "AR Coin Drive";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageName);
            PlayerSettings.bundleVersion = VersionName;
            PlayerSettings.Android.bundleVersionCode = VersionCode;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.useCustomKeystore = false;
            EditorUserBuildSettings.buildAppBundle = false;

            AdMobSetup.ForceTestIds = true;
            Directory.CreateDirectory("build");
            var path = "build/ARCoinDrive-" + VersionName + "-test-ads.apk";
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/ARCoinDriveDemo.unity" },
                locationPathName = path,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                extraScriptingDefines = new[] { "ACD_TEST_ADS" },
                options = BuildOptions.None
            });

            Debug.Log("TEST_BUILD " + report.summary.result + " errors=" + report.summary.totalErrors + " path=" + path);
            ok = report.summary.result == BuildResult.Succeeded;
        }
        catch (Exception e)
        {
            Debug.LogError("TEST_BUILD_FAILED " + e.Message);
        }

        // Put the real app ID back into the plugin settings for the next release build
        // (before Exit, which would skip a finally block).
        AdMobSetup.ForceTestIds = false;
        AdMobSetup.Apply();
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }

    /// <summary>
    /// UI review: a Windows build with UI_CAPTURE that screenshots every screen at phone and tablet
    /// sizes into build/uicapture/shots, then quits. Run build/uicapture/ARCoinDrive.exe after it.
    /// </summary>
    [MenuItem("AR Coin Drive/Build UI Capture (Windows)")]
    public static void BuildUiCapture()
    {
        var ok = false;
        try
        {
            ARCoinDriveSceneBuilder.CreateDemoScene();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/ARCoinDriveDemo.unity" },
                locationPathName = "build/uicapture/ARCoinDrive.exe",
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                extraScriptingDefines = new[] { "UI_CAPTURE" },
                options = BuildOptions.None
            });
            Debug.Log("CAPTURE_BUILD " + report.summary.result + " errors=" + report.summary.totalErrors);
            ok = report.summary.result == BuildResult.Succeeded;
        }
        catch (Exception e)
        {
            Debug.LogError("CAPTURE_BUILD_FAILED " + e.Message);
        }

        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }

    private static Dictionary<string, string> ReadProperties()
    {
        var path = Environment.GetEnvironmentVariable("ACD_RELEASE_PROPERTIES");
        // The shared TheLightCome upload key, used by all the developer's games.
        if (string.IsNullOrEmpty(path)) path = "D:/WORK/Passwords/thelightcome/release.properties";
        if (!File.Exists(path)) throw new FileNotFoundException("Release signing properties not found: " + path);
        var values = new Dictionary<string, string>();
        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;
            var equals = trimmed.IndexOf('=');
            if (equals > 0) values[trimmed.Substring(0, equals).Trim()] = trimmed.Substring(equals + 1).Trim();
        }

        foreach (var key in new[] { "keystore", "storePassword", "alias", "keyPassword" })
            if (!values.ContainsKey(key)) throw new InvalidDataException("Missing '" + key + "' in " + path);
        return values;
    }
}
