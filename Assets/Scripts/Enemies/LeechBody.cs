using System;
using Dreamteck.Splines;
using UnityEngine;

/// Leech visuals (E3b), driven by EnemyLeech every frame:
/// - the crater (hole) around the leech's position, a mesh that follows the ground;
/// - the tentacle: a Dreamteck SplineComputer + TubeGenerator rebuilt by hand every LateUpdate (same idea as the
///   pipe), from underground through the hole to the back of the head; tapers, slithers, pulls taut, pulses;
/// - the head: a fleshy bulb with four claw petals that open wide before a strike and close around the player.
/// Everything uses the AutoMars/LeechFlesh shader, which hides every pixel that is not in someone's sight, so the
/// tentacle seems to come out of the dark. EnemyLeech writes the pose fields below; this only draws them.
[DefaultExecutionOrder(20)]   // after EnemyLeech.LateUpdate (pose) and before nothing in particular
public class LeechBody : MonoBehaviour
{
    const string MaterialResource = "Enemies/LeechFlesh";

    [Header("Tentacle")]
    public float thickness = 0.56f;
    [Tooltip("Thickness at the head end (fraction of the base)")]
    public float tipSize = 0.6f;
    [Range(4, 16)] public int sides = 10;
    [Range(8, 40)] public int points = 22;
    [Tooltip("How far below the hole the tentacle starts")]
    public float underground = 1.4f;

    [Header("Colours")]
    public Color fleshColor = new Color(0.93f, 0.63f, 0.62f);
    public Color bandColor = new Color(0.74f, 0.4f, 0.45f);
    public Color dirtColor = new Color(0.36f, 0.22f, 0.2f);
    public Color rimColor = new Color(1f, 0.42f, 0.5f);
    public Color clawColor = new Color(0.96f, 0.9f, 0.8f);
    public Color mouthColor = new Color(0.45f, 0.03f, 0.06f);
    public Color mouthGlow = new Color(0.9f, 0.12f, 0.1f);

    [Header("Fog")]
    [Tooltip("Show the leech over explored ground nobody is looking at right now (dimmed on screen). Off = only inside current vision")]
    public bool showInExplored = true;

    [Header("Hole")]
    public float holeRadius = 0.8f;
    public float rimRadius = 1.8f;
    public float rimHeight = 0.22f;
    [Range(12, 64)] public int holeSegments = 30;

    // ---------------------------------------------------------------- pose (written by EnemyLeech)
    [NonSerialized] public bool show;
    [NonSerialized] public Vector3 head;              // centre of the head bulb (world)
    [NonSerialized] public Vector3 headDir = Vector3.forward;   // where the mouth points
    [NonSerialized] public float arch = 0.5f;         // how high the body bends up out of the hole
    [NonSerialized] public float wiggle = 0.5f;       // sideways slither (m)
    [NonSerialized] public float wiggleSpeed = 6f;
    [NonSerialized] public float taut;                // 0..1 pulled straight (holding the player)
    [NonSerialized] public float jaw;                  // 0 closed .. 1 wide open
    [NonSerialized] public float sizeMul = 1f;        // dissolve when dead
    [NonSerialized] public float limp;                // 0..1 dead: the body lies down on the ground
    [NonSerialized] public float pulse;               // 0..1 swallowing pulses running down the body

    public Transform HeadModel => headModel;
    /// Where the tentacle comes out of the ground.
    public Vector3 Mouth { get { Vector3 p = transform.position; p.y = Ground(p) + 0.1f; return p; } }

    SplineComputer spline;
    TubeGenerator tube;
    SplinePoint[] pts;
    Transform headPivot, headModel, mouthInner;
    readonly Transform[] petals = new Transform[4];
    readonly Vector3[] petalAxis = new Vector3[4];
    MeshFilter holeFilter;
    Mesh holeMesh;
    Material tubeMat, headMat, holeMat;
    MaterialPropertyBlock mpb;
    int groundVersion = -999;
    float phase, pulseT, smoothJaw;
    Vector3 smoothDir = Vector3.forward;
    bool built;

    // ---------------------------------------------------------------- build

    /// Builds the hole, tentacle and head. `pivot` is the head object (it carries the leech's Health).
    public void Build(Transform pivot)
    {
        if (built) return;
        built = true;
        headPivot = pivot;
        phase = UnityEngine.Random.value * 10f;
        mpb = new MaterialPropertyBlock();
        MakeMaterials();
        BuildHole();
        BuildTentacle();
        BuildHead();
        SetVisible(false);
    }

