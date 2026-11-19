using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// Enemy Drone (E2): a flying guard. Patrols slowly inside its home radius. When the player, a player robot or a
/// combat drone comes within `detectRange` it attacks the current target; if the target leaves `loseRange` (or the
/// leash around home) it switches to the nearest other target, otherwise flies home and calms down.
/// Same flight idea as CombatDrone: the root stays on the ground, the model hovers above it.
/// Broken / dead bots and a knocked-out player are not targets. Spawn with EnemyDrone.Spawn(pos, home) or an EnemySpawner.
[RequireComponent(typeof(Health))]
public class EnemyDrone : MonoBehaviour
{
    public static readonly List<EnemyDrone> All = new List<EnemyDrone>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { All.Clear(); lit = null; unlit = null; }

    [Header("Home")]
    public Vector3 home;
    [Tooltip("Patrols inside this radius")]
    public float homeRadius = 10f;
    [Tooltip("Never goes further than this from home (chasing stops there)")]
    public float leashRadius = 20f;

    [Header("Senses")]
    public float detectRange = 13f;
    [Tooltip("A target further away than this is dropped")]
    public float loseRange = 18f;
    public float attackRange = 11f;

    [Header("Flight")]
    public float hoverHeight = 2.5f;
    public float patrolSpeed = 1.8f;
    public float chaseSpeed = 4f;
    public float bobAmplitude = 0.12f;
    public float tiltAmount = 14f;
    [Tooltip("Rotate the model so its nose points along +Z")]
    public float modelYaw = 0f;
    public float modelWidth = 1.6f;

    [Header("Weapon")]
    public float damage = 6f;
    public float fireRate = 1f;
    public float shotSpeed = 17f;
    public float aimCone = 20f;
    public Color shotColor = new Color(1f, 0.25f, 0.2f);

    [Tooltip("Hide it while it is outside your sight (fog). Debug spawns leave it visible, like the dummy.")]
    public bool hideInFog = true;

    public enum State { Patrol, Aggro, Return }
    public State Current { get; private set; } = State.Patrol;
    public Health Target { get; private set; }
    public Health Health { get; private set; }

    ShotSpec spec;
    Transform model;
    Vector3 modelBase, patrolTarget, lastPos, smoothVel;
    float hover, phase, fireT, scanT, patrolWait, recoil, losT;
    bool hasLos = true;
    static Material lit, unlit;

    const string ModelResource = "Enemies/EnemyDroneModel";

    /// Builds an enemy drone on the ground at `pos`, with `home` as its post.
    public static EnemyDrone Spawn(Vector3 pos, Vector3? home = null)
    {
        var go = new GameObject("Enemy Drone");
        pos.y = GroundUtil.Height(pos, pos.y);
        go.transform.position = pos;
        var h = go.AddComponent<Health>();
        h.SetTeam(Team.Enemy);
        h.SetMax(40f);
        h.aimHeight = 2.5f;
        h.hitRadius = 0.8f;
        h.barHeight = 4f;
        h.barWidth = 1.3f;
        var dfx = go.AddComponent<DeathFX>();
        dfx.size = 1.6f;
        dfx.color = new Color(1f, 0.35f, 0.2f);
        var d = go.AddComponent<EnemyDrone>();
        d.home = home ?? pos;
        d.Build();
        return d;
    }

