using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// Picks trash from piles into a stack on the player's back, unloads it at home.
/// capacity / pickupInterval / depositInterval are the stats to upgrade later.
public class PlayerCarrier : MonoBehaviour
{
    [Header("Upgradeable stats")]
    public int capacity = 10;
    [Tooltip("Seconds between picking up two items")]
    public float pickupInterval = 0.2f;
    [Tooltip("Seconds between dropping two items at home")]
    public float depositInterval = 0.1f;

    [Header("Stack visuals")]
    public Transform stackAnchor;
    public GameObject stackItemPrefab;
    public float itemSpacing = 0.26f;
    public float flyTime = 0.35f;
    public float arcHeight = 1.5f;
    public float swayAmount = 2.5f;
    public TMP_Text carryLabel;

    public int Carried => carried;
    public bool IsFull => carried >= capacity;

    struct Flight
    {
        public Transform item;
        public Vector3 from;
        public Quaternion fromRot;
        public float t;
        public int slot;
        public HomeBase home;   // null => flying onto the stack
    }

    readonly List<Transform> stack = new List<Transform>();
    readonly Stack<Transform> pool = new Stack<Transform>();
    readonly List<Flight> flights = new List<Flight>();

    int carried;
    float timer;
    TrashPile currentPile;
    HomeBase currentHome;
    Vector3 lastPos;
    Quaternion swayRot = Quaternion.identity;
    int shownCarried = -1;

    void Start()
    {
        lastPos = transform.position;
        UpdateLabel();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        timer -= dt;

        // --- find zones ---
        HomeBase home = null;
        foreach (var h in HomeBase.All) if (h.InRange(transform.position)) { home = h; break; }

        TrashPile pile = null;
        if (home == null)
        {
            float best = float.MaxValue;
            foreach (var p in TrashPile.All)
            {
                if (p.IsEmpty || !p.InRange(transform.position)) continue;
                float d = (p.transform.position - transform.position).sqrMagnitude;
                if (d < best) { best = d; pile = p; }
            }
        }
        if (pile != currentPile)
        {
            if (currentPile != null) currentPile.SetHighlight(false);
            if (pile != null) pile.SetHighlight(true);
            currentPile = pile;
        }
        if (home != currentHome)
        {
            if (currentHome != null) currentHome.SetHighlight(false);
            if (home != null) home.SetHighlight(true);
            currentHome = home;
        }

        // --- act ---
        if (timer <= 0f)
        {
            if (home != null && stack.Count > 0) { Deposit(home); timer = depositInterval; }
            else if (pile != null && !IsFull && pile.TryTake(out var from)) { Pickup(from); timer = pickupInterval; }
            else if (home == null && !IsFull && !PlayerHealth.KnockedOut)
            {
                // loose trash on the ground (E1): walk over it
                var loose = LooseTrash.Nearest(transform.position, 1.6f);
                if (loose != null && loose.InRange(transform.position) && loose.TryTake(out var lf)) { Pickup(lf); timer = pickupInterval; }
            }
        }

        UpdateFlights(dt);
        UpdateSway(dt);
        if (shownCarried != carried) UpdateLabel();
    }

    void Pickup(Vector3 from)
    {
        var item = Get();
        item.SetParent(null, false);
        item.position = from;
        item.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
        int slot = stack.Count;
        stack.Add(item);
        flights.Add(new Flight { item = item, from = from, fromRot = item.rotation, slot = slot });
        carried++;
    }

    void Deposit(HomeBase home)
    {
        var item = stack[stack.Count - 1];
        stack.RemoveAt(stack.Count - 1);
        for (int i = flights.Count - 1; i >= 0; i--) if (flights[i].item == item) flights.RemoveAt(i);
        item.SetParent(null, true);
        flights.Add(new Flight { item = item, from = item.position, fromRot = item.rotation, home = home });
        carried--;
    }

    /// Drops the whole trash bag (player knocked out): it lands around the player as loose trash (E1).
    /// Returns how many items were dropped.
    public int DropAll()
    {
        int n = stack.Count;
        if (n == 0) return 0;
        for (int i = flights.Count - 1; i >= 0; i--) if (flights[i].home == null) flights.RemoveAt(i);
        foreach (var t in stack) if (t != null) Release(t);
        stack.Clear();
        carried = 0;
        LooseTrash.Drop(transform.position, n, 2.2f, 2f);
        PileClearFX.Play(transform.position, 1.5f);
        UpdateLabel();
        return n;
    }

    void UpdateFlights(float dt)
    {
        for (int i = flights.Count - 1; i >= 0; i--)
        {
            var f = flights[i];
            f.t = Mathf.Min(1f, f.t + dt / flyTime);
            Vector3 target; Quaternion targetRot;
            if (f.home != null) { target = f.home.IntakePoint; targetRot = f.fromRot; }
            else
            {
                target = stackAnchor.TransformPoint(new Vector3(0, f.slot * itemSpacing, 0));
                targetRot = stackAnchor.rotation * SlotTwist(f.slot);
            }
            float e = f.t * f.t * (3f - 2f * f.t);
            f.item.position = Vector3.Lerp(f.from, target, e) + Vector3.up * Mathf.Sin(f.t * Mathf.PI) * arcHeight;
            f.item.rotation = Quaternion.Slerp(f.fromRot, targetRot, e);
            if (f.home != null) f.item.localScale = Vector3.one * Mathf.Lerp(1f, 0.4f, e);

            if (f.t >= 1f)
            {
                if (f.home != null) { Release(f.item); f.home.Receive(1); }
                else
                {
                    f.item.SetParent(stackAnchor, false);
                    f.item.localPosition = new Vector3(0, f.slot * itemSpacing, 0);
                    f.item.localRotation = SlotTwist(f.slot);
                }
                flights.RemoveAt(i);
            }
            else flights[i] = f;
        }
    }

    // Stack leans against movement, like it has weight.
    void UpdateSway(float dt)
    {
        if (stackAnchor == null || dt <= 0f) return;
        Vector3 vel = (transform.position - lastPos) / dt;
        lastPos = transform.position;
        Vector3 local = transform.InverseTransformDirection(vel);
        var target = Quaternion.Euler(Mathf.Clamp(-local.z * swayAmount, -12f, 12f), 0f,
                                      Mathf.Clamp(local.x * swayAmount, -12f, 12f));
        swayRot = Quaternion.Slerp(swayRot, target, dt * 6f);
        stackAnchor.localRotation = swayRot;
    }

    static Quaternion SlotTwist(int slot) => Quaternion.Euler(0f, (slot * 67f) % 50f - 25f, 0f);

    void UpdateLabel()
    {
        shownCarried = carried;
        if (carryLabel == null) return;
        carryLabel.gameObject.SetActive(carried > 0);
        carryLabel.text = IsFull ? "MAX" : carried + "/" + capacity;
        carryLabel.color = IsFull ? new Color(1f, 0.35f, 0.3f) : Color.white;
        if (stackAnchor != null)
        {
            // keep the label just above the top of the stack (and at least above the head)
            var a = stackAnchor.localPosition;
            float top = a.y + carried * itemSpacing + 0.8f;
            carryLabel.transform.localPosition = new Vector3(0f, Mathf.Max(3f, top), a.z * 0.5f);
        }
    }

    Transform Get()
    {
        Transform t = pool.Count > 0 ? pool.Pop() : Instantiate(stackItemPrefab).transform;
        t.gameObject.SetActive(true);
        t.localScale = Vector3.one;
        return t;
    }

    void Release(Transform t)
    {
        t.gameObject.SetActive(false);
        t.SetParent(transform, false);
        pool.Push(t);
    }
}
