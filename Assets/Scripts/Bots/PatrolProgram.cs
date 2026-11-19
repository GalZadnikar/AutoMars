using UnityEngine;

/// PATROL disk (drones only): flies between random spots around the base and over the map you have already
/// explored (never into the black fog), hovers at each spot for a few seconds, then picks the next one.
/// Shoots anything it meets on the way (the gun works as always) and chases a threat a little before going back.
[CreateAssetMenu(menuName = "Trash Game/Disk Programs/Drone Patrol", fileName = "Disk_Patrol")]
public class PatrolProgram : DiskProgram
{
    [Tooltip("Most patrol spots are within this distance of the base")]
    public float baseRadius = 16f;
    [Tooltip("Some spots are further out, anywhere explored within this distance of the base")]
    public float farRadius = 38f;
    [Tooltip("Chance (0..1) that the next spot is a far one")]
    [Range(0f, 1f)] public float farChance = 0.4f;
    [Tooltip("Seconds it hovers at each spot (random between x and y)")]
    public Vector2 waitTime = new Vector2(2.5f, 5f);
    [Tooltip("Next spot is at least this far from the current one")]
    public float minHop = 7f;
    [Tooltip("How far a drone may chase a threat away from where it met it")]
    public float chaseLeash = 9f;

    void Reset() { compatibleWith = RobotType.Drone; displayName = "Patrol"; shortName = "PTL"; color = new Color(0.55f, 1f, 0.45f); }

    public override BotBehaviour CreateBehaviour(ScoutBot bot) => new PatrolBehaviour(bot, this);
}

public class PatrolBehaviour : BotBehaviour
{
    readonly CombatDrone drone;
    readonly PatrolProgram prog;
    Vector3 spot;
    bool hasSpot, waiting;
    float waitT;
    Vector3 fightAnchor;
    bool fighting;
    Vector3 lookDir;

    public PatrolBehaviour(ScoutBot bot, PatrolProgram prog) : base(bot)
    {
        drone = bot as CombatDrone;
        this.prog = prog;
    }

    public override void Tick(float dt)
    {
        if (drone == null) { bot.Status = "Patrol needs a drone"; return; }

        // a fight: stay near where it started, then carry on with the patrol
        if (!fighting) fightAnchor = drone.transform.position;
        if (drone.EngageMove(fightAnchor, prog.chaseLeash, 0f, true))
        {
            fighting = true;
            drone.Status = "Patrol: engaging";
            return;
        }
        if (fighting) { fighting = false; waiting = false; hasSpot = false; }

        if (!hasSpot) { spot = PickSpot(); hasSpot = true; waiting = false; }

        if (!waiting)
        {
            drone.Status = "Patrolling";
            if (drone.MoveTowards(spot, 0.4f))
            {
                waiting = true;
                waitT = Random.Range(prog.waitTime.x, Mathf.Max(prog.waitTime.x, prog.waitTime.y));
                lookDir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
            }
            return;
        }

        // hover at the spot and look around (standing still uses no battery)
        drone.Status = "Patrolling: watching";
        drone.FaceDirection(lookDir);
        waitT -= dt;
        if (waitT <= 0f) hasSpot = false;
    }

    Vector3 Center()
    {
        HomeBase best = null;
        float bd = float.MaxValue;
        foreach (var h in HomeBase.All)
        {
            if (h == null) continue;
            float d = (h.transform.position - drone.transform.position).sqrMagnitude;
            if (d < bd) { bd = d; best = h; }
        }
        return best != null ? best.transform.position : drone.HomePosition;
    }

    /// Random spot near the base, or (sometimes) further out, but only where the map is explored.
    Vector3 PickSpot()
    {
        Vector3 c = Center();
        Vector3 me = drone.transform.position;
        var fog = FogOfWar.Instance;
        bool far = Random.value < prog.farChance;
        Vector3 fallback = c;
        for (int i = 0; i < 40; i++)
        {
            float r = far ? prog.farRadius : prog.baseRadius;
            Vector2 o = Random.insideUnitCircle * r;
            Vector3 p = c + new Vector3(o.x, 0f, o.y);
            if (fog != null && !fog.IsExplored(p)) continue;   // never into the black fog
            fallback = p;
            Vector3 hop = p - me; hop.y = 0f;
            if (hop.magnitude < prog.minHop) continue;
            p.y = GroundUtil.Height(p, me.y);
            return p;
        }
        fallback.y = GroundUtil.Height(fallback, me.y);
        return fallback;
    }

}
