using UnityEngine;

/// Keeps this (stretched) RectTransform inside the phone's safe area (notch, rounded corners, home bar).
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class SafeAreaFitter : MonoBehaviour
{
    Rect applied;
    Vector2Int appliedScreen;

    void OnEnable() => Apply();
    void Update() => Apply();

    void Apply()
    {
        var safe = Screen.safeArea;
        var screen = new Vector2Int(Screen.width, Screen.height);
        if (safe == applied && screen == appliedScreen) return;
        if (screen.x <= 0 || screen.y <= 0) return;
        applied = safe; appliedScreen = screen;
        var rt = (RectTransform)transform;
        rt.anchorMin = new Vector2(safe.xMin / screen.x, safe.yMin / screen.y);
        rt.anchorMax = new Vector2(safe.xMax / screen.x, safe.yMax / screen.y);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
