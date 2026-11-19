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

/// A robot unit (C3: shared base of gatherer bots and combat drones).
/// Battery + chargers, follow, program disk, level, HP + broken/repair, popup and scrap live here.
/// The gatherer is this class as-is (carrying trash); CombatDrone subclasses it and overrides the virtual hooks
/// (OnAwake, TickIdle, TickCombat, UpdateAnimation, FaceDirection, ApplyStats, SetProgram, PopupStats).
/// Without a disk it waits at its spot; with a disk it runs that program.
public class ScoutBot : MonoBehaviour
{
    public static readonly List<ScoutBot> All = new List<ScoutBot>();

    [Header("Stats (upgradeable)")]
    public float moveSpeed = 3.5f;
    public float turnSpeed = 540f;
    public int capacity = 5;
    public float pickupInterval = 0.5f;
    public float depositInterval = 0.2f;

    [Header("Power")]
    [Tooltip("Seconds of active work (moving / working) on a full battery")]
    public float batteryRuntime = 120f;
    [Tooltip("Always go charge below this fraction")]
    [Range(0f, 1f)] public float lowBatteryFraction = 0.15f;
    [Tooltip("Extra seconds kept in reserve on top of the walk to the charger")]
    public float reserveSeconds = 8f;
    [Tooltip("A dead bot gets jump-started while the player stands this close")]
    public float jumpStartRadius = 2.6f;
    [Tooltip("Battery fraction per second the player gives a dead bot")]
    public float jumpStartRate = 0.12f;
    [Tooltip("A dead bot wakes up at this charge (then walks to a charger)")]
    [Range(0f, 1f)] public float jumpStartTo = 0.25f;
    public BatteryBar batteryBar;

    [Header("Combat")]
    [Tooltip("Hit points (C1). At 0 the bot goes down in place; the player repairs it by standing next to it")]
    public float maxHP = 60f;
    [Tooltip("Height of the HP bar (just under the battery bar)")]
    public float hpBarHeight = 3.2f;

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

    [Header("Type (disk compatibility)")]
    public RobotType robotType = RobotType.Gatherer;

    public DiskProgram Program { get; private set; }
    public string BotName { get; private set; } = "Scout Bot";
    /// Tech level of the factory that built this bot (snapshot at spawn).
    public int Level { get; private set; } = 1;

    /// Materials this bot cost when it was built (snapshot at spawn, so later upgrades don't change the scrap refund).
    public int BuiltCost { get; private set; }
    /// True while the program on board came from the factory's Master Disk (not from the player's inventory).
    public bool ProgramFromMasterDisk { get; private set; }
    /// Refund for scrapping: 50 % of what it cost, rounded down.
    public int ScrapRefund => BuiltCost / 2;

    public string Status { get; set; } = "Waiting for a disk";
    public Vector3 HomePosition { get; set; }
    public int Carried => carried;
    public bool IsFull => carried >= capacity;

    // ---------- power ----------
    public float Battery01 => batteryRuntime > 0f ? Mathf.Clamp01(battery / batteryRuntime) : 1f;
    public bool IsDead => dead;
    /// Shot down to 0 HP: stands still until the player repairs it (battery untouched, disk stays in).
    public bool IsBroken => broken;
    /// Out of power or broken: not doing anything until the player helps.
    public bool IsDown => dead || broken;
    public Health Health => health;
    /// The prefab this unit was built from (set by the factory): shown in the bot popup.
    public GameObject SourcePrefab { get; set; }
    /// Moved or worked this frame (valid after the brain ran; used by visuals).
    protected bool MovedThisFrame => moving;
    protected bool WorkedThisFrame => working;
    /// Name used for "<name> #N".
    protected virtual string UnitName => "Scout Bot";
    /// Extra height for the floating PROGRAM button (drones fly higher).
    public virtual float ButtonHeight => 0f;
    /// Standing on a pad and charging right now.
    public bool IsCharging => charging != null && charging.IsCharging;
    /// Walking to / queueing at / sitting on a charger.
    public bool SeekingCharge => charging != null;
    public void AddCharge(float fraction) => battery = Mathf.Min(batteryRuntime, battery + fraction * batteryRuntime);
    // ---------- follow ----------
    /// Escort mode: trails the player instead of running its program.
    public bool Following { get; private set; }
    static readonly List<ScoutBot> followers = new List<ScoutBot>();

