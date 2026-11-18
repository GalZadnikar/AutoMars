using System;
using UnityEngine;
using UnityEngine.UI;

/// A screen-space button that floats above a world object (e.g. "BUILD BOTS" over the factory).
/// Lives in the overlay canvas so it stays crisp and clickable on mobile.
[DefaultExecutionOrder(10000)] // after Cinemachine has moved the camera
public class WorldButton : MonoBehaviour
{
    public Button button;

    Transform target;
    Vector3 offset;
    Action onClick;
    RectTransform rt;
    Camera cam;
    float appear;
    bool inited;

    void Init()
    {
        if (inited) return;
        inited = true;
        rt = (RectTransform)transform;
        if (button == null) button = GetComponent<Button>();
        button.onClick.AddListener(() => onClick?.Invoke());
    }

    public void Show(Transform worldTarget, Vector3 worldOffset, Action click)
    {
        Init();
        target = worldTarget;
        offset = worldOffset;
        onClick = click;
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
            appear = 0f;
        }
        UpdatePosition();
    }

    public void Hide()
    {
        target = null;
        onClick = null;
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    void LateUpdate() => UpdatePosition();

    void UpdatePosition()
    {
        if (target == null) { Hide(); return; }
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Vector3 sp = cam.WorldToScreenPoint(target.position + offset);
        var parent = (RectTransform)rt.parent;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, sp, null, out var lp))
            rt.localPosition = lp;

        appear = Mathf.MoveTowards(appear, 1f, Time.unscaledDeltaTime * 5f);
        float s = appear < 1f ? EaseOutBack(appear) : 1f + Mathf.Sin(Time.unscaledTime * 4f) * 0.04f;
        rt.localScale = Vector3.one * s;
    }

    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }
}
