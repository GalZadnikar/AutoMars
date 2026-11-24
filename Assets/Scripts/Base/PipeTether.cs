using System;
using System.Collections.Generic;
using Dreamteck.Splines;
using UnityEngine;

/// The pipe that connects the player's back to a base (Dreamteck Splines tube).
/// It hangs through the air like a cable from the top of the base to the player's backpack, sagging a lot when
/// you're close and pulling almost straight at the end of its range, so you can see when you're near the limit.
///
/// Base network: this component sits on the home base. Every other Base whose pipe circle overlaps the circle of a
/// base already in the network (by at least linkOverlap) joins the network. The player can walk anywhere inside the
/// circles of the network, and the pipe plugs into the nearest base of the network, moving over to another base
/// as you walk from one circle into the next.
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
    [Tooltip("Where the pipe leaves the base. Empty = the top of the base model (found automatically). " +
             "Other bases use their own child with the same name, or the top of their model.")]
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
    [Tooltip("How far from a base the player can walk, per level")]
    public float[] rangePerLevel = { 12f, 16f, 20f, 24f, 28f };
    [Tooltip("Materials to reach level 2, 3, ...")]
    public int[] upgradeCost = { 40, 100, 200, 350 };
    [Range(1, 8)] public int level = 1;
    [Tooltip("Extra range from player perks (set by PlayerUpgrades)")]
    public float bonusRange;

    [Header("Base network")]
    [Tooltip("Two bases are linked when their pipe circles overlap by at least this much (m), so there is room to walk from one to the other")]
    public float linkOverlap = 2f;
    [Tooltip("The pipe moves over to another base once that base is this much closer than the current one (m)")]
    public float switchMargin = 1f;
    [Tooltip("On = a Base that is too far away to link still has its own circle and pipe: walk to it on air and its pipe grabs you. Off = only linked bases count")]
    public bool separateBases = true;

    [Tooltip("Handoff: seconds the new base's pipe takes to reach out and plug into the player")]
    public float reachTime = 0.45f;
    [Tooltip("Handoff: seconds the old pipe takes to reel back into its base after it unplugs")]
    public float retractTime = 0.6f;
    [Tooltip("Handoff: how high the reaching pipe end flies on its way to the player (m)")]
    public float reachArc = 1.5f;
    [Tooltip("Handoff: how far the unplugged pipe end droops while it's reeled in (m)")]
    public float retractDroop = 1.2f;
    [Tooltip("Build mode: circle of a Base being placed that will link up")]
    public Color previewLinkedColor = new Color(0.4f, 1f, 0.45f, 0.95f);
    [Tooltip("Build mode: circle of a Base being placed that is too far away to link")]
    public Color previewUnlinkedColor = new Color(1f, 0.35f, 0.3f, 0.95f);

    [Header("Elastic edge (stretch and snap back)")]
    [Tooltip("On = the pipe stretches past the edge, slows you down, then snaps you back. Off = the old hard stop at the edge")]
    public bool elasticEdge = true;
    [Tooltip("How far past the edge the pipe stretches before it snaps you back (m)")]
    public float stretchDistance = 2.5f;
    [Tooltip("Outward walking speed at full stretch, as a part of normal speed (lower = more tension, longer before the snap)")]
    [Range(0.03f, 1f)] public float stretchMinSpeed = 0.35f;
    [Tooltip("When you stop pushing, the stretched pipe pulls you back toward the edge (higher = faster)")]
    public float stretchPull = 4f;
    [Tooltip("Speed of the snap back (m/s)")]
    public float snapSpeed = 20f;
    [Tooltip("The snap puts you this far inside the edge (m)")]
    public float snapInside = 0.3f;
    [Tooltip("Pipe thickness at full stretch, as a part of normal thickness")]
    [Range(0.2f, 1f)] public float stretchThickness = 0.5f;
    [Tooltip("Pipe colour at full stretch")]
    public Color stretchColor = new Color(0.95f, 0.13f, 0.08f, 1f);
    [Tooltip("How hard the pipe whips loose after a snap")]
    public float snapKick = 9f;

    [Header("Unplug trip (walk out past the full stretch)")]
    [Tooltip("On = at full stretch the pipe pops off the player and whips back to the base; the player walks on without it (AirSupply counts down). Off = the pipe snaps the player back")]
    public bool unplugAtFullStretch = true;
    [Tooltip("Seconds the popped pipe takes to whip back into the base")]
    public float recoilTime = 0.3f;
    [Tooltip("The freed player lurches forward for a moment (m/s)")]
    public float unplugLurch = 4f;
    [Tooltip("Coming back: the pipe shoots out and grabs the player this far outside the edge (m)")]
    public float catchReach = 1.2f;
    [Tooltip("After the pipe pops off it can't grab the player until they are this much further out than Catch Reach (m), or one second has passed. Stops it from grabbing them back at once")]
    public float catchArmMargin = 0.3f;
    [Tooltip("Seconds the pipe takes to shoot out to the player")]
    public float catchTime = 0.16f;
    [Tooltip("The grabbing pipe is fully red and thin while it shoots out and pulls the player in; afterwards it goes back to normal over this many seconds")]
    public float catchStretchFade = 0.35f;
    [Tooltip("Range outline while unplugged (pulsing): where the pipe is. Pipe orange, so it doesn't mix with the blue air circle")]
    public Color safeEdgeColor = new Color(1f, 0.62f, 0.2f, 1f);


    