    public void SetFollow(bool on)
    {
        if (Following == on) return;
        Following = on;
        if (on) { if (!followers.Contains(this)) followers.Add(this); }
        else
        {
            followers.Remove(this);
            behaviour = Program != null ? Program.CreateBehaviour(this) : null;   // straight back to its job
        }
        labelInit = false;
    }

    public void SetBattery01(float f) { battery = Mathf.Clamp01(f) * batteryRuntime; if (battery <= 0f) Die(); }

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
    int shownPower = -1;
    string shownExtra;

    float battery = -1f;
    bool dead;
    Health health;
    bool broken;
    ParticleSystem brokenSmoke;
    ChargeBehaviour charging;
    float chargeCheckT;

    void Awake()
    {
        baseScale = transform.localScale;
        HomePosition = transform.position;
        battery = batteryRuntime;
        SetupHealth();
        OnAwake();
    }

    /// Subclass setup at the end of Awake (Health exists by now).
    protected virtual void OnAwake() { }

    void SetupHealth()
    {
        health = GetComponent<Health>();
        if (health == null)
        {
            health = gameObject.AddComponent<Health>();
            health.aimHeight = 1.1f;
            health.hitRadius = 0.65f;
            health.barHeight = hpBarHeight;
            health.SetMax(maxHP);
        }
        health.SetTeam(Team.Player);
        health.OnDied += Break;
    }

