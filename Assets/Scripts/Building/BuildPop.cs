using UnityEngine;

/// Little "built!" pop-in for a freshly placed building. Removes itself when done.
public class BuildPop : MonoBehaviour
{
    public float duration = 0.45f;

    Vector3 target;
    float t;

    void Start()
    {
        target = transform.localScale;
        transform.localScale = target * 0.2f;
    }

    void Update()
    {
        t = Mathf.Min(1f, t + Time.deltaTime / duration);
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float e = 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        transform.localScale = target * Mathf.LerpUnclamped(0.2f, 1f, e);
        if (t >= 1f)
        {
            transform.localScale = target;
            Destroy(this);
        }
    }
}
