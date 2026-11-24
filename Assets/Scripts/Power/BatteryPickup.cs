using UnityEngine;

/// A battery lying on the map. Walk into it to pick it up (needs a free battery slot).
public class BatteryPickup : MonoBehaviour
{
    public BatteryItem battery;
    public float pickupRadius = 1.8f;
    public Transform visual;
    public GameObject ring;
    public GameObject beam;
    public float spinSpeed = 70f;
    public float bobHeight = 0.18f;
    [Tooltip("Only shown while inside somebody's sight right now. Off = always shown")]
    public bool hideOutOfSight = true;


    Vector3 basePos;
    float phase, t, fullWarnCooldown;
    bool collected;
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
            visual.localRotation = Quaternion.Euler(0f, Time.time * spinSpeed + phase * 360f, 12f);
            visual.localPosition = basePos + Vector3.up * Mathf.Sin(Time.time * 2f + phase * 6f) * bobHeight;
            fullWarnCooldown -= Time.deltaTime;

            var inv = PlayerInventory.Instance;
            if (inv == null || battery == null) return;
            Vector3 d = inv.transform.position - transform.position; d.y = 0f;
            if (d.sqrMagnitude > pickupRadius * pickupRadius) return;

            if (!inv.HasBatterySpace)
            {
                if (fullWarnCooldown <= 0f)
                {
                    fullWarnCooldown = 3f;
                    GameToast.Show("BATTERY SLOTS FULL (" + inv.batterySlots + ")", new Color(1f, 0.4f, 0.32f));
                }
                return;
            }
            collected = true;
            target = inv.transform;
            from = visual.position;
            t = 0f;
            if (ring != null) ring.SetActive(false);
            if (beam != null) beam.SetActive(false);
            return;
        }

        t = Mathf.Min(1f, t + Time.deltaTime / 0.4f);
        float e = t * t * (3f - 2f * t);
        Vector3 to = target.position + Vector3.up * 1.6f;
        visual.position = Vector3.Lerp(from, to, e) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 1.2f;
        visual.localScale = Vector3.one * Mathf.Lerp(1f, 0.2f, e);
        if (t >= 1f)
        {
            var inv = PlayerInventory.Instance;
            if (inv != null && inv.AddBattery(battery))
                GameToast.Show("+ " + battery.displayName.ToUpper() + "   " + Stats.Clock(battery.runtimeSeconds), battery.Color);
            gameObject.SetActive(false);
        }
    }
}
