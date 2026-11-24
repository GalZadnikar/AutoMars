using System.Collections.Generic;
using UnityEngine;

/// SAND PROTOTYPE - vacuum gun.
/// When sand is in a cone in front of the player it gets sucked in, until the tank is full.
/// Stand at the home base to empty the tank. Standalone: it only reads the player's position and the
/// home base zone; PlayerCarrier, Economy and the rest of the game are not touched.
/// Turn the whole prototype off by disabling its root object.
public class SuctionGun : MonoBehaviour
{
    [Tooltip("The player (found automatically)")]
    public Transform player;

    [Header("Gun stats")]
    [Tooltip("Gun level. Tough (crusted) sand needs a gun of at least its hardness; a weaker one only scratches it.")]
    public int power = 1;
    public float range = 3.5f;
    [Range(5f, 80f)] public float halfAngle = 30f;
    [Tooltip("How fast sand disappears at the nozzle (1 = one full grid point per second)")]
    public float suckRate = 1.8f;
    public int capacity = 60;
    [Tooltip("Testing: the tank never fills up (sucked sand still counts, and pays out at the base)")]
    public bool unlimitedCapacity;
    [Tooltip("Tank units per cubic metre of sand")]
    public float unitsPerCubicMetre = 1f;
    [Tooltip("Units per second emptied at the home base")]
    public float unloadRate = 40f;
    [Tooltip("Multiplies the materials paid out (for gun upgrades / bonuses)")]
    public float materialMultiplier = 1f;

    [Tooltip("Walking speed while sucking (0.6 = 60% of normal). Eases in and out with the suction.")]
    [Range(0.1f, 1f)] public float suckMoveSpeed = 0.6f;

    [Header("Walking into sand")]
    [Tooltip("Reach of the short, wide suction right in front of the feet (m). It clears the path as wide as the player.")]
    public float mouthRange = 1.6f;
    [Range(20f, 120f)] public float mouthHalfAngle = 75f;
    [Tooltip("How much stronger the mouth is than the long cone. Higher = you walk through sand faster. Good upgrade stat.")]
    public float mouthPower = 2.5f;
    [Tooltip("Sand right ahead slows the player smoothly, so he walks in at the pace the gun clears it")]
    public bool sandSlowsWalking = true;
    /// Set by the shovel when its steady digging walk is off (A/B test): sand slows the player even with the gun put away.
    [System.NonSerialized] public bool resistWhileHolstered;
    [Tooltip("Walking speed against deep sand (share of normal speed)")]
    [Range(0.05f, 1f)] public float minSandSpeed = 0.15f;

    [Header("Auto-aim (joystick released)")]
    [Tooltip("When the joystick is let go, the player slowly turns towards the most sand in range")]
    public bool autoAim = true;
    [Tooltip("Seconds after letting go of the joystick before auto-aim starts turning")]
    public float autoAimDelay = 0.25f;
    [Tooltip("Turn speed while auto-aiming (degrees per second)")]
    public float autoAimTurnSpeed = 220f;
    [Tooltip("Seconds the turn takes to ease in and out (0 = constant speed)")]
    public float autoAimSmoothTime = 0.2f;
    [Tooltip("How much less sand behind the player counts than sand in front (0 = all directions equal)")]
    [Range(0f, 0.9f)] public float autoAimFrontBias = 0.45f;
    [Tooltip("A new direction must have this much more sand (0.2 = 20%) before it switches to it - stops wobbling between two piles")]
    [Range(0f, 1f)] public float autoAimStickiness = 0.2f;
    [Tooltip("Ignore directions with less sand than this (about square metres of full sand in the cone)")]
    public float autoAimMinSand = 0.25f;
    [Tooltip("How often the best direction is searched again (seconds)")]
    public float autoAimRescan = 0.1f;

    [Header("Look")]
    public Vector3 gunLocalPos = new Vector3(0.4f, 1.25f, 0.5f);
    public float gunScale = 1.5f;
    public Material particleMaterial;
    public Material swirlMaterial;
    public Color sandColor = new Color(1f, 0.83f, 0.42f);
    public float particlesPerSecond = 110f;
    public float particleSpeed = 7f;

