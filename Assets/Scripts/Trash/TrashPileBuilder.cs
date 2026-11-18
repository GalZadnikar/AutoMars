using System.Collections.Generic;
using UnityEngine;

/// Generates the look of a pile (big heap + a few small heaps around it) from WasteOvergrowth prefabs.
/// DYNAMIC MODE (Auto Size): set Radius (how big the pile is, the whole area it covers) and Amount (how much trash is in it).
/// The heap height, number of bags / boxes and small heaps are worked out to fill that sphere: a big radius gives a big pile,
/// and Amount decides how densely it is packed. The pile is generated when you drop the prefab in the scene, and again
/// whenever you change Radius / Amount (use "Rebuild Pile" after moving it or to re-roll). A prefab spawned from code builds itself in Awake.
/// Manual mode (Auto Size off): the heap / piece fields below are used as they are. Right-click > "Build Pile".
[DefaultExecutionOrder(-10)]   // before TrashPile.Awake collects the pieces
[RequireComponent(typeof(TrashPile))]
public class TrashPileBuilder : MonoBehaviour
{
    [Header("Dynamic pile")]
    [Tooltip("On: Amount decides the size of the pile (the heap / piece fields below are filled in automatically)")]
    public bool autoSize = false;
    [Tooltip("Size of the pile: the radius of the area it covers, in metres (the cyan sphere in the Scene view)")]
    [Min(1f)] public float radius = 3.6f;
    [Tooltip("How much trash this pile holds (also its Max Amount). More trash = packed denser, fewer = sparser")]
    public int amount = 100;
    [Tooltip("Auto Size: a different look for every position (turn off to keep the Seed below)")]
    public bool seedFromPosition = true;

    [Header("Look (filled in by Auto Size)")]
    public int seed = 1;
    public float heapRadius = 2.2f;
    public float heapHeight = 1.6f;
    public int pieceCount = 18;
    public int satelliteCount = 3;
    public Vector2 pieceScale = new Vector2(1.2f, 1.7f);

    [Header("Prefabs")]
    public GameObject[] mainMounds;      // big heap meshes
    public GameObject[] smallMounds;     // small heap meshes
    public GameObject[] pieces;          // bags, boxes...

    bool flat;
    float flatY;
    float Ground(Vector3 p, float fallback) => flat ? flatY : GroundUtil.Height(p, fallback);

    System.Random rnd;
    float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
    T Pick<T>(T[] arr) => arr[rnd.Next(arr.Length)];

    // calibrated on the hand-made piles (heap 2 m / 14 pieces / 3 heaps at radius 3.6 and 100 trash)
    const float HeapPerRadius = 0.55f;      // the main heap fills ~55% of the radius, the small heaps sit around it
    const float HeightPerRadius = 0.42f;

    /// Amount a pile of this radius looks 'normal full' with (more = denser, less = sparser).
    public static float NormalAmountFor(float radius) => 7.6f * radius * radius;

    void ApplyAutoSize()
    {
        radius = Mathf.Max(1f, radius);
        heapRadius = radius * HeapPerRadius;
        heapHeight = radius * HeightPerRadius;
        float density = Mathf.Clamp(Mathf.Sqrt(Mathf.Max(1, amount) / NormalAmountFor(radius)), 0.5f, 1.6f);
        pieceCount = Mathf.Clamp(Mathf.RoundToInt(5f * Mathf.Pow(heapRadius, 1.5f) * density), 3, 200);
        satelliteCount = Mathf.Clamp(Mathf.RoundToInt(1.5f * Mathf.Pow(heapRadius, 0.8f)), 1, 12);
        if (seedFromPosition)
        {
            var pos = transform.position;
            seed = Mathf.Abs(Mathf.RoundToInt(pos.x * 73.1f + pos.z * 179.7f)) + 1;
        }
    }

    void Awake()
    {
        // spawned from code: generate before the pile collects its parts
        var pile = GetComponent<TrashPile>();
        if (Application.isPlaying && autoSize && (pile.visualRoot == null || pile.visualRoot.childCount == 0)) Build();
    }

    /// True when there is nothing generated yet.
    public bool IsEmpty { get { var pile = GetComponent<TrashPile>(); return pile.visualRoot == null || pile.visualRoot.childCount == 0; } }