[Header("Expand effect (when the range grows)")]
    [Tooltip("On = a longer pipe is shown off: the camera pulls back, a pulse runs out from the base and pushes the edge out to the new range")]
    public bool expandEffect = true;
    [Tooltip("Material of the pulse dome (AutoMars/PipeBubble). Empty = found by shader name")]
    public Material bubbleMaterial;
    [Tooltip("The camera pulls back until the whole new circle fits, but not more than this many times its distance")]
    public float expandZoomMax = 3f;
    [Tooltip("Seconds: camera pulls back (a surge runs down the pipe to the base)")]
    public float expandZoomTime = 0.5f;
    [Tooltip("Seconds: the pulse dome grows from the base to the old edge")]
    public float expandPulseTime = 0.55f;
    [Tooltip("Seconds: the edge is pushed out to the new range (with a little overshoot)")]
    public float expandGrowTime = 0.7f;
    [Tooltip("Seconds the camera stays back after that, while the edge cools down")]
    public float expandHoldTime = 0.6f;
    [Tooltip("Colour of the edge and the pulse while they glow")]
    public Color expandGlowColor = new Color(1f, 0.78f, 0.4f, 1f);
    [Tooltip("The growing edge pushes the dark fog back: everything inside the new range becomes explored")]
    public bool expandRevealsFog = true;
    [Tooltip("How far past the edge the fog is cleared (m)")]
    public float expandFogMargin = 2f;
    [Tooltip("Height of the pulse dome, as a part of its radius")]
    public float bubbleHeight = 0.55f;

    
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
    [Tooltip("Show the range outline when the player is this close to it")]
    public float limitShowDistance = 6f;

    public event Action OnChanged;

    public int MaxLevel => pumpAtLevel > 0 ? Mathf.Min(rangePerLevel.Length + 1, pumpAtLevel) : rangePerLevel.Length;
    /// Range the pipe has at a level, before perks (Stats.NoLimit once the pump replaces it).
    public float RangeAt(int lv) => pumpAtLevel > 0 && lv >= pumpAtLevel ? Stats.NoLimit
                                  : rangePerLevel[Mathf.Clamp(lv - 1, 0, rangePerLevel.Length - 1)];
    /// The pipe was replaced by the H2O pump: the player can go anywhere.
    public bool IsPumped => pumpAtLevel > 0 && level >= pumpAtLevel;
    /// The next upgrade installs the pump.
    public bool NextIsPump => pumpAtLevel > 0 && !IsPumped && level + 1 >= pumpAtLevel;
    public GameObject Pump => pump;
    public float Range => rangePerLevel[Mathf.Clamp(level - 1, 0, rangePerLevel.Length - 1)] + bonusRange;
    public bool IsMaxLevel => level >= MaxLevel;
    public int NextCost => IsMaxLevel ? 0 : upgradeCost[Mathf.Clamp(level - 1, 0, upgradeCost.Length - 1)];
    public float NextRange => rangePerLevel[Mathf.Min(level, rangePerLevel.Length - 1)] + bonusRange;
    public bool Active => !Disabled && !IsPumped && player != null && isActiveAndEnabled;
    /// Where the pipe leaves the home base (top of the base model).
    public Vector3 AnchorPoint => baseAnchor != null ? baseAnchor.position : transform.TransformPoint(topLocal) + baseAnchorOffset;

    /// The bases the player can walk between: [0] = this home base, then every Base linked to it (directly or through others).
    public IReadOnlyList<Transform> Network { get { RefreshNetwork(); return nodes; } }
    /// The base the pipe is plugged into right now.
    public Transform AttachedBase => attached != null ? attached : transform;
    /// Two bases at these positions are close enough for their circles to link.
    public bool Links(Vector3 a, Vector3 b) => Flat(a - b) <= 2f * Range - linkOverlap;

    const int Points = 14;
    const int RingSegments = 160;
    readonly SplinePoint[] pts = new SplinePoint[Points];
    // one pipe per base that has a pipe out right now (normally 1, 2 during a handoff)
    readonly List<Hose> hoses = new List<Hose>();
    Transform rangeRoot;
    readonly List<LineRenderer> arcs = new List<LineRenderer>();
    int arcCount;
    LineRenderer previewLine;
    bool previewOn, previewLinked;
    Transform bone;
    Vector3 topLocal = new Vector3(0f, 4f, 0f);
    float limitAlpha, limitToastT, limitHitT;
    int groundVersion = -1;
    GameObject pump;

    // network
    readonly List<Transform> nodes = new List<Transform>();
    readonly List<Transform> nextNodes = new List<Transform>();
    readonly List<Transform> candidates = new List<Transform>();
    readonly Dictionary<Transform, Anchor> anchors = new Dictionary<Transform, Anchor>();
    readonly bool[] ringVisible = new bool[RingSegments];
    int netFrame = -1;
    float netRange = -1f;
    bool netStarted;
    Transform attached;

    // elastic edge
    bool snapping;
    float snapT;
    MaterialPropertyBlock mpb;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    /// 0 = inside the range, 1 = the pipe is stretched as far as it goes (about to snap back).
    public float Stretch01 => Active && elasticEdge && nodes.Count > 0
        ? Mathf.Clamp01(-DepthInside(player.position) / Mathf.Max(0.01f, stretchDistance)) : 0f;
    /// The pipe is pulling the player back in right now.
    public bool Snapping => snapping;

    // unplug trip
    bool detached, catching, catchArmed;
    bool catchPull;      // the pipe has grabbed the returning player and is pulling them in
    float catchLook;     // 1 = the grabbing pipe is shown fully stretched (red, thin, straight)
    float unplugT;
    /// The pipe popped off: the player is out on their own air (true until the pipe has grabbed them again).
    public bool Detached => detached;
    /// The pipe is shooting out to grab the returning player.
    public bool Catching => catching;
    /// The pipe popped off the player (full stretch).
    public event Action Unplugged;
    /// The pipe grabbed the player again.
    public event Action Replugged;


    // expand effect
    bool fxOn;
    float fxT, fxFrom, fxTo, fxR, fxGlow, fxSurge = -1f, fxZoom = 1f, fxZoomApplied = 1f, lastRange = -1f;
    Transform fxCentre;
    GameObject fxBubble;
    Material fxBubbleMat;
    LineRenderer fxRing;
    Unity.Cinemachine.CinemachineFollow fxFollow;
    static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    const int PulseSegments = 72;

    /// The range upgrade effect is playing.
    public bool Expanding => fxOn && fxT > 0f;



    struct Anchor { public Transform point; public Vector3 local; }

    const int Free = 0, Reaching = 1, Plugged = 2, Retracting = 3;
    class Hose
    {
        public Transform node;   // the base this pipe comes out of
        public GameObject go;
        public SplineComputer spline;
        public TubeGenerator tube;
        public int state;        // Free / Reaching / Plugged / Retracting
        public float t;          // 0 = reeled into the base, 1 = plugged into the player
        public float sag, sagVel;
        public MeshRenderer mr;
        public float shownStretch = -1f;
        public bool recoil;      // popped off the player: whips back fast


    }

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
        topLocal = TopLocalOf(transform);
        BuildLimitCircle();
    }

    void Start()
    {
        if (IsPumped && pump == null) SpawnPump();   // level set to the pump level in the Inspector
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    /// Top centre of a base's model (its HomeBase "body" if it has one), in that base's local space.
    static Vector3 TopLocalOf(Transform baseRoot)
    {
        var hb = baseRoot.GetComponent<HomeBase>();
        Transform root = hb != null && hb.body != null ? hb.body : baseRoot;
        bool any = false;
        Bounds b = default;
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
        {
            if (r.bounds.size.sqrMagnitude < 1e-4f) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return any ? baseRoot.InverseTransformPoint(new Vector3(b.center.x, b.max.y, b.center.z)) : new Vector3(0f, 4f, 0f);
    }

    /// Where the pipe leaves a base of the network.
    Vector3 AnchorOf(Transform node)
    {
        if (node == null || node == transform) return AnchorPoint;
        if (!anchors.TryGetValue(node, out var a))
        {
            string pointName = baseAnchor != null ? baseAnchor.name : "Pipe Point (Base)";
            foreach (var t in node.GetComponentsInChildren<Transform>(true))
                if (t != node && t.name == pointName) { a.point = t; break; }
            if (a.point == null) a.local = TopLocalOf(node);
            anchors[node] = a;
        }
        return a.point != null ? a.point.position : node.TransformPoint(a.local) + baseAnchorOffset;
    }

    Hose NewHose()
    {
        var go = new GameObject("Pipe (spline) " + hoses.Count);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        var spline = go.AddComponent<SplineComputer>();
        spline.space = SplineComputer.Space.World;
        spline.type = Spline.Type.CatmullRom;
        spline.sampleMode = SplineComputer.SampleMode.Default;
        spline.sampleRate = 4;
        spline.updateMode = SplineComputer.UpdateMode.None;   // rebuilt by hand once per frame

        var tube = go.AddComponent<TubeGenerator>();
        tube.spline = spline;
        tube.sides = sides;
        tube.size = thickness;
        tube.capMode = TubeGenerator.CapMethod.Round;
        tube.markDynamic = true;
        tube.uvMode = MeshGenerator.UVMode.UniformClip;
        var mr = go.GetComponent<MeshRenderer>();
        if (pipeMaterial != null) mr.sharedMaterial = pipeMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        go.SetActive(false);
        var h = new Hose { go = go, spline = spline, tube = tube, mr = mr };
        hoses.Add(h);
        return h;
    }

    void BuildLimitCircle()
    {
        var go = new GameObject("Pipe Range");
        go.transform.SetParent(transform, false);
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // lines lie flat
        rangeRoot = go.transform;
        previewLine = NewLine("Placement Preview");
        previewLine.loop = true;
        previewLine.widthMultiplier = 0.22f;
        previewLine.gameObject.SetActive(false);
        RebuildLimitCircle();
    }

    LineRenderer NewLine(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(rangeRoot, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.widthMultiplier = 0.18f;
        lr.numCornerVertices = 0;
        lr.alignment = LineAlignment.TransformZ;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        if (limitMaterial != null) lr.sharedMaterial = limitMaterial;
        lr.enabled = false;
        return lr;
    }

    // ------------------------------------------------------------------ network

    /// Which bases are linked to the home base (once per frame). Rebuilds the outline when it changes.
    void RefreshNetwork()
    {
        if (netFrame == Time.frameCount && nodes.Count > 0) return;
        netFrame = Time.frameCount;
        float r = Range;
        TrackRange(r);

        candidates.Clear();
        foreach (var hb in HomeBase.All)
            if (hb != null && hb.transform != transform && hb.isActiveAndEnabled) candidates.Add(hb.transform);
        nextNodes.Clear();
        nextNodes.Add(transform);
        for (int i = 0; i < nextNodes.Count; i++)
            for (int j = candidates.Count - 1; j >= 0; j--)
                if (Links(nextNodes[i].position, candidates[j].position)) { nextNodes.Add(candidates[j]); candidates.RemoveAt(j); }
        // bases too far away to link still have their own circle and pipe: the player gets there on air
        if (separateBases) { nextNodes.AddRange(candidates); candidates.Clear(); }


        bool changed = nextNodes.Count != nodes.Count || !Mathf.Approximately(r, netRange);
        if (!changed)
            foreach (var n in nextNodes) if (!nodes.Contains(n)) { changed = true; break; }
        if (!changed) return;

        if (netStarted)
            foreach (var n in nextNodes)
                if (!nodes.Contains(n)) { GameToast.Show("BASE LINKED  -  YOU CAN WALK THERE", new Color(0.45f, 1f, 0.45f)); break; }
        netStarted = true;
        nodes.Clear();
        nodes.AddRange(nextNodes);
        netRange = r;
        if (attached != null && !nodes.Contains(attached)) attached = null;
        RebuildLimitCircle();
    }

    /// Build mode: would a Base placed here link up? Counts bases still under construction that will link too.
    public bool WouldLink(Vector3 pos)
    {
        RefreshNetwork();
        var linked = new List<Vector3>();
        foreach (var n in nodes) linked.Add(n.position);
        var pending = new List<Vector3>();
        foreach (var s in FindObjectsByType<ConstructionSite>(FindObjectsSortMode.None))
            if (s != null && s.def != null && s.def.prefab != null && s.def.prefab.GetComponent<HomeBase>() != null)
                pending.Add(s.transform.position);
        for (int i = 0; i < linked.Count; i++)
            for (int j = pending.Count - 1; j >= 0; j--)
                if (Links(linked[i], pending[j])) { linked.Add(pending[j]); pending.RemoveAt(j); }
        foreach (var p in linked) if (Links(p, pos)) return true;
        return false;
    }

    /// Build mode: shows the network outline and the circle of the Base being placed (green = links, red = too far).
    public void ShowPlacementPreview(Vector3 pos, bool linked)
    {
        previewOn = true;
        previewLinked = linked;
        if (previewLine == null) return;
        float r = Range;
        previewLine.positionCount = RingSegments;
        for (int i = 0; i < RingSegments; i++)
        {
            float a = i * Mathf.PI * 2f / RingSegments;
            var p = pos + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            p.y = GroundUtil.Height(p, pos.y) + 0.1f;
            previewLine.SetPosition(i, p);
        }
    }

    public void HidePlacementPreview() => previewOn = false;

    /// How far inside the walkable area a point is (negative = outside every circle).
    float DepthInside(Vector3 p)
    {
        float r = Range, best = float.MinValue;
        foreach (var n in nodes) best = Mathf.Max(best, r - Flat(p - n.position));
        return best;
    }

    /// How much of what is seen at this ground point the fog remembers: 1 inside the walkable circles of the
    /// base network, fading to 0 over 'soft' metres and ending 'margin' metres past the edge.
public float Coverage(float x, float z, float margin, float soft)
    {
        if (nodes.Count == 0) return 1f;
        float end = Range + margin;   // nothing is remembered past this distance from a base
        float best = 0f;
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            if (n == null) continue;
            Vector3 c = n.position;
            float dx = x - c.x, dz = z - c.z;
            float d2 = dx * dx + dz * dz;
            if (d2 >= end * end) continue;
            float k = (end - Mathf.Sqrt(d2)) / Mathf.Max(0.01f, soft);
            if (k >= 1f) return 1f;
            if (k > best) best = k;
        }
        return best;
    }

    
/// Pick the base the pipe plugs into: the nearest one of the network (with a margin so it doesn't flicker).
    /// Pick the base the pipe should plug into: the nearest one of the network (with a margin so it doesn't flicker).
    void PickAttached()
    {
        Vector3 p = player.position;
        Transform best = null;
        float bestD = float.MaxValue;
        foreach (var n in nodes)
        {
            float d = Flat(p - n.position);
            if (d < bestD) { bestD = d; best = n; }
        }
        if (best == null || best == attached) return;
        if (attached != null)
        {
            float cur = Flat(p - attached.position);
            if (cur - bestD < switchMargin && cur <= Range) return;   // still fine where it is
        }
        attached = best;   // UpdateHoses does the handoff
    }

    // ------------------------------------------------------------------ outline (edge of the walkable area)

    /// One circle per base of the network, minus the parts that lie inside another base's circle.
    void RebuildLimitCircle()
    {
        if (rangeRoot == null) return;
        groundVersion = GroundUtil.Version;
        arcCount = 0;
        if (nodes.Count == 0) nodes.Add(transform);
        float r = OutlineRange;   // the expand effect animates it
        const int n = RingSegments;
        float step = Mathf.PI * 2f / n;
        for (int i = 0; i < nodes.Count; i++)
        {
            Vector3 c = nodes[i].position;
            bool all = true;
            for (int k = 0; k < n; k++)
            {
                ringVisible[k] = !InsideOther(RingPoint(c, r, k * step), i, r);
                if (!ringVisible[k]) all = false;
            }
            if (all) { EmitArc(c, r, 0f, Mathf.PI * 2f, true); continue; }
            for (int k = 0; k < n; k++)
            {
                if (!ringVisible[k] || ringVisible[(k + n - 1) % n]) continue;   // not the start of a visible arc
                int len = 0;
                while (len < n && ringVisible[(k + len) % n]) len++;
                float a0 = Edge(c, r, i, k * step, (k - 1) * step);
                float a1 = Edge(c, r, i, (k + len - 1) * step, (k + len) * step);
                EmitArc(c, r, a0, a1, false);
            }
        }
        for (int i = arcCount; i < arcs.Count; i++)
            if (arcs[i].gameObject.activeSelf) arcs[i].gameObject.SetActive(false);
        ApplyLimitColor();
    }

    static Vector3 RingPoint(Vector3 c, float r, float a) => c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);

    bool InsideOther(Vector3 p, int self, float r)
    {
        for (int j = 0; j < nodes.Count; j++)
            if (j != self && Flat(p - nodes[j].position) < r - 0.02f) return true;
        return false;
    }

    /// The exact angle between a visible and a hidden ring point (where two circles cross).
    float Edge(Vector3 c, float r, int self, float visibleA, float hiddenA)
    {
        for (int it = 0; it < 10; it++)
        {
            float m = (visibleA + hiddenA) * 0.5f;
            if (InsideOther(RingPoint(c, r, m), self, r)) hiddenA = m; else visibleA = m;
        }
        return visibleA;
    }

    void EmitArc(Vector3 c, float r, float a0, float a1, bool loop)
    {
        if (arcCount >= arcs.Count) arcs.Add(NewLine("Arc " + arcs.Count));
        var lr = arcs[arcCount++];
        if (!lr.gameObject.activeSelf) lr.gameObject.SetActive(true);
        lr.loop = loop;
        float step = Mathf.PI * 2f / RingSegments;
        int count = loop ? RingSegments : Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(a1 - a0) / step) + 1);
        lr.positionCount = count;
        for (int s = 0; s < count; s++)
        {
            float a = loop ? s * step : Mathf.Lerp(a0, a1, s / (float)(count - 1));
            var p = RingPoint(c, r, a);
            p.y = GroundUtil.Height(p, c.y) + 0.08f;
            lr.SetPosition(s, p);
        }
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
        netFrame = -1;
        RefreshNetwork();   // a longer pipe can link more bases
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

    /// Called by the player controller before it moves: the player can't leave the circles of the base network
    /// (slides along the edge instead), and is pulled back into the nearest circle if outside.