    void OnDestroy()
    {
        if (health != null) health.OnDied -= Break;
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable()
    {
        All.Remove(this);
        followers.Remove(this);
        ChargingStation.LeaveAll(this);
    }

    /// Called by the factory right after spawning.
    public void Init(Vector3 rallyPoint, int number)
    {
        HomePosition = rallyPoint;
        BotName = UnitName + " #" + number;
        spawnT = 0f;
        transform.localScale = Vector3.zero;
    }

    /// fromMasterDisk = the factory's Master Disk put it there (nothing drops when scrapped); the popup passes false.
    public virtual void SetProgram(DiskProgram program, bool fromMasterDisk = false)
    {
        if (program != null && !program.FitsOn(robotType))
        {
            Debug.LogWarning(program.displayName + " doesn't fit a " + robotType + " robot");
            program = null;
        }
        Program = program;
        ProgramFromMasterDisk = fromMasterDisk && program != null;
        behaviour = program != null ? program.CreateBehaviour(this) : null;
        Status = program != null ? "Starting " + program.displayName : "Waiting for a disk";
    }

    /// Called by the factory at spawn: what this bot cost (for the scrap refund).
    public void SetBuiltCost(int cost) { BuiltCost = Mathf.Max(0, cost); }

    /// Scraps the bot: refunds 50 % of its build cost, drops a player-inserted disk at its feet
    /// (a Master Disk program drops nothing), carried trash drops as loose trash (E1).
    /// Returns the refund.
    public int Scrap()
    {
        int refund = ScrapRefund;
        if (Economy.Instance != null && refund > 0) Economy.Instance.AddMaterials(refund);
        if (Program != null && !ProgramFromMasterDisk) DiskPickup.Drop(Program, transform.position);
        int carriedItems = stack.Count;
        foreach (var t in stack) if (t != null) Destroy(t.gameObject);
        stack.Clear();
        if (carriedItems > 0) LooseTrash.Drop(transform.position, carriedItems, 1.5f, 1f);   // carried trash drops on the ground (E1)
        foreach (var t in pool) if (t != null) Destroy(t.gameObject);
        pool.Clear();
        flights.Clear();
        ScrapFX.Play(transform.position);
        GameToast.Show(BotName.ToUpper() + " SCRAPPED" + (refund > 0 ? " +" + refund : ""), new Color(1f, 0.6f, 0.25f));
        Destroy(gameObject);
        return refund;
    }

    /// Called by the factory at spawn: the bot keeps the level and perks it was built with.
    public virtual void ApplyStats(StatBlock s)
    {
        if (s == null) return;
        Level = s.Level;
        moveSpeed = s[StatType.MoveSpeed];
        capacity = s.Int(StatType.CarryCapacity);
        pickupInterval = 1f / Mathf.Max(0.01f, s[StatType.CollectRate]);
        depositInterval = 1f / Mathf.Max(0.01f, s[StatType.UnloadRate]);
        batteryRuntime = Mathf.Max(1f, s[StatType.BatteryRuntime]);
        battery = batteryRuntime;   // new bots roll out fully charged
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

        TickBrain(dt);
        TickCombat(dt);
        DrainBattery(dt);

        Separate(dt);
        UpdateFlights(dt);
        UpdateAnimation();
        UpdateLabel();
    }

    // ---------- brain + power ----------

    void TickBrain(float dt)
    {
        if (broken) { TickBroken(dt); return; }
        if (dead) { TickDead(dt); return; }

        if (charging != null)
        {
            charging.Tick(dt);
            if (charging.Done)
            {
                charging = null;
                // start the program fresh (it re-checks piles / goes home if full)
                behaviour = Program != null ? Program.CreateBehaviour(this) : null;
            }
            return;
        }

        if (TickSquad(dt)) return;   // C4: a drone in a formation follows its squad leader

        if (Following)
        {
            if (NeedsCharge(dt)) { charging = new ChargeBehaviour(this); charging.Tick(dt); return; }
            TickFollow(dt);
            return;
        }

        if (behaviour != null)
        {
            if (NeedsCharge(dt))
            {
                charging = new ChargeBehaviour(this);
                charging.Tick(dt);
                return;
            }
            behaviour.Tick(dt);
            return;
        }

        TickIdle(dt);
    }

    /// Squad hook (drones, C4): return true when the squad moved the robot this frame (the normal brain is skipped).
    protected virtual bool TickSquad(float dt) => false;

    /// Starts the walk to a charger if the battery needs it (for brains that skip the normal one).
    protected bool TryStartCharge(float dt)
    {
        if (!NeedsCharge(dt)) return false;
        charging = new ChargeBehaviour(this);
        charging.Tick(dt);
        return true;
    }

    /// Starts the disk program fresh (e.g. after leaving a squad).
    public void RestartProgram() { behaviour = Program != null ? Program.CreateBehaviour(this) : null; }

    /// Optional label text instead of the program name (e.g. "SQUAD"); null = the normal label.
    protected virtual string ExtraLabel(out Color color) { color = Color.white; return null; }

    /// No disk (and not following): wait at the home spot.
    protected virtual void TickIdle(float dt)
    {
        Status = "Waiting for a disk";
        if (MoveTowards(HomePosition, 0.05f)) FaceDirection(Vector3.back);
    }

    /// Runs every frame after the brain (weapons). Gatherers have none.
    protected virtual void TickCombat(float dt) { }

    /// Text for the bot popup's stats box.
    public virtual string PopupStats()
    {
        return "Move  " + moveSpeed.ToString("0.0") + " m/s\n" +
               "Carry  " + Carried + " / " + capacity + "\n" +
               "Collect  " + (1f / Mathf.Max(0.01f, pickupInterval)).ToString("0.0") + "/s\n" +
               "Full charge  " + Stats.Clock(batteryRuntime);
    }

    [Header("Follow")]
    public float followDistance = 2.4f;
    public float followSpacing = 1.7f;
    [Tooltip("Speed multiplier while catching up with the player")]
    public float followCatchUp = 1.6f;
    float speedMul = 1f;

    // V-ish formation behind the player, 3 per row
    void TickFollow(float dt)
    {
        Status = "Following you";
        var p = PlayerInventory.Instance != null ? PlayerInventory.Instance.transform : null;
        if (p == null) return;
        int i = Mathf.Max(0, followers.IndexOf(this));
        int row = i / 3, col = i % 3;
        Vector3 back = -p.forward; back.y = 0f;
        if (back.sqrMagnitude < 0.01f) back = Vector3.back;
        back.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, back);
        float lateral = (col == 0 ? 0f : col == 1 ? -1f : 1f) * followSpacing;
        Vector3 slot = p.position + back * (followDistance + row * followSpacing + Mathf.Abs(lateral) * 0.35f) + side * lateral;

        Vector3 d = slot - transform.position; d.y = 0f;
        float dist = d.magnitude;
        if (dist > 0.5f)
        {
            speedMul = dist > 4f ? followCatchUp : 1f;
            MoveTowards(slot, 0.2f);
            speedMul = 1f;
        }
        else FaceDirection(p.forward);
    }