    public float Stored { get; private set; }
    /// Materials the sand in the tank is worth (paid out at the base).
    public float StoredMaterials { get; private set; }
    /// Fired with the amount each time materials are paid out.
    public event System.Action<int> PaidOut;
    float payout;
    public bool IsFull => !unlimitedCapacity && Stored >= capacity - 0.001f;
    public bool Sucking { get; private set; }
    public bool Unloading { get; private set; }
    /// True while auto-aim is turning / holding the player towards sand.
    public bool AutoAiming { get; private set; }
    /// Put away (another tool is in the hands): hidden, no suction, no auto-aim.
    public bool Holstered { get; set; }
    /// The gun model (null outside Play mode).
    public Transform GunTransform => gun;
    /// Debug: set the tank content (0 = empty it without paying out).
    public void DevSetStored(float units) { Stored = Mathf.Max(0f, units); if (Stored <= 0f) { StoredMaterials = 0f; payout = 0f; } }
    /// Sand from another tool (the shovel) goes into the same tank. Returns the units that fitted.
    public float AddSand(float volume, float materials)
    {
        float units = volume * unitsPerCubicMetre;
        float add = Mathf.Max(0f, unlimitedCapacity ? units : Mathf.Min(units, capacity - Stored));
        Stored += add;
        StoredMaterials += materials;
        return add;
    }

    JoystickPlayerController pc;
    const int AimBins = 72;   // 5 degrees each
    readonly float[] aimBins = new float[AimBins];
    float idleTime, rescanT, aimYaw;
    bool aimHasTarget;

    Transform gun, nozzle, swirl;
    ParticleSystem ps;
    float blend, emitAcc, swirlAngle;
    readonly List<Object> owned = new List<Object>();

    // ------------------------------------------------------------------ lifecycle

    void OnEnable()
    {
        if (player == null)
        {
            var pc = FindFirstObjectByType<JoystickPlayerController>();
            if (pc != null) player = pc.transform;
        }
        if (player != null) pc = player.GetComponent<JoystickPlayerController>();
        if (player == null || !Application.isPlaying) return;
        BuildGun();
        BuildFX();
    }

    void OnDisable()
    {
        if (pc != null) pc.speedMultiplier = 1f;
        foreach (var o in owned) if (o != null) Destroy(o);
        owned.Clear();
        gun = nozzle = swirl = null;
        ps = null;
    }

    void Update()
    {
        if (player == null || gun == null) return;
        float dt = Time.deltaTime;
        Vector3 pos = player.position;
        if (gun.gameObject.activeSelf == Holstered) gun.gameObject.SetActive(!Holstered);
        UpdateAutoAim(dt, pos);   // before reading forward, so the gun sucks where we just turned
        Vector3 fwd = player.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        fwd.Normalize();

        // empty the tank at home
        Unloading = false;
        if (Stored > 0f)
            foreach (var h in HomeBase.All)
                if (h != null && h.InRange(pos)) { Unload(dt, h); Unloading = true; break; }

        // suck: the long cone, plus a short wide 'mouth' at the feet that keeps the path as wide as the player
        Sucking = false;
        if (!Holstered && !IsFull && !Unloading && !PlayerHealth.KnockedOut)
        {
            float vol = 0f, mats = 0f;
            Vector3 from = pos + fwd * 0.2f;
            Vector3 mouth = pos - fwd * MouthBack;
            for (int i = 0; i < SandField.All.Count; i++)
            {
                var f = SandField.All[i];
                vol += f.Suck(from, fwd, range, halfAngle, suckRate, dt, out float m, power);
                mats += m;
                CrustFX(f);
                if (mouthPower > 0f && mouthRange > 0f)
                {
                    vol += f.Suck(mouth, fwd, mouthRange + MouthBack, mouthHalfAngle, suckRate * mouthPower, dt, out m, power);
                    mats += m;
                    CrustFX(f);
                }
            }
            if (vol > 0f)
            {
                Sucking = true;
                float units = vol * unitsPerCubicMetre;
                float add = unlimitedCapacity ? units : Mathf.Min(units, capacity - Stored);
                Stored += add;
                StoredMaterials += mats;   // full value, also for the auto-cleared rest of a field
            }
        }

        blend = Mathf.MoveTowards(blend, Sucking ? 1f : 0f, dt * (Sucking ? 10f : 3f));

        // walking speed: a bit slower while sucking, and sand right ahead slows the player smoothly, so he glides
        // into it at the pace the gun clears it (instead of running into a wall and stopping dead)
        float want = Mathf.Lerp(1f, suckMoveSpeed, blend);
        // only while the gun is out: the shovel paces the walk itself (a steady digging walk), and this per-bite
        // slow-down on top of it made the player surge and stop after every bite
        if (sandSlowsWalking && (!Holstered || resistWhileHolstered)) want *= SandResistance(pos, fwd);
        moveMul = Mathf.MoveTowards(moveMul, want, dt * (want < moveMul ? 14f : 5f));
        if (pc != null) pc.speedMultiplier = moveMul;
        UpdateFX(dt, pos, fwd);
    }