    void Awake()
    {
        Health = GetComponent<Health>();
        Health.OnDamaged += OnHit;
        Health.OnDied += OnDied;
        spec = new ShotSpec { damage = damage, speed = shotSpeed, color = shotColor, radius = 0.12f, trailTime = 0.1f };
        hover = hoverHeight;
        phase = Random.value * 10f;
        lastPos = transform.position;
        if (home == Vector3.zero) home = transform.position;
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    IEnumerator Start()
    {
        if (home == Vector3.zero) home = transform.position;
        patrolWait = Random.Range(0.5f, 2f);
        yield return null;   // after the HP bar exists
        if (hideInFog) gameObject.AddComponent<FogHideable>().hide = FogHideable.Mode.WhenNotVisible;
    }

    // ------------------------------------------------------------------ visuals

    void Build()
    {
        if (lit == null) lit = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        if (unlit == null) unlit = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        model = new GameObject("Model").transform;
        model.SetParent(transform, false);

        var src = Resources.Load<GameObject>(ModelResource);   // made by Tools > Trash Game > 13. Enemies
        if (src != null)
        {
            var mesh = Instantiate(src, model);
            foreach (var c in mesh.GetComponentsInChildren<Collider>()) Destroy(c);
            foreach (var mb in mesh.GetComponentsInChildren<MonoBehaviour>()) Destroy(mb);
            // measure at the model's origin, then convert the (world-space) bounds centre into model space;
            // using the world centre directly offset the mesh by the spawn position, so it swung around the
            // root whenever the drone turned (e.g. to face the player when attacking)
            mesh.transform.localPosition = Vector3.zero;
            mesh.transform.localRotation = Quaternion.identity;
            mesh.transform.localScale = Vector3.one;
            var b = BoundsOf(mesh);
            Vector3 ctr = model.InverseTransformPoint(b.center);
            Vector3 size = model.InverseTransformVector(b.size);
            float s = modelWidth / Mathf.Max(0.01f, Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.z)));
            var rot = Quaternion.Euler(0f, modelYaw, 0f);
            mesh.transform.localScale = Vector3.one * s;
            mesh.transform.localRotation = rot;
            mesh.transform.localPosition = -(rot * ctr) * s;
            // tint it red so it reads as hostile
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", new Color(1f, 0.45f, 0.4f));
            foreach (var r in mesh.GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(mpb);
        }
        else
        {
            // fallback when the model resource doesn't exist: a simple hostile quadcopter
            Part(PrimitiveType.Sphere, Vector3.zero, new Vector3(0.8f, 0.45f, 0.9f), new Color(0.4f, 0.1f, 0.1f), lit);
            for (int i = 0; i < 4; i++)
            {
                float x = i % 2 == 0 ? -0.6f : 0.6f, z = i < 2 ? -0.5f : 0.5f;
                Part(PrimitiveType.Cylinder, new Vector3(x, 0.1f, z), new Vector3(0.5f, 0.02f, 0.5f), new Color(0.15f, 0.15f, 0.17f), lit);
            }
        }
        // a red "eye" at the front
        Part(PrimitiveType.Sphere, new Vector3(0f, -0.1f, 0.6f), Vector3.one * 0.22f, new Color(1f, 0.15f, 0.1f), unlit);
        modelBase = model.localPosition;
    }