    /// Head back when the remaining runtime only just covers the walk to the nearest charger.
    bool NeedsCharge(float dt)
    {
        chargeCheckT -= dt;
        if (chargeCheckT > 0f) return false;
        chargeCheckT = 0.5f;
        var st = ChargingStation.Nearest(transform.position);
        if (st == null) return false;
        Vector3 d = st.transform.position - transform.position; d.y = 0f;
        float walk = d.magnitude / Mathf.Max(0.1f, moveSpeed);
        return battery <= walk * 1.3f + reserveSeconds || Battery01 <= lowBatteryFraction;
    }

    // energy is only used while moving or working
    void DrainBattery(float dt)
    {
        if (dead || broken || IsCharging) return;
        if (moving || working) battery = Mathf.Max(0f, battery - dt);
        if (battery <= 0f) Die();
    }

    void Die()
    {
        battery = 0f;
        dead = true;
        if (charging != null) { charging.Cancel(); charging = null; }
        ChargingStation.LeaveAll(this);
        Status = "Out of power";
    }

    // shut down in place; the player can jump-start it by standing next to it
    void TickDead(float dt)
    {
        var player = PlayerInventory.Instance != null ? PlayerInventory.Instance.transform : null;
        bool near = false;
        if (player != null)
        {
            Vector3 d = player.position - transform.position; d.y = 0f;
            near = d.sqrMagnitude <= jumpStartRadius * jumpStartRadius;
        }
        if (!near) { Status = "Out of power. Stand next to it to jump-start"; return; }

        battery = Mathf.Min(batteryRuntime, battery + jumpStartRate * batteryRuntime * dt);
        Status = "Jump-starting " + Mathf.RoundToInt(Battery01 * 100f) + "%";
        if (Battery01 >= jumpStartTo)
        {
            dead = false;
            chargeCheckT = 0f;
            if (Program != null) behaviour = Program.CreateBehaviour(this);
            // only a little juice: walk straight to a charger
            if (ChargingStation.Nearest(transform.position) != null) charging = new ChargeBehaviour(this);
        }
    }

    // ---------- combat: broken + repair ----------

    void Break()
    {
        if (broken) return;
        broken = true;
        health.targetable = false;   // down bots aren't targeted
        if (charging != null) { charging.Cancel(); charging = null; }
        ChargingStation.LeaveAll(this);
        Status = "Broken. Stand next to it to repair";
        CombatFX.Explode(transform.position, 0.55f, new Color(1f, 0.55f, 0.25f));
        if (brokenSmoke == null) brokenSmoke = CombatFX.BrokenSmoke(transform, 1.6f);
        labelInit = false;
    }

