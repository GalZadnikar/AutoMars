using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// Charging Station: a few pads where bots recharge, plus a waiting line.
/// A pad is reserved for a bot as soon as it's assigned (while it walks there).
/// When bots have to queue, the label turns red: the player needs more chargers.
public class ChargingStation : MonoBehaviour
{
    public static readonly List<ChargingStation> All = new List<ChargingStation>();

    [Tooltip("Battery fraction per second while on a pad (0.125 = empty to full in 8 s)")]
    public float chargePerSecond = 0.125f;
    public Transform[] pads;

    [Header("Waiting line (local space)")]
    public Vector3 queueStart = new Vector3(0f, 0f, -4.6f);
    public Vector3 queueStep = new Vector3(0f, 0f, -1.4f);

    [Header("Visuals")]
    public TMP_Text label;
    public Renderer[] padRenderers;
    public Transform core;
    public string idleText = "Charging Station";
    public Color padIdle = new Color(0.25f, 0.55f, 0.65f);
    public Color padActive = new Color(0.45f, 0.95f, 1f);

    ScoutBot[] occupant;
    readonly List<ScoutBot> queue = new List<ScoutBot>();
    MaterialPropertyBlock mpb;
    string shownText;
    Vector3 coreScale = Vector3.one;

    public int PadCount => pads != null ? pads.Length : 0;
    public int Queued => queue.Count;

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Awake()
    {
        occupant = new ScoutBot[PadCount];
        mpb = new MaterialPropertyBlock();
        if (core != null) coreScale = core.localScale;
    }

    // pads are thin discs: put them right on the (possibly leveled) ground
    void Start()
    {
        if (pads == null) return;
        foreach (var pad in pads)
        {
            if (pad == null) continue;
            var pp = pad.position;
            pp.y = GroundUtil.Height(pp, pp.y) + 0.03f;
            pad.position = pp;
        }
    }

    public static ChargingStation Nearest(Vector3 p)
    {
        ChargingStation best = null;
        float bd = float.MaxValue;
        foreach (var s in All)
        {
            Vector3 d = s.transform.position - p; d.y = 0f;
            float m = d.sqrMagnitude;
            if (m < bd) { bd = m; best = s; }
        }
        return best;
    }

    /// Ask for a pad; if all are taken the bot joins the line.
    public void Join(ScoutBot bot)
    {
        if (bot == null || PadOf(bot) >= 0 || queue.Contains(bot)) return;
        int free = FreePad();
        if (free >= 0) occupant[free] = bot;
        else queue.Add(bot);
    }

    public void Leave(ScoutBot bot)
    {
        for (int i = 0; i < occupant.Length; i++) if (occupant[i] == bot) occupant[i] = null;
        queue.Remove(bot);
        Promote();
    }

    public static void LeaveAll(ScoutBot bot)
    {
        foreach (var s in All) s.Leave(bot);
    }

    public int PadOf(ScoutBot bot)
    {
        for (int i = 0; i < occupant.Length; i++) if (occupant[i] == bot) return i;
        return -1;
    }

    public int QueueIndex(ScoutBot bot) => queue.IndexOf(bot);

    public Vector3 PadPosition(int i) => OnGround(pads[i].position);

    public Vector3 QueuePosition(int i) => OnGround(transform.TransformPoint(queueStart + queueStep * i));

    static Vector3 OnGround(Vector3 p)
    {
        p.y = GroundUtil.Height(p, p.y);
        return p;
    }

    int FreePad()
    {
        for (int i = 0; i < occupant.Length; i++) if (occupant[i] == null) return i;
        return -1;
    }

    void Promote()
    {
        for (int i = 0; i < occupant.Length; i++)
            if (occupant[i] == null && queue.Count > 0)
            {
                occupant[i] = queue[0];
                queue.RemoveAt(0);
            }
    }

    void Update()
    {
        // drop destroyed bots
        for (int i = 0; i < occupant.Length; i++) if (occupant[i] == null) occupant[i] = null;
        queue.RemoveAll(b => b == null);
        Promote();

        int busy = 0, charging = 0;
        for (int i = 0; i < occupant.Length; i++)
        {
            var b = occupant[i];
            bool on = b != null && b.IsCharging;
            if (b != null) busy++;
            if (on) charging++;
            if (padRenderers != null && i < padRenderers.Length && padRenderers[i] != null)
            {
                float pulse = on ? 0.6f + 0.4f * Mathf.Sin(Time.time * 6f + i) : 0f;
                var c = on ? Color.Lerp(padIdle, padActive, pulse) : padIdle;
                padRenderers[i].GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", c);
                mpb.SetColor("_EmissionColor", c * (on ? 1.5f : 0.4f));
                padRenderers[i].SetPropertyBlock(mpb);
            }
        }

        if (core != null)
        {
            core.Rotate(0f, (charging > 0 ? 180f : 40f) * Time.deltaTime, 0f, Space.World);
            core.localScale = coreScale * (charging > 0 ? 1f + 0.08f * Mathf.Sin(Time.time * 8f) : 1f);
        }

        if (label != null)
        {
            string t = queue.Count > 0
                ? "<color=#FF6A55>QUEUE " + queue.Count + "\n<size=60%>NEED MORE CHARGERS</size></color>"
                : busy > 0 ? "Charging " + busy + "/" + PadCount : idleText;
            if (t != shownText) { shownText = t; label.text = t; }
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        if (pads != null) foreach (var p in pads) if (p != null) Gizmos.DrawWireSphere(p.position, 0.5f);
        Gizmos.color = Color.yellow;
        for (int i = 0; i < 5; i++) Gizmos.DrawWireSphere(transform.TransformPoint(queueStart + queueStep * i), 0.3f);
    }
}

/// What a bot does while it needs power: walk to the nearest station, queue if all pads are
/// taken, charge to full, then hand control back to its disk program.
public class ChargeBehaviour : BotBehaviour
{
    readonly ChargingStation station;

    public bool Done { get; private set; }
    public bool IsCharging { get; private set; }

    public ChargeBehaviour(ScoutBot bot) : base(bot)
    {
        station = ChargingStation.Nearest(bot.transform.position);
        if (station != null) station.Join(bot);
        else Done = true;
    }

    public override void Tick(float dt)
    {
        IsCharging = false;
        if (station == null) { Done = true; return; }
        int pct = Mathf.RoundToInt(bot.Battery01 * 100f);

        int pad = station.PadOf(bot);
        if (pad >= 0)
        {
            if (bot.MoveTowards(station.PadPosition(pad), 0.05f))
            {
                IsCharging = true;
                bot.FaceTowards(station.transform.position);
                bot.AddCharge(station.chargePerSecond * dt);
                bot.Status = "Charging " + pct + "%";
                if (bot.Battery01 >= 0.999f)
                {
                    station.Leave(bot);
                    Done = true;
                }
            }
            else bot.Status = "Low battery: going to charge (" + pct + "%)";
            return;
        }

        int q = station.QueueIndex(bot);
        if (q < 0) { station.Join(bot); return; }
        if (bot.MoveTowards(station.QueuePosition(q), 0.05f)) bot.FaceTowards(station.transform.position);
        bot.Status = "Waiting to charge: " + (q + 1) + " in line (" + pct + "%)";
    }

    public void Cancel()
    {
        if (station != null) station.Leave(bot);
        Done = true;
        IsCharging = false;
    }
}
