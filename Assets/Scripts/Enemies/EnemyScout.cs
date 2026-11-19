using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// Enemy Scout (E1): a small thief. Wanders, goes to the nearest trash pile and steals from it (up to `capacity`),
/// then idles near where it is. It never attacks: when the player or a combat drone comes close it runs away.
/// Killed (HP 0): explosion and everything it carries drops as LooseTrash.
/// Spawn with EnemyScout.Spawn(pos) or with an EnemySpawner. Built in code, no prefab or builder needed.
[RequireComponent(typeof(Health))]
public class EnemyScout : MonoBehaviour
{
    public static readonly List<EnemyScout> All = new List<EnemyScout>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { All.Clear(); lit = null; }

    [Header("Stealing")]
    public int capacity = 10;
    [Tooltip("Seconds per stolen item")]
    public float stealInterval = 0.6f;

    [Header("Movement")]
    public float moveSpeed = 3f;
    public float fleeSpeed = 5.5f;
    [Tooltip("Wander radius around its anchor (spawn point, or where it filled up)")]
    public float wanderRadius = 6f;

    [Header("Fear")]
    [Tooltip("The player or a combat drone this close makes it run")]
    public float fleeRange = 8f;
    [Tooltip("It keeps running until the threat is this far away")]
    public float calmRange = 12f;

    public enum State { Wander, ToPile, Stealing, Idle, Flee }
    public State Current { get; private set; } = State.Wander;
    public int Carried => carried;
    public bool IsFull => carried >= capacity;
    public Health Health { get; private set; }

    int carried;
    Vector3 anchor, wanderTarget;
    float wanderWait, stealT, scanT, bob;
    bool leaving;   // walking away from a pile it just robbed (full speed, straight out)
    TrashPile pile;
    Transform stackRoot;
    readonly List<Transform> stackItems = new List<Transform>();
    Transform body;
    Animator anim;
    int animState = -1;
    const string ModelResource = "Enemies/EnemyScoutModel";
    static Material lit;

    /// Builds a scout on the ground at `pos`.
    public static EnemyScout Spawn(Vector3 pos)
    {
        var go = new GameObject("Enemy Scout");
        pos.y = GroundUtil.Height(pos, pos.y);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        var h = go.AddComponent<Health>();
        h.SetTeam(Team.Enemy);   // AddComponent already registered it with the default team
        h.SetMax(30f);
        h.aimHeight = 0.55f;
        h.hitRadius = 0.6f;
        h.barHeight = 1.7f;
        h.barWidth = 1f;
        var dfx = go.AddComponent<DeathFX>();
        dfx.size = 0.9f;
        dfx.color = new Color(1f, 0.4f, 0.25f);
        var s = go.AddComponent<EnemyScout>();
        s.Build();
        return s;
    }