    void MakeMaterials()
    {
        var src = Resources.Load<Material>(MaterialResource);
        Shader sh = src != null ? src.shader : Shader.Find("AutoMars/LeechFlesh");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
        Material Make(string name)
        {
            var m = src != null ? new Material(src) : new Material(sh);
            m.name = name;
            return m;
        }
        tubeMat = Make("Leech Tentacle");
        Set(tubeMat, "_ExploredShow", showInExplored ? 1f : 0f);
        Set(tubeMat, "_BaseColor", Color.white);
        Set(tubeMat, "_VertexColorAmount", 1f);
        Set(tubeMat, "_RimColor", rimColor);

        headMat = Make("Leech Head");
        Set(headMat, "_ExploredShow", showInExplored ? 1f : 0f);
        Set(headMat, "_BaseColor", fleshColor);
        Set(headMat, "_VertexColorAmount", 0f);
        Set(headMat, "_RimColor", rimColor);

        holeMat = Make("Leech Hole");
        Set(holeMat, "_ExploredShow", showInExplored ? 1f : 0f);
        Set(holeMat, "_BaseColor", Color.white);
        Set(holeMat, "_VertexColorAmount", 1f);
        Set(holeMat, "_RimColor", Color.black);
        Set(holeMat, "_Specular", 0.08f);
        Set(holeMat, "_Wrap", 0.2f);
    }

    static void Set(Material m, string prop, Color c) { if (m.HasProperty(prop)) m.SetColor(prop, c); }
    static void Set(Material m, string prop, float f) { if (m.HasProperty(prop)) m.SetFloat(prop, f); }

    void BuildTentacle()
    {
        var go = new GameObject("Tentacle (spline)");
        go.transform.SetParent(transform, false);
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
        mr.sharedMaterial = tubeMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // a shadow would give the hidden part away
        mr.receiveShadows = true;
        pts = new SplinePoint[Mathf.Max(8, points)];
    }

    void BuildHead()
    {
        headModel = new GameObject("Head Model").transform;
        headModel.SetParent(headPivot, false);

        // bulb + neck collar
        Part(headModel, PrimitiveType.Sphere, new Vector3(0f, 0f, 0f), new Vector3(0.66f, 0.58f, 0.88f), Quaternion.identity, fleshColor, Color.black);
        Part(headModel, PrimitiveType.Sphere, new Vector3(0f, 0f, -0.36f), new Vector3(0.52f, 0.5f, 0.46f), Quaternion.identity, bandColor, Color.black);
        Part(headModel, PrimitiveType.Sphere, new Vector3(0f, 0.05f, 0.12f), new Vector3(0.5f, 0.42f, 0.5f), Quaternion.identity, Color.Lerp(fleshColor, Color.white, 0.25f), Color.black);
        // throat: dark red, glows a little when the mouth opens
        mouthInner = Part(headModel, PrimitiveType.Sphere, new Vector3(0f, 0f, 0.36f), new Vector3(0.38f, 0.38f, 0.22f), Quaternion.identity, mouthColor, Color.black);

        // four claw petals around the mouth
        for (int i = 0; i < 4; i++)
        {
            float a = (45f + 90f * i) * Mathf.Deg2Rad;
            Vector3 radial = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            var pivot = new GameObject("Petal " + i).transform;
            pivot.SetParent(headModel, false);
            pivot.localPosition = radial * 0.19f + new Vector3(0f, 0f, 0.3f);
            petals[i] = pivot;
            petalAxis[i] = Vector3.Cross(Vector3.forward, radial);   // rotating about this swings the petal outward
            // the petal: a flattened, slightly curved leaf with a pale claw at the tip
            var rot = Quaternion.LookRotation(Vector3.forward, radial);
            Part(pivot, PrimitiveType.Sphere, new Vector3(0f, 0f, 0.24f), Vector3.Scale(new Vector3(0.24f, 0.12f, 0.58f), Vector3.one), rot, Color.Lerp(fleshColor, bandColor, 0.35f), Color.black);
            Part(pivot, PrimitiveType.Sphere, radial * -0.035f + new Vector3(0f, 0f, 0.52f), new Vector3(0.09f, 0.07f, 0.3f),
                 Quaternion.AngleAxis(-18f, petalAxis[i]) * rot, clawColor, Color.black);
        }
    }

