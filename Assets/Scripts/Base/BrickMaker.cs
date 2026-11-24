using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// The sand-to-brick machine on a Base. Sand the player unloads goes into the hopper (SuctionGun.Unload calls
/// AddSand), the machine presses it into bricks one by one, the bricks ride the chute onto the pad and wait
/// there until the player walks onto the pad and picks them up. Only a picked-up brick pays (payKind).
/// All motion is done here in code; the model (Assets/Models/BrickMaker/BrickMaker.fbx) only has the parts.
public class BrickMaker : MonoBehaviour
{
    public static readonly List<BrickMaker> All = new List<BrickMaker>();

    public static BrickMaker Of(HomeBase h)
    {
        for (int i = 0; i < All.Count; i++) if (All[i] != null && All[i].home == h) return All[i];
        return null;
    }

    public HomeBase home;

    [Header("Conversion")]
    [Tooltip("Sand used for one brick")]
    public int sandPerBrick = 5;
    [Tooltip("Seconds to press one brick (upgradeable)")]
    public float brickTime = 0.6f;
    [Tooltip("What a picked-up brick pays")]
    public ResourceKind payKind = ResourceKind.Sand;
    public int payPerBrick = 5;

    [Header("Pad")]
    public int columns = 3;
    public int rows = 4;
    public int layers = 3;
    [Tooltip("Distance between bricks on the pad, in model units (x across, y up, z deep)")]
    public Vector3 slotSpacing = new Vector3(0.40f, 0.135f, 0.215f);
    [Tooltip("Metres from the pad centre where the player picks bricks up")]
    public float collectRadius = 1.5f;
    public float collectInterval = 0.06f;

    [Header("Hopper")]
    [Tooltip("Sand in the hopper that shows as a full heap")]
    public int hopperFull = 30;
    [Range(0.2f, 1f)] public float hopperMinScale = 0.5f;

    [Header("Parts")]
    public Transform modelRoot;
    public Transform hopperSand, valveWheel, pressHead, brickTemplate;
    public Transform mouth, chuteEnd, padCenter, hopperTop, smokePoint;
    [Tooltip("The tower, shakes while the machine works")]
    public Transform body;
    public Renderer kilnGlow;
    public TMP_Text padLabel;

    [Header("Feel")]
    public float wheelSpeed = 300f;
    public Vector3 wheelAxis = Vector3.forward;
    public float pressStroke = 0.13f;
    [Range(0f, 0.1f)] public float shake = 0.025f;
    public Color sandColor = new Color(0.97f, 0.76f, 0.36f);
    public Color glowIdle = new Color(0.35f, 0.14f, 0.05f);
    public Color glowWork = new Color(1f, 0.62f, 0.15f);
    public float streamPerSecond = 70f;

    public int Hopper { get; private set; }
    public int PadCount => resting.Count;
    public int Capacity => Mathf.Max(1, columns * rows * layers);
    public bool Working { get; private set; }
    public bool PadFull => resting.Count + incoming >= Capacity;

    class Brick { public Transform t; public int state; public float time; public Vector3 from; }
    readonly List<Brick> moving = new List<Brick>();     // state 0 = on the chute, 1 = hop to the pad, 3 = flying to the player
    readonly List<Brick> resting = new List<Brick>();    // on the pad, in slot order
    readonly Stack<Brick> pool = new Stack<Brick>();
    int incoming;

