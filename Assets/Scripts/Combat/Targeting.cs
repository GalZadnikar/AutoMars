using System;
using System.Collections.Generic;
using UnityEngine;

/// Target picking for anything that shoots.
public static class Targeting
{
    /// Nearest targetable unit of `team` within `range` of `pos` (flat distance).
    /// lineOfSight = ray from `eye` (default pos + 1 m) to the target's aim point must not hit terrain/buildings.
    /// ignoreBlackFog = skip targets standing in never-explored fog (FogOfWar.IsExplored).
    public static Health FindNearest(Team team, Vector3 pos, float range, Func<Health, bool> filter = null,
                                     bool lineOfSight = false, bool ignoreBlackFog = true, Vector3? eye = null)
    {
        Health best = null;
        float bestD = range * range;
        var list = Health.All(team);
        var fog = FogOfWar.Instance;
        for (int i = 0; i < list.Count; i++)
        {
            var h = list[i];
            if (h == null || !h.Targetable) continue;
            Vector3 d = h.transform.position - pos; d.y = 0f;
            float m = d.sqrMagnitude;
            if (m > bestD) continue;
            if (ignoreBlackFog && fog != null && !fog.IsExplored(h.transform.position)) continue;
            if (filter != null && !filter(h)) continue;
            if (lineOfSight && !HasLineOfSight(eye ?? pos + Vector3.up, h)) continue;
            best = h; bestD = m;
        }
        return best;
    }

    /// Is `target` still worth shooting from here (alive, in range, optional LOS)?
    public static bool StillValid(Health target, Vector3 pos, float range, bool lineOfSight = false, Vector3? eye = null)
    {
        if (target == null || !target.Targetable) return false;
        Vector3 d = target.transform.position - pos; d.y = 0f;
        if (d.sqrMagnitude > range * range) return false;
        return !lineOfSight || HasLineOfSight(eye ?? pos + Vector3.up, target);
    }

    static readonly RaycastHit[] hits = new RaycastHit[16];

    /// True when nothing solid (ground, rocks, buildings) is between `from` and the target's aim point.
    /// Colliders that belong to any unit with Health are ignored (units don't block shots).
    public static bool HasLineOfSight(Vector3 from, Health target)
    {
        Vector3 to = target.AimPoint;
        Vector3 d = to - from;
        float len = d.magnitude;
        if (len < 0.01f) return true;
        int n = Physics.RaycastNonAlloc(from, d / len, hits, len - target.hitRadius * 0.5f,
                                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var c = hits[i].collider;
            if (c is CharacterController) continue;
            if (c.GetComponentInParent<Health>() != null) continue;
            return false;
        }
        return true;
    }

    /// Fills `result` with every targetable unit of `team` within range (for splash damage etc.).
    public static void FindAll(Team team, Vector3 pos, float range, List<Health> result)
    {
        result.Clear();
        float r2 = range * range;
        foreach (var h in Health.All(team))
        {
            if (h == null || !h.Targetable) continue;
            Vector3 d = h.transform.position - pos; d.y = 0f;
            if (d.sqrMagnitude <= r2) result.Add(h);
        }
    }
}