    /// Seconds a full repair takes: the same as a jump-start (0 -> jumpStartTo at jumpStartRate).
    public float RepairTime => jumpStartTo / Mathf.Max(0.01f, jumpStartRate);

    // shot down; the player repairs it to full HP by standing next to it (same flow and timing as the jump-start)
    void TickBroken(float dt)
    {
        var player = PlayerInventory.Instance != null ? PlayerInventory.Instance.transform : null;
        bool near = false;
        if (player != null && !PlayerHealth.KnockedOut)
        {
            Vector3 d = player.position - transform.position; d.y = 0f;
            near = d.sqrMagnitude <= jumpStartRadius * jumpStartRadius;
        }
        if (!near) { Status = "Broken. Stand next to it to repair"; return; }

        health.Heal(health.maxHP / RepairTime * dt);
        Status = "Repairing " + Mathf.RoundToInt(health.Fraction * 100f) + "%";
        if (health.IsFull) Repair();
    }

    /// Back on its feet at full HP (also used by debug).
    public void Repair()
    {
        if (!broken) { health.HealFull(); return; }
        broken = false;
        health.HealFull();
        health.targetable = true;
        if (brokenSmoke != null)
        {
            brokenSmoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(brokenSmoke.gameObject, 2.5f);
            brokenSmoke = null;
        }
        chargeCheckT = 0f;
        if (!dead && Program != null) behaviour = Program.CreateBehaviour(this);
        Status = dead ? "Out of power" : "Repaired";
        labelInit = false;
    }

    [ContextMenu("Dev: battery to 20%")]
    void DevBattery20() => SetBattery01(0.2f);

    [ContextMenu("Dev: battery empty")]
    void DevBatteryEmpty() => SetBattery01(0f);

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
        float step = Mathf.Min(moveSpeed * speedMul * Time.deltaTime, dist - stopDistance);
        pos += dir * step;
        pos.y = GroundUtil.Height(pos, pos.y);
        transform.position = pos;
        moving = true;
        return false;
    }

    public virtual void FaceDirection(Vector3 dir)
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
            if (o == this || o.robotType != robotType) continue;   // drones fly over gatherers
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

    public bool TryPickLoose(LooseTrash loose)
    {
        if (IsFull || loose == null || !loose.TryTake(out var from)) return false;
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

    protected virtual void UpdateAnimation()
    {
        if (animator == null) return;
        float spd = dead || broken ? 0f : 1f;   // frozen when out of power / broken
        if (animator.speed != spd) animator.speed = spd;
        int s = moving ? 1 : working ? 2 : 0;
        if (s == animState) return;
        animState = s;
        animator.CrossFadeInFixedTime(s == 1 ? runState : s == 2 ? workState : idleState, 0.15f);
    }

    void UpdateLabel()
    {
        if (statusLabel == null) return;
        int power = broken ? 4 : dead ? 2 : IsCharging ? 1 : Following ? 3 : 0;
        string extra = ExtraLabel(out Color extraColor);
        if (labelInit && shownCarried == carried && shownProgram == Program && shownPower == power && shownExtra == extra) return;
        labelInit = true; shownCarried = carried; shownProgram = Program; shownPower = power; shownExtra = extra;
        if (power == 4)
        {
            statusLabel.text = "BROKEN";
            statusLabel.color = new Color(1f, 0.32f, 0.25f);
        }
        else if (power == 2)
        {
            statusLabel.text = "NO POWER";
            statusLabel.color = new Color(1f, 0.32f, 0.25f);
        }
        else if (power == 1)
        {
            statusLabel.text = "CHARGING";
            statusLabel.color = new Color(0.45f, 0.95f, 1f);
        }
        else if (extra != null)
        {
            statusLabel.text = extra;
            statusLabel.color = extraColor;
        }
        else if (power == 3)
        {
            statusLabel.text = "FOLLOW";
            statusLabel.color = new Color(0.45f, 0.95f, 1f);
        }
        else if (Program == null)
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
