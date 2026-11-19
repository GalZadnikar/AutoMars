using UnityEngine;

/// Flying combat robot (C3). Built by the Drone Bay. Shares everything with gatherer bots through ScoutBot
/// (battery + chargers, follow, disk program, level tag, HP + broken/repair, popup, scrap) and adds:
/// - flight: the root stays on the ground (pads, rally points, ground height work unchanged) and the `model`
///   child hovers `hoverHeight` above it with a bob and a tilt into the move direction; it lands on a charger pad
///   and drops to the ground when broken / out of power;
/// - a gun: picks the nearest visible enemy in range (or PriorityTarget), turns to face it and fires
///   projectiles (C1) whenever it isn't down or sitting on a charger, whatever program it runs;
/// - EngageMove(): moves into range of a threat without leaving a leash circle (used by idle / GUARD / ESCORT).
public partial class CombatDrone : ScoutBot
{
    [Header("Flight")]
    public Transform model;
    public float hoverHeight = 2.5f;
    [Tooltip("Height while sitting on a charging pad")]
    public float landedHeight = 0.55f;
    [Tooltip("Height when broken / out of power (on the ground)")]
    public float downHeight = 0.3f;
    public float bobAmplitude = 0.12f;
    public float bobFrequency = 0.8f;
    [Tooltip("Degrees of tilt at full speed")]
    public float tiltAmount = 16f;

    [Header("Weapon (Drone Bay stats overwrite these at spawn)")]
    public float damage = 8f;
    public float fireRate = 1.5f;
    public float attackRange = 12f;
    public float shotSpeed = 26f;
    public Color shotColor = new Color(0.45f, 0.95f, 1f);
    [Tooltip("Optional muzzle; otherwise the front of the model")]
    public Transform muzzle;
    public Vector3 muzzleOffset = new Vector3(0f, -0.1f, 0.6f);
    [Tooltip("Only fires when facing the target within this angle")]
    public float aimCone = 20f;
    public bool needLineOfSight = true;

    [Header("Engaging")]
    [Tooltip("Idle drones (no disk) fight back up to this far from their spot")]
    public float idleLeash = 6f;
    [Tooltip("Seconds an attacker is remembered (fight back even if it's out of range)")]
    public float retaliateTime = 6f;

    /// What the gun is shooting at right now (null = nothing).
    public Health Target { get; private set; }
    /// Set by others to force a target while it is in reach (E3a: the leech holding the player).
    public Health PriorityTarget { get; set; }
    /// Where GUARD stays: the drop point (where the player gave it the disk) or its rally point (Master Disk).
    public Vector3 GuardPoint { get; set; }
    public float Hover => hover;

    protected override string UnitName => "Combat Drone";
    public override float ButtonHeight => hoverHeight - 0.6f;

    ShotSpec spec;
    float fireT, hover, recoil, phase, retargetT;
    Vector3 lastPos, smoothVel;
    Quaternion modelBaseRot = Quaternion.identity;
    Vector3 modelBasePos;
    Health attacker;
    float attackedT = -99f;

    protected override void OnAwake()
    {
        robotType = RobotType.Drone;
        capacity = 0;
        spec = new ShotSpec { damage = damage, speed = shotSpeed, color = shotColor, radius = 0.11f, trailTime = 0.1f };
        hover = hoverHeight;
        phase = Random.value * 10f;
        lastPos = transform.position;
        GuardPoint = transform.position;
        if (model != null) { modelBaseRot = model.localRotation; modelBasePos = model.localPosition; }
        Health.aimHeight = hoverHeight;
        Health.OnDamaged += OnHit;
    }

    public override void ApplyStats(StatBlock s)
    {
        base.ApplyStats(s);
        if (s == null) return;
        capacity = 0;
        if (s[StatType.Damage] > 0f) damage = s[StatType.Damage];
        if (s[StatType.FireRate] > 0f) fireRate = s[StatType.FireRate];
        if (s[StatType.AttackRange] > 0f) attackRange = s[StatType.AttackRange];
        if (s[StatType.MaxHealth] > 0f) { maxHP = s[StatType.MaxHealth]; Health.SetMax(maxHP); }
    }

    public override void SetProgram(DiskProgram program, bool fromMasterDisk = false)
    {
        // GUARD's spot: where the player hands it the disk, or the rally point for Master Disk drones
        GuardPoint = fromMasterDisk ? HomePosition : transform.position;
        base.SetProgram(program, fromMasterDisk);
    }

    public override string PopupStats()
    {
        return "Damage  " + damage.ToString("0.#") + "  ·  " + fireRate.ToString("0.0#") + "/s\n" +
               "Range  " + attackRange.ToString("0") + " m  ·  Move  " + moveSpeed.ToString("0.0") + " m/s\n" +
               "Health  " + Mathf.CeilToInt(Health.Current) + " / " + Mathf.RoundToInt(Health.maxHP) + "\n" +
               "Full charge  " + Stats.Clock(batteryRuntime);
    }

    void OnHit(float amount, GameObject source)
    {
        if (source == null) return;
        var h = source.GetComponentInParent<Health>();
        if (h != null && h.team == Team.Enemy) { attacker = h; attackedT = Time.time; }
    }

    /// The enemy that shot this drone recently (still alive), or null.
    public Health RecentAttacker => attacker != null && attacker.Targetable && Time.time - attackedT < retaliateTime ? attacker : null;