    static Bounds BoundsOf(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        var b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    void Part(PrimitiveType type, Vector3 pos, Vector3 scale, Color c, Material m)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(model, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = m;
        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_BaseColor", c);
        r.SetPropertyBlock(mpb);
    }

    // ------------------------------------------------------------------ brain

    Vector3 Eye => transform.position + Vector3.up * hover;

    void OnHit(float amount, GameObject source)
    {
        if (source == null) return;
        var h = source.GetComponentInParent<Health>();
        // shot by someone while it has no target: turn on the shooter (if it is in reach)
        if (h != null && h.team == Team.Player && (Target == null || !Valid(Target)) && Valid(h, loseRange)) { Target = h; Current = State.Aggro; }
    }

    void OnDied()
    {
        Target = null;
    }

    bool Valid(Health t) => Valid(t, loseRange);

    bool Valid(Health t, float range)
    {
        if (t == null || !t.Targetable) return false;
        var ph = PlayerHealth.Instance;
        if (ph != null && t == ph.Health && PlayerHealth.KnockedOut) return false;
        Vector3 p = t.transform.position;
        Vector3 d = p - transform.position; d.y = 0f;
        if (d.sqrMagnitude > range * range) return false;
        Vector3 h = p - home; h.y = 0f;
        return h.sqrMagnitude <= leashRadius * leashRadius;
    }

    Health PickTarget()
    {
        return Targeting.FindNearest(Team.Player, transform.position, detectRange, t => Valid(t, detectRange), false, false);
    }

    void Update()
    {
        if (Health.IsDead) return;
        float dt = Time.deltaTime;
        fireT -= dt;
        scanT -= dt;

        // target upkeep: drop an invalid one, look for the next nearest
        if (Target != null && !Valid(Target)) Target = null;
        if (scanT <= 0f)
        {
            scanT = 0.4f;
            if (Target == null) Target = PickTarget();
        }
        if (Target != null) Current = State.Aggro;
        else if (Current == State.Aggro) Current = State.Return;   // nothing left: back to the post

        switch (Current)
        {
            case State.Patrol: Patrol(dt); break;
            case State.Return:
                if (Fly(home, patrolSpeed * 2f, 0.6f, dt) == false) { Current = State.Patrol; patrolWait = 1f; }
                break;
            case State.Aggro: Aggro(dt); break;
        }
        Animate(dt);
    }

    void Patrol(float dt)
    {
        if (patrolWait > 0f)
        {
            patrolWait -= dt;
            if (patrolWait <= 0f)
            {
                Vector2 r = Random.insideUnitCircle * homeRadius;
                patrolTarget = home + new Vector3(r.x, 0f, r.y);
            }
            return;
        }
        if (!Fly(patrolTarget, patrolSpeed, 0.3f, dt)) patrolWait = Random.Range(1.5f, 4f);
    }

    void Aggro(float dt)
    {
        Vector3 tp = Target.transform.position;
        Vector3 to = tp - transform.position; to.y = 0f;
        float dist = to.magnitude;

        // keep about 70 % of weapon range away, never outside the leash circle around home
        float stand = attackRange * 0.7f;
        Vector3 want = dist > stand ? tp - to / Mathf.Max(0.01f, dist) * stand : transform.position;
        Vector3 off = want - home; off.y = 0f;
        if (off.magnitude > leashRadius) want = home + off.normalized * leashRadius;
        Fly(want, chaseSpeed, 0.3f, dt, faceMove: false);
        Face(to, dt);

        losT -= dt;
        if (losT <= 0f) { losT = 0.25f; hasLos = Targeting.HasLineOfSight(Eye, Target); }
        Vector3 fwd = transform.forward; fwd.y = 0f;
        if (dist > attackRange || !hasLos || fireT > 0f) return;
        if (to.sqrMagnitude > 0.01f && Vector3.Angle(fwd, to) > aimCone) return;

        fireT = 1f / Mathf.Max(0.05f, fireRate);
        spec.damage = damage; spec.speed = shotSpeed; spec.color = shotColor;
        Vector3 muzzle = model != null ? model.position + transform.forward * 0.6f : Eye;
        Projectile.Fire(muzzle, Target, spec, Team.Enemy, gameObject);
        recoil = 1f;
    }

    /// Moves on the ground plane (the model hovers). Returns true while still travelling.
    bool Fly(Vector3 target, float speed, float stop, float dt, bool faceMove = true)
    {
        Vector3 d = target - transform.position; d.y = 0f;
        float dist = d.magnitude;
        if (dist <= stop) return false;
        if (faceMove) Face(d, dt);
        Vector3 p = transform.position + d / dist * Mathf.Min(dist, speed * dt);
        p.y = GroundUtil.Height(p, p.y);
        transform.position = p;
        return true;
    }

    void Face(Vector3 dir, float dt)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 240f * dt);
    }

    void Animate(float dt)
    {
        if (model == null) return;
        dt = Mathf.Max(1e-4f, dt);
        Health.aimHeight = hover;
        Vector3 v = (transform.position - lastPos) / dt;
        lastPos = transform.position;
        smoothVel = Vector3.Lerp(smoothVel, v, dt * 6f);
        Vector3 local = transform.InverseTransformDirection(smoothVel);
        float k = tiltAmount / Mathf.Max(0.5f, chaseSpeed);
        float pitch = Mathf.Clamp(local.z * k, -tiltAmount, tiltAmount);
        float roll = Mathf.Clamp(-local.x * k, -tiltAmount, tiltAmount);
        recoil = Mathf.MoveTowards(recoil, 0f, dt * 6f);
        float bob = Mathf.Sin((Time.time + phase) * 0.8f * Mathf.PI * 2f) * bobAmplitude;
        model.localPosition = modelBase + new Vector3(0f, hover + bob, -0.12f * recoil);
        model.localRotation = Quaternion.Euler(pitch - 6f * recoil, 0f, roll);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red; Gizmos.DrawWireSphere(home, homeRadius);
        Gizmos.color = new Color(1f, 0.5f, 0f); Gizmos.DrawWireSphere(home, leashRadius);
        Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(transform.position, detectRange);
    }
}
