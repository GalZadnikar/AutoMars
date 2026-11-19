using System.Collections.Generic;
using UnityEngine;

/// C4 FORMATION. A drone with FORMATION on becomes the squad leader and recalls the nearest free drones
/// (free = has a disk in, not in another squad, not down / charging; drones without a disk are inactive and stay put)
/// (squad of up to maxSquadSize, leader included). Members fly in a V behind the leader (smoothed slots),
/// focus the leader's target and go back to their own program when the squad ends.
/// - Ends when: the leader turns FORMATION off, goes down (broken / no power) or is scrapped.
/// - A member that needs a charger leaves, charges, and rejoins once it is up again (leader's rejoin list).
/// - One squad per leader; a drone is in at most one squad. Members show "SQUAD" over their head, the leader "LEAD".
public partial class CombatDrone
{
    [Header("Formation (C4)")]
    [Tooltip("Squad size including the leader (used by the leader)")]
    public int maxSquadSize = 3;
    [Tooltip("Metres between rows / wings of the V")]
    public float squadSpacing = 2.4f;
    [Tooltip("Members chase the squad's target at most this far from the leader")]
    public float squadLeash = 8f;
    [Tooltip("A recharged drone rejoins when its battery is at least this full")]
    [Range(0f, 1f)] public float rejoinBattery = 0.6f;

    bool formationOn;
    CombatDrone leaderRef;
    readonly List<CombatDrone> members = new List<CombatDrone>();
    readonly List<CombatDrone> away = new List<CombatDrone>();   // left to recharge / be repaired: rejoin later
    Vector3 heading = Vector3.back, lastLeaderPos;
    bool headingInit;
    Vector3 slotPos, slotVel;
    bool slotInit;
    Health squadFocus;   // the target the squad gave me (my PriorityTarget while it is the same)

    public bool IsSquadLeader => formationOn;
    public bool InSquad => leaderRef != null;
    public CombatDrone SquadLeader => leaderRef;
    public IReadOnlyList<CombatDrone> SquadMembers => members;
    /// Leader included; 0 when not in a squad.
    public int SquadSize => formationOn ? 1 + members.Count : leaderRef != null ? 1 + leaderRef.members.Count : 0;

    /// "SQUAD 2/3 · LEADER" / "SQUAD 2/3 · led by Combat Drone 4" / "" (shown in the bot popup).
    public string SquadText
    {
        get
        {
            if (formationOn) return "SQUAD " + SquadSize + "/" + maxSquadSize + "  ·  LEADER";
            if (leaderRef != null) return "SQUAD " + SquadSize + "/" + leaderRef.maxSquadSize + "  ·  led by " + leaderRef.BotName;
            return "";
        }
    }

    /// Turns FORMATION on / off. On = lead a squad: recalls the nearest free drones (up to maxSquadSize - 1).
    /// Returns the number of members. If nobody is free the formation stays off (returns 0).
    public int SetFormation(bool on)
    {
        if (on == formationOn) return members.Count;
        if (!on) { Disband(); return 0; }

        if (leaderRef != null) LeaveSquad(false);   // a member can't lead: leave first
        var free = new List<CombatDrone>();
        foreach (var b in All)
        {
            var d = b as CombatDrone;
            if (d != null && d != this && CanRecruit(d)) free.Add(d);
        }
        Vector3 me = transform.position;
        free.Sort((a, b) => (a.transform.position - me).sqrMagnitude.CompareTo((b.transform.position - me).sqrMagnitude));

        formationOn = true;
        headingInit = false;
        foreach (var d in free)
        {
            if (members.Count >= Mathf.Max(1, maxSquadSize) - 1) break;
            Join(d);
        }
        if (members.Count == 0) formationOn = false;
        return members.Count;
    }

    /// A member leaves its squad. rejoin = come back after charging / repair (otherwise it just goes back to its program).
    public void LeaveSquad(bool rejoin)
    {
        if (leaderRef == null) return;
        var l = leaderRef;
        l.members.Remove(this);
        if (rejoin) { if (!l.away.Contains(this)) l.away.Add(this); }
        leaderRef = null;
        ClearFocus();
        RestartProgram();
    }

    /// Free = has a disk (a drone without one is inactive and stays where it is), not in a squad, not down, not charging.
    static bool CanRecruit(CombatDrone d) =>
        d != null && d.Program != null && d.leaderRef == null && !d.formationOn && !d.IsDown && !d.SeekingCharge && d.CurrentOrder == Order.None;

    // RECALL / UPDATE from the factory: leave or disband the squad first
    protected override void OnOrderGiven()
    {
        if (formationOn) SetFormation(false);
        if (leaderRef != null) LeaveSquad(false);
    }

