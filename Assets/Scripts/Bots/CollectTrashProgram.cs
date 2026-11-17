using UnityEngine;

[CreateAssetMenu(menuName = "Trash Game/Disk Programs/Collect Trash", fileName = "Disk_CollectTrash")]
public class CollectTrashProgram : DiskProgram
{
    public override BotBehaviour CreateBehaviour(ScoutBot bot) => new CollectTrashBehaviour(bot);
}

/// Go to the nearest pile, fill up (bot capacity), bring it to the base, repeat.
/// Materials from the base go to the player like normal.
public class CollectTrashBehaviour : BotBehaviour
{
    enum State { FindPile, ToPile, Picking, ToHome, Unloading, NoTrash }

    State state;
    TrashPile pile;
    HomeBase home;
    Vector3 spot;
    float timer, retry;

    public CollectTrashBehaviour(ScoutBot bot) : base(bot)
    {
        if (bot.IsFull) GoHome(); else state = State.FindPile;
    }

    public override void Tick(float dt)
    {
        switch (state)
        {
            case State.FindPile:
                pile = NearestPile();
                if (pile == null)
                {
                    if (bot.Carried > 0) GoHome(); else state = State.NoTrash;
                    break;
                }
                spot = SpotNear(pile.transform.position, pile.pickupRadius * 0.55f);
                state = State.ToPile;
                break;

            case State.ToPile:
                bot.Status = "Going to a trash pile";
                if (pile == null || pile.IsEmpty) { state = State.FindPile; break; }
                if (bot.MoveTowards(spot, 0.1f)) { state = State.Picking; timer = 0.2f; }
                break;

            case State.Picking:
                bot.Status = "Collecting trash " + bot.Carried + "/" + bot.capacity;
                bot.FaceTowards(pile.transform.position);
                bot.MarkWorking();
                timer -= dt;
                if (timer > 0f) break;
                timer = bot.pickupInterval;
                if (bot.IsFull) { GoHome(); break; }
                if (!bot.TryPickFrom(pile))
                {
                    if (bot.Carried > 0) GoHome(); else state = State.FindPile;
                }
                break;

            case State.ToHome:
                bot.Status = "Bringing trash to base";
                if (home == null) { GoHome(); break; }
                if (bot.MoveTowards(spot, 0.1f)) { state = State.Unloading; timer = 0.1f; }
                break;

            case State.Unloading:
                bot.Status = "Unloading";
                bot.FaceTowards(home.transform.position);
                bot.MarkWorking();
                timer -= dt;
                if (timer > 0f) break;
                timer = bot.depositInterval;
                if (!bot.TryUnloadTo(home)) state = State.FindPile;
                break;

            case State.NoTrash:
                bot.Status = "No trash left";
                bot.MoveTowards(bot.HomePosition, 0.05f);
                retry -= dt;
                if (retry <= 0f)
                {
                    retry = 2f;
                    if (NearestPile() != null) state = State.FindPile;
                }
                break;
        }
    }

    void GoHome()
    {
        home = null;
        float best = float.MaxValue;
        foreach (var h in HomeBase.All)
        {
            float d = (h.transform.position - bot.transform.position).sqrMagnitude;
            if (d < best) { best = d; home = h; }
        }
        if (home == null) { state = State.NoTrash; return; }
        spot = SpotNear(home.transform.position, home.radius * 0.6f);
        state = State.ToHome;
    }

    TrashPile NearestPile()
    {
        TrashPile result = null;
        float best = float.MaxValue;
        foreach (var p in TrashPile.All)
        {
            if (p.IsEmpty) continue;
            float d = (p.transform.position - bot.transform.position).sqrMagnitude;
            if (d < best) { best = d; result = p; }
        }
        return result;
    }

    // a point on the near side of the target, slightly randomised so bots spread out
    Vector3 SpotNear(Vector3 center, float r)
    {
        Vector3 dir = bot.transform.position - center; dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) dir = Vector3.back;
        dir = Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f) * dir.normalized;
        return center + dir * r;
    }
}