public Vector3 ConstrainVelocity(Vector3 pos, Vector3 velocity, float dt)
    {
        if (!Active || dt <= 0f) { snapping = false; return velocity; }
        RefreshNetwork();
        if (detached) { snapping = false; return velocity; }   // unplugged: walks freely until the pipe grabs them again (UpdateUnplug)
        float r = Range;
        Vector3 h = new Vector3(velocity.x, 0f, velocity.z);
        Vector3 next = pos + h * dt;
        if (!elasticEdge && DepthInside(next) >= 0f) return velocity;   // still inside some base's circle

        // the circle we're most inside of (or least outside of)
        Transform best = null;
        float bestOver = float.MaxValue;
        foreach (var node in nodes)
        {
            float over = Flat(pos - node.position) - r;
            if (over < bestOver) { bestOver = over; best = node; }
        }
        if (best == null) return velocity;

        Vector3 n;
        if (!elasticEdge)
        {
            // hard stop: slide along the edge
            snapping = false;
            Vector3 d = next - best.position; d.y = 0f;
            float dist = d.magnitude;
            if (dist < 0.001f) return velocity;
            n = d / dist;
            float outward = Vector3.Dot(h, n);
            if (outward > 0f) h -= n * outward;                        // end of the pipe: slide along the edge
            if (bestOver > 0f) h -= n * Mathf.Min(bestOver / dt, 8f);  // outside (teleport / downgrade): pulled back in
            if (outward > 0.5f) limitHitT = 0.25f;
            return new Vector3(h.x, velocity.y, h.z);
        }

        // elastic edge: past the edge the pipe stretches and slows you down; at full stretch it pops off
        // (unplug trip) or snaps you back
        Vector3 dp = pos - best.position; dp.y = 0f;
        float dpLen = dp.magnitude;
        if (dpLen < 0.001f) return velocity;
        n = dp / dpLen;

        if (snapping)
        {
            snapT += dt;
            float remaining = bestOver + snapInside;
            if (remaining <= 0.01f || snapT > 0.6f) snapping = false;   // back inside (or stuck on something)
            else
            {
                h = -n * Mathf.Min(snapSpeed, remaining / dt);
                return new Vector3(h.x, velocity.y, h.z);
            }
        }
        if (bestOver <= 0f) return velocity;   // inside: walk freely (also out through the edge)

        float len = Mathf.Max(0.01f, stretchDistance);
        if (bestOver >= len)
        {
            if (unplugAtFullStretch)
            {
                Unplug(n);   // too much tension: the pipe pops off and the player walks on without it
                return velocity;
            }
            // too much tension: snap back
            snapping = true;
            snapT = 0f;
            limitHitT = 0.25f;   // outline + toast
            foreach (var hose in hoses) if (hose.state == Plugged) hose.sagVel += snapKick;   // the pipe whips loose
            h = -n * Mathf.Min(snapSpeed, (bestOver + snapInside) / dt);
            return new Vector3(h.x, velocity.y, h.z);
        }

        float x = bestOver / len;
        float outSpeed = Vector3.Dot(h, n);
        if (outSpeed > 0.05f) h -= n * outSpeed * (1f - Mathf.Lerp(1f, stretchMinSpeed, x));    // harder and harder to walk out
        else h -= n * Mathf.Min(bestOver / dt, bestOver * stretchPull + 1f);                     // not pushing: pulled back to the edge
        return new Vector3(h.x, velocity.y, h.z);
    }

    // ------------------------------------------------------------------ pipe shape

