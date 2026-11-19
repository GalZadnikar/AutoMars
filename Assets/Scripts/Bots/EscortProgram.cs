using System.Collections.Generic;
using UnityEngine;

/// ESCORT disk (drones only): stay near the player and fight anything that comes close to them.
[CreateAssetMenu(menuName = "Trash Game/Disk Programs/Drone Escort", fileName = "Disk_Escort")]
public class EscortProgram : DiskProgram
{
    [Tooltip("Distance of the escort ring around the player")]
    public float ringRadius = 3.2f;
    [Tooltip("Enemies this close to the player count as threats")]
    public float threatRadius = 11f;
    [Tooltip("How far from the player an escort may go to chase a threat")]
    public float leash = 8f;

    void Reset() { compatibleWith = RobotType.Drone; displayName = "Escort"; shortName = "ESC"; color = new Color(0.45f, 0.75f, 1f); }

    public override BotBehaviour CreateBehaviour(ScoutBot bot) => new EscortBehaviour(bot, this);
}

public class EscortBehaviour : BotBehaviour
{
    static readonly List<CombatDrone> escorts = new List<CombatDrone>();

    readonly CombatDrone drone;
    readonly EscortProgram prog;

    public EscortBehaviour(ScoutBot bot, EscortProgram prog) : base(bot)
    {
        drone = bot as CombatDrone;
        this.prog = prog;
    }

    public override void Tick(float dt)
    {
        if (drone == null) { bot.Status = "Escort needs a drone"; return; }
        var p = PlayerInventory.Instance != null ? PlayerInventory.Instance.transform : null;
        if (p == null) return;

        // slot on a ring around the player, spread by index among active escorts
        escorts.RemoveAll(e => e == null || !(e.Program is EscortProgram) || e.IsDown || e.Following);
        if (!escorts.Contains(drone)) escorts.Add(drone);
        int i = escorts.IndexOf(drone), n = Mathf.Max(1, escorts.Count);

        if (drone.EngageMove(p.position, prog.leash, prog.threatRadius))
        {
            drone.Status = "Escort: engaging";
            return;
        }

        Vector3 back = -p.forward; back.y = 0f;
        if (back.sqrMagnitude < 0.01f) back = Vector3.back;
        float ang = (n == 1 ? 0f : (i - (n - 1) * 0.5f) * Mathf.Min(55f, 300f / n));
        Vector3 slot = p.position + Quaternion.Euler(0f, ang + 35f, 0f) * back.normalized * prog.ringRadius;

        Vector3 d = slot - drone.transform.position; d.y = 0f;
        float dist = d.magnitude;
        drone.Status = "Escorting you";
        if (dist > 0.6f)
        {
            float mul = dist > 5f ? 1.7f : 1f;   // catch up
            float s = drone.moveSpeed;
            drone.moveSpeed = s * mul;
            drone.MoveTowards(slot, 0.3f);
            drone.moveSpeed = s;
        }
        else drone.FaceDirection(p.forward);
    }
}
