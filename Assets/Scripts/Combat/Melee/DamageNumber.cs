using TMPro;
using UnityEngine;

/// Floating damage number: pops in bigger than its final size, drifts up and fades. Built in code, destroys itself.
public class DamageNumber : MonoBehaviour
{
    public float life = 0.75f;

    TextMeshPro text;
    Transform cam;
    float age, size, side;

    public static void Spawn(Vector3 pos, float amount, bool big = false)
    {
        var go = new GameObject("Damage Number");
        go.transform.position = pos + new Vector3(Random.Range(-0.35f, 0.35f), Random.Range(-0.1f, 0.15f), 0f);
        var t = go.AddComponent<TextMeshPro>();
        t.text = Mathf.Max(1, Mathf.RoundToInt(amount)).ToString();
        t.fontSize = 8f;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.color = big ? new Color(1f, 0.85f, 0.3f) : Color.white;
        t.outlineWidth = 0.25f;
        t.outlineColor = new Color32(45, 28, 18, 255);
        t.rectTransform.sizeDelta = new Vector2(4f, 2f);
        t.sortingOrder = 20;
        var d = go.AddComponent<DamageNumber>();
        d.text = t;
        d.size = big ? 1.5f : 1f;
        d.side = Random.Range(-0.4f, 0.4f);
        go.transform.localScale = Vector3.one * 0.3f;
    }

    void LateUpdate()
    {
        age += Time.deltaTime;
        float k = age / life;
        if (k >= 1f) { Destroy(gameObject); return; }

        // pop: 0.3 -> 1.4 in 0.08 s, settle to 1 in the next 0.12 s
        float s = age < 0.08f ? Mathf.Lerp(0.3f, 1.4f, age / 0.08f) : Mathf.Lerp(1.4f, 1f, Mathf.Clamp01((age - 0.08f) / 0.12f));
        transform.localScale = Vector3.one * s * size;
        transform.position += (Vector3.up * Mathf.Lerp(2.4f, 0.4f, k) + Vector3.right * side) * Time.deltaTime;
        text.alpha = k > 0.6f ? 1f - (k - 0.6f) / 0.4f : 1f;

        if (cam == null && Camera.main != null) cam = Camera.main.transform;
        if (cam != null) transform.rotation = cam.rotation;
    }
}
