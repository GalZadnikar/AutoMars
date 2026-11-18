using System;
using System.Collections.Generic;
using Dreamteck.Splines;
using UnityEngine;

/// The pipe that connects the player's back to the home base (Dreamteck Splines tube).
/// It hangs through the air like a cable from the top of the base to the player's backpack, sagging a lot when
/// you're close and pulling almost straight at the end of its range, so you can see when you're near the limit.
/// The player can't walk further from the base than Range. Upgrading (button at the base) makes it longer.
[DefaultExecutionOrder(-50)]   // LateUpdate before the tube's own LateUpdate
public class PipeTether : MonoBehaviour
{
    public static PipeTether Instance { get; private set; }
    /// Debug menu: PIPE OFF lets the player go anywhere (the pipe is hidden).
    public static bool Disabled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Disabled = false; Instance = null; }

    [Header("Ends")]
    public Transform player;
    [Tooltip("Where the pipe leaves the base. Empty = the top of the base model (found automatically)")]
    public Transform baseAnchor;
    [Tooltip("Moves the automatic top-of-base point (world space)")]
    public Vector3 baseAnchorOffset = new Vector3(0f, -0.15f, 0f);
    [Tooltip("Where the pipe plugs into the player. Empty = a point on the 'Player Bone' (below) moved by Back Offset. " +
             "Easiest: make an empty child of the player's back bone, drag it where you want it, and put it here.")]
    public Transform playerAnchor;
    [Tooltip("Used only when Player Anchor is empty")]
    public string playerBone = "Body_Upper";
    [Tooltip("Plug position from that bone, in player space (z < 0 = behind)")]
    public Vector3 backOffset = new Vector3(0f, 0.2f, -0.34f);

    [Header("Length (upgrade levels)")]
    [Tooltip("How far from the base the player can walk, per level")]
    public float[] rangePerLevel = { 12f, 16f, 20f, 24f, 28f };
    [Tooltip("Materials to reach level 2, 3, ...")]
    public int[] upgradeCost = { 40, 100, 200, 350 };
    [Range(1, 5)] public int level = 1;

    [Header("H2O pump (last upgrade)")]
    [Tooltip("At this level the pipe is replaced by an H2O pump dropped where the player stands: no more pipe, no range limit. 0 = never")]
    public int pumpAtLevel = 4;
    public GameObject pumpPrefab;
    [Tooltip("Size of the pump worn on the back (the prefab is the full-size model)")]
    public float packScale = 0.2f;
    [Tooltip("Where the pump sits, from the pipe's player point, in player space (z < 0 = behind, y < 0 = lower)")]
    public Vector3 packOffset = new Vector3(0f, -0.3f, -0.05f);
    [Tooltip("Extra rotation of the worn pump (degrees, player space)")]
    public Vector3 packRotation = Vector3.zero;

    [Header("Look")]
    public Material pipeMaterial;
    public Material limitMaterial;
    [Tooltip("Pipe diameter")]
    public float thickness = 0.26f;
    [Range(3, 16)] public int sides = 8;
    [Tooltip("Sag in the middle when standing next to the base (m)")]
    public float maxSag = 2.2f;
    [Tooltip("Sag at the end of the range: almost straight (m)")]
    public float minSag = 0.15f;
    [Tooltip("How bouncy the cable is when you start / stop")]
    public float sagSpring = 18f;
    public float sagDamping = 5f;
    [Tooltip("Show the range circle when the player is this close to it")]
    public float limitShowDistance = 6f;

    public event Action OnChanged;

    public int MaxLevel => pumpAtLevel > 0 ? Mathf.Min(rangePerLevel.Length, pumpAtLevel) : rangePerLevel.Length;
    /// The pipe was replaced by the H2O pump: the player can go anywhere.
    public bool IsPumped => pumpAtLevel > 0 && level >= pumpAtLevel;
    /// The next upgrade installs the pump.
    public bool NextIsPump => pumpAtLevel > 0 && !IsPumped && level + 1 >= pumpAtLevel;
    public GameObject Pump => pump;
    public float Range => rangePerLevel[Mathf.Clamp(level - 1, 0, rangePerLevel.Length - 1)];
    public bool IsMaxLevel => level >= MaxLevel;
    public int NextCost => IsMaxLevel ? 0 : upgradeCost[Mathf.Clamp(level - 1, 0, upgradeCost.Length - 1)];
    public float NextRange => rangePerLevel[Mathf.Min(level, rangePerLevel.Length - 1)];
    public bool Active => !Disabled && !IsPumped && player != null && isActiveAndEnabled;
    /// Where the pipe leaves the base (top of the base model).
    public Vector3 AnchorPoint => baseAnchor != null ? baseAnchor.position : transform.TransformPoint(topLocal) + baseAnchorOffset;

    const int Points = 14;
    readonly SplinePoint[] pts = new SplinePoint[Points];
    SplineComputer spline;
    TubeGenerator tube;
    LineRenderer limitLine;
    Transform bone;
    Vector3 topLocal = new Vector3(0f, 4f, 0f);
    float sag, sagVel;
    float limitAlpha, limitToastT, limitHitT;
    int groundVersion = -1;
    GameObject pump;

    // ------------------------------------------------------------------ setup

    void Awake()
    {
        Instance = this;
        if (player == null)
        {
            var pc = FindFirstObjectByType<JoystickPlayerController>();
            if (pc != null) player = pc.transform;
        }
        if (player != null)
            foreach (var t in player.GetComponentsInChildren<Transform>())
                if (t.name == playerBone) { bone = t; break; }
        FindTopOfBase();
        BuildPipeObject();
        BuildLimitCircle();
        sag = maxSag;
    }

    void Start()
    {
        if (IsPumped && pump == null) SpawnPump();   // level set to the pump level in the Inspector
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    /// Top centre of the base's model (its "body" if it has one), so swapping the model needs no setup.
    void FindTopOfBase()
    {
        var hb = GetComponent<HomeBase>();
        Transform root = hb != null && hb.body != null ? hb.body : transform;
        bool any = false;
        Bounds b = default;
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
        {
            if (r.bounds.size.sqrMagnitude < 1e-4f) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (any) topLocal = transform.InverseTransformPoint(new Vector3(b.center.x, b.max.y, b.center.z));
    }

    void BuildPipeObject()
    {
        var go = new GameObject("Pipe (spline)");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        spline = go.AddComponent<SplineComputer>();
        spline.space = SplineComputer.Space.World;
        spline.type = Spline.Type.CatmullRom;
        spline.sampleMode = SplineComputer.SampleMode.Default;
        spline.sampleRate = 4;
        spline.updateMode = SplineComputer.UpdateMode.None;   // rebuilt by hand once per frame

        tube = go.AddComponent<TubeGenerator>();
        tube.spline = spline;
        tube.sides = sides;
        tube.size = thickness;
        tube.capMode = TubeGenerator.CapMethod.Round;
        tube.markDynamic = true;
        tube.uvMode = MeshGenerator.UVMode.UniformClip;
        var mr = go.GetComponent<MeshRenderer>();
        if (pipeMaterial != null) mr.sharedMaterial = pipeMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
    }

    void BuildLimitCircle()
    {
        var go = new GameObject("Pipe Range");
        go.transform.SetParent(transform, false);
        limitLine = go.AddComponent<LineRenderer>();
        limitLine.loop = true;
        limitLine.useWorldSpace = true;
        limitLine.widthMultiplier = 0.18f;
        limitLine.numCornerVertices = 0;
        limitLine.alignment = LineAlignment.TransformZ;
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // lie flat
        limitLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        limitLine.receiveShadows = false;
        if (limitMaterial != null) limitLine.sharedMaterial = limitMaterial;
        limitLine.enabled = false;
        RebuildLimitCircle();
    }

    void RebuildLimitCircle()
    {
        if (limitLine == null) return;
        const int n = 120;
        limitLine.positionCount = n;
        Vector3 c = transform.position;
        float r = Range;
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            var p = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            p.y = GroundUtil.Height(p, c.y) + 0.08f;
            limitLine.SetPosition(i, p);
        }
        groundVersion = GroundUtil.Version;
    }

    // ------------------------------------------------------------------ upgrades

    public bool TryUpgrade()
    {
        if (IsMaxLevel) return false;
        var eco = Economy.Instance;
        if (!BuildMode.FreeBuild && (eco == null || !eco.TrySpend(NextCost))) return false;
        SetLevel(level + 1);
        return true;
    }

    public void SetLevel(int lv)
    {
        level = Mathf.Clamp(lv, 1, MaxLevel);
        if (IsPumped && pump == null && player != null) SpawnPump();
        else if (!IsPumped && pump != null) { Destroy(pump); pump = null; }   // debug: back to the pipe
        RebuildLimitCircle();
        OnChanged?.Invoke();
    }

    // ------------------------------------------------------------------ H2O pump

    /// The pump becomes a small portable unit on the player's back, where the pipe used to plug in.
    void SpawnPump()
    {
        if (pumpPrefab == null || player == null) return;
        Transform mount = playerAnchor != null ? playerAnchor : (bone != null ? bone : player);
        // build it inactive and strip what a worn item must not have (collider, its own fog circle)
        var holder = new GameObject("holder");
        holder.SetActive(false);
        pump = Instantiate(pumpPrefab, holder.transform);
        pump.name = "H2O Pack";
        foreach (var c in pump.GetComponentsInChildren<Collider>(true)) Destroy(c);
        foreach (var f in pump.GetComponentsInChildren<FogRevealer>(true)) Destroy(f);
        foreach (var r in pump.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Quaternion rot = player.rotation * Quaternion.Euler(packRotation);
        pump.transform.SetPositionAndRotation(mount.position + player.rotation * packOffset, rot);
        pump.transform.localScale = Vector3.one * packScale;
        pump.transform.SetParent(mount, true);   // follows the back bone (animation)
        Destroy(holder);
        GameToast.Show("PORTABLE H2O PUMP EQUIPPED", new Color(0.4f, 0.85f, 1f));
    }

    // ------------------------------------------------------------------ player limit

    /// Called by the player controller before it moves: the player can't get further from the base than Range
    /// (slides along the edge instead), and is pulled back in if outside.
    public Vector3 ConstrainVelocity(Vector3 pos, Vector3 velocity, float dt)
    {
        if (!Active || dt <= 0f) return velocity;
        Vector3 c = transform.position;
        Vector3 d = pos - c; d.y = 0f;
        Vector3 h = new Vector3(velocity.x, 0f, velocity.z);
        Vector3 next = d + h * dt;
        float r = Range;
        float dist = next.magnitude;
        if (dist <= r || dist < 0.001f) return velocity;

        Vector3 n = next / dist;
        float outward = Vector3.Dot(h, n);
        if (outward > 0f) h -= n * outward;               // end of the pipe: slide along the edge
        float over = d.magnitude - r;
        if (over > 0f) h -= n * Mathf.Min(over / dt, 8f);  // outside (teleport / downgrade): pulled back in
        if (outward > 0.5f) limitHitT = 0.25f;
        return new Vector3(h.x, velocity.y, h.z);
    }

    // ------------------------------------------------------------------ pipe shape

    void LateUpdate()
    {
        bool on = Active;
        if (spline.gameObject.activeSelf != on) spline.gameObject.SetActive(on);
        UpdateLimitFeedback(on);
        if (!on) return;

        Vector3 a = AnchorPoint;
        Vector3 b = PlugPoint();
        float dist = Flat(player.position - transform.position);
        float t01 = Mathf.Clamp01(dist / Mathf.Max(0.1f, Range));
        // sag: lots of slack near the base, almost straight at the end (a bit of spring so it bounces)
        float target = Mathf.Lerp(maxSag, minSag, t01 * t01);
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        sagVel += ((target - sag) * sagSpring - sagVel * sagDamping) * dt;
        sag += sagVel * dt;

        float r = thickness * 0.5f;
        for (int i = 0; i < Points; i++)
        {
            float t = i / (float)(Points - 1);
            Vector3 p = Vector3.Lerp(a, b, t);
            p.y -= sag * 4f * t * (1f - t);                       // hanging curve
            if (i > 0 && i < Points - 1) p.y = Mathf.Max(p.y, Ground(p) + r);   // rests on the ground if it's that low
            pts[i] = new SplinePoint(p) { size = 1f, color = Color.white, normal = Vector3.up };
        }

        spline.SetPoints(pts, SplineComputer.Space.World);
        spline.RebuildImmediate(true, true);
        tube.RebuildImmediate();
    }

    Vector3 PlugPoint()
    {
        if (playerAnchor != null) return playerAnchor.position;
        Vector3 basePos = bone != null ? bone.position : player.position + Vector3.up * 1.5f;
        return basePos + player.rotation * backOffset;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.1f);
        Gizmos.DrawWireSphere(baseAnchor != null ? baseAnchor.position : AnchorPoint, 0.2f);
        if (playerAnchor != null) Gizmos.DrawWireSphere(playerAnchor.position, 0.15f);
    }

    float Ground(Vector3 p) => GroundUtil.Height(p, transform.position.y);
    static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

    // ------------------------------------------------------------------ upgrade (from the base popup)

    public bool CanAffordUpgrade => !IsMaxLevel && (BuildMode.FreeBuild || (Economy.Instance != null && Economy.Instance.materials >= NextCost));

    /// Called by the base popup's PIPE LENGTH card: upgrades and shows a toast.
    public void UpgradeWithFeedback()
    {
        if (IsMaxLevel) { GameToast.Show(IsPumped ? "H2O PUMP ALREADY INSTALLED" : "PIPE IS AT MAX LENGTH", new Color(1f, 0.62f, 0.2f)); return; }
        int cost = NextCost;
        bool toPump = NextIsPump;
        if (TryUpgrade())
            GameToast.Show(toPump ? "PIPE REPLACED  -  GO ANYWHERE" : "PIPE LV " + level + "  -  RANGE " + Mathf.RoundToInt(Range) + " m", new Color(0.45f, 1f, 0.45f));
        else GameToast.Show("NEED " + cost + " MATERIALS", new Color(1f, 0.4f, 0.35f));
    }

    // ------------------------------------------------------------------ limit feedback

    void UpdateLimitFeedback(bool on)
    {
        float dt = Time.deltaTime;
        limitHitT -= dt;
        limitToastT -= Time.unscaledDeltaTime;
        if (groundVersion != GroundUtil.Version) RebuildLimitCircle();

        float target = 0f;
        if (on)
        {
            float d = Flat(player.position - transform.position);
            target = Mathf.Clamp01(1f - (Range - d) / Mathf.Max(0.1f, limitShowDistance));
            if (limitHitT > 0f)
            {
                target = 1f;
                if (limitToastT <= 0f)
                {
                    limitToastT = 4f;
                    GameToast.Show(IsMaxLevel ? "PIPE AT MAX LENGTH" : "PIPE TOO SHORT  -  UPGRADE IT AT THE BASE (BASE BUTTON)", new Color(1f, 0.62f, 0.2f));
                }
            }
        }
        limitAlpha = Mathf.MoveTowards(limitAlpha, target, dt * 3f);
        if (limitLine == null) return;
        limitLine.enabled = limitAlpha > 0.01f;
        var col = new Color(1f, 0.55f, 0.15f, limitAlpha * 0.85f);
        limitLine.startColor = limitLine.endColor = col;
    }
}
