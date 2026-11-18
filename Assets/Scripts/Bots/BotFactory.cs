using TMPro;
using UnityEngine;

/// Sphere next to the base that turns materials into Scout Bots.
/// Bots are built one after another; each one walks out and lines up in front of the factory.
public class BotFactory : MonoBehaviour
{
    public static BotFactory Instance { get; private set; }

    [Header("Production (upgradeable)")]
    public GameObject botPrefab;
    public int costPerBot = 10;
    public float buildTime = 5f;

    [Header("Zone")]
    public float radius = 3.8f;

    [Header("Spawn / line-up")]
    public Transform spawnPoint;
    public Vector3 rallyOffset = new Vector3(0f, 0f, -5f);
    public int rallyColumns = 4;
    public float rallySpacing = 1.8f;

    [Header("Visuals")]
    public Transform body;
    public TMP_Text label;
    public GroundRing ring;
    public string idleText = "Bot Factory";

    /// Level + perk tree of this factory (optional component on the same object).
    public BuildingUpgrades Upgrades { get; private set; }
    /// Cost / build time after upgrades. Use these instead of the raw fields.
    public int CostPerBot => Upgrades != null ? Upgrades.Current.Int(StatType.BotCost) : costPerBot;
    public float BuildTime => Upgrades != null ? Mathf.Max(0.5f, Upgrades.Current[StatType.BuildTime]) : buildTime;

    
public int Queued { get; private set; }
    public float TimeLeft => Mathf.Max(0f, BuildTime - timer);
    public float Progress => Queued > 0 ? timer / BuildTime : 0f;
    public int MaxAffordable => Economy.Instance != null ? Economy.Instance.materials / Mathf.Max(1, CostPerBot) : 0;

    int built;
    float timer;
    Vector3 bodyScale = Vector3.one;
    int shownSeconds = -1, shownQueued = -1;

    void Awake()
    {
        Instance = this;
        Upgrades = GetComponent<BuildingUpgrades>();
        if (body != null) bodyScale = body.localScale;
    }

    public bool InRange(Vector3 worldPos)
    {
        Vector3 d = worldPos - transform.position; d.y = 0f;
        return d.sqrMagnitude <= radius * radius;
    }

    public void SetHighlight(bool on) { if (ring != null) ring.SetHighlight(on); }

    /// Spends materials and queues bots. Returns false if the player can't afford it.
    public bool Order(int count)
    {
        if (count <= 0 || Economy.Instance == null) return false;
        if (!Economy.Instance.TrySpend(count * CostPerBot)) return false;
        Queued += count;
        return true;
    }

    void Update()
    {
        if (Queued > 0)
        {
            timer += Time.deltaTime;
            if (timer >= BuildTime)
            {
                timer = 0f;
                Queued--;
                Spawn();
            }
        }
        else timer = 0f;

        if (body != null)
        {
            float s = Queued > 0 ? 1f + Mathf.Sin(Time.time * 4f) * 0.04f : 1f;
            body.localScale = bodyScale * s;
        }
        UpdateLabel();
    }

    void UpdateLabel()
    {
        if (label == null) return;
        int sec = Queued > 0 ? Mathf.CeilToInt(TimeLeft) : -1;
        if (sec == shownSeconds && Queued == shownQueued) return;
        shownSeconds = sec; shownQueued = Queued;
        if (Queued <= 0) label.text = idleText;
        else label.text = "Building " + sec + "s" + (Queued > 1 ? "\n+" + (Queued - 1) + " queued" : "");
    }

    void Spawn()
    {
        if (botPrefab == null) return;
        Vector3 pos = spawnPoint != null ? spawnPoint.position : transform.position;
        var go = Instantiate(botPrefab, pos, Quaternion.LookRotation(Vector3.back));
        built++;
        go.name = "Scout Bot " + built;
        var bot = go.GetComponent<ScoutBot>();
        if (bot != null)
        {
            bot.Init(RallyPoint(built - 1), built);
            if (Upgrades != null) bot.ApplyStats(Upgrades.Current);   // bots keep what they were built with
        }
    }

    /// Debug: build one bot right now (no cost, no wait).
    public void DevSpawnNow() => Spawn();

    public Vector3 RallyPoint(int index)
    {
        int col = index % rallyColumns, row = index / rallyColumns;
        var p = transform.position + rallyOffset +
                new Vector3((col - (rallyColumns - 1) * 0.5f) * rallySpacing, 0f, -row * rallySpacing);
        p.y = GroundUtil.Height(p, p.y);
        return p;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        for (int i = 0; i < rallyColumns * 2; i++) Gizmos.DrawWireSphere(RallyPoint(i), 0.4f);
    }
}
