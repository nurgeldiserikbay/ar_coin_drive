using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

/// <summary>
/// App name, Android adaptive icon and the splash logo.
/// The PNGs come from the artwork in the repo's Branding folder (logo padded, icon foreground
/// scaled to 88% so it stays inside the adaptive-icon safe zone).
/// </summary>
public static class ARCoinDriveBranding
{
    private const string IconPath = "Assets/Branding/AppIcon.png";
    private const string IconForegroundPath = "Assets/Branding/AppIconForeground.png";
    private const string IconBackgroundPath = "Assets/Branding/AppIconBackground.png";
    private const string LogoPath = "Assets/Resources/ARCoinDriveLogo.png";

    [MenuItem("Tools/AR Coin Drive/Apply Branding")]
    public static void Apply()
    {
        PlayerSettings.productName = "AR Coin Drive";

        var icon = ImportTexture(IconPath, false);
        var foreground = ImportTexture(IconForegroundPath, false);
        var background = ImportTexture(IconBackgroundPath, false);
        ImportTexture(LogoPath, true);
        var logo = AssetDatabase.LoadAssetAtPath<Sprite>(LogoPath);

        // Default icon (store listing, other platforms). Android API 26+ uses the adaptive layers.
        if (icon != null)
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);

        if (foreground != null && background != null)
        {
            var target = NamedBuildTarget.Android;
            var adaptive = PlayerSettings.GetPlatformIcons(target, AndroidPlatformIconKind.Adaptive);
            foreach (var slot in adaptive)
                slot.SetTextures(background, foreground);
            PlayerSettings.SetPlatformIcons(target, AndroidPlatformIconKind.Adaptive, adaptive);
        }

        PlayerSettings.SplashScreen.show = true;
        PlayerSettings.SplashScreen.backgroundColor = new Color(0.04f, 0.14f, 0.26f);
        PlayerSettings.SplashScreen.unityLogoStyle = PlayerSettings.SplashScreen.UnityLogoStyle.LightOnDark;
        PlayerSettings.SplashScreen.drawMode = PlayerSettings.SplashScreen.DrawMode.AllSequential;
        if (logo != null)
            PlayerSettings.SplashScreen.logos = new[] { PlayerSettings.SplashScreenLogo.Create(2f, logo) };

        AssetDatabase.SaveAssets();
    }

    private static Texture2D ImportTexture(string path, bool sprite)
    {
        if (AssetImporter.GetAtPath(path) is TextureImporter importer)
        {
            var changed = false;
            var type = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
            if (importer.textureType != type) { importer.textureType = type; changed = true; }
            if (importer.mipmapEnabled) { importer.mipmapEnabled = false; changed = true; }
            if (!importer.alphaIsTransparency) { importer.alphaIsTransparency = true; changed = true; }
            if (importer.npotScale != TextureImporterNPOTScale.None) { importer.npotScale = TextureImporterNPOTScale.None; changed = true; }
            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                changed = true;
            }

            var maxSize = sprite ? 2048 : 1024;
            if (importer.maxTextureSize != maxSize) { importer.maxTextureSize = maxSize; changed = true; }
            if (changed) importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
