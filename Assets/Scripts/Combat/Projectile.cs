using System.Collections.Generic;
using UnityEngine;

/// What a shot does. Pass one to Projectile.Fire (shooters keep one and update damage/speed from their stats).
[System.Serializable]
public class ShotSpec
{
    public float damage = 10f;
    public float speed = 22f;
    [Tooltip("Seconds before the shot fizzles out")]
    public float lifetime = 2f;
    [Tooltip("0 = straight line; >0 = turns toward the target (degrees per second)")]
    public float homing = 0f;
    public float radius = 0.12f;
    public Color color = new Color(0.45f, 0.95f, 1f);
    public float trailTime = 0.12f;
    [Tooltip("Spawn a muzzle flash at the start")]
    public bool muzzleFlash = true;
}

/// A pooled bullet / bolt. Moves in a straight line (or homes), hits units of the other team by distance to their
/// aim point (no physics needed), stops on the ground and on solid colliders, spawns hit sparks.
/// Fire with Projectile.Fire(...); never instantiate directly.
public class Projectile : MonoBehaviour
{
    static readonly Stack<Projectile> pool = new Stack<Projectile>();
    static readonly List<Projectile> live = new List<Projectile>();
    static Transform poolRoot;
    static Mesh sphere;
    static Material headMat, trailMat;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { pool.Clear(); live.Clear(); poolRoot = null; }

    public static int LiveCount => live.Count;
    /// Raised for every hit on a unit (for tests / sounds later): target, damage done.
    public static event System.Action<Health, float> AnyHit;

    Team team;           // shooter's team; hits the other one
    GameObject source;
    Health target;
    ShotSpec spec;
    Vector3 velocity;
    float age;
    MeshRenderer head;
    TrailRenderer trail;
    MaterialPropertyBlock mpb;

    /// Fires a shot from `from` at `target` (aims at where it is now). Returns the projectile.
    public static Projectile Fire(Vector3 from, Health target, ShotSpec spec, Team shooterTeam, GameObject source = null)
    {
        Vector3 dir = target != null ? target.AimPoint - from : (source != null ? source.transform.forward : Vector3.forward);
        return Fire(from, dir, spec, shooterTeam, source, target);
    }

    /// Fires a shot from `from` along `dir`.
    public static Projectile Fire(Vector3 from, Vector3 dir, ShotSpec spec, Team shooterTeam, GameObject source = null, Health target = null)
    {
        if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
        dir.Normalize();
        var p = Get();
        p.team = shooterTeam;
        p.source = source;
        p.target = target;
        p.spec = spec ?? new ShotSpec();
        p.velocity = dir * p.spec.speed;
        p.age = 0f;
        p.transform.position = from;
        p.transform.rotation = Quaternion.LookRotation(dir);
        p.transform.localScale = Vector3.one * p.spec.radius * 2f;
        p.mpb.SetColor("_BaseColor", Color.Lerp(p.spec.color, Color.white, 0.55f));
        p.head.SetPropertyBlock(p.mpb);
        p.trail.time = p.spec.trailTime;
        p.trail.widthMultiplier = p.spec.radius * 1.6f;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(p.spec.color, 0f), new GradientColorKey(p.spec.color, 1f) },
                  new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
        p.trail.colorGradient = g;
        p.gameObject.SetActive(true);
        p.trail.Clear();
        p.trail.emitting = true;
        live.Add(p);
        if (p.spec.muzzleFlash) CombatFX.Muzzle(from, dir, p.spec.color);
        return p;
    }

    /// Removes every live projectile (debug / scene reset).
    public static void ClearAll()
    {
        for (int i = live.Count - 1; i >= 0; i--) live[i].Release();
    }

    static Projectile Get()
    {
        if (pool.Count > 0) return pool.Pop();
        if (poolRoot == null) poolRoot = new GameObject("Projectiles").transform;
        if (sphere == null) sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        if (headMat == null) headMat = Resources.Load<Material>("FX/ParticleSolid");
        if (trailMat == null) trailMat = Resources.Load<Material>("FX/ParticleSoft");

        var go = new GameObject("Projectile");
        go.transform.SetParent(poolRoot, false);
        go.SetActive(false);
        var p = go.AddComponent<Projectile>();
        p.mpb = new MaterialPropertyBlock();
        go.AddComponent<MeshFilter>().sharedMesh = sphere;
        p.head = go.AddComponent<MeshRenderer>();
        p.head.sharedMaterial = headMat;
        p.head.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        p.head.receiveShadows = false;
        p.trail = go.AddComponent<TrailRenderer>();
        p.trail.sharedMaterial = trailMat;
        p.trail.minVertexDistance = 0.05f;
        p.trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        p.trail.receiveShadows = false;
        p.trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
        p.trail.numCapVertices = 2;
        return p;
    }

    void Release()
    {
        live.Remove(this);
        trail.emitting = false;
        gameObject.SetActive(false);
        target = null; source = null;
        pool.Push(this);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        age += dt;
        if (age >= spec.lifetime) { Release(); return; }

        if (spec.homing > 0f && target != null && target.Targetable)
        {
            Vector3 want = (target.AimPoint - transform.position).normalized * spec.speed;
            velocity = Vector3.RotateTowards(velocity, want, spec.homing * Mathf.Deg2Rad * dt, 0f);
        }

        Vector3 from = transform.position;
        Vector3 to = from + velocity * dt;
        Vector3 seg = to - from;
        float segLen = seg.magnitude;

        // 1) units of the other team (closest along the segment wins)
        Health hit = null;
        float hitT = float.MaxValue;
        var list = Health.All(Health.Opponent(team));
        for (int i = 0; i < list.Count; i++)
        {
            var h = list[i];
            if (h == null || h.IsDead || !h.isActiveAndEnabled) continue;
            float t = SegmentT(from, seg, h.AimPoint);
            Vector3 closest = from + seg * t;
            float r = h.hitRadius + spec.radius;
            if ((h.AimPoint - closest).sqrMagnitude <= r * r && t < hitT) { hitT = t; hit = h; }
        }

        // 2) solid things (buildings, rocks, the ground mesh)
        float solidT = float.MaxValue;
        Vector3 solidNormal = -seg;
        if (segLen > 1e-5f && Physics.Raycast(from, seg / segLen, out var rh, segLen, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            && !(rh.collider is CharacterController) && rh.collider.GetComponentInParent<Health>() == null)
        {
            solidT = rh.distance / segLen;
            solidNormal = rh.normal;
        }

        if (hit != null && hitT <= solidT)
        {
            Vector3 p = from + seg * hitT;
            float done = hit.Damage(spec.damage, source);
            CombatFX.Hit(p, -velocity, spec.color);
            AnyHit?.Invoke(hit, done);
            Release();
            return;
        }
        if (solidT < float.MaxValue)
        {
            CombatFX.Hit(from + seg * solidT, solidNormal, spec.color, 0.7f);
            Release();
            return;
        }

        // 3) fell below the ground surface (terrain without a collider)
        float gy = GroundUtil.Height(to, float.MinValue);
        if (gy > float.MinValue && to.y < gy)
        {
            to.y = gy;
            CombatFX.Hit(to, Vector3.up, spec.color, 0.7f);
            Release();
            return;
        }

        transform.position = to;
        if (velocity.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(velocity);
    }

    static float SegmentT(Vector3 a, Vector3 ab, Vector3 p)
    {
        float l2 = ab.sqrMagnitude;
        if (l2 < 1e-8f) return 0f;
        return Mathf.Clamp01(Vector3.Dot(p - a, ab) / l2);
    }
}