    Transform Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Quaternion rot, Color c, Color emission)
    {
        var go = GameObject.CreatePrimitive(type);
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        go.transform.localScale = scale;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = headMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        SetColor(r, c, emission);
        return go.transform;
    }

    void SetColor(Renderer r, Color c, Color emission)
    {
        r.GetPropertyBlock(mpb);
        mpb.SetColor("_BaseColor", c);
        mpb.SetColor("_EmissionColor", emission);
        r.SetPropertyBlock(mpb);
    }

    // ---------------------------------------------------------------- hole

    void BuildHole()
    {
        var go = new GameObject("Hole");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        holeFilter = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = holeMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        holeMesh = new Mesh { name = "Leech Hole" };
        holeMesh.MarkDynamic();
        holeFilter.sharedMesh = holeMesh;
        RebuildHole();
    }

    /// Crater ring that follows the ground (rebuilt when the ground changes, e.g. leveling).
    void RebuildHole()
    {
        groundVersion = GroundUtil.Version;
        int seg = holeSegments;
        // rings: centre, inner floor, lip, crest, outer edge
        float[] radius = { 0f, holeRadius * 0.55f, holeRadius, holeRadius + (rimRadius - holeRadius) * 0.42f, rimRadius };
        float[] height = { 0.05f, 0.05f, 0.1f, rimHeight, -0.03f };
        Color[] color =
        {
            new Color(0.01f, 0.008f, 0.01f), new Color(0.025f, 0.02f, 0.02f), new Color(0.12f, 0.07f, 0.06f),
            new Color(0.42f, 0.27f, 0.2f), new Color(0.5f, 0.31f, 0.22f)
        };
        int rings = radius.Length;
        var verts = new Vector3[1 + (rings - 1) * seg];
        var cols = new Color[verts.Length];
        var rnd = new System.Random(Mathf.RoundToInt(transform.position.x * 31f + transform.position.z * 17f));
        Vector3 c = transform.position;
        float baseY = c.y;
        verts[0] = new Vector3(0f, Ground(c) + height[0] - baseY, 0f);
        cols[0] = color[0];
        var jitter = new float[seg];
        for (int s = 0; s < seg; s++) jitter[s] = 0.85f + (float)rnd.NextDouble() * 0.3f;
        for (int r = 1; r < rings; r++)
            for (int s = 0; s < seg; s++)
            {
                float a = s * Mathf.PI * 2f / seg;
                float rr = radius[r] * (r >= 2 ? jitter[s] : 1f);
                Vector3 w = c + new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);
                int i = 1 + (r - 1) * seg + s;
                float h = height[r] * (r == 3 ? (0.7f + 0.6f * jitter[(s + 7) % seg] - 0.3f) : 1f);
                verts[i] = new Vector3(w.x - c.x, Ground(w) + h - baseY, w.z - c.z);
                cols[i] = color[r];
            }
        var tris = new System.Collections.Generic.List<int>();
        for (int s = 0; s < seg; s++)
        {
            int n = (s + 1) % seg;
            tris.Add(0); tris.Add(1 + n); tris.Add(1 + s);
        }
        for (int r = 1; r < rings - 1; r++)
            for (int s = 0; s < seg; s++)
            {
                int n = (s + 1) % seg;
                int a0 = 1 + (r - 1) * seg + s, a1 = 1 + (r - 1) * seg + n;
                int b0 = 1 + r * seg + s, b1 = 1 + r * seg + n;
                tris.Add(a0); tris.Add(a1); tris.Add(b0);
                tris.Add(a1); tris.Add(b1); tris.Add(b0);
            }
        holeMesh.Clear();
        holeMesh.vertices = verts;
        holeMesh.colors = cols;
        holeMesh.SetTriangles(tris, 0);
        holeMesh.RecalculateNormals();
        holeMesh.RecalculateBounds();
        holeFilter.transform.rotation = Quaternion.identity;
    }

    // ---------------------------------------------------------------- per frame

    public void SetVisible(bool on)
    {
        if (spline != null && spline.gameObject.activeSelf != on) spline.gameObject.SetActive(on);
        if (headModel != null && headModel.gameObject.activeSelf != on) headModel.gameObject.SetActive(on);
    }

    void LateUpdate()
    {
        if (!built) return;
        if (groundVersion != GroundUtil.Version) RebuildHole();
        SetVisible(show);
        if (!show) return;

        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        float time = Time.time + phase;

        // ---- head
        Vector3 dir = headDir.sqrMagnitude > 1e-4f ? headDir.normalized : smoothDir;
        smoothDir = Vector3.Slerp(smoothDir, dir, 1f - Mathf.Exp(-dt * 14f));
        if (smoothDir.sqrMagnitude < 1e-4f) smoothDir = Vector3.forward;
        headPivot.position = head;
        headPivot.rotation = Quaternion.identity;   // keeps the HP bar upright
        headModel.rotation = Quaternion.LookRotation(smoothDir, Vector3.up) * Quaternion.Euler(0f, 0f, Mathf.Sin(time * 1.3f) * 8f * (1f - taut));
        float breathe = 1f + Mathf.Sin(time * 2.4f) * 0.035f * (1f - limp);
        headModel.localScale = Vector3.one * Mathf.Max(0.001f, sizeMul) * breathe;

        smoothJaw = Mathf.Lerp(smoothJaw, Mathf.Clamp01(jaw), 1f - Mathf.Exp(-dt * 18f));
        float ang = Mathf.Lerp(-14f, 72f, smoothJaw);
        for (int i = 0; i < 4; i++)
            petals[i].localRotation = Quaternion.AngleAxis(ang + Mathf.Sin(time * 5f + i * 1.7f) * 4f * (1f - limp), petalAxis[i]);
        if (mouthInner != null)
            SetColor(mouthInner.GetComponent<Renderer>(), mouthColor, mouthGlow * (smoothJaw * 0.9f));

        // ---- tentacle
        BuildPoints(time, dt);
        spline.SetPoints(pts, SplineComputer.Space.World);
        spline.RebuildImmediate(true, true);
        tube.RebuildImmediate();
    }

    void BuildPoints(float time, float dt)
    {
        int n = pts.Length;
        Vector3 m = Mouth;
        // the tube ends inside the back of the bulb
        Vector3 neck = head - smoothDir * 0.32f * Mathf.Max(0.2f, sizeMul);
        Vector3 flat = neck - m; flat.y = 0f;
        float dist = flat.magnitude;
        Vector3 fdir = dist > 0.01f ? flat / dist : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, fdir);

        // cubic Bézier from the hole to the neck: comes up out of the hole, bends over, arrives along the head's direction
        float rise = Mathf.Clamp(dist * 0.42f, 0.7f, 3.4f) * Mathf.Lerp(arch, 0.35f, taut);
        Vector3 p0 = m;
        Vector3 p1 = m + Vector3.up * rise;
        Vector3 p2 = neck - smoothDir * Mathf.Clamp(dist * 0.35f, 0.45f, 2.6f);
        Vector3 p3 = neck;
        // taut: handles move onto the straight line, so the body becomes a tense, almost straight rope
        p1 = Vector3.Lerp(p1, Vector3.Lerp(p0, p3, 0.33f) + Vector3.up * 0.4f, taut * 0.8f);
        p2 = Vector3.Lerp(p2, Vector3.Lerp(p0, p3, 0.7f), taut * 0.8f);

        if (pulse > 0f) pulseT += dt * 0.9f;
        float wig = wiggle * (1f - taut * 0.85f) * (1f - limp);
        float radiusBase = thickness * 0.5f;

        pts[0] = new SplinePoint(m - Vector3.up * underground) { size = 1f, color = dirtColor, normal = Vector3.up };
        for (int i = 1; i < n; i++)
        {
            float t = (i - 1) / (float)(n - 2);
            float u = 1f - t;
            Vector3 p = u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;

            float env = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * t)), 0.8f);   // Max: sin(pi) is slightly negative -> NaN
            p += side * (Mathf.Sin(t * 2.6f * Mathf.PI * 2f - time * wiggleSpeed) * wig * env);
            p.y += Mathf.Sin(t * 1.7f * Mathf.PI * 2f - time * wiggleSpeed * 0.7f) * wig * 0.3f * env;

            float size = Mathf.Lerp(1f, tipSize, t);
            if (pulse > 0f)
            {
                // two swallowing bulges running from the head down into the hole
                for (int k = 0; k < 2; k++)
                {
                    float pp = 1f - Mathf.Repeat(pulseT + k * 0.5f, 1f);
                    float d = (t - pp) / 0.07f;
                    size *= 1f + 0.38f * pulse * Mathf.Exp(-d * d);
                }
            }
            size *= Mathf.Max(0f, sizeMul);

            float r = radiusBase * size;
            float g = Ground(p);
            if (limp > 0f) p.y = Mathf.Lerp(p.y, g + r * 0.8f, limp * Mathf.Clamp01(t * 6f));
            if (t > 0.06f) p.y = Mathf.Max(p.y, g + r * 0.85f);   // never through the ground (the first bit is in the hole)

            Color col = (i % 2 == 0) ? fleshColor : Color.Lerp(fleshColor, bandColor, 0.55f);
            col = Color.Lerp(dirtColor, col, Mathf.Clamp01(t * 7f));
            pts[i] = new SplinePoint(p) { size = Mathf.Max(0.001f, size), color = col, normal = Vector3.up };
        }
    }

    float Ground(Vector3 p) => GroundUtil.Height(p, transform.position.y);

    void OnDestroy()
    {
        if (tubeMat != null) Destroy(tubeMat);
        if (headMat != null) Destroy(headMat);
        if (holeMat != null) Destroy(holeMat);
        if (holeMesh != null) Destroy(holeMesh);
    }
}