    float nextCrustFx;
    static readonly Color CrackCol = new Color(0.42f, 0.19f, 0.1f);

    /// Tough sand: plates of crust break off and fly to the nozzle.
    void CrustFX(SandField f)
    {
        if (f.LastCrustBreaks <= 0 || Time.time < nextCrustFx) return;
        nextCrustFx = Time.time + 0.06f;
        Vector3 to = nozzle.position - f.LastCrustPos;
        CombatFX.CrustBreak(f.LastCrustPos, to.normalized + Vector3.up * 0.3f, Color.Lerp(f.ParticleColor, CrackCol, 0.45f));
    }

    const float MouthBack = 0.35f;   // the mouth cone starts a little behind the player, so it also covers beside the feet
    float moveMul = 1f, aimYawVel;

    /// 1 = free, minSandSpeed = deep sand right in front of the feet.
    float SandResistance(Vector3 pos, Vector3 fwd)
    {
        float a = 0f;
        for (int k = -1; k <= 1; k++)
        {
            Vector3 d = Quaternion.Euler(0f, k * 35f, 0f) * fwd;
            a = Mathf.Max(a, SandAt(pos + d * 0.7f));
            a = Mathf.Max(a, SandAt(pos + d * 1.0f) * 0.7f);
        }
        return Mathf.Lerp(1f, minSandSpeed, Mathf.InverseLerp(0.08f, 0.45f, a));
    }

    static float SandAt(Vector3 p)
    {
        float a = 0f;
        for (int i = 0; i < SandField.All.Count; i++) a = Mathf.Max(a, SandField.All[i].AmountAt(p));
        return a;
    }

    
    // ------------------------------------------------------------------ auto-aim

    /// Joystick let go: turn smoothly towards the direction with the most sand in range.
    /// Only turns - the cone, range and suck speed stay the same, so walking into sand is still faster.
    void UpdateAutoAim(float dt, Vector3 pos)
    {
        AutoAiming = false;
        bool steering = pc != null && pc.RawInput.magnitude > pc.deadZone;
        bool blocked = UIState.ModalOpen || UIState.BuildMode || PlayerHealth.KnockedOut || EnemyLeech.PlayerHeld
                       || PlayerMelee.Engaged;   // swinging at a melee target: that turns the player, not the sand
        if (!autoAim || Holstered || steering || blocked || IsFull || Unloading)   // Unloading = last frame's value
        {
            idleTime = 0f;
            rescanT = 0f;
            aimHasTarget = false;
            aimYawVel = 0f;
            return;
        }
        idleTime += dt;
        if (idleTime < autoAimDelay) return;

        rescanT -= dt;
        if (rescanT <= 0f)
        {
            rescanT = autoAimRescan;
            System.Array.Clear(aimBins, 0, AimBins);
            for (int i = 0; i < SandField.All.Count; i++) SandField.All[i].AddToAimBins(pos, range, aimBins);

            float best = 0f, bestYaw = 0f;
            for (int b = 0; b < AimBins; b++)
            {
                float yaw = (b + 0.5f) * 360f / AimBins;
                float s = AimScore(yaw) * FrontBias(yaw);
                if (s > best) { best = s; bestYaw = yaw; }
            }
            // keep the current direction (or the way we already face) unless the best one is clearly better
            float curYaw = aimHasTarget ? aimYaw : player.eulerAngles.y;
            float current = AimScore(curYaw) * FrontBias(curYaw);
            if (best < autoAimMinSand) aimHasTarget = false;
            else if (current >= autoAimMinSand && best <= current * (1f + autoAimStickiness)) { aimYaw = curYaw; aimHasTarget = true; }
            else { aimYaw = bestYaw; aimHasTarget = true; }
        }
        if (!aimHasTarget) return;

        AutoAiming = true;
        // eases into and out of the turn (a constant-speed turn looks robotic)
        float newYaw = Mathf.SmoothDampAngle(player.eulerAngles.y, aimYaw, ref aimYawVel, autoAimSmoothTime, autoAimTurnSpeed, dt);
        player.rotation = Quaternion.Euler(0f, newYaw, 0f);
    }