    void Awake()
    {
        Health = GetComponent<Health>();
        Health.OnDied += OnDied;
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    IEnumerator Start()
    {
        anchor = transform.position;
        wanderWait = Random.Range(0.5f, 2f);   // a short wander first
        yield return null;   // after Health created its bar, so FogHideable hides that too
        var fh = gameObject.AddComponent<FogHideable>();
        fh.hide = FogHideable.Mode.WhenNotVisible;
    }

    void OnDied()
    {
        // everything it stole drops (the explosion comes from DeathFX)
        if (carried > 0) LooseTrash.Drop(transform.position, carried, 1.6f, 0.6f);
        carried = 0;
    }

    // ------------------------------------------------------------------ visuals

    void Build()
    {
        if (lit == null) lit = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        body = new GameObject("Body").transform;
        body.SetParent(transform, false);
        var src = Resources.Load<GameObject>(ModelResource);   // the player's Scout Bot mesh, made by Tools > Trash Game > 13. Enemies
        if (src != null)
        {
            var mesh = Instantiate(src, body);
            mesh.transform.localPosition = Vector3.zero;
            mesh.transform.localRotation = Quaternion.identity;
            foreach (var c in mesh.GetComponentsInChildren<Collider>()) Destroy(c);
            anim = mesh.GetComponentInChildren<Animator>();
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", new Color(1f, 0.3f, 0.25f));   // red = hostile
            foreach (var r in mesh.GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(mpb);
        }
        else
        {
            // fallback when the model resource doesn't exist: a simple wheeled body
            Part(body, PrimitiveType.Cube, new Vector3(0f, 0.45f, 0f), new Vector3(0.7f, 0.4f, 0.95f), new Color(0.55f, 0.18f, 0.16f));
            Part(body, PrimitiveType.Cube, new Vector3(0f, 0.78f, 0.28f), new Vector3(0.45f, 0.3f, 0.4f), new Color(0.3f, 0.3f, 0.33f));
            Part(body, PrimitiveType.Sphere, new Vector3(0.12f, 0.8f, 0.5f), Vector3.one * 0.11f, new Color(1f, 0.85f, 0.2f));
            Part(body, PrimitiveType.Sphere, new Vector3(-0.12f, 0.8f, 0.5f), Vector3.one * 0.11f, new Color(1f, 0.85f, 0.2f));
            for (int i = 0; i < 4; i++)
            {
                float x = i % 2 == 0 ? -0.42f : 0.42f, z = i < 2 ? -0.3f : 0.3f;
                var w = Part(body, PrimitiveType.Cylinder, new Vector3(x, 0.2f, z), new Vector3(0.36f, 0.08f, 0.36f), new Color(0.12f, 0.12f, 0.13f));
                w.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
        }
        // the stolen stack on its back: grows with `carried`
        stackRoot = new GameObject("Stolen").transform;
        stackRoot.SetParent(body, false);
        stackRoot.localPosition = anim != null ? new Vector3(0f, 0.9f, -0.35f) : new Vector3(0f, 0.68f, -0.22f);
        for (int i = 0; i < capacity; i++)
        {
            var it = Part(stackRoot, PrimitiveType.Cube, new Vector3(0f, i * 0.17f, 0f), new Vector3(0.34f, 0.15f, 0.34f),
                          new Color(0.45f, 0.4f, 0.33f) * Random.Range(0.8f, 1.15f));
            it.localRotation = Quaternion.Euler(0f, (i * 67f) % 50f - 25f, 0f);
            it.gameObject.SetActive(false);
            stackItems.Add(it);
        }
    }

    Transform Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Color c)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = lit;
        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_BaseColor", c);
        r.SetPropertyBlock(mpb);
        return go.transform;
    }

    void ShowStack()
    {
        for (int i = 0; i < stackItems.Count; i++) stackItems[i].gameObject.SetActive(i < carried);
    }

    // ------------------------------------------------------------------ brain

    void Update()
    {
        if (Health.IsDead) return;
        float dt = Time.deltaTime;

        // fear first: the player or a combat drone close by
        Vector3 threat;
        bool scared = FindThreat(Current == State.Flee ? calmRange : fleeRange, out threat);
        if (scared) { Current = State.Flee; pile = null; leaving = false; }
        else if (Current == State.Flee) { Current = State.Wander; anchor = transform.position; wanderWait = 1f; }

        bool moved = false;
        switch (Current)
        {
            case State.Flee:
                moved = Flee(threat, dt);
                break;

            case State.Wander:
                moved = WanderStep(dt);
                scanT -= dt;
                if (!IsFull && scanT <= 0f)
                {
                    scanT = 1f;
                    pile = NearestPile();
                    if (pile != null) Current = State.ToPile;
                }
                break;

            case State.ToPile:
                if (pile == null || pile.IsEmpty) { Current = State.Wander; wanderWait = 0.5f; break; }
                Vector3 spot = pile.transform.position;
                Vector3 dir = transform.position - spot; dir.y = 0f;
                if (dir.sqrMagnitude < 0.01f) dir = Vector3.back;
                spot += dir.normalized * Mathf.Min(pile.pickupRadius * 0.55f, 2.2f);
                moved = MoveTo(spot, moveSpeed, 0.2f, dt);
                if (!moved && (spot - transform.position).sqrMagnitude < 0.5f) { Current = State.Stealing; stealT = stealInterval; }
                break;

            case State.Stealing:
                if (pile == null || pile.IsEmpty || IsFull)
                {
                    // walk off the pile first, then wander/idle around that new spot (not over the pile)
                    anchor = pile != null ? AwayFrom(pile) : transform.position;
                    pile = null;
                    Current = IsFull || carried > 0 && NearestPile() == null ? State.Idle : State.Wander;
                    wanderTarget = anchor;
                    wanderWait = 0f;
                    leaving = true;
                    break;
                }
                Face(pile.transform.position - transform.position, dt);
                stealT -= dt;
                if (stealT <= 0f)
                {
                    stealT = stealInterval;
                    if (pile.TryTake(out var _)) { carried++; ShowStack(); }
                }
                break;

            case State.Idle:
                moved = WanderStep(dt, leaving ? 1f : 0.6f);
                break;
        }

        // little bounce while rolling
        bob = Mathf.MoveTowards(bob, moved ? 1f : 0f, dt * 5f);
        SetAnim(moved ? 1 : Current == State.Stealing ? 2 : 0);
        if (body != null && anim == null)
            body.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Sin(Time.time * (Current == State.Flee ? 16f : 9f))) * 0.06f * bob, 0f);
    }

    // same animator states as the player's Scout Bot: 0 idle, 1 run, 2 work
    void SetAnim(int st)
    {
        if (anim == null || st == animState) return;
        animState = st;
        anim.speed = st == 1 && Current == State.Flee ? 1.5f : 1f;
        anim.CrossFadeInFixedTime(st == 1 ? "01_Scout_Bot_Run" : st == 2 ? "01_Scout_Bot_Attack" : "01_Scout_Bot_Idle", 0.15f);
    }

    bool FindThreat(float range, out Vector3 pos)
    {
        pos = default;
        float best = range * range;
        bool found = false;
        var inv = PlayerInventory.Instance;
        if (inv != null && !PlayerHealth.KnockedOut)
        {
            Vector3 d = inv.transform.position - transform.position; d.y = 0f;
            if (d.sqrMagnitude < best) { best = d.sqrMagnitude; pos = inv.transform.position; found = true; }
        }
        foreach (var b in ScoutBot.All)
        {
            if (b == null || !(b is CombatDrone) || b.IsDown) continue;
            Vector3 d = b.transform.position - transform.position; d.y = 0f;
            if (d.sqrMagnitude < best) { best = d.sqrMagnitude; pos = b.transform.position; found = true; }
        }
        return found;
    }

    TrashPile NearestPile()
    {
        TrashPile result = null;
        float best = float.MaxValue;
        foreach (var p in TrashPile.All)
        {
            if (p.IsEmpty) continue;
            float d = (p.transform.position - transform.position).sqrMagnitude;
            if (d < best) { best = d; result = p; }
        }
        return result;
    }

    bool WanderStep(float dt, float speedFactor = 1f)
    {
        if (wanderWait > 0f)
        {
            wanderWait -= dt;
            if (wanderWait <= 0f) wanderTarget = PickWanderTarget();
            return false;
        }
        bool moving = MoveTo(wanderTarget, moveSpeed * speedFactor, 0.2f, dt);
        if (!moving) { wanderWait = Random.Range(1.5f, 4f); leaving = false; }
        return moving;
    }

    /// A random point around the anchor that is not on top of a trash pile (a few tries, then the anchor itself).
    Vector3 PickWanderTarget()
    {
        for (int i = 0; i < 8; i++)
        {
            Vector2 r = Random.insideUnitCircle * wanderRadius;
            Vector3 t = ClampToMap(anchor + new Vector3(r.x, 0f, r.y));
            if (!OnAPile(t)) return t;
        }
        return anchor;
    }

    static bool OnAPile(Vector3 p, float margin = 1.5f)
    {
        foreach (var pl in TrashPile.All)
        {
            if (pl == null) continue;
            Vector3 d = p - pl.transform.position; d.y = 0f;
            float r = pl.pickupRadius + margin;
            if (d.sqrMagnitude < r * r) return true;
        }
        return false;
    }

    /// A spot outside the pile's circle on the scout's side of it, far enough that wandering stays clear of it.
    Vector3 AwayFrom(TrashPile p)
    {
        Vector3 dir = transform.position - p.transform.position; dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) dir = -transform.forward;
        dir.y = 0f;
        dir = Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f) * dir.normalized;
        return ClampToMap(p.transform.position + dir * (p.pickupRadius + wanderRadius + 1.5f));
    }

    bool Flee(Vector3 threat, float dt)
    {
        Vector3 away = transform.position - threat; away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere;
        away.y = 0f;
        away.Normalize();
        // the map edge: slide along it instead of running into it
        Vector3 next = transform.position + away * 3f;
        if (next.x < GridMin.x || next.x > GridMax.x || next.z < GridMin.y || next.z > GridMax.y)
        {
            Vector3 side = Vector3.Cross(Vector3.up, away);
            Vector3 toCenter = -transform.position; toCenter.y = 0f;
            away = (Vector3.Dot(side, toCenter) >= 0f ? side : -side + toCenter.normalized * 0.3f).normalized;
        }
        return MoveTo(transform.position + away * 4f, fleeSpeed, 0f, dt);
    }

    // piles lie outside the build grid (up to about z = 47), so the scout's own limit is wider
    static readonly Vector2 GridMin = new Vector2(-55f, -55f);
    static readonly Vector2 GridMax = new Vector2(55f, 55f);

    static Vector3 ClampToMap(Vector3 p)
    {
        p.x = Mathf.Clamp(p.x, GridMin.x, GridMax.x);
        p.z = Mathf.Clamp(p.z, GridMin.y, GridMax.y);
        return p;
    }

    /// Rolls toward `target` on the ground. True while still moving.
    bool MoveTo(Vector3 target, float speed, float stop, float dt)
    {
        target = ClampToMap(target);
        Vector3 d = target - transform.position; d.y = 0f;
        float dist = d.magnitude;
        if (dist <= Mathf.Max(0.05f, stop)) return false;
        Face(d, dt);
        Vector3 p = transform.position + d / dist * Mathf.Min(dist, speed * dt);
        p.y = GroundUtil.Height(p, p.y);
        transform.position = p;
        return true;
    }

    void Face(Vector3 dir, float dt)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 540f * dt);
    }
}
