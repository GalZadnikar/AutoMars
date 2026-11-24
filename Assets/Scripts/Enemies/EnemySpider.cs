using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// Spider (close-combat enemy, model: Polygonal Alien Horror). Wanders near its home, runs at the player (or any
/// player-team unit) that comes close and attacks with a front-leg slam or a longer tail stab.
/// Every attack has a wind-up the player can read and walk out of: the damage only lands if the target is still
/// inside the attack's reach and arc on the contact frame.
/// The player fights it with the pickaxe: it is a MeleeTarget (flash, squash, sparks, damage number), every hit
/// knocks it back, and enough damage in a short time staggers it (interrupts its attack).
/// At 0 HP it plays its death, lies there for a moment and sinks into the ground.
/// Prefab: Assets/Resources/Enemies/Enemy Spider.prefab (Tools > Trash Game > 13. Enemies (Spider Prefab)).
[RequireComponent(typeof(Health))]
[RequireComponent(typeof(MeleeTarget))]
[RequireComponent(typeof(CharacterController))]
public class EnemySpider : MonoBehaviour
{
    public static readonly List<EnemySpider> All = new List<EnemySpider>();
    public const string PrefabResource = "Enemies/Enemy Spider";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { All.Clear(); }

    [System.Serializable]
    public class Attack
    {
        public string name = "Attack";
        [Tooltip("Animator state")]
        public string state = "AttackLegs";
        public float damage = 8f;
        [Tooltip("Starts this attack when the target is this close (centre to centre)")]
        public float startRange = 1.9f;
        [Tooltip("Seconds from the start of the attack to the blow: the time the player has to step away")]
        public float windUp = 0.55f;
        [Tooltip("The blow lands if the target is within this distance (centre to centre) ...")]
        public float reach = 2.1f;
        [Tooltip("... and inside this angle in front of the spider")]
        public float arc = 100f;
        [Tooltip("Metres it jumps forward just before the blow")]
        public float lunge = 0.4f;
        [Tooltip("How hard the blow shoves the player (m/s)")]
        public float push = 6f;
        [Tooltip("Camera kick when it connects")]
        public float shake = 0.35f;
        [Tooltip("Seconds after the blow until it can act again")]
        public float recover = 0.55f;
        [Tooltip("Time of the blow inside the animation clip (seconds at normal speed)")]
        public float clipContact = 0.33f;
        [Tooltip("Length of the animation clip in seconds")]
        public float clipLength = 1f;
        [Tooltip("How often it is picked compared with the others")]
        public float weight = 1f;
    }

    [Header("Senses")]
    [Tooltip("Notices a player-team unit this close")]
    public float detectRange = 9f;
    [Tooltip("Gives up when the target is this far away")]
    public float loseRange = 15f;
    [Tooltip("Never chases further than this from its home")]
    public float leashRadius = 22f;

    [Header("Movement")]
    public float walkSpeed = 1.1f;
    public float chaseSpeed = 3.4f;
    public float turnSpeed = 420f;
    [Tooltip("Wanders this far from its home while calm")]
    public float wanderRadius = 4f;
    [Tooltip("Stops this far from the target (centre to centre)")]
    public float stopDistance = 1.65f;
    [Tooltip("Ground speed of the walk / run clips at animation speed 1 (so the feet don't slide)")]
    public float walkClipSpeed = 1f;
    public float runClipSpeed = 3f;

    [Header("Attacks")]
    public Attack[] attacks =
    {
        new Attack { name = "Leg slam", state = "AttackLegs", damage = 6f, startRange = 2.05f, windUp = 0.55f, reach = 2.3f, arc = 110f, lunge = 0.4f, push = 3.5f, shake = 0.3f, recover = 0.5f, clipContact = 0.33f, weight = 2f },
        new Attack { name = "Tail stab", state = "AttackTail", damage = 12f, startRange = 2.6f, windUp = 0.8f, reach = 2.9f, arc = 60f, lunge = 0.15f, push = 7f, shake = 0.5f, recover = 0.7f, clipContact = 0.36f, weight = 1f },
    };
    [Tooltip("Seconds between attacks (random between the two)")]
    public Vector2 attackCooldown = new Vector2(0.7f, 1.3f);
    [Tooltip("It keeps turning to the target during the wind-up until this many seconds before the blow (then the player can side-step)")]
    public float aimLockTime = 0.22f;