void LateUpdate()
    {
        bool on = Active;
        if (on) RefreshNetwork();
        UpdateExpandFx();
        UpdateLimitFeedback(on);
        if (!on)
        {
            foreach (var h in hoses) { h.state = Free; h.node = null; h.recoil = false; if (h.go.activeSelf) h.go.SetActive(false); }
            attached = null;
            detached = false;
            catching = false;
            return;
        }
        PickAttached();
        UpdateUnplug();
        UpdateHoses();
    }

    // ------------------------------------------------------------------ unplug trip

    /// Full stretch: the pipe pops off the player and whips back to its base. The player is free (and on their own air).
    void Unplug(Vector3 outward)
    {
        if (detached) return;
        detached = true;
        catching = false;
        catchArmed = false;
        catchPull = false;
        catchLook = 0f;
        unplugT = 0f;
        snapping = false;
        foreach (var h in hoses)
        {
            if (h.state != Plugged && h.state != Reaching) continue;
            h.state = Retracting;
            h.recoil = true;
            h.sagVel -= snapKick;   // flicks up as the tension lets go
        }
        if (unplugLurch > 0f)
        {
            var pc = player.GetComponent<JoystickPlayerController>();
            if (pc != null) pc.Push(outward * unplugLurch);   // the freed player stumbles forward
        }
        CombatFX.Hit(PlugPoint(), -outward, stretchColor, 1.3f);
        Unplugged?.Invoke();
    }

    /// Unplugged and back within the pipe's reach: it shoots out to grab the player.
