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
    [Tooltip("Only shown while inside somebody's sight right now. Off = always shown")]
    public bool hideOutOfSight = true;


    /// Ignore the player until this time (a freshly dropped disk stays on the ground a moment).
    float enableTime;

    /// Drops a disk on the ground (built from code, no prefab needed): used when a bot is scrapped.
    public static DiskPickup Drop(DiskProgram program, Vector3 pos)
    {
        var root = new GameObject("Disk (dropped)");
        root.transform.position = new Vector3(pos.x, GroundUtil.Height(pos, pos.y), pos.z);
        var visual = new GameObject("Visual").transform;
        visual.SetParent(root.transform, false);
        visual.localPosition = new Vector3(0f, 0.8f, 0f);
        Part(visual, "Body", Vector3.zero, new Vector3(1.1f, 0.14f, 1.1f), new Color(0.93f, 0.93f, 0.95f));
        Part(visual, "Label", new Vector3(0f, 0.075f, 0.14f), new Vector3(0.75f, 0.02f, 0.6f), program.color);
        Part(visual, "Shutter", new Vector3(0f, 0.075f, -0.36f), new Vector3(0.48f, 0.02f, 0.3f), new Color(0.55f, 0.57f, 0.62f));
        var dp = root.AddComponent<DiskPickup>();
        dp.program = program;
        dp.visual = visual;
        dp.enableTime = Time.time + 1f;
        return dp;
    }

    static void Part(Transform parent, string name, Vector3 pos, Vector3 scale, Color c)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().material.color = c;
    }

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
        // only shown while somebody sees it right now (not on dimmed, explored ground)
        if (hideOutOfSight && GetComponent<FogHideable>() == null)
            gameObject.AddComponent<FogHideable>().hide = FogHideable.Mode.WhenNotVisible;
    }

    void Update()
    {
        if (!collected)
        {
            float a = Time.time * spinSpeed + phase * 360f;
            visual.localRotation = Quaternion.Euler(0f, a, 0f) * Quaternion.Euler(tilt, 0f, 0f);
            visual.localPosition = basePos + Vector3.up * Mathf.Sin(Time.time * 2f + phase * 6f) * bobHeight;

            var inv = PlayerInventory.Instance;
            if (inv == null || Time.time < enableTime) return;
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
