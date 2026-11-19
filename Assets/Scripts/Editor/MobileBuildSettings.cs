using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// Landscape-only + 60 fps settings for mobile builds. Applied automatically before every build
/// (so nothing can drift back to portrait), and on demand via Tools > Trash Game > Mobile Build Settings.
/// The 60 fps lock itself is done at runtime by AppSettings.
public class MobileBuildSettings : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report) => Apply(false);

    [MenuItem("Tools/Trash Game/Mobile Build Settings (Landscape + 60 fps)")]
    public static void ApplyFromMenu() => Apply(true);

    static void Apply(bool log)
    {
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.useAnimatedAutorotation = true;
        // Android: Unity's frame pacing (Swappy) keeps a steady 60 instead of jittering between 30 and 60
        PlayerSettings.Android.optimizedFramePacing = true;
        AssetDatabase.SaveAssets();
        if (log) Debug.Log("[Mobile] Landscape left/right only, frame pacing on. Runtime: AppSettings sets targetFrameRate = " + AppSettings.TargetFps + ".");
    }
}