void UpdateUnplug()
    {
        if (!detached || catching) return;
        unplugT += Time.deltaTime;
        float over = -DepthInside(player.position);   // how far outside the edge
        // armed once the player has really left (or after a moment), so the pipe doesn't grab them the instant it pops off
        if (!catchArmed && (over > catchReach + catchArmMargin || unplugT > 1f)) catchArmed = true;
        if (catchArmed && over <= catchReach) catching = true;
    }

    /// The pipe reached the returning player: plugged in again, and it pulls them inside the edge.
    void Caught(Hose h)
    {
        detached = false;
        catching = false;
        snapping = true;   // ConstrainVelocity pulls the player in (ends at once if they're already inside)
        catchPull = true;
        catchLook = 1f;
        snapT = 0f;
        h.sagVel += snapKick;
        CombatFX.Hit(PlugPoint(), Vector3.up, safeEdgeColor, 1.3f);
        Replugged?.Invoke();
    }

    /// Handoff between bases: the new base's pipe reaches out and plugs into the player; once it's in,
    /// the old pipe unplugs and is reeled back into its own base. Turning back halfway reverses it.
void UpdateHoses()
    {
        float dt = Time.deltaTime;
        Transform target = attached != null ? attached : transform;
        // the pipe that grabs the returning player is at full tension: red and thin until it has pulled them in, then it relaxes
        if (catching || (catchPull && snapping)) catchLook = 1f;
        else
        {
            catchPull = false;
            catchLook = Mathf.MoveTowards(catchLook, 0f, Time.deltaTime / Mathf.Max(0.01f, catchStretchFade));
        }
        bool wanted = !detached || catching;   // unplugged: no pipe goes to the player until it grabs them again
        Hose th = null;
        bool anyInUse = false;
        foreach (var h in hoses)
        {
            if (h.state != Free && h.node == null) { h.state = Free; h.recoil = false; h.go.SetActive(false); }   // its base was destroyed
            if (h.state == Free) continue;
            anyInUse = true;
            if (wanted && h.node == target) th = h;
        }
        if (wanted && th == null)
        {
            foreach (var h in hoses) if (h.state == Free) { th = h; break; }
            if (th == null) th = NewHose();
            th.node = target;
            th.sagVel = 0f;
            th.recoil = false;
            if (!anyInUse && !catching) { th.state = Plugged; th.t = 1f; th.sag = maxSag; }   // start / after the pump: just there
            else { th.state = Reaching; th.t = 0f; th.sag = 0f; }
        }

        bool targetPlugged = th != null && th.state == Plugged;
        foreach (var h in hoses)
        {
            if (h.state == Free) continue;
            if (h == th)
            {
                if (h.state == Retracting) { h.state = Reaching; h.recoil = false; }   // turned back: reach out again from where it is
                if (h.state == Reaching)
                {
                    h.t += dt / Mathf.Max(0.01f, catching ? catchTime : reachTime);
                    if (h.t >= 1f)
                    {
                        h.t = 1f; h.state = Plugged; h.sagVel -= 3f;   // clunk
                        if (catching) Caught(h);
                    }
                }
            }
            else
            {
                if (h.state == Reaching) h.state = Retracting;                                       // changed our mind: pull it back
                else if (h.state == Plugged && (targetPlugged || th == null)) h.state = Retracting;   // new one is in (or unplugged): let go
                if (h.state == Retracting)
                {
                    h.t -= dt / Mathf.Max(0.01f, h.recoil ? recoilTime : retractTime);
                    if (h.t <= 0f) { h.t = 0f; h.state = Free; h.node = null; h.recoil = false; }
                }
            }
        }

        foreach (var h in hoses)
        {
            if (h.state == Free) { if (h.go.activeSelf) h.go.SetActive(false); continue; }
            DrawHose(h);
        }
    }

