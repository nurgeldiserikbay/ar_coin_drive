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
/// read from a properties file, by default ../keys/release.properties next to the project folder,
/// or from the path in the ACD_RELEASE_PROPERTIES environment variable. Expected keys:
///   keystore=C:/path/upload.keystore   storePassword=...   alias=upload   keyPassword=...
/// Run from the menu (AR Coin Drive > Build Google Play Bundle) or in batch mode with
/// -executeMethod ARCoinDriveRelease.BuildBundle.
/// </summary>
public static class ARCoinDriveRelease
{
    public const string PackageName = "com.arcoindrive.game";

    // Raise VersionCode for EVERY upload to Google Play (Play rejects a code it has seen).
    // Set here, not in Player Settings, because the Android build profile keeps its own copy.
    public const string VersionName = "1.0";
    public const int VersionCode = 2;

    [MenuItem("AR Coin Drive/Build Google Play Bundle")]
    public static void BuildBundle()
    {
        try
        {
            var settings = ReadProperties();
            ARCoinDriveSceneBuilder.CreateDemoScene();

            PlayerSettings.companyName = "ARCoinDrive";
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

            Directory.CreateDirectory("build");
            var path = "build/ARCoinDrive-" + PlayerSettings.bundleVersion + "-" + PlayerSettings.Android.bundleVersionCode + ".aab";
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/ARCoinDriveDemo.unity" },
                locationPathName = path,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            });

            // Do not leave the passwords in memory-backed settings for later builds of this session.
            PlayerSettings.Android.keystorePass = "";
            PlayerSettings.Android.keyaliasPass = "";
            EditorUserBuildSettings.buildAppBundle = false;

            Debug.Log("RELEASE_BUILD " + report.summary.result + " errors=" + report.summary.totalErrors + " path=" + path
                      + " version=" + PlayerSettings.bundleVersion + " (" + PlayerSettings.Android.bundleVersionCode + ")"
                      + " targetSdk=" + PlayerSettings.Android.targetSdkVersion + " minSdk=" + PlayerSettings.Android.minSdkVersion);
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
        catch (Exception e)
        {
            Debug.LogError("RELEASE_BUILD_FAILED " + e.Message);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }

    private static Dictionary<string, string> ReadProperties()
    {
        var path = Environment.GetEnvironmentVariable("ACD_RELEASE_PROPERTIES");
        if (string.IsNullOrEmpty(path)) path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../keys/release.properties"));
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
