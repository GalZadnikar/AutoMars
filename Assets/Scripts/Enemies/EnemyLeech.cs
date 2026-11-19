using System.Collections.Generic;
using UnityEngine;

/// Cave Leech (E3a): lives in a hole in the ground, placed by hand (Tools > Trash Game > 13. Enemies (Add Leech Hole))
/// or spawned with EnemyLeech.Spawn(pos). It only goes for the player.
///
/// Lurk (hidden in the hole) → player comes within wakeRange → Stalk (the head bursts out and creeps toward the
/// player, never further than `reach` from the hole) → Strike (rears up with the claws wide open, then lunges)
/// → Hold (player locked and lifted, small damage over time; drones near the player switch to the leech) →
/// Release (time up, rescued by enough damage, or the player knocked out) → Retract (head back into the hole)
/// → Cooldown → Lurk again: it comes back. A missed lunge goes back to stalking; losing the player = retract with a
/// short cooldown. HP does not regenerate between strikes (option). Killed = gone for good, the crater stays.
///
/// The head (child "Leech Head") carries the Health (Enemy team), so drones target it like any enemy. It is only
/// active while out of the hole. Visuals: LeechBody (tentacle, head, hole) + the fog-masked LeechFlesh shader.
[DefaultExecutionOrder(10)]   // Update after JoystickPlayerController
public class EnemyLeech : MonoBehaviour
{
    public static readonly List<EnemyLeech> All = new List<EnemyLeech>();
    /// The leech holding the player right now (null = free). JoystickPlayerController ignores input while set.
    public static EnemyLeech Holder { get; private set; }
    public static bool PlayerHeld => Holder != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { All.Clear(); Holder = null; }

    public enum State { Lurk, Stalk, Strike, Hold, Release, Retract, Cooldown, Dead }

    [Header("Reach")]
    [Tooltip("How far the head can get from the hole (tentacle length)")]
    public float reach = 16f;
    [Tooltip("The leech wakes up when the player comes this close to the hole")]
    public float wakeRange = 14f;
    [Tooltip("Seconds the player has to stay out of reach before it gives up")]
    public float loseTime = 1.5f;

    [Header("Stalk")]
    public float stalkSpeed = 4.2f;
    public float stalkAccel = 5f;
    [Tooltip("Head height while creeping")]
    public float stalkHeight = 0.75f;
    [Tooltip("Seconds to burst out of the hole")]
    public float emergeTime = 0.45f;

    [Header("Strike")]
    [Tooltip("Head this close to the player: strike")]
    public float strikeRange = 2.4f;
    [Tooltip("Rearing up with the claws open (the tell)")]
    public float windupTime = 0.4f;
    public float lungeTime = 0.16f;
    [Tooltip("How far the lunge can travel")]
    public float lungeReach = 3.6f;
    [Tooltip("Head this close to the player's chest after the lunge: grabbed")]
    public float grabRadius = 1.6f;
    [Tooltip("Pause after a missed lunge")]
    public float missRecover = 0.6f;

    [Header("Hold")]
    public float holdTime = 5f;
    public float liftHeight = 1.8f;
    public float grabDamage = 5f;
    public float holdDamagePerSecond = 3f;
    [Tooltip("Damage the leech must take during one hold to let go early (drones rescue). 0 = never")]
    public float releaseDamage = 30f;
    [Tooltip("Optional: tapping the joystick shortens the hold")]
    public bool struggleToEscape = false;
    public float struggleSecondsPerTap = 0.35f;

    [Header("After a strike")]
    public float retractSpeed = 7f;
    [Tooltip("Seconds in the hole after a grab before it can come out again")]
    public float cooldown = 12f;
    [Tooltip("Seconds in the hole after it gave up without a grab")]
    public float missCooldown = 3f;

    [Header("HP")]
    public float maxHP = 100f;
    public bool regenerateBetweenStrikes = false;

    [Header("Drones")]
    [Tooltip("Combat drones within this distance of the held player switch to the leech (priority target)")]
    public float rescueRadius = 25f;

    public State Current { get; private set; } = State.Lurk;
    public Health Health { get; private set; }
    public Transform Head => head;
    public LeechBody Body => body;
    public bool IsDead => Current == State.Dead;
    /// Seconds in the current state.
    public float StateTime => stateT;
    /// Seconds the player has been held this time.
    public float HoldTime => holdT;