    [Header("Getting hit")]
    [Tooltip("Speed (m/s) a hit knocks it back with")]
    public float knockback = 4.5f;
    [Tooltip("Knockback multiplier while it is winding up an attack (it stands firmer)")]
    [Range(0f, 1f)] public float attackKnockback = 0.35f;
    [Tooltip("This much damage within Stagger Window staggers it and interrupts its attack (0 = never)")]
    public float staggerDamage = 9f;
    public float staggerWindow = 2f;
    public float staggerTime = 0.55f;
    [Tooltip("Seconds after a stagger during which it can't be staggered again (so it gets its attack in)")]
    public float staggerImmunity = 2.2f;
    [Tooltip("Colour of the goo that sprays out")]
    public Color gooColor = new Color(0.55f, 0.85f, 0.2f);

    [Header("Death")]
    [Tooltip("Seconds the body lies there before it sinks")]
    public float corpseTime = 2f;
    public float sinkTime = 1.2f;
    public float sinkDepth = 1.1f;
    [Tooltip("Camera kick when it dies")]
    public float deathShake = 0.45f;
    [Tooltip("Testing: seconds until it comes back at its home after dying (0 = it stays dead and is removed)")]
    public float respawnDelay = 0f;

    public enum State { Idle, Wander, Chase, Attack, Stagger, Return, Dead }
    public State Current { get; private set; } = State.Idle;
    public Health Health { get; private set; }
    public Health Target => target;

    static readonly int IdleId = Animator.StringToHash("Idle");
    static readonly int LookId = Animator.StringToHash("Look");
    static readonly int WalkId = Animator.StringToHash("Walk");
    static readonly int RunId = Animator.StringToHash("Run");
    static readonly int HitId = Animator.StringToHash("Hit");
    static readonly int DieId = Animator.StringToHash("Die");
    const float HitClipLength = 0.83f;

    MeleeTarget melee;
    CharacterController cc;
    Animator anim;
    Transform visual;
    Vector3 visualPos, visualScale;
    Health target;
    Vector3 home, wanderTo, knock;
    float vy, stateT, waitT, scanT, cooldownT, poise, poiseT, immuneT, popT = -1f;
    int animNow;
    Attack attack, nextAttack;
    bool struck;
    Coroutine deathRoutine;

