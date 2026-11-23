using UnityEngine;

/// Auto melee: when a MeleeTarget is in reach and the joystick is released, the player turns to it and
/// swings left / right / left ... (Animator states "Attack_L" and "Attack_R").
/// The damage is NOT dealt when the swing starts: the clips carry an Animation Event (MeleeHit) on the contact
/// frame, and everything that makes the hit feel good (flash, sparks, number, hit-stop) fires from there.
[RequireComponent(typeof(Animator))]
public class PlayerMelee : MonoBehaviour
{
    /// True while the player is facing / swinging at a target (the suction gun's auto-aim stays out of the way).
    public static bool Engaged { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Engaged = false; }

    [Header("Attack")]
    public float damage = 3f;
    [Tooltip("Reach from the player's centre to the edge of the target")]
    public float range = 1.5f;
    [Tooltip("Swing animation speed (1 = normal). A good upgrade stat.")]
    public float attackSpeed = 1f;
    [Tooltip("Seconds into a swing when the next one may start (lower = tighter combo)")]
    public float comboTime = 0.42f;
    [Tooltip("Blend time into each swing")]
    public float crossFade = 0.07f;
    [Tooltip("How fast the player turns to face the target (degrees per second)")]
    public float turnSpeed = 900f;
    [Tooltip("Swings only start when facing the target within this angle")]
    public float faceAngle = 50f;

    [Header("Area hit")]
    [Tooltip("One swing damages every target in reach, not only the one the player faces")]
    public bool hitAllInReach = true;
    [Tooltip("Extra reach for the other targets, on top of Range")]
    public float areaExtraRange = 0.4f;
    [Tooltip("Width of the area in degrees, centred on where the player faces (360 = all around)")]
    [Range(0f, 360f)] public float areaArc = 360f;

    [Header("Hit feel")]
    [Tooltip("Seconds the swing freezes on contact")]
    public float hitStop = 0.05f;
    [Tooltip("Longer freeze on the hit that destroys the target")]
    public float killHitStop = 0.12f;

    [Header("Weapon")]
    [Tooltip("End of the weapon: sparks appear where this meets the target")]
    public Transform weaponTip;
    [Tooltip("Swing trail, only emits during the fast part of the swing")]
    public TrailRenderer trail;

    public MeleeTarget Target => target;

    static readonly int AttackL = Animator.StringToHash("Attack_L");
    static readonly int AttackR = Animator.StringToHash("Attack_R");
    static readonly int AttackSpeedId = Animator.StringToHash("AttackSpeed");

    Animator anim;
    JoystickPlayerController pc;
    MeleeTarget target;
    bool swinging, hasSpeedParam;
    int swingIndex;
    float swingT, stopT;

    void Awake()
    {
        anim = GetComponent<Animator>();
        pc = GetComponent<JoystickPlayerController>();
        foreach (var p in anim.parameters)
            if (p.nameHash == AttackSpeedId) hasSpeedParam = true;
        if (trail != null) trail.emitting = false;
    }

    void OnDisable()
    {
        Engaged = false;
        swinging = false;
        stopT = 0f;
        if (anim != null) anim.speed = 1f;
        if (trail != null) trail.emitting = false;
    }

    void Update()
    {
        // hit-stop runs on unscaled time, so the debug game-speed button doesn't stretch it
        if (stopT > 0f)
        {
            stopT -= Time.unscaledDeltaTime;
            if (stopT <= 0f) anim.speed = 1f;
        }
        if (hasSpeedParam) anim.SetFloat(AttackSpeedId, attackSpeed);

        bool blocked = UIState.ModalOpen || UIState.BuildMode || PlayerHealth.KnockedOut || EnemyLeech.PlayerHeld;
        bool moving = pc != null && pc.RawInput.magnitude > pc.deadZone;
        target = blocked ? null : FindTarget();
        Engaged = target != null && !moving;

        if (swinging)
        {
            swingT += Time.deltaTime * anim.speed * attackSpeed;
            if (swingT >= comboTime) swinging = false;   // the next swing may start now
        }

        if (!Engaged)
        {
            // walking away: the Animator leaves the swing by itself (Attack -> Run), just stop the trail
            if (moving && trail != null) trail.emitting = false;
            if (moving) swinging = false;
            return;
        }

        // turn to the target
        Vector3 to = target.transform.position - transform.position; to.y = 0f;
        float angle = 0f;
        if (to.sqrMagnitude > 0.0001f)
        {
            var look = Quaternion.LookRotation(to);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
            angle = Quaternion.Angle(transform.rotation, look);
        }

        if (!swinging && angle <= faceAngle) StartSwing();
    }

    void StartSwing()
    {
        anim.CrossFadeInFixedTime(swingIndex == 0 ? AttackL : AttackR, crossFade, 0, 0f);
        swingIndex = 1 - swingIndex;
        swingT = 0f;
        swinging = true;
    }

    MeleeTarget FindTarget()
    {
        // keep the current one a little longer than the reach, so it doesn't flicker at the edge
        if (target != null && target.Alive && EdgeDistance(target) <= range + 0.4f) return target;
        MeleeTarget best = null;
        float bestD = range;
        var all = MeleeTarget.All;
        for (int i = 0; i < all.Count; i++)
        {
            var t = all[i];
            if (t == null || !t.Alive) continue;
            float d = EdgeDistance(t);
            if (d <= bestD) { bestD = d; best = t; }
        }
        return best;
    }

    float EdgeDistance(MeleeTarget t)
    {
        Vector3 d = t.transform.position - transform.position; d.y = 0f;
        return d.magnitude - t.radius;
    }

    // ------------------------------------------------------------------ animation events (on the swing clips)

    /// Contact frame of a swing.
public void MeleeHit()
    {
        if (!enabled) return;   // the shovel uses the same clips
        if (target == null || !target.Alive) return;
        if (EdgeDistance(target) > range + 0.6f) return;
        Vector3 tip = weaponTip != null ? weaponTip.position : transform.position + Vector3.up * 1.2f + transform.forward * 0.8f;
        bool killed = HitOne(target, tip);

        if (hitAllInReach)
        {
            // everything else standing in reach takes the same blow (backwards: a broken target may leave the list)
            Vector3 chest = transform.position + Vector3.up * 1f;
            var all = MeleeTarget.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (i >= all.Count) continue;
                var t = all[i];
                if (t == null || t == target || !t.Alive) continue;
                if (EdgeDistance(t) > range + areaExtraRange) continue;
                if (areaArc < 360f)
                {
                    Vector3 to = t.transform.position - transform.position; to.y = 0f;
                    if (to.sqrMagnitude > 0.0001f && Vector3.Angle(transform.forward, to) > areaArc * 0.5f) continue;
                }
                killed |= HitOne(t, chest);
            }
        }

        // one freeze per swing, however many things it hit
        float stop = killed ? killHitStop : hitStop;
        if (stop > 0f)
        {
            anim.speed = 0f;
            stopT = stop;
        }
    }

    bool HitOne(MeleeTarget t, Vector3 from)
    {
        Vector3 dir = t.transform.position - transform.position; dir.y = 0f;
        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward;
        return t.TakeHit(damage, t.HitPoint(from), dir, gameObject);
    }

    /// 1 at the start of the fast part of a swing, 0 after the follow-through.
    public void MeleeTrail(int on)
    {
        if (!enabled) return;
        if (trail != null) trail.emitting = on != 0;
    }
}