    void OnDrawGizmosSelected()
    {
        // the sphere the pile will fill
        float r = autoSize ? radius : heapRadius * 1.9f;
        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * (r * 0.3f), r);
    }

    [ContextMenu("Build Pile")]
    public void Build()
    {
        var pile = GetComponent<TrashPile>();
        if (autoSize)
        {
            amount = Mathf.Max(1, amount);
            pile.maxAmount = amount;
            pile.amount = amount;
            ApplyAutoSize();
        }
        rnd = new System.Random(seed);

        var root = pile.visualRoot;
        if (root == null)
        {
            root = new GameObject("Visual").transform;
            root.SetParent(transform, false);
            pile.visualRoot = root;
        }
        for (int i = root.childCount - 1; i >= 0; i--) DestroyImmediate(root.GetChild(i).gameObject);

        // Level Ground on: the ground under the pile will be one flat pad, so build everything on that height
        flat = pile.levelGround;
        flatY = 0f;
        if (flat) flatY = GroundLeveler.PadHeightRound(transform.position, pile.LevelRadius);

        Vector3 c = transform.position;
        float groundC = Ground(c, c.y);
        transform.position = new Vector3(c.x, groundC, c.z);
        c = transform.position;

        // Main heap + wide flat base
        if (mainMounds != null && mainMounds.Length > 0)
        {
            Place(root, "Mound_Main", mainMounds[0], c, heapRadius, heapHeight, R(0, 360), true, -0.1f);
            var baseMesh = mainMounds.Length > 1 ? mainMounds[1] : mainMounds[0];
            Place(root, "Mound_Base", baseMesh, c, heapRadius * 1.3f, heapHeight * 0.35f, R(0, 360), true, -0.05f);
        }

        // Pieces on the heap surface
        for (int i = 0; i < pieceCount; i++)
        {
            float ang = R(0, Mathf.PI * 2);
            float d = Mathf.Sqrt(R(0, 1)) * heapRadius * 0.95f;
            var p = c + new Vector3(Mathf.Cos(ang) * d, 0, Mathf.Sin(ang) * d);
            float k = d / heapRadius;
            float surface = c.y + heapHeight * (1f - k * k) * 0.8f;
            p.y = Mathf.Max(Ground(p, c.y), surface);
            float s = R(pieceScale.x, pieceScale.y);
            var tilt = Quaternion.Euler(R(-20, 20), R(0, 360), R(-20, 20));
            PlacePiece(root, "Piece_" + i, Pick(pieces), p, s, tilt);
        }

        // Small heaps around
        float a0 = R(0, Mathf.PI * 2);
        for (int j = 0; j < satelliteCount; j++)
        {
            float ang = a0 + j * Mathf.PI * 2 / Mathf.Max(1, satelliteCount) + R(-0.5f, 0.5f);
            float dist = heapRadius * R(1.4f, 1.8f);
            var p = c + new Vector3(Mathf.Cos(ang) * dist, 0, Mathf.Sin(ang) * dist);
            p.y = Ground(p, c.y);
            var sat = new GameObject("Satellite_" + j).transform;
            sat.SetParent(root, false);
            sat.position = p;
            float r = R(0.6f, 0.95f);
            if (smallMounds != null && smallMounds.Length > 0)
                Place(sat, "Heap", Pick(smallMounds), p, r, r * 0.5f, R(0, 360), true, -0.05f);
            int extra = rnd.Next(1, 3);
            for (int e = 0; e < extra; e++)
            {
                var q = p + new Vector3(R(-r, r) * 0.6f, 0, R(-r, r) * 0.6f);
                q.y = Ground(q, p.y) + 0.1f;
                PlacePiece(sat, "Piece", Pick(pieces), q, R(pieceScale.x, pieceScale.y) * 0.8f,
                           Quaternion.Euler(R(-15, 15), R(0, 360), R(-15, 15)));
            }
        }

        pile.topHeight = heapHeight;
        pile.pickupRadius = heapRadius * 1.8f + 1f;

        if (pile.label != null)
            pile.label.transform.position = c + Vector3.up * (heapHeight + 1.6f);
        if (pile.ring != null)
        {
            pile.ring.transform.position = c;
            pile.ring.radius = pile.pickupRadius;
            pile.ring.Rebuild();
        }
    }

    // A wrapper sits on the ground; the model inside has its bottom at the wrapper origin,
    // so scaling the wrapper shrinks the object into the ground.
    Transform Place(Transform parent, string name, GameObject prefab, Vector3 pos, float radius, float height,
                    float yaw, bool shadows, float sink)
    {
        var w = new GameObject(name).transform;
        w.SetParent(parent, false);
        w.position = pos + Vector3.up * sink;
        var m = Instantiate(prefab, w);
        m.name = prefab.name;
        m.transform.localPosition = Vector3.zero;
        m.transform.localRotation = Quaternion.Euler(0, yaw, 0);
        m.transform.localScale = Vector3.one;
        var b = Bounds(m);
        float sx = radius * 2f / Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z));
        float sy = height / Mathf.Max(0.01f, b.size.y);
        m.transform.localScale = new Vector3(sx, sy, sx);
        b = Bounds(m);
        m.transform.position += new Vector3(pos.x - b.center.x, w.position.y - b.min.y, pos.z - b.center.z);
        SetShadows(m, shadows);
        return w;
    }

    void PlacePiece(Transform parent, string name, GameObject prefab, Vector3 pos, float scale, Quaternion rot)
    {
        var w = new GameObject(name).transform;
        w.SetParent(parent, false);
        w.position = pos;
        var m = Instantiate(prefab, w);
        m.name = prefab.name;
        m.transform.localRotation = rot;
        m.transform.localScale = Vector3.one * scale;
        var b = Bounds(m);
        // sink 25% into the heap so it looks embedded
        m.transform.position += new Vector3(pos.x - b.center.x, pos.y - b.min.y - b.size.y * 0.25f, pos.z - b.center.z);
        SetShadows(m, b.size.y > 0.35f);
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    static void SetShadows(GameObject go, bool on)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = on ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            if (r is ParticleSystemRenderer) r.gameObject.SetActive(false); // no fly particles
        }
        foreach (var c in go.GetComponentsInChildren<Collider>()) DestroyImmediate(c);
    }
}
