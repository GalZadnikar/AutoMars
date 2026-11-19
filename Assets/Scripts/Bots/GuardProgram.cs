using System.Collections.Generic;
using UnityEngine;

/// GUARD disk (drones only): stay at the drop point and defend the area around it.
[CreateAssetMenu(menuName = "Trash Game/Disk Programs/Drone Guard", fileName = "Disk_Guard")]
public class GuardProgram : DiskProgram
{
    [Tooltip("Engages enemies up to this far from its guard point (then turns back)")]
    public float guardRadius = 10f;

    void Reset() { compatibleWith = RobotType.Drone; displayName = "Guard"; shortName = "GRD"; color = new Color(1f, 0.42f, 0.3f); }

    public override BotBehaviour CreateBehaviour(ScoutBot bot) => new GuardBehaviour(bot, this);
}

public class GuardBehaviour : BotBehaviour
{
    readonly CombatDrone drone;
    readonly GuardProgram prog;

    public GuardBehaviour(ScoutBot bot, GuardProgram prog) : base(bot)
    {
        drone = bot as CombatDrone;
        this.prog = prog;
    }

    public override void Tick(float dt)
    {
        if (drone == null) { bot.Status = "Guard needs a drone"; return; }
        Vector3 home = drone.GuardPoint;
        // anything inside the guarded circle (+ gun range) is fair game
        if (drone.EngageMove(home, prog.guardRadius, prog.guardRadius + drone.attackRange * 0.5f))
        {
            drone.Status = "Guarding: engaging";
            return;
        }
        drone.Status = "Guarding";
        drone.MoveTowards(home, 0.15f);   // hover still at the post (no drain while idle)
    }
}
