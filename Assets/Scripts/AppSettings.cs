using UnityEngine;

/// Runs once at startup (before the first scene), in builds and in the Editor:
/// - frame rate locked to 60 (mobile defaults to 30 fps otherwise; vSync off so targetFrameRate is used on PC too)
/// - landscape only (left + right, auto-rotate between them), same as Player Settings
/// - the screen doesn't dim / sleep while playing
public static class AppSettings
{
    public const int TargetFps = 60;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Apply()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = TargetFps;

#if UNITY_ANDROID || UNITY_IOS
        Screen.autorotateToPortrait = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft = true;
        Screen.autorotateToLandscapeRight = true;
        Screen.orientation = ScreenOrientation.AutoRotation;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
#endif
    }
}
