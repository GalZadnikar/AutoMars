using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// A pile the player can take trash from. Shrinks as amount goes down:
/// pieces disappear from the top first, the main mounds scale down.
/// Children of visualRoot named "Mound*" shrink smoothly, all other children pop out one by one.
public class TrashPile : MonoBehaviour
{
    public static readonly List<TrashPile> All = new List<TrashPile>();

    [Tooltip("Set automatically when the game starts (= Amount): a pile always starts full, Amount does not shrink the look")]
    public int maxAmount = 100;
    [Tooltip("How much trash the pile holds. With Auto Size (Trash Pile Builder) the builder's Amount is used")]
    public int amount = 100;
    public float pickupRadius = 4f;
    [Tooltip("Height of the top of the pile, used as the point items fly from")]
    public float topHeight = 1.6f;
    [Range(0.05f, 1f)] public float minMoundScale = 0.3f;

    [Header("Ground")]
    [Tooltip("Flatten the ground under this pile (round pad with a soft border) when the game starts. After switching it on press 'Rebuild Pile' / 'Build Pile' so the heap is built on the flat ground.")]
    public bool levelGround = false;
    [Tooltip("Radius of the flat pad in metres. 0 = automatic (the size of the pile)")]
    public float levelRadius = 0f;
    [HideInInspector] public bool groundBaked;
    [HideInInspector] public Vector3 bakedPosition;

    [Header("References")]
    public Transform visualRoot;
    public TMP_Text label;
    public GroundRing ring;

    class Part
    {
        public Transform t;
        public Vector3 baseScale;
        public float threshold;   // visible while fraction > threshold
        public float current = 1f;
        public float target = 1f;
    }

    readonly List<Part> pieces = new List<Part>();
    readonly List<Part> mounds = new List<Part>();
    bool animating;
    float labelPunch;
    Vector3 labelBaseScale = Vector3.one;
    Vector3 labelBasePos;

    [System.NonSerialized] public bool leveled;   // the ground under it has been leveled this session

    /// Radius of the flat pad: Level Radius, or the size of the pile.
    public float LevelRadius
    {
        get
        {
            if (levelRadius > 0f) return levelRadius;
            var b = GetComponent<TrashPileBuilder>();
            if (b != null) return b.autoSize ? b.radius : b.heapRadius * 1.9f;
            return pickupRadius * 0.8f;
        }
    }

    public bool IsEmpty => amount <= 0;
    float Fraction => maxAmount > 0 ? (float)amount / maxAmount : 0f;

    // piles spawned after the scene started (the scene's own piles are leveled together with the buildings)
    void Start()
    {
        if (levelGround && !leveled) GroundLeveler.LevelPile(this);
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Awake()
    {
        // a pile starts full: the amount only changes how it looks through Auto Size (the builder rebuilds the heap)
        var builder = GetComponent<TrashPileBuilder>();
        if (builder != null && builder.autoSize) amount = Mathf.Max(1, builder.amount);
        amount = Mathf.Max(0, amount);
        maxAmount = Mathf.Max(1, amount);
        if (label != null) { labelBaseScale = label.transform.localScale; labelBasePos = label.transform.localPosition; }
        CollectParts();
        Refresh(instant: true);
    }

    void CollectParts()
    {
        pieces.Clear(); mounds.Clear();
        if (visualRoot == null) return;
        var tmp = new List<(Part p, float top)>();
        foreach (Transform child in visualRoot)
        {
            var part = new Part { t = child, baseScale = child.localScale };
            if (child.name.StartsWith("Mound")) { mounds.Add(part); continue; }
            float top = child.position.y;
            foreach (var r in child.GetComponentsInChildren<Renderer>()) top = Mathf.Max(top, r.bounds.max.y);
            tmp.Add((part, top));
        }
        tmp.Sort((a, b) => b.top.CompareTo(a.top)); // highest first => removed first
        int n = tmp.Count;
        for (int i = 0; i < n; i++)
        {
            tmp[i].p.threshold = 1f - (i + 1f) / (n + 1f);
            pieces.Add(tmp[i].p);
        }
    }

    public bool InRange(Vector3 worldPos)
    {
        Vector3 d = worldPos - transform.position; d.y = 0f;
        return d.sqrMagnitude <= pickupRadius * pickupRadius;
    }

    /// Point items fly out from (top of the current pile).
    public Vector3 TakePoint =>
        transform.position + Vector3.up * Mathf.Max(0.4f, topHeight * Mathf.Lerp(minMoundScale, 1f, Fraction));

    public bool TryTake(out Vector3 from)
    {
        from = TakePoint;
        if (amount <= 0) return false;
        amount--;
        if (amount == 0) PileClearFX.Play(transform.position, LevelRadius);   // fully cleared
        labelPunch = 1f;
        Refresh(instant: false);
        return true;
    }

    public void SetHighlight(bool on) { if (ring != null) ring.SetHighlight(on); }

    void Refresh(bool instant)
    {
        float f = Fraction;
        foreach (var p in pieces) p.target = f > p.threshold ? 1f : 0f;
        float moundTarget = amount > 0 ? Mathf.Lerp(minMoundScale, 1f, f) : 0f;
        foreach (var m in mounds) m.target = moundTarget;

        if (label != null)
        {
            label.text = amount.ToString();
            label.gameObject.SetActive(amount > 0);
        }
        if (ring != null && amount <= 0) ring.gameObject.SetActive(false);

        if (instant)
        {
            foreach (var p in pieces) Apply(p, p.target);
            foreach (var m in mounds) Apply(m, m.target);
        }
        animating = true;
    }

    static void Apply(Part p, float value)
    {
        p.current = value;
        p.t.localScale = p.baseScale * value;
        bool visible = value > 0.001f;
        if (p.t.gameObject.activeSelf != visible) p.t.gameObject.SetActive(visible);
    }

    void Update()
    {
        if (label != null && labelPunch > 0f)
        {
            labelPunch = Mathf.MoveTowards(labelPunch, 0f, Time.deltaTime * 6f);
            label.transform.localScale = labelBaseScale * (1f + 0.25f * labelPunch);
        }
        if (!animating) return;

        bool still = false;
        float dt = Time.deltaTime;
        foreach (var p in pieces) still |= Step(p, dt * 5f);
        foreach (var m in mounds) still |= Step(m, dt * 2f);
        animating = still;
        // label sinks together with the pile
        if (label != null && mounds.Count > 0)
            label.transform.localPosition = labelBasePos - Vector3.up * topHeight * (1f - mounds[0].current);
    }

    static bool Step(Part p, float speed)
    {
        if (Mathf.Approximately(p.current, p.target)) return false;
        float v = Mathf.MoveTowards(p.current, p.target, speed);
        if (v > 0.001f && !p.t.gameObject.activeSelf) p.t.gameObject.SetActive(true);
        Apply(p, v);
        return !Mathf.Approximately(v, p.target);
    }
}
