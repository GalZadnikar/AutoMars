using System;
using UnityEngine;

/// The player's weapon slot. Holds one WeaponDef at a time (pistol, revolver, scatter gun ...); WeaponPickup
/// swaps it for the one lying on the ground.
///  - PlayerTools takes the weapon out when an enemy comes near. With the joystick released the player turns to
///    the nearest enemy he can see, raises his arm and fires by himself.
///  - Every shot uses one round of the magazine. Empty = it reloads by itself, then goes on. With no enemy around
///    for idleReloadDelay a magazine that isn't full is topped up. Reload() = by hand (tap on the HUD tile).
///  - A shot = WeaponProjectile(s) + the weapon's muzzle effect, a light flash, recoil of the arm and the gun,
///    a casing and a small camera kick. The hit (DealHit) makes the enemy flash, squash and show a number,
///    pulses the ring under it and kicks the camera harder on the kill.
///  - The HUD tile (WeaponSlotView) reads weapon / Ammo / Reloading / ReloadProgress.
public class PlayerWeapon : MonoBehaviour
{
    public static PlayerWeapon Instance { get; private set; }
    /// Not 1 only while one of the weapon's hits deals its damage: enemies multiply their knockback with it,
    /// so a stream of small bullets doesn't pin them the way as many pickaxe blows would.
    public static float HitKnockScale { get; private set; } = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; HitKnockScale = 1f; }

    [Header("Weapon")]
    [Tooltip("The weapon he has now (empty = none: the HUD tile is hidden). A pickup on the ground swaps it")]
    public WeaponDef weapon;
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

    [Header("Feel")]
    [Tooltip("Ring on the ground under the enemy he is shooting at")]
    public bool targetRing = true;
    [Tooltip("Play the sounds that come with the Epic Toon FX prefabs (shots, impacts)")]
    public bool sound;

    [Header("Pickups (effects shared by every WeaponPickup)")]
    [Tooltip("Looping glow under a weapon lying on the ground")]
    public GameObject pickupGlowFx;
    [Tooltip("Burst at the player when he takes a weapon")]
    public GameObject pickupTakeFx;
    public float pickupFxScale = 0.6f;

    [Header("Parts")]
    [Tooltip("End of the right forearm: the gun sits here")]
    public Transform hand;
    public Transform rightUpperArm, rightForearm;

    public bool Owned => weapon != null;
    public int Ammo { get; private set; }
    public int Magazine => weapon != null ? weapon.magazine : 0;
    public float Range => weapon != null ? weapon.range : 0f;
    public bool Reloading { get; private set; }
    /// 0 at the start of a reload, 1 when it is done (1 while not reloading).
    public float ReloadProgress => Reloading && weapon != null && weapon.reloadTime > 0f ? Mathf.Clamp01(reloadT / weapon.reloadTime) : 1f;
    /// The weapon is the tool in the player's hands (set by PlayerTools).
    public bool InHands { get; private set; }
    public Health Target => target;

    public event Action OnShot;
    /// true = that hit killed the target
    public event Action<bool> OnHit;
    public event Action OnReloadStart, OnReloadDone;
    /// A pickup (or code) gave him another weapon.
    public event Action OnWeaponChanged;

    JoystickPlayerController pc;
    Health target;
    Vector3 aimPoint;
    float aim, cooldown, reloadT, idleT, kick, ringPunch, flashT;
    int lastKickFrame = -1;
    Transform holder, muzzle, fxRoot;
    ParticleSystem casingFx;
    LineRenderer ring;
    Light flash;

    void Awake()
    {
        Instance = this;
        FxPool.Sound = sound;
        pc = GetComponent<JoystickPlayerController>();
        if (weapon != null) Ammo = weapon.magazine;
        BuildModel();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (fxRoot != null) Destroy(fxRoot.gameObject);
    }

    void OnDisable()
    {
        target = null;
        aim = 0f;
        if (ring != null) ring.gameObject.SetActive(false);
        if (flash != null) flash.enabled = false;
    }

    /// Called by PlayerTools: the weapon is / is not the tool in the hands.
    public void SetInHands(bool on)
    {
        InHands = on;
        bool show = on && weapon != null;
        if (holder != null && holder.gameObject.activeSelf != show) holder.gameObject.SetActive(show);
    }

    /// Gives him this weapon with a full magazine. Returns the one he had (null = none).
    public WeaponDef Equip(WeaponDef def)
    {
        var old = weapon;
        weapon = def;
        Reloading = false;
        cooldown = 0.25f;
        kick = 0f;
        Ammo = def != null ? def.magazine : 0;
        BuildModel();
        OnWeaponChanged?.Invoke();
        return old;
    }

    /// Reload by hand (tap on the HUD tile). Does nothing with a full magazine.
    public void Reload()
    {
        if (weapon != null && !Reloading && Ammo < weapon.magazine) StartReload();
    }

    /// Debug / upgrades: a full magazine right now.
    public void Refill()
    {
        Reloading = false;
        Ammo = Magazine;
    }

    void StartReload()
    {
        Reloading = true;
        reloadT = 0f;
        OnReloadStart?.Invoke();
    }

    /// The gun in the hand: a holder whose origin is the grip, with the weapon's model and a muzzle point in it.
    void BuildModel()
    {
        if (holder != null) Destroy(holder.gameObject);
        holder = null;
        muzzle = null;
        if (weapon == null || weapon.model == null) return;

        holder = new GameObject("Weapon (" + weapon.displayName + ")").transform;
        float s = weapon.modelScale;
        var m = Instantiate(weapon.model, holder);
        m.name = "Model";
        m.transform.localRotation = Quaternion.identity;
        m.transform.localScale = weapon.model.transform.localScale * s;
        m.transform.localPosition = -weapon.grip * s;
        foreach (var c in m.GetComponentsInChildren<Collider>(true)) Destroy(c);
        muzzle = new GameObject("Muzzle").transform;
        muzzle.SetParent(holder, false);
        muzzle.localPosition = (weapon.muzzle - weapon.grip) * s;

        Transform arm = rightForearm != null ? rightForearm : transform;
        holder.position = hand != null ? hand.position : arm.position;
        holder.SetParent(arm, true);   // the arm bones are scaled; this keeps the gun's world size
        holder.gameObject.SetActive(InHands);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (cooldown > 0f) cooldown -= dt;
        if (kick > 0f) kick = Mathf.MoveTowards(kick, 0f, weapon != null && weapon.recoilTime > 0f ? dt / weapon.recoilTime : 1f);
        if (ringPunch > 0f) ringPunch = Mathf.MoveTowards(ringPunch, 0f, dt * 6f);
        if (flash != null && flash.enabled)
        {
            flashT -= dt;
            if (flashT <= 0f) flash.enabled = false;
            else flash.intensity = (weapon != null ? weapon.flashLight : 1f) * (flashT / 0.07f);
        }

        if (weapon == null) { target = null; aim = 0f; return; }

        if (Reloading)
        {
            reloadT += dt;
            if (reloadT >= weapon.reloadTime)
            {
                Reloading = false;
                Ammo = weapon.magazine;
                OnReloadDone?.Invoke();
            }
        }

        bool blocked = UIState.ModalOpen || UIState.BuildMode || PlayerHealth.KnockedOut || EnemyLeech.PlayerHeld;
        bool moving = pc != null && pc.RawInput.magnitude > pc.deadZone;
        target = blocked || !InHands ? null : FindTarget();

        // empty = reload at once; half empty = reload when there is nothing to shoot at
        idleT = target == null ? idleT + dt : 0f;
        if (!Reloading && Ammo < weapon.magazine && (Ammo <= 0 || idleT >= idleReloadDelay)) StartReload();

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
        if (Targeting.StillValid(target, transform.position, weapon.range + 0.6f, lineOfSight, eye) && Seen(target)) return target;
        return Targeting.FindNearest(Team.Enemy, transform.position, weapon.range, Seen, lineOfSight, true, eye);
    }

    /// Only enemies the player can see right now (not ones hidden in the fog at the edge of his sight).
    static bool Seen(Health h)
    {
        var fog = FogOfWar.Instance;
        return fog == null || fog.IsVisible(h.transform.position);
    }

    void Fire()
    {
        var def = weapon;
        Ammo--;
        cooldown = def.fireInterval;
        kick = 1f;

        Vector3 from = muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.3f + transform.forward * 0.7f;
        Vector3 dir = aimPoint - from;
        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward;

        int n = Mathf.Max(1, def.pellets);
        for (int i = 0; i < n; i++)
        {
            Vector3 d = dir;
            if (def.spread > 0f)
            {
                // several pellets: one per slice of the fan, so the pattern is even; a single shot: anywhere in it
                float a = n > 1 ? Mathf.Lerp(-0.5f, 0.5f, (i + UnityEngine.Random.value) / n) : UnityEngine.Random.Range(-0.5f, 0.5f);
                d = Quaternion.AngleAxis(a * def.spread, Vector3.up) * dir;
            }
            WeaponProjectile.Fire(def, this, from, d, n == 1 ? target : null);
        }

        if (def.muzzleFx != null) FxPool.Play(def.muzzleFx, from, Quaternion.LookRotation(dir), def.muzzleScale, 1f);
        else CombatFX.Muzzle(from, dir, def.color);
        CameraShake.Kick(def.shotShake);
        if (def.flashLight > 0f)
        {
            EnsureFx();
            flash.color = Color.Lerp(def.color, Color.white, 0.3f);
            flash.transform.position = from + dir * 0.3f + Vector3.up * 0.2f;
            flash.intensity = def.flashLight;
            flash.enabled = true;
            flashT = 0.07f;
        }
        if (def.casings)
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

    /// One hit of a weapon on an enemy, with everything that makes it feel like a hit. Returns true if it killed it.
    /// point = where it lands, dir = the way the projectile flew, from = where it was fired from.
    public bool DealHit(WeaponDef def, Health h, float damage, Vector3 point, Vector3 dir, Vector3 from, float knock)
    {
        if (h == null || h.IsDead || !h.isActiveAndEnabled) return false;
        Vector3 flat = dir; flat.y = 0f;
        flat = flat.sqrMagnitude > 0.0001f ? flat.normalized : transform.forward;
        bool killed;
        HitKnockScale = knock;
        try
        {
            var mt = h.GetComponent<MeleeTarget>();
            if (mt != null && mt.Alive)
            {
                // flash, squash, sparks and the damage number come from the target itself
                killed = mt.TakeHit(damage, mt.HitPoint(from), flat, gameObject);
            }
            else
            {
                float done = h.Damage(damage, gameObject);
                killed = h.IsDead;
                if (done > 0f) DamageNumber.Spawn(h.transform.position + Vector3.up * (h.barHeight + 0.4f), done, killed);
            }
        }
        finally { HitKnockScale = 1f; }

        if (h == target) ringPunch = 1f;
        // one camera kick per frame, however many pellets land in it (the kill always kicks)
        if (killed || Time.frameCount != lastKickFrame)
        {
            lastKickFrame = Time.frameCount;
            CameraShake.Kick(killed ? def.killShake : def.hitShake);
        }
        OnHit?.Invoke(killed);
        return killed;
    }

    // ------------------------------------------------------------------ pose: after the Animator

    void LateUpdate()
    {
        UpdateRing();
        if (holder == null || !holder.gameObject.activeInHierarchy) return;

        float k = kick * kick;
        if (aim > 0f && rightUpperArm != null)
        {
            Point(rightUpperArm, aimPoint, aim);
            if (rightForearm != null) Point(rightForearm, aimPoint, aim);
            // recoil: the whole arm snaps up, then settles
            if (k > 0f) rightUpperArm.rotation = Quaternion.AngleAxis(-weapon.recoilAngle * k * aim, transform.right) * rightUpperArm.rotation;
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
            float r = ReloadProgress;
            r = r * r * (3f - 2f * r);
            rot *= Quaternion.AngleAxis(-360f * r, Vector3.right);
        }
        holder.rotation = rot;
        holder.position = (hand != null ? hand.position : arm.position) - barrel * (weapon.recoilBack * k);
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
        fxRoot = new GameObject("Weapon FX").transform;

        // target ring: a circle lying on the ground
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
        m.maxParticles = 48;
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
        if (weapon == null) return;
        Gizmos.color = new Color(1f, 0.6f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, weapon.range);
    }
}