void DrawHose(Hose h)
    {
        Vector3 a = AnchorOf(h.node);
        Vector3 plug = PlugPoint();
        float r = thickness * 0.5f;
        float e = h.recoil ? h.t * h.t : h.t * h.t * (3f - 2f * h.t);   // a popped pipe flies back fast at first
        bool grabbing = catching && h.state == Reaching;
        Vector3 tip = h.state == Plugged ? plug : Vector3.Lerp(a, plug, e);
        if (h.state == Reaching) tip.y += Mathf.Sin(e * Mathf.PI) * (grabbing ? reachArc * 0.3f : reachArc);   // flies up and over to the player
        else if (h.recoil)
        {
            // whips: flicks up and wiggles sideways on its way back
            Vector3 side = Vector3.Cross(Vector3.up, plug - a); side.y = 0f;
            if (side.sqrMagnitude > 1e-4f) tip += side.normalized * (Mathf.Sin(e * Mathf.PI * 3f) * 0.6f * e);
            tip.y += Mathf.Sin(e * Mathf.PI) * 0.9f;
        }
        else if (h.state == Retracting) tip.y -= Mathf.Sin(e * Mathf.PI) * retractDroop; // droops while it's reeled in
        if (h.state != Plugged) tip.y = Mathf.Max(tip.y, Ground(tip) + r);

        bool visible = (tip - a).sqrMagnitude > 0.04f;   // fully reeled in: hide
        if (h.go.activeSelf != visible) h.go.SetActive(visible);
        if (!visible) return;

        // sag: lots of slack near the base, almost straight at the end (a bit of spring so it bounces);
        // a pipe that is only partly out hangs less
        float t01 = Mathf.Clamp01(Flat(tip - h.node.position) / Mathf.Max(0.1f, Range));
        float target = Mathf.Lerp(maxSag, minSag, t01 * t01) * (h.state == Plugged ? 1f : Mathf.Lerp(0.25f, 1f, e));

        // elastic edge: a stretched pipe is pulled tight, gets thinner and turns red
        // (a popped pipe goes back to normal while it is reeled in; a grabbing pipe is stretched from the start)
        float stretch = h.recoil ? e : (h.state == Plugged || grabbing) ? Mathf.Max(Stretch01, catchLook) : 0f;
        target *= 1f - stretch;
        if (!Mathf.Approximately(stretch, h.shownStretch))
        {
            h.shownStretch = stretch;
            h.tube.size = thickness * Mathf.Lerp(1f, stretchThickness, stretch);
            if (h.mr != null)
            {
                if (stretch <= 0f) h.mr.SetPropertyBlock(null);
                else
                {
                    if (mpb == null) mpb = new MaterialPropertyBlock();
                    var mat = h.mr.sharedMaterial;
                    Color normal = mat != null && mat.HasProperty(BaseColorId) ? mat.GetColor(BaseColorId) : Color.gray;
                    Color c = Color.Lerp(normal, stretchColor, stretch);
                    mpb.SetColor(BaseColorId, c);
                    mpb.SetColor(ColorId, c);
                    h.mr.SetPropertyBlock(mpb);
                }
            }
        }
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        h.sagVel += ((target - h.sag) * sagSpring - h.sagVel * sagDamping) * dt;
        h.sag += h.sagVel * dt;

        for (int i = 0; i < Points; i++)
        {
            float t = i / (float)(Points - 1);
            Vector3 p = Vector3.Lerp(a, tip, t);
            p.y -= h.sag * 4f * t * (1f - t);                     // hanging curve
            if (i > 0 && i < Points - 1) p.y = Mathf.Max(p.y, Ground(p) + r);   // rests on the ground if it's that low
            pts[i] = new SplinePoint(p) { size = SurgeSize(t), color = Color.white, normal = Vector3.up };
        }
        h.spline.SetPoints(pts, SplineComputer.Space.World);
        h.spline.RebuildImmediate(true, true);
        h.tube.RebuildImmediate();
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

    // ------------------------------------------------------------------ expand effect (the range grew)

    float OutlineRange => fxOn ? fxR : Range;

    /// Notices a longer pipe (level-up, perk) and starts the effect from the old range.
    void TrackRange(float r)
    {
        float old = lastRange;
        lastRange = r;
        if (old <= 0f || !Application.isPlaying) return;
        if (r > old + 0.01f && expandEffect && !IsPumped) PlayExpandEffect(fxOn ? fxR : old, r);
        else if (r < old - 0.01f) EndExpandFx();
    }

    /// Plays the range effect: the edge grows from one radius to another (the real range is not changed).
    public void PlayExpandEffect(float from, float to)
    {
        if (fxOn) SetFxZoom(1f);
        fxOn = true;
        fxT = 0f;
        fxFrom = from; fxTo = to; fxR = from;
        fxGlow = 0f; fxSurge = -1f;
    }

    [ContextMenu("Play Expand Effect (test)")]
    void TestExpandEffect()
    {
        if (!Application.isPlaying) return;
        PlayExpandEffect(Range * 0.75f, Range);
        RebuildLimitCircle();
    }

    void EndExpandFx()
    {
        if (!fxOn) return;
        fxOn = false;
        fxT = 0f; fxGlow = 0f; fxSurge = -1f;
        SetFxZoom(1f);
        DrawPulse(false, 0f, 0f);
        RebuildLimitCircle();
    }

    void OnDisable()
    {
        if (!fxOn) return;
        fxOn = false;
        fxT = 0f; fxGlow = 0f; fxSurge = -1f;
        if (fxFollow != null) SetFxZoom(1f);   // give the camera back its distance
        if (fxBubble != null) Destroy(fxBubble);
    }

    void UpdateExpandFx()
    {
        if (!fxOn) return;
        if (!Active) { EndExpandFx(); return; }
        if (fxT <= 0f)
        {
            if (UIState.ModalOpen || UIState.BuildMode) return;   // wait until the world is on screen again
            fxCentre = AttachedBase;
            fxZoom = AutoZoom();
        }
        fxT += Mathf.Max(Time.deltaTime, 0.0001f);
        float zoomTime = Mathf.Max(0.01f, expandZoomTime);
        float tA = zoomTime, tB = tA + expandPulseTime, tC = tB + expandGrowTime, tD = tC + expandHoldTime, tE = tD + zoomTime;

        // camera: back, hold, in again
        float z = Mathf.SmoothStep(0f, 1f, fxT / zoomTime) * (1f - Mathf.SmoothStep(0f, 1f, (fxT - tD) / zoomTime));
        SetFxZoom(Mathf.Lerp(1f, fxZoom, z));

        // a surge runs down the pipe from the player to the base...
        fxSurge = fxT < tA ? 1f - fxT / zoomTime : -1f;
        // ...the base answers with a pulse that grows to the old edge...
        float p = Mathf.Clamp01((fxT - tA) / Mathf.Max(0.01f, expandPulseTime));
        // ...and pushes the edge out to the new range
        float g = Mathf.Clamp01((fxT - tB) / Mathf.Max(0.01f, expandGrowTime));
        float gb = g - 1f;
        float back = 1f + 2.70158f * gb * gb * gb + 1.70158f * gb * gb;   // ease out with a little overshoot
        float newR = Mathf.LerpUnclamped(fxFrom, fxTo, back);
        fxGlow = fxT < tB ? 0.35f * p * p : 1f - Mathf.Clamp01((fxT - tC) / Mathf.Max(0.01f, expandHoldTime));

        bool pulse = fxT > tA && fxT < tC;
        float pe = 1f - (1f - p) * (1f - p) * (1f - p);
        DrawPulse(pulse, fxT < tB ? fxFrom * pe : newR, fxT < tB ? Mathf.Lerp(0.55f, 1f, p) : 1f - g);

        if (!Mathf.Approximately(newR, fxR))
        {
            fxR = newR;
            RebuildLimitCircle();
            // the growing edge pushes the dark fog back
            if (expandRevealsFog && FogOfWar.Instance != null)
                foreach (var node in nodes) FogOfWar.Instance.RevealExplored(node.position, fxR + expandFogMargin);
        }
        if (fxT >= tE) EndExpandFx();
    }

    /// How far the camera has to pull back so the whole new circle is on screen.
    float AutoZoom()
    {
        var cam = Camera.main;
        if (cam == null || player == null) return 1.5f;
        float d = Vector3.Distance(cam.transform.position, player.position);
        float half = d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * Mathf.Min(cam.aspect, 1f);
        float need = (fxTo + Flat(player.position - fxCentre.position)) * 1.1f;
        return Mathf.Clamp(need / Mathf.Max(0.1f, half), 1f, Mathf.Max(1f, expandZoomMax));
    }

    /// Scales the follow camera's distance (on top of the zoom slider's own value).
    void SetFxZoom(float k)
    {
        if (Mathf.Approximately(k, fxZoomApplied)) return;
        if (fxFollow == null)
        {
            var brain = Unity.Cinemachine.CinemachineBrain.GetActiveBrain(0);
            var cam = brain != null ? brain.ActiveVirtualCamera as Unity.Cinemachine.CinemachineCamera : null;
            fxFollow = cam != null ? cam.GetComponent<Unity.Cinemachine.CinemachineFollow>() : null;
            if (fxFollow == null) return;
        }
        fxFollow.FollowOffset *= k / fxZoomApplied;
        fxZoomApplied = k;
    }

    /// The pulse: a see-through dome around the base plus a ring where it touches the ground.
    void DrawPulse(bool show, float radius, float alpha)
    {
        if (!show)
        {
            if (fxBubble != null && fxBubble.activeSelf) fxBubble.SetActive(false);
            if (fxRing != null) fxRing.enabled = false;
            return;
        }
        if (fxBubble == null)
        {
            var sh = bubbleMaterial != null ? bubbleMaterial.shader : Shader.Find("AutoMars/PipeBubble");
            if (sh != null)
            {
                fxBubble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                fxBubble.name = "Pipe Expand Pulse";
                Destroy(fxBubble.GetComponent<Collider>());
                fxBubbleMat = bubbleMaterial != null ? new Material(bubbleMaterial) : new Material(sh);
                var mr = fxBubble.GetComponent<MeshRenderer>();
                mr.sharedMaterial = fxBubbleMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
        }
        if (fxRing == null && rangeRoot != null)
        {
            fxRing = NewLine("Expand Pulse");
            fxRing.loop = true;
            fxRing.widthMultiplier = 0.3f;
            fxRing.positionCount = PulseSegments;
        }
        Vector3 c = fxCentre != null ? fxCentre.position : transform.position;
        c.y = Ground(c);
        if (fxBubble != null)
        {
            if (!fxBubble.activeSelf) fxBubble.SetActive(true);
            fxBubble.transform.SetPositionAndRotation(c, Quaternion.identity);
            fxBubble.transform.localScale = new Vector3(radius * 2f, radius * 2f * bubbleHeight, radius * 2f);
            var col = expandGlowColor; col.a = 1f;
            fxBubbleMat.SetColor(ColorId, col);
            fxBubbleMat.SetFloat(AlphaId, alpha);
        }
        if (fxRing != null)
        {
            fxRing.enabled = true;
            for (int i = 0; i < PulseSegments; i++)
            {
                var q = RingPoint(c, radius, i * Mathf.PI * 2f / PulseSegments);
                q.y = GroundUtil.Height(q, c.y) + 0.1f;
                fxRing.SetPosition(i, q);
            }
            var rc = expandGlowColor; rc.a = alpha;
            fxRing.startColor = fxRing.endColor = rc;
        }
    }

    /// Pipe thickness along its length (0 = base, 1 = player): a bulge travels down it during the expand effect.
    float SurgeSize(float t)
    {
        if (fxSurge < 0f) return 1f;
        float d = (t - fxSurge) / 0.12f;
        return 1f + 0.9f * Mathf.Exp(-d * d);
    }

    
// ------------------------------------------------------------------ limit feedback

    void UpdateLimitFeedback(bool on)
    {
        limitHitT -= Time.deltaTime;
        limitToastT -= Time.unscaledDeltaTime;
        if (groundVersion != GroundUtil.Version) RebuildLimitCircle();

        float target = 0f;
        if (on)
        {
            target = Mathf.Clamp01(1f - DepthInside(player.position) / Mathf.Max(0.1f, limitShowDistance));
            if (limitHitT > 0f)
            {
                target = 1f;
                if (limitToastT <= 0f)
                {
                    limitToastT = 4f;
                    GameToast.Show(IsMaxLevel ? "PIPE AT MAX LENGTH" : "PIPE TOO SHORT  -  LEVEL UP OR BUILD A BASE CLOSER", new Color(1f, 0.62f, 0.2f));
                }
            }
            if (previewOn) target = 1f;   // build mode: placing a Base shows where you can walk
        }
        limitAlpha = Mathf.MoveTowards(limitAlpha, target, Time.unscaledDeltaTime * 3f);
        if (Expanding) limitAlpha = 1f;

        ApplyLimitColor();

        if (previewLine != null)
        {
            bool show = on && previewOn;
            if (previewLine.gameObject.activeSelf != show) previewLine.gameObject.SetActive(show);
            previewLine.enabled = show;
            if (show)
            {
                var c = previewLinked ? previewLinkedColor : previewUnlinkedColor;
                c.a *= 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 5f);
                previewLine.startColor = previewLine.endColor = c;
            }
        }
    }

void ApplyLimitColor()
    {
        bool show = limitAlpha > 0.01f;
        var col = new Color(1f, 0.55f, 0.15f, limitAlpha * 0.85f);
        if (detached)
        {
            // unplugged: the edge is where the pipe can grab you again
            col = safeEdgeColor;
            col.a = limitAlpha * (0.65f + 0.3f * Mathf.Sin(Time.unscaledTime * 6f));
        }
        if (fxGlow > 0f) col = Color.Lerp(col, expandGlowColor, fxGlow);
        float width = Mathf.Lerp(detached ? 0.3f : 0.18f, 0.5f, fxGlow);
        for (int i = 0; i < arcCount && i < arcs.Count; i++)
        {
            arcs[i].enabled = show;
            arcs[i].widthMultiplier = width;
            arcs[i].startColor = arcs[i].endColor = col;
        }
    }
}
