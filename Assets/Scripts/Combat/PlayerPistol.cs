using System;
using System.Collections.Generic;
using UnityEngine;

/// The player's weapon slot: a pistol with a small magazine.
///  - PlayerTools takes it out when an enemy comes near. With the joystick released the player turns to the
///    nearest enemy in range, raises his arm and fires by himself.
///  - Every shot uses one bullet. When the magazine is empty it reloads by itself (reloadTime), then goes on.
///    With no enemy around for idleReloadDelay a half-empty magazine is topped up too. Reload() = by hand (HUD tile).
///  - Feel: muzzle flash + light, a fast tracer, the arm kicks up and the gun slides back, a casing flies out,
///    a small camera kick. The damage lands when the tracer arrives: the enemy flashes, squashes, shows a number
///    (MeleeTarget), a ring under it pulses, and the kill gets a bigger camera kick.
///  - The HUD tile (WeaponSlotView) reads Ammo / Reloading / ReloadProgress.
public class PlayerPistol : MonoBehaviour
{
    public static PlayerPistol Instance { get; private set; }
    /// Below 1 while one of the pistol's bullets deals its damage: enemies multiply their knockback with it,
    /// so six fast bullets don't pin them in place the way six pickaxe blows would.
    public static float HitKnockScale { get; private set; } = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; HitKnockScale = 1f; }

    [Header("Weapon")]
    [Tooltip("Off = the player doesn't have the weapon yet (HUD tile hidden, never taken out)")]
    public bool owned = true;
    public float damage = 4f;
    [Tooltip("How far it shoots (m)")]
    public float range = 6.5f;
    [Tooltip("Bullets per magazine")]
    [Min(1)] public int magazine = 6;
    [Tooltip("Seconds between two shots")]
    public float fireInterval = 0.26f;
    [Tooltip("Seconds to reload an empty magazine")]
    public float reloadTime = 1.1f;
    [Tooltip("With no enemy in range for this long, a magazine that isn't full is reloaded (s)")]
    public float idleReloadDelay = 1.2f;
    [Tooltip("Off = he only shoots while standing (like the pickaxe). On = also while walking, at enemies in front of him")]
    public bool fireWhileMoving;
    [Tooltip("While walking: only enemies inside this arc in front of him are shot (degrees)")]
    [Range(0f, 360f)] public float moveFireArc = 140f;

    [Header("Aim")]
    [Tooltip("How fast the player turns to face the target (degrees per second)")]
    public float turnSpeed = 900f;
    [Tooltip("Shots only start when facing the target within this angle")]
    public float faceAngle = 12f;
    [Tooltip("Seconds the arm takes to come up / go down")]
    public float aimTime = 0.1f;
    [Tooltip("Enemies behind rocks / buildings are not shot")]
    public bool lineOfSight = true;

    [Header("Shot feel")]
    public Color color = new Color(1f, 0.78f, 0.3f);
    [Tooltip("Tracer speed (m/s). The damage lands when it arrives")]
    public float bulletSpeed = 70f;
    public float tracerLength = 1.8f;
    public float tracerWidth = 0.1f;
    [Tooltip("How far the arm kicks up on a shot (degrees)")]
    public float recoilAngle = 24f;
    [Tooltip("How far the gun slides back on a shot (m)")]
    public float recoilBack = 0.1f;
    [Tooltip("Seconds the recoil takes to settle")]
    public float recoilTime = 0.16f;
    [Tooltip("Camera kick on every shot / on a hit / on the kill (0 = none)")]
    public float shotShake = 0.05f;
    public float hitShake = 0.08f;
    public float killShake = 0.3f;
    [Tooltip("How much of an enemy's normal knockback one bullet gives (1 = same as a pickaxe blow)")]
    [Range(0f, 1f)] public float knockback = 0.3f;
    [Tooltip("Brightness of the light flash at the muzzle (0 = none)")]
    public float flashLight = 5f;
    public bool casings = true;
    [Tooltip("Ring on the ground under the enemy he is shooting at")]
    public bool targetRing = true;

    [Header("Parts")]
    [Tooltip("The gun model (origin at the grip, barrel along its Z)")]
    public Transform model;
    public Transform muzzle;
    [Tooltip("End of the right forearm: the gun sits here")]
    public Transform hand;
    public Transform rightUpperArm, rightForearm;

    public int Ammo { get; private set; }
    public bool Reloading { get; private set; }
    /// 0 at the start of a reload, 1 when it is done (1 while not reloading).
    public float ReloadProgress => Reloading && reloadTime > 0f ? Mathf.Clamp01(reloadT / reloadTime) : 1f;
    /// The pistol is the tool in the player's hands (set by PlayerTools).
    public bool InHands { get; private set; }
    public Health Target => target;

    public event Action OnShot;
    /// true = that bullet killed the target
    public event Action<bool> OnHit;
    public event Action OnReloadStart, OnReloadDone;

    class Shot
    {
        public LineRenderer line;
        public Vector3 start, head, to, dir;
        public Health target;
    }

    readonly List<Shot> shots = new List<Shot>();
    readonly Stack<LineRenderer> linePool = new Stack<LineRenderer>();
    JoystickPlayerController pc;
    Health target;
    Vector3 aimPoint;
    float aim, cooldown, reloadT, idleT, kick, ringPunch, flashT;
    Transform fxRoot;
    ParticleSystem casingFx;
    LineRenderer ring;
    Light flash;

    void Awake()
    {
        Instance = this;
        pc = GetComponent<JoystickPlayerController>();
        Ammo = magazine;
        if (model != null) model.gameObject.SetActive(InHands && owned);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (fxRoot != null) Destroy(fxRoot.gameObject);
    }

    void OnDisable()
    {
        for (int i = shots.Count - 1; i >= 0; i--) Free(shots[i]);
        shots.Clear();
        target = null;
        aim = 0f;
        if (ring != null) ring.gameObject.SetActive(false);
        if (flash != null) flash.enabled = false;
    }

    /// Called by PlayerTools: the pistol is / is not the tool in the hands.
    public void SetInHands(bool on)
    {
        InHands = on;
        bool show = on && owned;
        if (model != null && model.gameObject.activeSelf != show) model.gameObject.SetActive(show);
    }

    /// Reload by hand (tap on the HUD tile). Does nothing with a full magazine.
    public void Reload()
    {
        if (owned && !Reloading && Ammo < magazine) StartReload();
    }

    /// Debug / upgrades: a full magazine right now.
    public void Refill()
    {
        Reloading = false;
        Ammo = magazine;
    }

    void StartReload()
    {
        Reloading = true;
        reloadT = 0f;
        OnReloadStart?.Invoke();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (cooldown > 0f) cooldown -= dt;
        if (kick > 0f) kick = Mathf.MoveTowards(kick, 0f, recoilTime > 0f ? dt / recoilTime : 1f);
        if (ringPunch > 0f) ringPunch = Mathf.MoveTowards(ringPunch, 0f, dt * 6f);
        if (flash != null && flash.enabled)
        {
            flashT -= dt;
            if (flashT <= 0f) flash.enabled = false;
            else flash.intensity = flashLight * (flashT / 0.07f);
        }
        UpdateShots(dt);

        if (!owned) { target = null; aim = 0f; return; }

        if (Reloading)
        {
            reloadT += dt;
            if (reloadT >= reloadTime)
            {
                Reloading = false;
                Ammo = magazine;
                OnReloadDone?.Invoke();
            }
        }

        bool blocked = UIState.ModalOpen || UIState.BuildMode || PlayerHealth.KnockedOut || EnemyLeech.PlayerHeld;
        bool moving = pc != null && pc.RawInput.magnitude > pc.deadZone;
        target = blocked || !InHands ? null : FindTarget();

        // empty = reload at once; half empty = reload when there is nothing to shoot at
        idleT = target == null ? idleT + dt : 0f;
        if (!Reloading && Ammo < magazine && (Ammo <= 0 || idleT >= idleReloadDelay)) StartReload();

        bool aiming = target != null && (!moving || fireWhileMoving);
        aim = Mathf.MoveTowards(aim, aiming ? 1f : 0f, aimTime > 0f ? dt / aimTime : 1f);
        if (target == null) return;
        aimPoint = target.AimPoint;

        Vector3 to = target.transform.position - transform.position; to.y = 0f;
        float angle = to.sqrMagnitude > 0.0001f ? Vector3.Angle(transform.forward, to) : 0f;
        if (!moving && to.sqrMagnitude > 0.0001f)
        {
            var look = Quaternion.LookRotation(to);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * dt);
            angle = Quaternion.Angle(transform.rotation, look);
        }

        if (!aiming || Reloading || Ammo <= 0 || cooldown > 0f || aim < 0.95f) return;
        if (angle > (moving ? moveFireArc * 0.5f : faceAngle)) return;
        Fire();
    }

    Health FindTarget()
    {
        Vector3 eye = transform.position + Vector3.up * 1.3f;
        // keep the current one a little past the range, so it doesn't flicker at the edge
        if (Targeting.StillValid(target, transform.position, range + 0.6f, lineOfSight, eye) && Seen(target)) return target;
        return Targeting.FindNearest(Team.Enemy, transform.position, range, Seen, lineOfSight, true, eye);
    }

    /// Only enemies the player can see right now (not ones hidden in the fog at the edge of his sight).
    static bool Seen(Health h)
    {
        var fog = FogOfWar.Instance;
        return fog == null || fog.IsVisible(h.transform.position);
    }

    void Fire()
    {
        Ammo--;
        cooldown = fireInterval;
        kick = 1f;

        Vector3 from = muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.3f + transform.forward * 0.7f;
        Vector3 dir = aimPoint - from;
        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward;

        var s = new Shot { line = GetLine(), start = from, head = from, to = aimPoint, dir = dir, target = target };
        s.line.SetPosition(0, from);
        s.line.SetPosition(1, from);
        shots.Add(s);

        CombatFX.Muzzle(from, dir, color);
        CameraShake.Kick(shotShake);
        if (flashLight > 0f)
        {
            EnsureFx();
            flash.transform.position = from + dir * 0.3f + Vector3.up * 0.2f;
            flash.intensity = flashLight;
            flash.enabled = true;
            flashT = 0.07f;
        }
        if (casings)
        {
            EnsureFx();
            var ep = new ParticleSystem.EmitParams
            {
                position = from - dir * 0.3f,
                velocity = transform.right * UnityEngine.Random.Range(1.6f, 2.6f) + Vector3.up * UnityEngine.Random.Range(2.6f, 3.6f) - dir * UnityEngine.Random.Range(0f, 0.8f),
            };
            casingFx.Emit(ep, 1);
        }

        OnShot?.Invoke();
        if (Ammo <= 0) StartReload();
    }

    // ------------------------------------------------------------------ bullets

    void UpdateShots(float dt)
    {
        for (int i = shots.Count - 1; i >= 0; i--)
        {
            var s = shots[i];
            if (s.target != null && s.target.isActiveAndEnabled && !s.target.IsDead) s.to = s.target.AimPoint;
            Vector3 d = s.to - s.head;
            float dist = d.magnitude;
            float step = bulletSpeed * dt;
            if (dist <= step || dist < 0.01f)
            {
                s.head = s.to;
                shots.RemoveAt(i);
                Free(s);
                Impact(s);
                continue;
            }
            s.dir = d / dist;
            s.head += s.dir * step;
            s.line.SetPosition(0, s.head - s.dir * Mathf.Min(tracerLength, Vector3.Distance(s.start, s.head)));
            s.line.SetPosition(1, s.head);
        }
    }

    void Impact(Shot s)
    {
        var h = s.target;
        if (h == null || h.IsDead || !h.isActiveAndEnabled)
        {
            // the target died while the bullet was on its way
            CombatFX.Hit(s.head, -s.dir, color, 0.7f);
            return;
        }

        Vector3 flat = s.dir; flat.y = 0f;
        flat = flat.sqrMagnitude > 0.0001f ? flat.normalized : transform.forward;
        bool killed;
        HitKnockScale = knockback;
        try
        {
            var mt = h.GetComponent<MeleeTarget>();
            if (mt != null && mt.Alive)
            {
                // flash, squash, sparks and the damage number come from the target itself
                killed = mt.TakeHit(damage, mt.HitPoint(s.start), flat, gameObject);
                CombatFX.Hit(s.head, -s.dir, color, 0.6f);
            }
            else
            {
                float done = h.Damage(damage, gameObject);
                killed = h.IsDead;
                if (done > 0f) DamageNumber.Spawn(h.transform.position + Vector3.up * (h.barHeight + 0.4f), done, killed);
                CombatFX.Hit(s.head, -s.dir, color, killed ? 1.5f : 1f);
            }
        }
        finally { HitKnockScale = 1f; }

        if (h == target) ringPunch = 1f;
        CameraShake.Kick(killed ? killShake : hitShake);
        OnHit?.Invoke(killed);
    }

    LineRenderer GetLine()
    {
        LineRenderer l;
        if (linePool.Count > 0) l = linePool.Pop();
        else
        {
            EnsureFx();
            var go = new GameObject("Tracer");
            go.transform.SetParent(fxRoot, false);
            l = go.AddComponent<LineRenderer>();
            l.sharedMaterial = Resources.Load<Material>("FX/ParticleSoft");
            l.useWorldSpace = true;
            l.positionCount = 2;
            l.numCapVertices = 3;
            l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            l.receiveShadows = false;
            l.widthCurve = new AnimationCurve(new Keyframe(0f, 0.15f), new Keyframe(1f, 1f));
        }
        l.widthMultiplier = tracerWidth;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(color, 0f), new GradientColorKey(Color.Lerp(color, Color.white, 0.7f), 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 1f) });
        l.colorGradient = g;
        l.gameObject.SetActive(true);
        return l;
    }

    void Free(Shot s)
    {
        if (s.line == null) return;
        s.line.gameObject.SetActive(false);
        linePool.Push(s.line);
    }

    // ------------------------------------------------------------------ pose: after the Animator

    void LateUpdate()
    {
        UpdateRing();
        if (model == null || !model.gameObject.activeInHierarchy) return;

        if (aim > 0f && rightUpperArm != null)
        {
            Point(rightUpperArm, aimPoint, aim);
            if (rightForearm != null) Point(rightForearm, aimPoint, aim);
            // recoil: the whole arm snaps up, then settles
            if (kick > 0f) rightUpperArm.rotation = Quaternion.AngleAxis(-recoilAngle * kick * kick * aim, transform.right) * rightUpperArm.rotation;
        }

        // the gun sits in the hand with its barrel along the forearm and its top up
        Transform arm = rightForearm != null ? rightForearm : transform;
        Vector3 barrel = rightForearm != null ? arm.up : transform.forward;
        Vector3 top = Vector3.up * (1f - Mathf.Abs(barrel.y)) + transform.forward * -barrel.y;
        if (top.sqrMagnitude < 0.0001f) top = Vector3.up;
        Quaternion rot = Quaternion.LookRotation(barrel, top);
        if (Reloading)
        {
            // reload: the gun spins once around in the hand
            float k = ReloadProgress;
            k = k * k * (3f - 2f * k);
            rot *= Quaternion.AngleAxis(-360f * k, Vector3.right);
        }
        model.rotation = rot;
        model.position = (hand != null ? hand.position : arm.position) - barrel * (recoilBack * kick * kick);
    }

    /// Turns a bone so its Y axis (which runs along the arm) points at the point.
    static void Point(Transform bone, Vector3 point, float weight)
    {
        Vector3 to = point - bone.position;
        if (to.sqrMagnitude < 1e-6f) return;
        Quaternion aimed = Quaternion.FromToRotation(bone.up, to) * bone.rotation;
        bone.rotation = Quaternion.Slerp(bone.rotation, aimed, weight);
    }

    // ------------------------------------------------------------------ runtime-built FX

    void UpdateRing()
    {
        bool show = targetRing && target != null && InHands;
        if (!show)
        {
            if (ring != null && ring.gameObject.activeSelf) ring.gameObject.SetActive(false);
            return;
        }
        EnsureFx();
        if (!ring.gameObject.activeSelf) ring.gameObject.SetActive(true);
        var t = ring.transform;
        t.position = target.transform.position + Vector3.up * 0.08f;
        t.Rotate(0f, 90f * Time.deltaTime, 0f, Space.World);
        t.localScale = Vector3.one * (target.hitRadius + 0.5f) * (1f + ringPunch * 0.3f);
        Color c = Color.Lerp(new Color(1f, 0.35f, 0.25f), Color.white, ringPunch);
        c.a = Reloading ? 0.35f : 0.9f;
        ring.startColor = ring.endColor = c;
    }

    void EnsureFx()
    {
        if (fxRoot != null) return;
        fxRoot = new GameObject("Pistol FX").transform;

        // target ring: a dashed circle lying on the ground
        var rgo = new GameObject("Target Ring");
        rgo.transform.SetParent(fxRoot, false);
        ring = rgo.AddComponent<LineRenderer>();
        ring.sharedMaterial = Resources.Load<Material>("FX/ParticleSolid");
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.alignment = LineAlignment.TransformZ;
        rgo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        const int N = 40;
        ring.positionCount = N;
        for (int i = 0; i < N; i++)
        {
            float a = i * Mathf.PI * 2f / N;
            ring.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
        }
        ring.widthMultiplier = 0.07f;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;
        rgo.SetActive(false);

        // muzzle light
        var lgo = new GameObject("Muzzle Light");
        lgo.transform.SetParent(fxRoot, false);
        flash = lgo.AddComponent<Light>();
        flash.type = LightType.Point;
        flash.color = Color.Lerp(color, Color.white, 0.3f);
        flash.range = 5f;
        flash.shadows = LightShadows.None;
        flash.enabled = false;

        // casings: small brass bits thrown out to the right
        var cgo = new GameObject("Casings");
        cgo.transform.SetParent(fxRoot, false);
        casingFx = cgo.AddComponent<ParticleSystem>();
        casingFx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = casingFx.main;
        m.loop = true;
        m.playOnAwake = false;
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.startLifetime = 0.6f;
        m.startSpeed = 0f;
        m.startSize = 0.07f;
        m.startColor = new Color(0.95f, 0.72f, 0.25f);
        m.gravityModifier = 2.6f;
        m.maxParticles = 32;
        m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var em = casingFx.emission; em.enabled = false;
        var sh = casingFx.shape; sh.enabled = false;
        var rot = casingFx.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(10f, 20f);
        var sz = casingFx.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.75f, 1f), new Keyframe(1f, 0f)));
        var pr = cgo.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.Mesh;
        pr.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        pr.sharedMaterial = Resources.Load<Material>("FX/ParticleSolid");
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        casingFx.Play();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, range);
    }
}