    /// Sand in front counts fully, sand behind less: letting go of the joystick shouldn't spin the player around.
    float FrontBias(float yaw)
    {
        float off = Mathf.Abs(Mathf.DeltaAngle(player.eulerAngles.y, yaw)) / 180f;
        return Mathf.Lerp(1f, 1f - autoAimFrontBias, off);
    }

    /// Sand in the gun's cone if it pointed at this yaw (centre of the cone counts most, like Suck).
    float AimScore(float yaw)
    {
        float s = 0f, binSize = 360f / AimBins;
        for (int b = 0; b < AimBins; b++)
        {
            if (aimBins[b] <= 0f) continue;
            float d = Mathf.Abs(Mathf.DeltaAngle(yaw, (b + 0.5f) * binSize));
            if (d > halfAngle) continue;
            float side = d / halfAngle;
            s += aimBins[b] * (1f - side * side);
        }
        return s;
    }

    // ------------------------------------------------------------------ unloading

    void Unload(float dt, HomeBase home)
    {
        float d = Mathf.Min(Stored, unloadRate * dt);
        float share = Stored > 0f ? d / Stored : 1f;
        float m = StoredMaterials * share;
        Stored -= d;
        StoredMaterials -= m;
        payout += m * materialMultiplier;
        if (Stored <= 0.0001f)
        {
            Stored = 0f;
            StoredMaterials = 0f;
            payout = Mathf.Round(payout);   // tank empty: pay the rest, rounded
        }
        int whole = Mathf.FloorToInt(payout);
        if (whole > 0)
        {
            payout -= whole;
            // a base with a brick machine takes the sand into its hopper (the bricks pay when picked up)
            var maker = BrickMaker.Of(home);
            if (maker != null) maker.AddSand(whole, Holstered || nozzle == null ? player.position + Vector3.up * 1.1f : nozzle.position);
            else if (Economy.Instance != null) Economy.Instance.AddMaterials(whole);
            PaidOut?.Invoke(whole);
        }
    }

    
// ------------------------------------------------------------------ effects

    void UpdateFX(float dt, Vector3 pos, Vector3 fwd)
    {
        // gun rattles while it works
        gun.localPosition = gunLocalPos + (blend > 0f ? Random.insideUnitSphere * 0.02f * blend : Vector3.zero);

        // swirl at the nozzle
        swirlAngle -= dt * 900f;
        bool showSwirl = blend > 0.01f;
        if (swirl.gameObject.activeSelf != showSwirl) swirl.gameObject.SetActive(showSwirl);
        if (showSwirl)
        {
            var cam = Camera.main;
            swirl.position = nozzle.position + fwd * 0.12f;
            var face = cam != null ? Quaternion.LookRotation(cam.transform.forward, Vector3.up) : Quaternion.LookRotation(-fwd);
            swirl.rotation = face * Quaternion.Euler(0f, 0f, swirlAngle);
            swirl.localScale = Vector3.one * (blend * (0.9f + 0.12f * Mathf.Sin(Time.time * 25f)));
        }

        // sand stream flying into the nozzle
        if (Sucking)
        {
            emitAcc += particlesPerSecond * dt;
            while (emitAcc >= 1f) { emitAcc -= 1f; EmitOne(pos, fwd); }
        }
        else emitAcc = 0f;
    }

    void EmitOne(Vector3 pos, Vector3 fwd)
    {
        for (int tries = 0; tries < 6; tries++)
        {
            float r = Mathf.Lerp(0.6f, range, Mathf.Sqrt(Random.value));
            float a = Random.Range(-halfAngle, halfAngle);
            Vector3 p = pos + Quaternion.Euler(0f, a, 0f) * fwd * r;
            if (!SandField.TryGetSurface(p, out float y, out float amt, out SandField field) || amt < 0.05f) continue;
            p.y = y - Random.Range(0f, 0.3f);
            Vector3 target = nozzle.position;
            float life = Mathf.Max(0.08f, Vector3.Distance(p, target) / particleSpeed);
            var ep = new ParticleSystem.EmitParams
            {
                position = p,
                velocity = (target - p) / life,
                startLifetime = life,
                startSize = Random.Range(0.12f, 0.32f),
                startColor = Color.Lerp(field != null ? field.ParticleColor : sandColor, Color.white, Random.value * 0.35f),
            };
            ps.Emit(ep, 1);
            return;
        }
    }