    public Vector3 MuzzlePosition => muzzle != null ? muzzle.position
                                   : model != null ? model.TransformPoint(Vector3.Scale(muzzleOffset, Inv(model.lossyScale)))
                                   : transform.position + Vector3.up * hover;

    static Vector3 Inv(Vector3 v) => new Vector3(1f / Mathf.Max(1e-4f, v.x), 1f / Mathf.Max(1e-4f, v.y), 1f / Mathf.Max(1e-4f, v.z));

    Vector3 Eye => transform.position + Vector3.up * hover;

    // ------------------------------------------------------------------ gun

    protected override void TickCombat(float dt)
    {
        SquadLeaderTick(dt);
        fireT -= dt;
        if (IsDown || IsCharging) { Target = null; return; }

        // keep a valid target; re-scan a few times a second for something better
        retargetT -= dt;
        bool keep = Targeting.StillValid(Target, transform.position, attackRange, needLineOfSight, Eye);
        if (PriorityTarget != null && Targeting.StillValid(PriorityTarget, transform.position, attackRange, needLineOfSight, Eye))
            Target = PriorityTarget;
        else if (!keep || retargetT <= 0f)
        {
            retargetT = 0.4f;
            var near = Targeting.FindNearest(Team.Enemy, transform.position, attackRange, null, needLineOfSight, true, Eye);
            if (!keep || near != null) Target = near;
        }
        if (Target == null) return;

        Vector3 to = Target.transform.position - transform.position; to.y = 0f;
        base.FaceDirection(to);
        MarkWorking();   // aiming / shooting uses power like work
        Vector3 fwd = transform.forward; fwd.y = 0f;
        if (to.sqrMagnitude > 0.01f && Vector3.Angle(fwd, to) > aimCone) return;
        if (fireT > 0f) return;

        fireT = 1f / Mathf.Max(0.05f, fireRate);
        spec.damage = damage;
        spec.speed = shotSpeed;
        spec.color = shotColor;
        Projectile.Fire(MuzzlePosition, Target, spec, Team.Player, gameObject);
        recoil = 1f;
    }

    // while it has a target the gun decides where the drone looks
    public override void FaceDirection(Vector3 dir)
    {
        if (Target != null) return;
        base.FaceDirection(dir);
    }

    /// Moves to fight a threat near `anchor` without going further than `leash` from it.
    /// Threat = priority target, current target (if chaseTarget), the recent attacker, or (if seekRadius > 0) the nearest
    /// enemy within seekRadius of anchor. Returns false when there is nothing to fight (the caller does its normal move).
    public bool EngageMove(Vector3 anchor, float leash, float seekRadius, bool chaseTarget = true)
    {
        Health threat = PriorityTarget != null && PriorityTarget.Targetable ? PriorityTarget : null;
        if (threat == null && chaseTarget) threat = Target;
        if (threat == null) threat = RecentAttacker;
        if (threat == null && seekRadius > 0f) threat = Targeting.FindNearest(Team.Enemy, anchor, seekRadius, null, false, true);
        if (threat == null) return false;

        Vector3 tp = threat.transform.position;
        Vector3 fromT = transform.position - tp; fromT.y = 0f;
        if (fromT.sqrMagnitude < 0.01f) fromT = Vector3.back;
        float stand = attackRange * 0.7f;
        Vector3 want = fromT.magnitude > stand ? tp + fromT.normalized * stand : transform.position;
        Vector3 off = want - anchor; off.y = 0f;
        if (off.magnitude > leash) want = anchor + off.normalized * leash;
        MoveTowards(want, 0.3f);
        Status = "Engaging target";
        return true;
    }

    /// No disk: hover at the home spot, fight back when attacked.
    protected override void TickIdle(float dt)
    {
        if (EngageMove(HomePosition, idleLeash, 0f, false)) { Status = "Idle: fighting back"; return; }
        Status = Target != null ? "Idle: shooting" : "Waiting for a disk";
        MoveTowards(HomePosition, 0.1f);
    }

    // ------------------------------------------------------------------ visuals (hover, bob, tilt)

    protected override void UpdateAnimation()
    {
        if (model == null) return;
        float dt = Mathf.Max(1e-4f, Time.deltaTime);
        float want = IsDown ? downHeight : IsCharging ? landedHeight : hoverHeight;
        hover = Mathf.MoveTowards(hover, want, dt * (IsDown ? 4f : 1.6f));
        Health.aimHeight = hover;

        Vector3 v = (transform.position - lastPos) / dt;
        lastPos = transform.position;
        smoothVel = Vector3.Lerp(smoothVel, v, dt * 6f);
        Vector3 local = transform.InverseTransformDirection(smoothVel);
        float k = tiltAmount / Mathf.Max(0.5f, moveSpeed);
        float pitch = Mathf.Clamp(local.z * k, -tiltAmount, tiltAmount);
        float roll = Mathf.Clamp(-local.x * k, -tiltAmount, tiltAmount);

        recoil = Mathf.MoveTowards(recoil, 0f, dt * 6f);
        float bob = IsDown ? 0f : Mathf.Sin((Time.time + phase) * bobFrequency * Mathf.PI * 2f) * bobAmplitude;
        if (IsDown) { pitch = 8f; roll = 22f; }   // crashed, lying askew

        model.localPosition = modelBasePos + new Vector3(0f, hover + bob, -0.12f * recoil);
        model.localRotation = Quaternion.Euler(pitch - 6f * recoil, 0f, roll) * modelBaseRot;
    }
}
