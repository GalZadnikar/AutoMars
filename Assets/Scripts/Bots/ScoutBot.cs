using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// Base class for what a disk makes a bot do. Add new programs by subclassing this
/// and creating a matching DiskProgram asset.
public abstract class BotBehaviour
{
    protected readonly ScoutBot bot;
    protected BotBehaviour(ScoutBot bot) { this.bot = bot; }
    public abstract void Tick(float dt);
}

/// A worker bot. Without a disk it waits at its spot; with a disk it runs that program.
public class ScoutBot : MonoBehaviour
{
    public static readonly List<ScoutBot> All = new List<ScoutBot>();

    [Header("Stats (upgradeable)")]
    public float moveSpeed = 3.5f;
    public float turnSpeed = 540f;
    public int capacity = 5;
    public float pickupInterval = 0.5f;
    public float depositInterval = 0.2f;

    [Header("References")]
    public Animator animator;
    public Transform carryAnchor;
    public GameObject carryItemPrefab;
    public float carryItemScale = 0.6f;
    public float carrySpacing = 0.24f;
    public TMP_Text statusLabel;
    [Tooltip("[Lv N] tag, shown only near the player (ProximityTags)")]
    public BotLevelTag levelTag;

    [Header("Animator states")]
    public string idleState = "01_Scout_Bot_Idle";
    public string runState = "01_Scout_Bot_Run";
    public string workState = "01_Scout_Bot_Attack";

    public DiskProgram Program { get; private set; }
    public string BotName { get; private set; } = "Scout Bot";
    /// Tech level of the factory that built this bot (snapshot at spawn).
    public int Level { get; private set; } = 1;

    public string Status { get; set; } = "Waiting for a disk";
    public Vector3 HomePosition { get; set; }
    public int Carried => carried;
    public bool IsFull => carried >= capacity;

    struct Flight
    {
        public Transform item;
        public Vector3 from;
        public float t;
        public int slot;
        public HomeBase home;
    }

    BotBehaviour behaviour;
    readonly List<Transform> stack = new List<Transform>();
    readonly Stack<Transform> pool = new Stack<Transform>();
    readonly List<Flight> flights = new List<Flight>();
    int carried;
    bool moving, working;
    int animState = -1;
    float spawnT = 1f;
    Vector3 baseScale;
    int shownCarried = -1;
    DiskProgram shownProgram;
    bool labelInit;