    // ------------------------------------------------------------------ building (placeholder art)

    void BuildGun()
    {
        gun = new GameObject("Suction Gun (prototype)").transform;
        owned.Add(gun.gameObject);
        gun.SetParent(player, false);
        gun.localPosition = gunLocalPos;
        gun.localScale = Vector3.one * gunScale;

        var red = LitMat(new Color(0.85f, 0.2f, 0.18f));
        var purple = LitMat(new Color(0.55f, 0.45f, 0.95f));
        var dark = LitMat(new Color(0.2f, 0.2f, 0.24f));
        Part(PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.05f), new Vector3(90f, 0f, 0f), new Vector3(0.2f, 0.3f, 0.2f), red);       // body
        Part(PrimitiveType.Cylinder, new Vector3(0f, 0f, -0.27f), new Vector3(90f, 0f, 0f), new Vector3(0.27f, 0.07f, 0.27f), purple); // back cap
        Part(PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.38f), new Vector3(90f, 0f, 0f), new Vector3(0.32f, 0.06f, 0.32f), purple);  // nozzle ring
        Part(PrimitiveType.Cylinder, new Vector3(0f, 0.15f, -0.05f), new Vector3(90f, 0f, 0f), new Vector3(0.09f, 0.2f, 0.09f), dark); // top tube
        Part(PrimitiveType.Cube, new Vector3(0f, -0.17f, -0.08f), new Vector3(20f, 0f, 0f), new Vector3(0.08f, 0.2f, 0.1f), dark);     // grip

        nozzle = new GameObject("Nozzle").transform;
        nozzle.SetParent(gun, false);
        nozzle.localPosition = new Vector3(0f, 0f, 0.45f);
    }

    Material LitMat(Color c)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", 0.35f);
        owned.Add(m);
        return m;
    }

    void Part(PrimitiveType type, Vector3 pos, Vector3 euler, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(gun, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    void BuildFX()
    {
        var shader = Shader.Find("SandPrototype/SandParticle");

        // particles
        var pgo = new GameObject("Sand Suction Particles (prototype)");
        owned.Add(pgo);
        ps = pgo.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 800;
        main.startSpeed = 0f;
        main.playOnAwake = true;
        main.loop = true;
        var em = ps.emission; em.rateOverTime = 0f;
        var shape = ps.shape; shape.enabled = false;
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.25f)));
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.8f, 1f) });
        col.color = g;
        var pr = pgo.GetComponent<ParticleSystemRenderer>();
        var pm = particleMaterial != null ? new Material(particleMaterial) : new Material(shader);
        pm.SetTexture("_MainTex", SoftDot());
        owned.Add(pm);
        pr.sharedMaterial = pm;
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        pr.receiveShadows = false;
        ps.Play();

        // swirl at the nozzle
        var sgo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        sgo.name = "Suction Swirl (prototype)";
        Destroy(sgo.GetComponent<Collider>());
        owned.Add(sgo);
        swirl = sgo.transform;
        var sm = swirlMaterial != null ? new Material(swirlMaterial) : new Material(shader);
        sm.SetTexture("_MainTex", SwirlTex());
        sm.SetColor("_Color", new Color(1f, 0.93f, 0.6f, 0.9f));
        owned.Add(sm);
        var sr = sgo.GetComponent<MeshRenderer>();
        sr.sharedMaterial = sm;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sgo.SetActive(false);
    }

    Texture2D SoftDot()
    {
        const int N = 32;
        var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SandDot" };
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float r = new Vector2(x - N * 0.5f + 0.5f, y - N * 0.5f + 0.5f).magnitude / (N * 0.5f);
            float a = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.45f, 1f, r));
            t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        t.Apply();
        owned.Add(t);
        return t;
    }

    Texture2D SwirlTex()
    {
        const int N = 128;
        var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SandSwirl" };
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float ang = Mathf.Atan2(dy, dx);
            float arms = Mathf.Pow(0.5f + 0.5f * Mathf.Cos(3f * ang + r * 9f), 3f);
            float fade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 0.55f, r)) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.3f, r));
            t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(arms * fade * 1.2f + fade * 0.15f)));
        }
        t.Apply();
        owned.Add(t);
        return t;
    }
}