    void Join(CombatDrone d)
    {
        if (d.Following) d.SetFollow(false);   // it will go back to its own program when the squad ends
        d.leaderRef = this;
        d.slotInit = false;
        d.squadFocus = null;
        d.RestartProgram();
        members.Add(d);
        away.Remove(d);
    }

    void Disband()
    {
        foreach (var m in members)
        {
            if (m == null) continue;
            m.leaderRef = null;
            m.ClearFocus();
            m.RestartProgram();
        }
        members.Clear();
        away.Clear();
        formationOn = false;
    }

    void ClearFocus()
    {
        if (squadFocus != null && PriorityTarget == squadFocus) PriorityTarget = null;
        squadFocus = null;
    }

    // ------------------------------------------------------------------ leader side (runs from TickCombat)

    void SquadLeaderTick(float dt)
    {
        if (!formationOn) return;
        if (IsDown) { Disband(); return; }

        // smoothed heading = where the leader is travelling (the V trails behind it)
        Vector3 v = transform.position - lastLeaderPos; v.y = 0f;
        lastLeaderPos = transform.position;
        if (!headingInit) { heading = transform.forward; heading.y = 0f; if (heading.sqrMagnitude < 0.01f) heading = Vector3.back; heading.Normalize(); headingInit = true; }
        if (dt > 0f && v.magnitude / dt > 0.6f) heading = Vector3.Slerp(heading, v.normalized, Mathf.Clamp01(dt * 3f)).normalized;

        // members that are gone / down leave (and rejoin later); everyone else stays
        for (int i = members.Count - 1; i >= 0; i--)
        {
            var m = members[i];
            if (m == null) { members.RemoveAt(i); continue; }
            if (m.IsDown)
            {
                members.RemoveAt(i);
                m.leaderRef = null;
                m.ClearFocus();
                away.Add(m);
            }
        }
        for (int i = away.Count - 1; i >= 0; i--)
        {
            var m = away[i];
            if (m == null || m.leaderRef != null || m.formationOn) { away.RemoveAt(i); continue; }
            if (members.Count >= Mathf.Max(1, maxSquadSize) - 1) break;
            if (CanRecruit(m) && !m.IsDown && m.Battery01 >= rejoinBattery) Join(m);
        }
        if (members.Count == 0 && away.Count == 0) formationOn = false;   // nobody left
    }

    Vector3 SquadSlot(int k)
    {
        int row = k / 2 + 1;
        float side = (k % 2 == 0) ? -1f : 1f;
        Vector3 right = Vector3.Cross(Vector3.up, heading);
        return transform.position - heading * (squadSpacing * row * 0.9f) + right * (side * squadSpacing * row * 0.85f);
    }

    // ------------------------------------------------------------------ member side (called by ScoutBot's brain)

    protected override bool TickSquad(float dt)
    {
        if (leaderRef == null) return false;
        var l = leaderRef;
        if (l == null || !l.formationOn)   // leader scrapped / turned it off
        {
            leaderRef = null; ClearFocus(); RestartProgram();
            return false;
        }

        if (TryStartCharge(dt)) { LeaveSquad(true); return true; }   // go recharge, rejoin afterwards

        // focus fire: the leader's target (a stronger priority from elsewhere, e.g. the leech, wins)
        Health f = l.PriorityTarget != null && l.PriorityTarget.Targetable ? l.PriorityTarget
                 : l.Target != null && l.Target.Targetable ? l.Target : null;
        if (f != null)
        {
            if (PriorityTarget == null || PriorityTarget == squadFocus) { PriorityTarget = f; squadFocus = f; }
        }
        else ClearFocus();

        if (PriorityTarget != null && EngageMove(l.transform.position, squadLeash, 0f, true))
        {
            Status = "Squad: engaging";
            return true;
        }

        // V slot behind the leader, smoothed so the wings don't jitter when the leader turns
        int k = Mathf.Max(0, l.members.IndexOf(this));
        Vector3 want = l.SquadSlot(k);
        if (!slotInit) { slotPos = transform.position; slotVel = Vector3.zero; slotInit = true; }
        slotPos = Vector3.SmoothDamp(slotPos, want, ref slotVel, 0.35f);

        Status = "In squad";
        Vector3 d = slotPos - transform.position; d.y = 0f;
        float dist = d.magnitude;
        if (dist > 0.4f)
        {
            float s = moveSpeed;
            moveSpeed = s * (dist > 4f ? 1.7f : 1f);   // catch up
            MoveTowards(slotPos, 0.2f);
            moveSpeed = s;
        }
        else FaceDirection(l.heading);
        return true;
    }

    protected override string ExtraLabel(out Color color)
    {
        color = new Color(1f, 0.82f, 0.28f);
        if (formationOn) return "LEAD";
        if (leaderRef != null) return "SQUAD";
        return null;
    }
}