    /// Puts a spider on the ground at `pos` (needs the prefab in Resources; null without it).
    public static EnemySpider Spawn(Vector3 pos)
    {
        var prefab = Resources.Load<GameObject>(PrefabResource);
        if (prefab == null) { Debug.LogWarning("[EnemySpider] prefab missing: Resources/" + PrefabResource); return null; }
        pos.y = GroundUtil.Height(pos, pos.y) + 0.05f;
        var inst = Instantiate(prefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        inst.name = "Enemy Spider";
        return inst.GetComponent<EnemySpider>();
    }

    void Awake()
    {
        Health = GetComponent<Health>();
        melee = GetComponent<MeleeTarget>();
        cc = GetComponent<CharacterController>();
        anim = GetComponentInChildren<Animator>();
        visual = melee.visual != null ? melee.visual : transform.childCount > 0 ? transform.GetChild(0) : transform;
        visualPos = visual.localPosition;
        visualScale = visual.localScale;
        melee.breakOnDeath = false;   // the death is played here
        Health.OnDamaged += OnDamaged;
        Health.OnDied += OnDied;
    }

    void OnDestroy()
    {
        if (Health != null) { Health.OnDamaged -= OnDamaged; Health.OnDied -= OnDied; }
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    IEnumerator Start()
    {
        home = transform.position;
        waitT = Random.Range(0.5f, 2f);
        PickNextAttack();
        Play(IdleId, 1f, 0f);
        yield return null;   // after Health made its bar, so the fog hides that too
        if (GetComponent<FogHideable>() == null)
        {
            var fh = gameObject.AddComponent<FogHideable>();
            fh.hide = FogHideable.Mode.WhenNotVisible;
        }
    }

    // ------------------------------------------------------------------ brain

    void Update()
    {
        if (Current == State.Dead) return;
        float dt = Time.deltaTime;
        stateT += dt;
        cooldownT -= dt;
        immuneT -= dt;
        if (poiseT > 0f) { poiseT -= dt; if (poiseT <= 0f) poise = 0f; }
        if (popT >= 0f) Pop(dt);

        // look for something to fight (not in the middle of an attack / stagger)
        if (Current != State.Attack && Current != State.Stagger)
        {
            scanT -= dt;
            if (scanT <= 0f)
            {
                scanT = 0.3f;
                if (!KeepTarget()) target = FindTarget();
                if (target != null && (Current == State.Idle || Current == State.Wander || Current == State.Return)) Go(State.Chase);
            }
        }

        Vector3 planar = Vector3.zero;
        switch (Current)
        {
            case State.Idle: UpdateIdle(); break;
            case State.Wander: planar = UpdateWander(); break;
            case State.Chase: planar = UpdateChase(); break;
            case State.Attack: planar = UpdateAttack(dt); break;
            case State.Stagger: UpdateStagger(); break;
            case State.Return: planar = UpdateReturn(); break;
        }
        Move(planar, dt);
    }

    void Go(State s)
    {
        Current = s;
        stateT = 0f;
    }

    void UpdateIdle()
    {
        if (animNow != IdleId && animNow != LookId) Play(IdleId, 1f);
        waitT -= Time.deltaTime;
        if (waitT > 0f) return;
        Vector2 r = Random.insideUnitCircle * wanderRadius;
        wanderTo = home + new Vector3(r.x, 0f, r.y);
        Go(State.Wander);
    }

    Vector3 UpdateWander()
    {
        Vector3 to = Flat(wanderTo - transform.position);
        if (to.magnitude < 0.4f || stateT > 8f)
        {
            waitT = Random.Range(1.5f, 4f);
            Go(State.Idle);
            Play(Random.value < 0.4f ? LookId : IdleId, 1f);
            return Vector3.zero;
        }
        Turn(to, turnSpeed * 0.5f);
        Play(WalkId, walkSpeed / Mathf.Max(0.05f, walkClipSpeed));
        return transform.forward * walkSpeed;
    }

    Vector3 UpdateReturn()
    {
        Vector3 to = Flat(home - transform.position);
        if (to.magnitude < Mathf.Max(1f, wanderRadius * 0.5f))
        {
            waitT = Random.Range(1f, 2.5f);
            Go(State.Idle);
            return Vector3.zero;
        }
        Turn(to, turnSpeed);
        float speed = chaseSpeed * 0.7f;
        Play(RunId, speed / Mathf.Max(0.05f, runClipSpeed));
        return transform.forward * speed;
    }

    Vector3 UpdateChase()
    {
        if (target == null) { Go(State.Return); return Vector3.zero; }
        Vector3 to = Flat(target.transform.position - transform.position);
        float dist = to.magnitude;
        Turn(to, turnSpeed);
        float facing = dist > 0.01f ? Vector3.Angle(transform.forward, to) : 0f;

        if (cooldownT <= 0f && nextAttack != null && dist <= nextAttack.startRange && facing < 30f)
        {
            StartAttack(nextAttack);
            return Vector3.zero;
        }
        if (dist > stopDistance)
        {
            // slows down for the last step so it doesn't bump into the target
            float speed = Mathf.Min(chaseSpeed, Mathf.Max(0.8f, (dist - stopDistance) * 6f));
            if (facing > 70f) speed *= 0.3f;   // turns first, then runs
            Play(RunId, speed / Mathf.Max(0.05f, runClipSpeed));
            return transform.forward * speed;
        }
        Play(IdleId, 1.4f);   // in its face, waiting for the next attack
        return Vector3.zero;
    }

    // ------------------------------------------------------------------ attacking

    void PickNextAttack()
    {
        nextAttack = null;
        if (attacks == null || attacks.Length == 0) return;
        float total = 0f;
        foreach (var a in attacks) total += Mathf.Max(0f, a.weight);
        float r = Random.value * total;
        foreach (var a in attacks)
        {
            r -= Mathf.Max(0f, a.weight);
            if (r <= 0f) { nextAttack = a; return; }
        }
        nextAttack = attacks[0];
    }

    void StartAttack(Attack a)
    {
        attack = a;
        struck = false;
        Go(State.Attack);
        // the clip is stretched so that its contact frame falls exactly at the end of the wind-up
        Play(Animator.StringToHash(a.state), a.clipContact / Mathf.Max(0.05f, a.windUp), 0.08f, true);
    }

    Vector3 UpdateAttack(float dt)
    {
        Vector3 planar = Vector3.zero;
        if (!struck)
        {
            float left = attack.windUp - stateT;
            if (target != null && left > aimLockTime)
                Turn(Flat(target.transform.position - transform.position), turnSpeed * 0.45f);
            // the jump forward, in the last moment before the blow
            const float lungeTime = 0.12f;
            if (left <= lungeTime && attack.lunge > 0f) planar = transform.forward * (attack.lunge / lungeTime);
            if (left <= 0f) Strike();
        }
        else if (stateT >= attack.windUp + attack.recover)
        {
            cooldownT = Random.Range(attackCooldown.x, attackCooldown.y);
            PickNextAttack();
            Go(State.Chase);
        }
        return planar;
    }

    void Strike()
    {
        struck = true;
        // the rest of the clip (pulling back) plays in the recover time
        float rest = Mathf.Max(0.05f, attack.clipLength - attack.clipContact);
        anim.speed = rest / Mathf.Max(0.05f, attack.recover);

        Vector3 fwd = transform.forward;
        Vector3 impact = transform.position + fwd * Mathf.Min(attack.reach * 0.6f, 1.3f) + Vector3.up * 0.15f;
        bool hit = false;
        if (target != null && target.Targetable)
        {
            Vector3 to = Flat(target.transform.position - transform.position);
            if (to.magnitude <= attack.reach && Vector3.Angle(fwd, to) <= attack.arc * 0.5f)
            {
                float done = target.Damage(attack.damage, gameObject);
                if (done > 0f)
                {
                    hit = true;
                    Vector3 dir = to.sqrMagnitude > 0.0001f ? to.normalized : fwd;
                    CombatFX.Hit(target.AimPoint - dir * 0.3f, -dir, new Color(1f, 0.3f, 0.2f), 1.5f);
                    var pc = target.GetComponent<JoystickPlayerController>();
                    if (pc != null)
                    {
                        pc.Push(dir * attack.push);
                        CameraShake.Kick(attack.shake, null, 0.22f);
                    }
                }
            }
        }
        // a miss thumps into the ground
        if (!hit) CombatFX.Hit(impact, Vector3.up, new Color(0.8f, 0.62f, 0.45f), 1.1f);
    }

    // ------------------------------------------------------------------ getting hit

    void OnDamaged(float amount, GameObject source)
    {
        if (Current == State.Dead) return;
        Vector3 dir = source != null ? Flat(transform.position - source.transform.position) : -transform.forward;
        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : -transform.forward;
        bool windingUp = Current == State.Attack && !struck;
        knock = dir * knockback * (windingUp ? attackKnockback : 1f) * PlayerWeapon.HitKnockScale;   // a bullet pushes less than a pickaxe blow
        CombatFX.Hit(Health.AimPoint - dir * 0.4f, -dir + Vector3.up * 0.6f, gooColor, 1.1f);

        // whoever hit it becomes the target
        if (source != null && (target == null || !target.Targetable))
        {
            var h = source.GetComponentInParent<Health>();
            if (h != null && h.team == Team.Player && h.Targetable) { target = h; if (Current != State.Attack && Current != State.Stagger) Go(State.Chase); }
        }

        if (Health.IsDead || staggerDamage <= 0f || immuneT > 0f || Current == State.Stagger) return;
        poise += amount;
        poiseT = staggerWindow;
        if (poise >= staggerDamage) Stagger(dir);
    }

    void Stagger(Vector3 dir)
    {
        poise = 0f;
        poiseT = 0f;
        immuneT = staggerTime + staggerImmunity;
        knock = dir * knockback * 1.6f;
        Go(State.Stagger);
        Play(HitId, HitClipLength / Mathf.Max(0.1f, staggerTime), 0.05f, true);
        CombatFX.Hit(Health.AimPoint, Vector3.up, gooColor, 1.8f);
    }

    void UpdateStagger()
    {
        if (stateT < staggerTime) return;
        cooldownT = Mathf.Min(cooldownT, 0.15f);   // it answers a stagger with an attack
        Go(target != null ? State.Chase : State.Return);
    }

    // ------------------------------------------------------------------ death

    void OnDied()
    {
        if (Current == State.Dead) return;
        Go(State.Dead);
        target = null;
        knock = Vector3.zero;
        cc.enabled = false;   // the player can walk over the body
        Play(DieId, 1f, 0.06f, true);
        CombatFX.Hit(Health.AimPoint, Vector3.up, gooColor, 2.4f);
        CombatFX.MeleeHit(Health.AimPoint, Vector3.up, gooColor, 2f);
        CameraShake.Kick(deathShake, null, 0.25f);
        deathRoutine = StartCoroutine(DeathRoutine());
    }

    IEnumerator DeathRoutine()
    {
        yield return new WaitForSeconds(corpseTime);
        Vector3 from = transform.position;
        for (float t = 0f; t < sinkTime; t += Time.deltaTime)
        {
            float k = t / sinkTime;
            transform.position = from + Vector3.down * (sinkDepth * k * k);
            yield return null;
        }
        if (respawnDelay <= 0f) { Destroy(gameObject); yield break; }

        visual.gameObject.SetActive(false);
        yield return new WaitForSeconds(respawnDelay);
        Revive();
    }

    /// Back alive at its home (the test respawn).
    public void Revive()
    {
        if (deathRoutine != null) { StopCoroutine(deathRoutine); deathRoutine = null; }
        Vector3 p = home;
        p.y = GroundUtil.Height(p, home.y) + 0.05f;
        transform.SetPositionAndRotation(p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        visual.gameObject.SetActive(true);
        visual.localPosition = visualPos;
        melee.Revive();
        if (Health.IsDead) Health.HealFull();
        cc.enabled = true;
        vy = 0f;
        poise = 0f;
        immuneT = 0f;
        cooldownT = 0.5f;
        waitT = 1f;
        animNow = 0;
        Go(State.Idle);
        Play(IdleId, 1f, 0f, true);
        popT = 0f;   // grows out of the ground
        visual.localScale = visualScale * 0.01f;
        CombatFX.Hit(p + Vector3.up * 0.2f, Vector3.up, new Color(0.8f, 0.62f, 0.45f), 1.6f);
    }

    void Pop(float dt)
    {
        const float time = 0.35f;
        popT += dt;
        float k = Mathf.Clamp01(popT / time);
        float s = 1f + 0.18f * Mathf.Sin(k * Mathf.PI);   // a little overshoot
        visual.localScale = visualScale * (k * s);
        if (k >= 1f) { popT = -1f; visual.localScale = visualScale; }
    }

    // ------------------------------------------------------------------ helpers

    bool Valid(Health h)
    {
        if (h == null || !h.Targetable) return false;
        if (PlayerHealth.KnockedOut && PlayerHealth.Instance != null && h == PlayerHealth.Instance.Health) return false;
        return Flat(h.transform.position - home).magnitude <= leashRadius;
    }

    bool KeepTarget()
    {
        if (target == null) return false;
        if (Valid(target) && Flat(target.transform.position - transform.position).magnitude <= loseRange) return true;
        target = null;
        return false;
    }

    Health FindTarget() => Targeting.FindNearest(Team.Player, transform.position, detectRange, Valid, false, false);

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    void Turn(Vector3 to, float speed)
    {
        if (to.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), speed * Time.deltaTime);
    }

    void Move(Vector3 planar, float dt)
    {
        if (!cc.enabled) return;
        if (cc.isGrounded && vy < 0f) vy = -2f;
        vy -= 20f * dt;
        Vector3 v = planar + knock;
        // deep sand is a wall for it too (it can still walk out if it ends up inside)
        if (v.sqrMagnitude > 0.0001f && SandField.All.Count > 0 && !SandField.BlockedAt(transform.position)
            && SandField.BlockedAt(transform.position + v.normalized * (cc.radius + 0.25f))) v = Vector3.zero;
        v.y = vy;
        cc.Move(v * dt);
        knock = Vector3.MoveTowards(knock, Vector3.zero, 16f * dt);
    }

    void Play(int state, float speed, float fade = 0.15f, bool restart = false)
    {
        if (anim == null) return;
        anim.speed = Mathf.Clamp(speed, 0.05f, 4f);
        if (animNow == state && !restart) return;
        animNow = state;
        anim.CrossFadeInFixedTime(state, fade, 0, 0f);
    }

    void OnDrawGizmosSelected()
    {
        Vector3 c = Application.isPlaying ? home : transform.position;
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, detectRange);
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(c, leashRadius);
        Gizmos.color = new Color(0.4f, 1f, 0.4f, 0.6f);
        Gizmos.DrawWireSphere(c, wanderRadius);
    }
}