    const float HideDepth = 1.3f;   // head height while inside the hole

    Transform head;
    LeechBody body;
    Vector3 headFlat;      // head position on the ground plane (y unused)
    float headHeight = -HideDepth;
    Vector3 headDir = Vector3.forward;
    float speed, stateT, loseT, holdT, holdDamage, lift, liftVel, priorityT, fogT, deadDrop;
    int strikePhase;   // 0 windup, 1 lunge, 2 recover after a miss
    bool emerging, missed;
    Vector3 windupFrom, lungeFrom, grabPoint;
    readonly List<CombatDrone> rescuers = new List<CombatDrone>();
    SpriteRenderer[] barRenderers;
    bool barShown = true;

    // player
    PlayerHealth ph;
    Transform playerT, chestBone;
    CharacterController cc;
    JoystickPlayerController controller;
    Animator playerAnim;
    bool lastStruggle;

    // ---------------------------------------------------------------- spawn / setup

    /// Makes a leech hole on the ground at `pos`.
    public static EnemyLeech Spawn(Vector3 pos)
    {
        pos.y = GroundUtil.Height(pos, pos.y);
        var go = new GameObject("Leech Hole");
        go.transform.position = pos;
        return go.AddComponent<EnemyLeech>();
    }

    void Awake()
    {
        head = new GameObject("Leech Head").transform;
        head.SetParent(transform, false);
        var h = head.gameObject.AddComponent<Health>();
        h.SetTeam(Team.Enemy);
        h.SetMax(maxHP);
        h.aimHeight = 0f;
        h.hitRadius = 0.7f;
        h.barHeight = 1.15f;
        h.barWidth = 1.5f;
        Health = h;
        Health.OnDied += Die;
        Health.OnDamaged += OnHurt;

        body = gameObject.AddComponent<LeechBody>();
        body.Build(head);

        headFlat = transform.position;
        Hide();
    }

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        PlayerHealth.KnockedOutEvent += OnPlayerKnockedOut;
    }

    void OnDisable()
    {
        All.Remove(this);
        PlayerHealth.KnockedOutEvent -= OnPlayerKnockedOut;
        if (Holder == this) FreePlayer();
        ClearDrones();
    }

    bool FindPlayer()
    {
        if (ph != null && ph == PlayerHealth.Instance) return true;
        ph = PlayerHealth.Instance;
        if (ph == null) return false;
        playerT = ph.transform;
        cc = ph.GetComponent<CharacterController>();
        controller = ph.GetComponent<JoystickPlayerController>();
        playerAnim = ph.GetComponentInChildren<Animator>();
        chestBone = null;
        string bone = PipeTether.Instance != null ? PipeTether.Instance.playerBone : "Body_Upper";
        foreach (var t in playerT.GetComponentsInChildren<Transform>())
            if (t.name == bone) { chestBone = t; break; }
        return true;
    }

    /// Player is a valid victim right now.
    bool CanTarget()
    {
        if (!FindPlayer()) return false;
        if (PlayerHealth.KnockedOut || ph.Health == null || ph.Health.IsDead) return false;
        return Holder == null || Holder == this;
    }

    Vector3 Chest => chestBone != null ? chestBone.position : playerT.position + Vector3.up * 1.3f;
    Vector3 Hole => transform.position;
    Vector3 HeadWorld { get { Vector3 p = headFlat; p.y = Ground(headFlat) + headHeight; return p; } }

    // ---------------------------------------------------------------- state machine

    void Enter(State s)
    {
        Current = s;
        stateT = 0f;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        stateT += dt;
        switch (Current)
        {
            case State.Lurk: TickLurk(); break;
            case State.Stalk: TickStalk(dt); break;
            case State.Strike: TickStrike(dt); break;
            case State.Hold: TickHold(dt); break;
            case State.Release: TickRelease(dt); break;
            case State.Retract: TickRetract(dt); break;
            case State.Cooldown: TickCooldown(); break;
            case State.Dead: TickDead(dt); break;
        }

        bool outOfHole = Current != State.Lurk && Current != State.Cooldown;
        Health.targetable = outOfHole && Current != State.Dead && headHeight > 0.1f;
        Health.invulnerable = headHeight < 0f && Current != State.Dead;
        UpdateBarFog(dt);
    }

    void TickLurk()
    {
        if (!CanTarget() || Holder != null) return;
        if (Flat(playerT.position - Hole) > wakeRange) return;
        // burst out
        head.gameObject.SetActive(true);
        headFlat = Hole;
        headHeight = -HideDepth;
        Vector3 to = playerT.position - Hole; to.y = 0f;
        headDir = to.sqrMagnitude > 0.01f ? to.normalized : Vector3.forward;
        emerging = true;
        speed = 0f;
        loseT = 0f;
        Enter(State.Stalk);
        CombatFX.Hit(body.Mouth + Vector3.up * 0.2f, Vector3.up, new Color(0.45f, 0.3f, 0.22f), 1.6f);
    }

    void TickStalk(float dt)
    {
        if (!CanTarget()) { GiveUp(); return; }
        Vector3 pp = playerT.position;

        if (emerging)
        {
            headHeight = Mathf.MoveTowards(headHeight, stalkHeight + 0.5f, (stalkHeight + 0.5f + HideDepth) / Mathf.Max(0.05f, emergeTime) * dt);
            if (headHeight >= stalkHeight + 0.49f) emerging = false;
            Pose(arch: 1.2f, wiggle: 0.25f, wiggleSpeed: 9f, jaw: 0.35f);
            return;
        }

        // creep toward the player, never further than the reach circle
        Vector3 target = pp;
        Vector3 off = target - Hole; off.y = 0f;
        if (off.magnitude > reach) target = Hole + off.normalized * reach;
        speed = Mathf.MoveTowards(speed, stalkSpeed, stalkAccel * dt);
        Vector3 to = target - headFlat; to.y = 0f;
        float stop = strikeRange * 0.6f;
        if (to.magnitude > stop) headFlat += to.normalized * Mathf.Min(to.magnitude - stop, speed * dt);
        headHeight = Mathf.MoveTowards(headHeight, stalkHeight + Mathf.Sin(Time.time * 3.1f) * 0.12f, dt * 2.5f);

        Vector3 toPlayer = pp - headFlat; toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > 0.01f) headDir = (toPlayer.normalized + Vector3.down * 0.15f).normalized;
        Pose(arch: 0.45f, wiggle: 0.55f, wiggleSpeed: 3f + 6f * speed / Mathf.Max(0.1f, stalkSpeed), jaw: 0.12f + Mathf.Sin(Time.time * 4f) * 0.08f);

        if (toPlayer.magnitude <= strikeRange) { StartStrike(); return; }

        bool outOfReach = Flat(pp - Hole) > reach + 1f;
        loseT = outOfReach ? loseT + dt : 0f;
        if (loseT >= loseTime) GiveUp();
    }

    void StartStrike()
    {
        strikePhase = 0;
        windupFrom = headFlat;
        Enter(State.Strike);
    }

    void TickStrike(float dt)
    {
        if (!CanTarget()) { GiveUp(); return; }
        Vector3 chest = Chest;
        Vector3 toP = playerT.position - headFlat; toP.y = 0f;
        Vector3 dirP = toP.sqrMagnitude > 0.01f ? toP.normalized : headDir;

        if (strikePhase == 0)
        {
            // rear up and pull back a little, claws wide open: the warning
            float k = Mathf.Clamp01(stateT / Mathf.Max(0.01f, windupTime));
            float e = k * k * (3f - 2f * k);
            Vector3 back = windupFrom - dirP * 0.6f * e;
            headFlat = Vector3.MoveTowards(headFlat, back, dt * 6f);
            headHeight = Mathf.MoveTowards(headHeight, stalkHeight + 1.1f * e, dt * 6f);
            headDir = (chest - HeadWorld).normalized;
            Pose(arch: 0.8f, wiggle: 0.15f, wiggleSpeed: 14f, jaw: e);
            if (k >= 1f) { strikePhase = 1; stateT = 0f; lungeFrom = HeadWorld; }
            return;
        }

        if (strikePhase == 1)
        {
            float k = Mathf.Clamp01(stateT / Mathf.Max(0.01f, lungeTime));
            Vector3 target = chest;
            Vector3 fl = target - lungeFrom; fl.y = 0f;
            if (fl.magnitude > lungeReach) target = lungeFrom + fl.normalized * lungeReach + Vector3.up * (target.y - lungeFrom.y);
            Vector3 offHole = target - Hole; offHole.y = 0f;
            if (offHole.magnitude > reach + 0.5f) { Vector3 c = Hole + offHole.normalized * (reach + 0.5f); target.x = c.x; target.z = c.z; }
            Vector3 p = Vector3.Lerp(lungeFrom, target, k * k);
            SetHeadWorld(p);
            headDir = (chest - p).sqrMagnitude > 0.01f ? (chest - p).normalized : headDir;
            Pose(arch: 0.6f, wiggle: 0.05f, wiggleSpeed: 4f, jaw: 1f - 0.4f * k);
            if (k >= 1f)
            {
                if (Vector3.Distance(p, chest) <= grabRadius) Grab();
                else { strikePhase = 2; stateT = 0f; }
            }
            return;
        }

        // missed: settle down and keep stalking
        headHeight = Mathf.MoveTowards(headHeight, stalkHeight, dt * 3f);
        headDir = Vector3.Slerp(headDir, dirP, dt * 6f);
        Pose(arch: 0.5f, wiggle: 0.35f, wiggleSpeed: 6f, jaw: 0.25f);
        if (stateT >= missRecover) { speed = stalkSpeed * 0.5f; loseT = 0f; emerging = false; Enter(State.Stalk); }
    }

    void Grab()
    {
        Holder = this;
        holdT = 0f;
        holdDamage = 0f;
        lift = 0f;
        liftVel = 0f;
        missed = false;
        grabPoint = playerT.position;
        if (cc != null) cc.enabled = false;
        if (playerAnim != null) playerAnim.SetInteger("AnimationPar", 0);
        Enter(State.Hold);
        CombatFX.Hit(Chest, -headDir, new Color(1f, 0.45f, 0.5f), 1.3f);
        GameToast.Show(struggleToEscape ? "A LEECH GRABBED YOU!  TAP TO STRUGGLE" : "A LEECH GRABBED YOU!", new Color(1f, 0.4f, 0.45f));
        CallDrones();
        ph.Health.Damage(grabDamage, gameObject);   // may knock the player out (then OnPlayerKnockedOut releases)
    }

    void TickHold(float dt)
    {
        if (Holder != this || !FindPlayer()) { Enter(State.Release); return; }
        if (PlayerHealth.KnockedOut) { Release(false); return; }

        holdT += dt;
        if (struggleToEscape && controller != null)
        {
            bool pressed = controller.RawInput.magnitude > 0.5f;
            if (pressed && !lastStruggle) holdT += struggleSecondsPerTap;
            lastStruggle = pressed;
        }

        // lift and sway
        lift = Mathf.SmoothDamp(lift, liftHeight, ref liftVel, 0.35f);
        float t = Time.time;
        Vector3 toHole = Hole - grabPoint; toHole.y = 0f;
        Vector3 fwd = toHole.sqrMagnitude > 0.01f ? toHole.normalized : playerT.forward;
        Vector3 side = Vector3.Cross(Vector3.up, fwd);
        Vector3 p = grabPoint + side * Mathf.Sin(t * 1.7f) * 0.14f + fwd * (Mathf.Sin(t * 1.1f) * 0.08f + 0.25f * lift / Mathf.Max(0.1f, liftHeight));
        p.y = Ground(grabPoint) + lift;
        playerT.position = p;
        playerT.rotation = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(Mathf.Sin(t * 2.1f) * 6f, 0f, Mathf.Sin(t * 1.6f) * 7f);
        if (playerAnim != null) playerAnim.SetInteger("AnimationPar", 0);

        ph.Health.Damage(holdDamagePerSecond * dt, gameObject);
        if (Current != State.Hold) return;   // the damage knocked the player out

        priorityT -= dt;
        if (priorityT <= 0f) CallDrones();

        if (releaseDamage > 0f && holdDamage >= releaseDamage) { Release(true); return; }
        if (holdT >= holdTime) Release(false);
    }

    /// Lets the player go. rescued = drones hurt it enough.
    void Release(bool rescued)
    {
        if (Holder == this) FreePlayer();
        ClearDrones();
        Vector3 hw = head.position;
        headFlat = hw;
        headHeight = hw.y - Ground(hw);
        Enter(State.Release);
        if (rescued) GameToast.Show("THE LEECH LET GO!", new Color(0.45f, 0.95f, 1f));
    }

    void FreePlayer()
    {
        if (Holder == this) Holder = null;
        if (playerT != null)
        {
            Vector3 f = playerT.forward; f.y = 0f;
            if (f.sqrMagnitude > 0.01f) playerT.rotation = Quaternion.LookRotation(f.normalized, Vector3.up);
        }
        if (cc != null) cc.enabled = true;
    }

    void TickRelease(float dt)
    {
        // pull back from the player, claws open
        Vector3 to = Hole - headFlat; to.y = 0f;
        if (to.sqrMagnitude > 0.01f) headFlat += to.normalized * Mathf.Min(to.magnitude, 2.5f * dt);
        headHeight = Mathf.MoveTowards(headHeight, stalkHeight + 0.6f, dt * 2f);
        Pose(arch: 0.7f, wiggle: 0.3f, wiggleSpeed: 8f, jaw: 0.8f);
        if (stateT >= 0.45f) Enter(State.Retract);
    }

    void TickRetract(float dt)
    {
        Vector3 to = Hole - headFlat; to.y = 0f;
        float d = to.magnitude;
        if (d > 0.05f)
        {
            headFlat += to / d * Mathf.Min(d, retractSpeed * dt);
            headHeight = Mathf.MoveTowards(headHeight, stalkHeight, dt * 3f);
            if (to.sqrMagnitude > 0.01f) headDir = Vector3.Slerp(headDir, -to.normalized, dt * 4f);
        }
        else
        {
            headFlat = Hole;
            headHeight = Mathf.MoveTowards(headHeight, -HideDepth, dt * 4f);
            headDir = Vector3.Slerp(headDir, Vector3.down, dt * 6f);
            if (headHeight <= -HideDepth + 0.01f) { Hide(); Enter(State.Cooldown); return; }
        }
        Pose(arch: 0.6f, wiggle: 0.35f, wiggleSpeed: 10f, jaw: 0f);
    }

    void TickCooldown()
    {
        if (stateT < (missed ? missCooldown : cooldown)) return;
        if (regenerateBetweenStrikes) Health.HealFull();
        missed = false;
        Enter(State.Lurk);
    }

    /// Lost the player without a grab: back into the hole, short cooldown.
    void GiveUp()
    {
        missed = true;
        Enter(State.Retract);
    }

    void Hide()
    {
        headHeight = -HideDepth;
        headFlat = Hole;
        body.show = false;
        if (head.gameObject.activeSelf) head.gameObject.SetActive(false);
    }

    // ---------------------------------------------------------------- damage / death

    void OnHurt(float amount, GameObject source)
    {
        if (Current == State.Hold) holdDamage += amount;
    }

    void OnPlayerKnockedOut()
    {
        if (Holder == this) Release(false);
    }

    void Die()
    {
        if (Current == State.Dead) return;
        if (Holder == this) FreePlayer();
        ClearDrones();
        Vector3 hw = head.position;
        headFlat = hw;
        headHeight = hw.y - Ground(hw);
        deadDrop = 0f;
        Enter(State.Dead);
        CombatFX.Explode(hw, 1.1f, new Color(1f, 0.5f, 0.55f));
        GameToast.Show("LEECH KILLED", new Color(0.45f, 1f, 0.45f));
    }

    void TickDead(float dt)
    {
        if (!head.gameObject.activeSelf) return;   // dissolved
        // falls limp onto the ground, then dissolves; the hole stays
        deadDrop += 9f * dt;
        headHeight = Mathf.Max(0.25f, headHeight - deadDrop * dt);
        float limp = Mathf.Clamp01(stateT / 1.0f);
        float size = 1f - Mathf.Clamp01((stateT - 1.4f) / 1.8f);
        body.show = true;
        body.head = HeadWorld;
        body.headDir = Vector3.Slerp(headDir, Vector3.down, limp * 0.6f);
        body.arch = Mathf.Lerp(0.5f, 0.1f, limp);
        body.wiggle = 0f;
        body.taut = 0f;
        body.jaw = 0.6f;
        body.pulse = 0f;
        body.limp = limp;
        body.sizeMul = size;
        if (size <= 0f)
        {
            CombatFX.Hit(body.Mouth, Vector3.up, new Color(0.45f, 0.3f, 0.22f), 1.2f);
            body.show = false;
            head.gameObject.SetActive(false);
        }
    }

    /// Kills the leech for good (debug / scripted).
    public void Kill() { if (Current != State.Dead) Health.Kill(); }

    // ---------------------------------------------------------------- drones

    /// Drones near the held player switch to the leech.
    void CallDrones()
    {
        priorityT = 0.5f;
        if (playerT == null) return;
        Vector3 p = playerT.position;
        foreach (var b in ScoutBot.All)
        {
            var d = b as CombatDrone;
            if (d == null || d.IsDown) continue;
            if (Flat(d.transform.position - p) > rescueRadius) continue;
            if (d.PriorityTarget != Health) d.PriorityTarget = Health;
            if (!rescuers.Contains(d)) rescuers.Add(d);
        }
    }

    void ClearDrones()
    {
        foreach (var d in rescuers)
            if (d != null && d.PriorityTarget == Health) d.PriorityTarget = null;
        rescuers.Clear();
    }

    // ---------------------------------------------------------------- pose → LeechBody

    void Pose(float arch, float wiggle, float wiggleSpeed, float jaw)
    {
        body.arch = arch;
        body.wiggle = wiggle;
        body.wiggleSpeed = wiggleSpeed;
        body.jaw = jaw;
        body.taut = 0f;
        body.pulse = 0f;
        body.limp = 0f;
        body.sizeMul = 1f;
    }

    void SetHeadWorld(Vector3 p)
    {
        headFlat = p;
        headHeight = p.y - Ground(p);
    }

    void LateUpdate()
    {
        if (Current == State.Dead || Current == State.Lurk || Current == State.Cooldown) return;
        if (Current == State.Hold && playerT != null)
        {
            // latched onto the player's upper body (after the Animator posed it), on the side facing the hole
            Vector3 chest = Chest;
            Vector3 toHole = Hole - chest; toHole.y = 0f;
            Vector3 f = toHole.sqrMagnitude > 0.01f ? toHole.normalized : -playerT.forward;
            Vector3 hp = chest + f * 0.42f + Vector3.up * 0.05f;
            SetHeadWorld(hp);
            headDir = (chest - hp).normalized;
            body.arch = 0.6f;
            body.wiggle = 0.12f;
            body.wiggleSpeed = 5f;
            body.taut = Mathf.Clamp01(lift / Mathf.Max(0.1f, liftHeight)) * 0.75f;
            body.jaw = 0.05f;
            body.pulse = 1f;
            body.limp = 0f;
            body.sizeMul = 1f;
        }
        body.show = true;
        body.head = HeadWorld;
        body.headDir = headDir;
        head.position = body.head;
        head.rotation = Quaternion.identity;
    }

    // HP bar hidden where the head is hidden (black fog; the meshes hide per pixel in the shader)
    void UpdateBarFog(float dt)
    {
        fogT -= dt;
        if (fogT > 0f) return;
        fogT = 0.2f;
        if (barRenderers == null || barRenderers.Length == 0)
        {
            var bar = head.GetComponentInChildren<HealthBar>(true);
            if (bar == null) return;
            barRenderers = bar.GetComponentsInChildren<SpriteRenderer>(true);
            barShown = true;
        }
        var fog = FogOfWar.Instance;
        bool show = fog == null || (body.showInExplored ? fog.IsExplored(head.position) : fog.IsVisible(head.position));
        if (show == barShown) return;
        barShown = show;
        foreach (var r in barRenderers) if (r != null) r.forceRenderingOff = !show;
    }

    // ---------------------------------------------------------------- helpers

    float Ground(Vector3 p) => GroundUtil.Height(p, transform.position.y);
    static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.4f, 0.55f);
        Gizmos.DrawSphere(transform.position + Vector3.up * 0.3f, 0.35f);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.4f, 0.55f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, reach);
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, wakeRange);
    }
}