    Transform bricksRoot, player;
    ParticleSystem stream, smoke;
    MaterialPropertyBlock mpb;
    Vector3 pourFrom, pressRest, bodyScale = Vector3.one, sandScale = Vector3.one, brickScale = Vector3.one;
    float pourUntil = -1f, workTimer, workBlend, level, collectTimer, emitAcc, wobble;
    int shownLabel = -1;
    bool bodyTouched;

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }

    void OnDisable()
    {
        All.Remove(this);
        if (body != null && bodyTouched) body.localScale = bodyScale;
    }

    void Awake()
    {
        if (home == null) home = GetComponentInParent<HomeBase>();
        if (modelRoot == null) modelRoot = transform;
        if (body != null) bodyScale = body.localScale;
        if (pressHead != null) pressRest = pressHead.localPosition;
        if (hopperSand != null) { sandScale = hopperSand.localScale; hopperSand.gameObject.SetActive(false); }
        if (brickTemplate != null) { brickScale = brickTemplate.localScale; brickTemplate.gameObject.SetActive(false); }
        bricksRoot = new GameObject("Bricks").transform;
        bricksRoot.SetParent(modelRoot, false);
        mpb = new MaterialPropertyBlock();
        BuildFX();
        SetGlow(0f);
        if (padLabel != null) padLabel.text = "";
    }

    /// Sand poured in by the player (whole units). `from` = where the stream starts.
    public void AddSand(int units, Vector3 from)
    {
        if (units <= 0) return;
        Hopper += units;
        pourFrom = from;
        pourUntil = Time.time + 0.15f;
    }

    public void DevSetHopper(int units) { Hopper = Mathf.Max(0, units); }

    void Update()
    {
        float dt = Time.deltaTime;
        if (player == null)
        {
            var pc = FindFirstObjectByType<JoystickPlayerController>();
            if (pc != null) player = pc.transform;
        }

        // ---- pressing bricks
        Working = Hopper >= sandPerBrick && !PadFull && brickTemplate != null;
        if (Working)
        {
            workTimer += dt;
            if (workTimer >= brickTime)
            {
                workTimer -= brickTime;
                Hopper -= sandPerBrick;
                SpawnBrick();
            }
        }
        else workTimer = 0f;
        workBlend = Mathf.MoveTowards(workBlend, Working ? 1f : 0f, dt * 4f);

        UpdateHopper(dt);
        UpdateMachine(dt);
        UpdateBricks(dt);
        UpdateCollect(dt);

        int label = PadFull ? -2 : resting.Count;
        if (padLabel != null && label != shownLabel)
        {
            shownLabel = label;
            padLabel.text = label == -2 ? "FULL" : label > 0 ? label.ToString() : "";
        }
    }

    // ------------------------------------------------------------------ sand going in

    void UpdateHopper(float dt)
    {
        bool pouring = Time.time < pourUntil;
        if (pouring && stream != null && hopperTop != null)
        {
            emitAcc += streamPerSecond * dt;
            while (emitAcc >= 1f) { emitAcc -= 1f; EmitSand(); }
        }
        else emitAcc = 0f;

        if (hopperSand == null) return;
        float want = Mathf.Clamp01(Hopper / (float)Mathf.Max(1, hopperFull));
        // fills fast while pouring, sinks at the pace the machine eats it
        level = Mathf.MoveTowards(level, want, dt * (want > level ? 2.5f : 0.8f));
        bool show = level > 0.01f;
        if (hopperSand.gameObject.activeSelf != show) hopperSand.gameObject.SetActive(show);
        if (!show) return;
        wobble = Mathf.MoveTowards(wobble, pouring ? 1f : 0f, dt * 6f);
        float s = Mathf.Lerp(hopperMinScale, 1f, level);
        s *= 1f + 0.035f * wobble * Mathf.Sin(Time.time * 28f) + 0.012f * workBlend * Mathf.Sin(Time.time * 19f);
        hopperSand.localScale = sandScale * s;
    }

    void EmitSand()
    {
        const float T = 0.42f;
        float size = modelRoot.lossyScale.x;
        Vector3 p = pourFrom + Random.insideUnitSphere * 0.12f;
        Vector3 target = hopperTop.position + Vector3.Scale(Random.insideUnitSphere, new Vector3(0.22f, 0.05f, 0.22f)) * size;
        var ep = new ParticleSystem.EmitParams
        {
            position = p,
            velocity = (target - p) / T - 0.5f * Physics.gravity * T,
            startLifetime = T,
            startSize = Random.Range(0.14f, 0.3f),
            startColor = Color.Lerp(sandColor, Color.white, Random.value * 0.3f),
        };
        stream.Emit(ep, 1);
    }

    // ------------------------------------------------------------------ the machine working

    void UpdateMachine(float dt)
    {
        float t = Time.time;
        if (valveWheel != null && workBlend > 0f) valveWheel.Rotate(wheelAxis, wheelSpeed * workBlend * dt, Space.Self);

        if (pressHead != null)
        {
            // stamps down at the end of every brick
            float p = brickTime > 0f ? workTimer / brickTime : 0f;
            float down = Working ? Mathf.Clamp01(1f - Mathf.Abs(p - 0.82f) / 0.18f) : 0f;
            pressHead.localPosition = pressRest + Vector3.down * (pressStroke * down);
        }

        if (body != null && (workBlend > 0f || bodyTouched))
        {
            float s = Mathf.Sin(t * 24f) * shake * workBlend;
            body.localScale = new Vector3(bodyScale.x * (1f + s * 0.6f), bodyScale.y * (1f - s), bodyScale.z * (1f + s * 0.6f));
            bodyTouched = workBlend > 0f;
        }

        SetGlow(workBlend * (0.7f + 0.3f * Mathf.Sin(t * 9f)));

        if (smoke != null)
        {
            var em = smoke.emission;
            em.rateOverTime = Working ? 7f : 0f;
        }
    }

    void SetGlow(float k)
    {
        if (kilnGlow == null) return;
        Color c = Color.Lerp(glowIdle, glowWork, k);
        kilnGlow.GetPropertyBlock(mpb);
        mpb.SetColor("_BaseColor", c);
        mpb.SetColor("_EmissionColor", c * (1f + 2f * k));
        kilnGlow.SetPropertyBlock(mpb);
    }

    // ------------------------------------------------------------------ bricks

    void SpawnBrick()
    {
        Brick b = pool.Count > 0 ? pool.Pop() : null;
        if (b == null)
        {
            b = new Brick { t = Instantiate(brickTemplate, bricksRoot) };
            b.t.name = "Brick";
        }
        b.state = 0; b.time = 0f;
        b.t.localPosition = MouthPos;
        b.t.localScale = Vector3.zero;
        b.t.gameObject.SetActive(true);
        moving.Add(b);
        incoming++;
    }

    Vector3 MouthPos => mouth != null ? Local(mouth) : Vector3.zero;
    Vector3 ChutePos => chuteEnd != null ? Local(chuteEnd) : MouthPos;
    Vector3 Local(Transform t) => bricksRoot.InverseTransformPoint(t.position);

    Vector3 SlotPos(int i)
    {
        int perLayer = Mathf.Max(1, columns * rows);
        int layer = i / perLayer, k = i % perLayer;
        int r = k / columns, c = k % columns;
        Vector3 centre = padCenter != null ? Local(padCenter) : ChutePos;
        // every second layer is shifted a little, so the stack reads as a stack
        float shift = (layer % 2) * 0.02f;
        return centre + new Vector3((c - (columns - 1) * 0.5f) * slotSpacing.x + shift, layer * slotSpacing.y, ((rows - 1) * 0.5f - r) * slotSpacing.z);
    }

    void UpdateBricks(float dt)
    {
        Vector3 a = MouthPos, b = ChutePos;
        Vector3 dir = b - a, flat = new Vector3(dir.x, 0f, dir.z);
        Quaternion tilt = flat.sqrMagnitude > 1e-6f ? Quaternion.FromToRotation(flat.normalized, dir.normalized) : Quaternion.identity;
        int hop = 0;
        for (int i = 0; i < moving.Count; i++)
        {
            var br = moving[i];
            br.time += dt;
            if (br.state == 0)
            {
                float k = Mathf.Clamp01(br.time / 0.3f);
                br.t.localPosition = Vector3.Lerp(a, b, k * k);
                br.t.localRotation = tilt;
                br.t.localScale = brickScale * Back(Mathf.Clamp01(br.time / 0.18f));
                if (k >= 1f) { br.state = 1; br.time = 0f; br.from = b; }
            }
            else if (br.state == 1)
            {
                float k = Mathf.Clamp01(br.time / 0.32f);
                Vector3 to = SlotPos(resting.Count + hop);
                hop++;
                br.t.localPosition = Vector3.Lerp(br.from, to, k) + Vector3.up * (0.35f * 4f * k * (1f - k));
                br.t.localRotation = Quaternion.Slerp(tilt, Quaternion.identity, k);
                br.t.localScale = brickScale;
                if (k >= 1f)
                {
                    br.state = 2; br.time = 0f;
                    br.t.localPosition = SlotPos(resting.Count);
                    resting.Add(br);
                    moving.RemoveAt(i); i--; hop--;
                    incoming--;
                }
            }
            else   // 3: to the player
            {
                float k = Mathf.Clamp01(br.time / 0.26f);
                Vector3 to = player != null ? bricksRoot.InverseTransformPoint(player.position + Vector3.up * 1.1f) : br.from;
                br.t.localPosition = Vector3.Lerp(br.from, to, k * k) + Vector3.up * (0.5f * 4f * k * (1f - k));
                br.t.localRotation = Quaternion.Euler(0f, br.time * 720f, 0f);
                br.t.localScale = brickScale * Mathf.Lerp(1f, 0.45f, k);
                if (k >= 1f)
                {
                    if (Economy.Instance != null) Economy.Instance.Add(payKind, payPerBrick);
                    br.t.gameObject.SetActive(false);
                    pool.Push(br);
                    moving.RemoveAt(i); i--;
                }
            }
        }
        // landing squash
        for (int i = 0; i < resting.Count; i++)
        {
            var br = resting[i];
            if (br.time >= 0.2f) continue;
            br.time += dt;
            float k = Mathf.Clamp01(br.time / 0.2f);
            float s = Mathf.Sin(k * Mathf.PI) * 0.22f;
            br.t.localScale = new Vector3(brickScale.x * (1f + s * 0.5f), brickScale.y * (1f - s), brickScale.z * (1f + s * 0.5f));
        }
    }

    static float Back(float k) { k -= 1f; return 1f + k * k * (2.7f * k + 1.7f); }   // ease-out with a small overshoot

    // ------------------------------------------------------------------ picking up

    void UpdateCollect(float dt)
    {
        if (player == null || resting.Count == 0 || padCenter == null || PlayerHealth.KnockedOut) { collectTimer = 0f; return; }
        Vector3 d = player.position - padCenter.position; d.y = 0f;
        if (d.sqrMagnitude > collectRadius * collectRadius) { collectTimer = 0f; return; }
        collectTimer += dt;
        while (collectTimer >= collectInterval && resting.Count > 0)
        {
            collectTimer -= collectInterval;
            var br = resting[resting.Count - 1];
            resting.RemoveAt(resting.Count - 1);
            br.state = 3; br.time = 0f; br.from = br.t.localPosition;
            moving.Add(br);
        }
    }

    // ------------------------------------------------------------------ particles (code-built, like CombatFX)

    void BuildFX()
    {
        var solid = Resources.Load<Material>("FX/ParticleSolid");
        var soft = Resources.Load<Material>("FX/ParticleSoft");

        stream = NewSystem("Sand Stream", transform, solid != null ? solid : soft);
        {
            var m = stream.main;
            m.loop = false; m.playOnAwake = false;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.gravityModifier = 1f;
            m.maxParticles = 300;
            var em = stream.emission; em.enabled = false;
            var sh = stream.shape; sh.enabled = false;
        }

        smoke = NewSystem("Smoke", smokePoint != null ? smokePoint : transform, soft != null ? soft : solid);
        {
            smoke.transform.rotation = Quaternion.LookRotation(Vector3.up);
            var m = smoke.main;
            m.loop = true;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.4f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
            m.startColor = new Color(0.85f, 0.8f, 0.76f, 0.55f);
            m.maxParticles = 40;
            var em = smoke.emission; em.rateOverTime = 0f;
            var sh = smoke.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 12f; sh.radius = 0.12f;
            var col = smoke.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = smoke.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 1.5f));
            smoke.Play();
        }
    }

    static ParticleSystem NewSystem(string name, Transform parent, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return ps;
    }
}
