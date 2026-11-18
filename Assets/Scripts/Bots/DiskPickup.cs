using UnityEngine;

/// A disk lying on the map. Walk into it to pick it up.
public class DiskPickup : MonoBehaviour
{
    public DiskProgram program;
    public float pickupRadius = 1.8f;
    public Transform visual;
    public GameObject ring;
    public float tilt = 25f;
    public float spinSpeed = 90f;
    public float bobHeight = 0.15f;

    Vector3 basePos;
    float phase;
    bool collected;
    float t;
    Vector3 from;
    Transform target;

    void Start()
    {
        if (visual == null) visual = transform;
        basePos = visual.localPosition;
        phase = Random.value;
    }

    void Update()
    {
        if (!collected)
        {
            float a = Time.time * spinSpeed + phase * 360f;
            visual.localRotation = Quaternion.Euler(0f, a, 0f) * Quaternion.Euler(tilt, 0f, 0f);
            visual.localPosition = basePos + Vector3.up * Mathf.Sin(Time.time * 2f + phase * 6f) * bobHeight;

            var inv = PlayerInventory.Instance;
            if (inv == null) return;
            Vector3 d = inv.transform.position - transform.position; d.y = 0f;
            if (d.sqrMagnitude < pickupRadius * pickupRadius)
            {
                collected = true;
                target = inv.transform;
                from = visual.position;
                t = 0f;
                if (ring != null) ring.SetActive(false);
            }
            return;
        }

        t = Mathf.Min(1f, t + Time.deltaTime / 0.4f);
        float e = t * t * (3f - 2f * t);
        Vector3 to = target.position + Vector3.up * 1.6f;
        visual.position = Vector3.Lerp(from, to, e) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 1.2f;
        visual.localScale = Vector3.one * Mathf.Lerp(1f, 0.2f, e);
        if (t >= 1f)
        {
            if (PlayerInventory.Instance != null) PlayerInventory.Instance.Add(program);
            gameObject.SetActive(false);
        }
    }
}
