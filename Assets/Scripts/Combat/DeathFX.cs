using UnityEngine;

/// Put next to a Health: when it dies, plays the explode/scrap burst, spawns optional drops,
/// and destroys (or hides) the object. Units that go "down" instead of dying (robots, the player) don't use this.
[RequireComponent(typeof(Health))]
public class DeathFX : MonoBehaviour
{
    [Tooltip("Size of the explosion in metres")]
    public float size = 1.2f;
    public Color color = new Color(1f, 0.55f, 0.25f);
    [Tooltip("Spawned at the death position (scattered a little)")]
    public GameObject[] drops;
    public float dropScatter = 1.2f;
    [Tooltip("Destroy the object after the burst (otherwise it is just deactivated)")]
    public bool destroyOnDeath = true;
    public float delay = 0f;

    Health hp;

    void Awake()
    {
        hp = GetComponent<Health>();
        hp.OnDied += Play;
    }

    void OnDestroy() { if (hp != null) hp.OnDied -= Play; }

    void Play()
    {
        Vector3 pos = transform.position;
        CombatFX.Explode(pos, size, color);
        if (drops != null)
            foreach (var d in drops)
            {
                if (d == null) continue;
                Vector2 r = Random.insideUnitCircle * dropScatter;
                Vector3 p = pos + new Vector3(r.x, 0f, r.y);
                p.y = GroundUtil.Height(p, pos.y);
                Instantiate(d, p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            }
        if (destroyOnDeath) Destroy(gameObject, delay);
        else if (delay <= 0f) gameObject.SetActive(false);
        else Invoke(nameof(Hide), delay);
    }

    void Hide() => gameObject.SetActive(false);
}
