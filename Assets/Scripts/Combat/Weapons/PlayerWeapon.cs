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
    /// HUD layout B (bottom bar): the player has two weapon slots and a pickup fills the empty one first.
    public static bool TwoSlots;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; HitKnockScale = 1f; TwoSlots = false; }

    [Header("Weapon")]
    [Tooltip("The weapon he has now (empty = none: the HUD tile is hidden). A pickup on the ground swaps it")]
    public WeaponDef weapon;
    [Tooltip("The weapon in the second slot at the start (only used by the bottom-bar HUD layout). Empty = the slot is empty")]
    public WeaponDef weapon2;
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
    /// Which of the two weapon slots is selected (0 or 1). `weapon` / `Ammo` are always that slot's.
    public int Slot { get; private set; }
    public WeaponDef SlotWeapon(int i) => i == Slot ? weapon : other;
    public int SlotAmmo(int i) => i == Slot ? Ammo : otherAmmo;

    public event Action OnShot;
    /// true = that hit killed the target
    public event Action<bool> OnHit;
    public event Action OnReloadStart, OnReloadDone;
    /// A pickup (or code) gave him another weapon.
    public event Action OnWeaponChanged;

    JoystickPlayerController pc;
    WeaponDef other;     // the weapon in the slot that is not selected
    int otherAmmo;
    Health target;
    Vector3 aimPoint;
    float aim, cooldown, reloadT, idleT, kick, ringPunch, flashT;
    int lastKickFrame = -1;
    Transform holder, muzzle, fxRoot;
    ParticleSystem casingFx;
    LineRenderer ring;
    Light flash;
    WeaponBeam beam;
    Health beamTarget;
    bool beamOn;
    float heat, tickT, arcRange, arcWidth;
    Transform arcRoot;
    LineRenderer arcLine;
    MeshRenderer arcFill;
    Mesh arcMesh;
    MaterialPropertyBlock arcBlock;

    void Awake()
    {
        Instance = this;
        FxPool.Sound = sound;
        pc = GetComponent<JoystickPlayerController>();
        if (weapon != null) Ammo = weapon.magazine;
        other = weapon2;
        otherAmmo = other != null ? other.magazine : 0;
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
        beamOn = false;
        if (beam != null) beam.Off();
        if (arcRoot != null) arcRoot.gameObject.SetActive(false);
    }

    /// Called by PlayerTools: the weapon is / is not the tool in the hands.
    public void SetInHands(bool on)
    {
        InHands = on;
        bool show = on && weapon != null;
        if (holder != null && holder.gameObject.activeSelf != show) holder.gameObject.SetActive(show);
        if (!show)
        {
            beamOn = false;
            if (beam != null) beam.Off();
        }
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

    /// Switches to the other weapon slot (0 or 1). Each slot keeps its own ammo; a reload in progress is dropped.
    public void SelectSlot(int i)
    {
        i = Mathf.Clamp(i, 0, 1);
        if (i == Slot) return;
        var w = weapon;
        int a = Ammo;
        weapon = other;
        Ammo = otherAmmo;
        other = w;
        otherAmmo = a;
        Slot = i;
        Reloading = false;
        cooldown = 0.2f;
        kick = 0f;
        BuildModel();
        OnWeaponChanged?.Invoke();
        if (weapon != null && Ammo <= 0) StartReload();
    }

    /// A weapon from a pickup. With two slots (HUD layout B) it goes into the empty slot if there is one;
    /// otherwise it replaces the selected slot. Returns the weapon that was replaced (null = nothing to drop).
    public WeaponDef Give(WeaponDef def)
    {
        if (TwoSlots && def != null && weapon != null && other == null)
        {
            other = def;
            otherAmmo = def.magazine;
            OnWeaponChanged?.Invoke();
            return null;
        }
        return Equip(def);
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
        if (beam != null) Destroy(beam.gameObject);
        beam = null;
        beamOn = false;
        beamTarget = null;
        heat = 0f;
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
        if (weapon.mode == WeaponMode.Beam && weapon.beamFx != null) beam = WeaponBeam.Create(weapon.beamFx, weapon.beamScale);
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
        if (target == null)
        {
            if (weapon.mode == WeaponMode.Beam) BeamStep(false, dt);
            return;
        }
        aimPoint = target.AimPoint;

        Vector3 to = target.transform.position - transform.position; to.y = 0f;
        float angle = to.sqrMagnitude > 0.0001f ? Vector3.Angle(transform.forward, to) : 0f;
        if (!moving && to.sqrMagnitude > 0.0001f)
        {
            var look = Quaternion.LookRotation(to);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * dt);
            angle = Quaternion.Angle(transform.rotation, look);
        }

        bool ready = aiming && !Reloading && Ammo > 0 && aim >= 0.95f && angle <= (moving ? moveFireArc * 0.5f : faceAngle);
        if (weapon.mode == WeaponMode.Beam) BeamStep(ready, dt);
        else if (ready && cooldown <= 0f) Fire();
    }

    /// Beam weapons: while `on`, the beam stays on the target; every Beam Tick it uses one round and deals a hit.
    /// The damage climbs from Damage to Damage Max over Ramp Time on the same enemy and starts over on a new one.
    void BeamStep(bool on, float dt)
    {
        var def = weapon;
        if (!on)
        {
            beamOn = false;
            if (beam != null) beam.Off();
            heat = Mathf.MoveTowards(heat, 0f, dt / 0.6f);   // a short break (a step, a reload) keeps part of the ramp
            return;
        }
        if (target != beamTarget)
        {
            beamTarget = target;
            heat = 0f;
            tickT = 0f;
        }
        beamOn = true;
        heat = Mathf.MoveTowards(heat, 1f, def.rampTime > 0f ? dt / def.rampTime : 1f);
        tickT += dt;
        float tick = Mathf.Max(0.05f, def.beamTick);
        while (tickT >= tick && Ammo > 0 && target != null && !target.IsDead)
        {
            tickT -= tick;
            Ammo--;
            kick = 1f;
            Vector3 from = muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.3f;
            Vector3 dir = aimPoint - from;
            float dmg = Mathf.Round(Mathf.Lerp(def.damage, Mathf.Max(def.damage, def.damageMax), heat));
            DealHit(def, target, dmg, aimPoint, dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward, from, def.knockback);
            OnShot?.Invoke();
        }
        if (Ammo <= 0 && !Reloading)
        {
            StartReload();
            beamOn = false;
            if (beam != null) beam.Off();
        }
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
        UpdateArc();
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

        // the beam runs from where the muzzle is now (after the pose) to the enemy; it and its light grow with the ramp
        if (beam != null)
        {
            if (beamOn && muzzle != null && target != null)
            {
                beam.Set(muzzle.position, aimPoint, heat);
                if (weapon.flashLight > 0f)
                {
                    EnsureFx();
                    flash.color = Color.Lerp(weapon.color, Color.white, 0.3f);
                    flash.transform.position = muzzle.position + Vector3.up * 0.2f;
                    flash.intensity = weapon.flashLight * (0.4f + 0.6f * heat);
                    flash.enabled = true;
                    flashT = 0.07f;
                }
            }
            else beam.Off();
        }
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

    /// A faint fan on the ground toward the enemy he shoots at: as long as the weapon's range, as wide as its spread.
    void UpdateArc()
    {
        bool show = weapon != null && weapon.rangeArc && target != null && InHands && aim > 0f;
        if (!show)
        {
            if (arcRoot != null && arcRoot.gameObject.activeSelf) arcRoot.gameObject.SetActive(false);
            return;
        }
        EnsureFx();
        float width = Mathf.Clamp(weapon.mode == WeaponMode.Beam ? 14f : Mathf.Max(weapon.spread, 24f), 10f, 180f);
        if (arcRoot == null || !Mathf.Approximately(arcRange, weapon.range) || !Mathf.Approximately(arcWidth, width)) BuildArc(weapon.range, width);
        if (!arcRoot.gameObject.activeSelf) arcRoot.gameObject.SetActive(true);
        Vector3 to = target.transform.position - transform.position; to.y = 0f;
        arcRoot.position = transform.position + Vector3.up * 0.07f;
        if (to.sqrMagnitude > 0.0001f) arcRoot.rotation = Quaternion.LookRotation(to);
        Color c = weapon.color;
        c.a = 0.45f * aim;
        arcLine.startColor = arcLine.endColor = c;
        c.a = 0.12f * aim;
        arcBlock.SetColor("_BaseColor", c);
        arcFill.SetPropertyBlock(arcBlock);
    }

    void BuildArc(float range, float width)
    {
        arcRange = range;
        arcWidth = width;
        if (arcRoot == null)
        {
            arcRoot = new GameObject("Range Arc").transform;
            arcRoot.SetParent(fxRoot, false);
            var lgo = new GameObject("Line");
            lgo.transform.SetParent(arcRoot, false);
            lgo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            arcLine = lgo.AddComponent<LineRenderer>();
            arcLine.sharedMaterial = Resources.Load<Material>("FX/ParticleSoft");
            arcLine.useWorldSpace = false;
            arcLine.loop = true;
            arcLine.alignment = LineAlignment.TransformZ;
            arcLine.widthMultiplier = 0.09f;
            arcLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            arcLine.receiveShadows = false;
            var fgo = new GameObject("Fill");
            fgo.transform.SetParent(arcRoot, false);
            arcMesh = new Mesh();
            fgo.AddComponent<MeshFilter>().sharedMesh = arcMesh;
            arcFill = fgo.AddComponent<MeshRenderer>();
            arcFill.sharedMaterial = Resources.Load<Material>("FX/ParticleSoft");
            arcFill.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            arcFill.receiveShadows = false;
            arcBlock = new MaterialPropertyBlock();
        }
        const int N = 24;
        var pts = new Vector3[N + 2];
        var verts = new Vector3[N + 2];
        var uv = new Vector2[N + 2];
        var mid = new Vector2(0.5f, 0.5f);
        uv[0] = mid;
        for (int i = 0; i <= N; i++)
        {
            float a = Mathf.Deg2Rad * Mathf.Lerp(-width * 0.5f, width * 0.5f, i / (float)N);
            float x = Mathf.Sin(a) * range, z = Mathf.Cos(a) * range;
            pts[i + 1] = new Vector3(x, z, 0f);      // the line object is turned flat, so its Y is forward
            verts[i + 1] = new Vector3(x, 0f, z);
            uv[i + 1] = mid;
        }
        arcLine.positionCount = N + 2;
        arcLine.SetPositions(pts);
        var tris = new int[N * 6];
        for (int i = 0; i < N; i++)
        {
            tris[i * 6] = 0; tris[i * 6 + 1] = i + 1; tris[i * 6 + 2] = i + 2;
            tris[i * 6 + 3] = 0; tris[i * 6 + 4] = i + 2; tris[i * 6 + 5] = i + 1;   // both sides
        }
        arcMesh.Clear();
        arcMesh.vertices = verts;
        arcMesh.uv = uv;
        arcMesh.triangles = tris;
        arcMesh.RecalculateBounds();
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