    void Awake()
    {
        baseScale = transform.localScale;
        HomePosition = transform.position;
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    /// Called by the factory right after spawning.
    public void Init(Vector3 rallyPoint, int number)
    {
        HomePosition = rallyPoint;
        BotName = "Scout Bot #" + number;
        spawnT = 0f;
        transform.localScale = Vector3.zero;
    }

    public void SetProgram(DiskProgram program)
    {
        Program = program;
        behaviour = program != null ? program.CreateBehaviour(this) : null;
        Status = program != null ? "Starting " + program.displayName : "Waiting for a disk";
    }

    /// Called by the factory at spawn: the bot keeps the level and perks it was built with.
    public void ApplyStats(StatBlock s)
    {
        if (s == null) return;
        Level = s.Level;
        moveSpeed = s[StatType.MoveSpeed];
        capacity = s.Int(StatType.CarryCapacity);
        pickupInterval = 1f / Mathf.Max(0.01f, s[StatType.CollectRate]);
        depositInterval = 1f / Mathf.Max(0.01f, s[StatType.UnloadRate]);
        labelInit = false;
    }


    void Update()
    {
        float dt = Time.deltaTime;
        moving = false; working = false;

        if (spawnT < 1f)
        {
            spawnT = Mathf.Min(1f, spawnT + dt * 2f);
            transform.localScale = baseScale * EaseOutBack(spawnT);
        }

        if (behaviour != null) behaviour.Tick(dt);
        else
        {
            Status = "Waiting for a disk";
            if (MoveTowards(HomePosition, 0.05f)) FaceDirection(Vector3.back);
        }

        Separate(dt);
        UpdateFlights(dt);
        UpdateAnimation();
        UpdateLabel();
    }

    // ---------- movement ----------

    /// Walks toward target on the ground. Returns true once within stopDistance.
    public bool MoveTowards(Vector3 target, float stopDistance)
    {
        Vector3 pos = transform.position;
        Vector3 d = target - pos; d.y = 0f;
        float dist = d.magnitude;
        if (dist <= stopDistance + 0.05f) return true;
        Vector3 dir = d / dist;
        FaceDirection(dir);
        float step = Mathf.Min(moveSpeed * Time.deltaTime, dist - stopDistance);
        pos += dir * step;
        pos.y = GroundUtil.Height(pos, pos.y);
        transform.position = pos;
        moving = true;
        return false;
    }

    public void FaceDirection(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), turnSpeed * Time.deltaTime);
    }

    public void FaceTowards(Vector3 point) => FaceDirection(point - transform.position);
    public void MarkWorking() => working = true;

    // keeps bots from standing inside each other
    void Separate(float dt)
    {
        Vector3 push = Vector3.zero;
        Vector3 me = transform.position;
        foreach (var o in All)
        {
            if (o == this) continue;
            Vector3 d = me - o.transform.position; d.y = 0f;
            float m = d.sqrMagnitude;
            if (m < 1f && m > 1e-4f) push += d.normalized * (1f - Mathf.Sqrt(m));
        }
        if (push.sqrMagnitude < 1e-6f) return;
        Vector3 p = me + push * dt * 3f;
        p.y = GroundUtil.Height(p, p.y);
        transform.position = p;
    }

    // ---------- carrying ----------

    public bool TryPickFrom(TrashPile pile)
    {
        if (IsFull || pile == null) return false;
        if (!pile.TryTake(out var from)) return false;
        var item = Get();
        item.SetParent(null, false);
        item.position = from;
        int slot = stack.Count;
        stack.Add(item);
        flights.Add(new Flight { item = item, from = from, slot = slot });
        carried++;
        return true;
    }

    public bool TryUnloadTo(HomeBase home)
    {
        if (stack.Count == 0 || home == null) return false;
        var item = stack[stack.Count - 1];
        stack.RemoveAt(stack.Count - 1);
        for (int i = flights.Count - 1; i >= 0; i--) if (flights[i].item == item) flights.RemoveAt(i);
        item.SetParent(null, true);
        flights.Add(new Flight { item = item, from = item.position, home = home });
        carried--;
        return true;
    }

    void UpdateFlights(float dt)
    {
        for (int i = flights.Count - 1; i >= 0; i--)
        {
            var f = flights[i];
            f.t = Mathf.Min(1f, f.t + dt / 0.4f);
            Vector3 target = f.home != null ? f.home.IntakePoint
                                             : carryAnchor.TransformPoint(new Vector3(0f, f.slot * carrySpacing, 0f));
            float e = f.t * f.t * (3f - 2f * f.t);
            f.item.position = Vector3.Lerp(f.from, target, e) + Vector3.up * Mathf.Sin(f.t * Mathf.PI) * 1f;
            f.item.localScale = Vector3.one * carryItemScale * (f.home != null ? Mathf.Lerp(1f, 0.4f, e) : 1f);
            if (f.home == null) f.item.rotation = Quaternion.Slerp(f.item.rotation, carryAnchor.rotation, e);

            if (f.t >= 1f)
            {
                if (f.home != null) { Release(f.item); f.home.Receive(1); }
                else
                {
                    f.item.SetParent(carryAnchor, true);
                    f.item.localPosition = new Vector3(0f, f.slot * carrySpacing, 0f);
                    f.item.localRotation = Quaternion.Euler(0f, (f.slot * 67f) % 50f - 25f, 0f);
                }
                flights.RemoveAt(i);
            }
            else flights[i] = f;
        }
    }

    Transform Get()
    {
        Transform t = pool.Count > 0 ? pool.Pop() : Instantiate(carryItemPrefab).transform;
        t.gameObject.SetActive(true);
        t.localScale = Vector3.one * carryItemScale;
        return t;
    }

    void Release(Transform t)
    {
        t.gameObject.SetActive(false);
        t.SetParent(transform, false);
        pool.Push(t);
    }

    // ---------- visuals ----------

    void UpdateAnimation()
    {
        if (animator == null) return;
        int s = moving ? 1 : working ? 2 : 0;
        if (s == animState) return;
        animState = s;
        animator.CrossFadeInFixedTime(s == 1 ? runState : s == 2 ? workState : idleState, 0.15f);
    }

    void UpdateLabel()
    {
        if (statusLabel == null) return;
        if (labelInit && shownCarried == carried && shownProgram == Program) return;
        labelInit = true; shownCarried = carried; shownProgram = Program;
        if (Program == null)
        {
            statusLabel.text = "NO DISK";
            statusLabel.color = new Color(1f, 0.55f, 0.45f);
        }
        else
        {
            statusLabel.text = carried > 0 ? carried + "/" + capacity : Program.shortName;
            statusLabel.color = IsFull ? new Color(1f, 0.4f, 0.35f) : Program.color;
        }
    }

    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }
}
