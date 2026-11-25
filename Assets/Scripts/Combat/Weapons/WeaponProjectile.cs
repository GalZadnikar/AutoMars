using System.Collections.Generic;
using UnityEngine;

/// One flying shot of a player weapon. Pooled per weapon; fire with WeaponProjectile.Fire(...).
///  - It really flies (speed from the WeaponDef) and looks like the weapon's projectile effect, so every shot can
///    be followed by eye. A little homing keeps it on the enemy it was fired at.
///  - It hits enemies by distance to their aim point (like Projectile), stops on rocks, buildings and the ground.
///  - Pierce: flies on through that many more enemies. Splash: explodes and damages everything around.
///  - The damage itself goes through PlayerWeapon.DealHit, which gives the hit its feel.
public class WeaponProjectile : MonoBehaviour
{
    static readonly Dictionary<WeaponDef, Stack<WeaponProjectile>> pools = new Dictionary<WeaponDef, Stack<WeaponProjectile>>();
    static readonly List<Health> around = new List<Health>();
    static Transform root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { pools.Clear(); around.Clear(); root = null; }

    WeaponDef def;
    PlayerWeapon owner;
    Health target;
    Vector3 velocity, start;
    float age, life, dieT;
    int pierceLeft;
    bool dying;
    ParticleSystem[] systems;
    TrailRenderer tracer;
    readonly List<Health> done = new List<Health>();

    public static WeaponProjectile Fire(WeaponDef def, PlayerWeapon owner, Vector3 from, Vector3 dir, Health target)
    {
        if (def == null) return null;
        if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
        dir.Normalize();
        var p = Get(def);
        p.def = def;
        p.owner = owner;
        p.target = target;
        p.start = from;
        p.velocity = dir * Mathf.Max(1f, def.speed);
        p.age = 0f;
        // a fan of piercing pellets ends at the weapon's range; other shots may fly a little past it
        p.life = (def.range + (def.pierce > 0 && def.pellets > 1 ? 0.4f : 1.5f)) / Mathf.Max(1f, def.speed);
        p.pierceLeft = def.pierce;
        p.dying = false;
        p.done.Clear();
        p.transform.SetPositionAndRotation(from, Quaternion.LookRotation(dir));
        p.transform.localScale = Vector3.one * def.projectileScale;
        p.gameObject.SetActive(true);
        p.SetTracer();
        for (int i = 0; i < p.systems.Length; i++)
        {
            p.systems[i].Clear(false);
            p.systems[i].Play(false);
        }
        return p;
    }

    static WeaponProjectile Get(WeaponDef def)
    {
        Stack<WeaponProjectile> pool;
        if (!pools.TryGetValue(def, out pool)) pools[def] = pool = new Stack<WeaponProjectile>();
        while (pool.Count > 0)
        {
            var old = pool.Pop();
            if (old != null) return old;
        }
        if (root == null) root = new GameObject("Weapon Projectiles").transform;
        var go = new GameObject("Shot (" + def.displayName + ")");
        go.SetActive(false);
        go.transform.SetParent(root, false);
        var p = go.AddComponent<WeaponProjectile>();
        if (def.projectileFx != null) FxPool.Create(def.projectileFx, go.transform);
        else
        {
            // no effect set: a plain glowing ball, so the shot is still visible
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(ball.GetComponent<Collider>());
            ball.transform.SetParent(go.transform, false);
            ball.transform.localScale = Vector3.one * 0.5f;
            ball.GetComponent<Renderer>().sharedMaterial = Resources.Load<Material>("FX/ParticleSolid");
        }
        p.systems = go.GetComponentsInChildren<ParticleSystem>(true);
        return p;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dying)
        {
            // the trail fades out, then the shot goes back to the pool
            dieT -= dt;
            if (dieT <= 0f) Release();
            return;
        }
        age += dt;
        if (age >= life) { Die(); return; }

        if (def.homing > 0f && target != null && target.Targetable)
        {
            Vector3 want = (target.AimPoint - transform.position).normalized * velocity.magnitude;
            velocity = Vector3.RotateTowards(velocity, want, def.homing * Mathf.Deg2Rad * dt, 0f);
        }

        Vector3 from = transform.position;
        Vector3 seg = velocity * dt;
        float segLen = seg.magnitude;
        if (segLen < 1e-5f) return;
        Vector3 dir = seg / segLen;

        // 1) enemies (closest along this frame's path wins)
        Health hit = null;
        float hitT = float.MaxValue;
        var list = Health.All(Team.Enemy);
        for (int i = 0; i < list.Count; i++)
        {
            var h = list[i];
            if (h == null || h.IsDead || !h.isActiveAndEnabled || done.Contains(h)) continue;
            float t = Mathf.Clamp01(Vector3.Dot(h.AimPoint - from, seg) / (segLen * segLen));
            float r = h.hitRadius + def.radius;
            if ((h.AimPoint - (from + seg * t)).sqrMagnitude <= r * r && t < hitT) { hitT = t; hit = h; }
        }

        // 2) solid things (buildings, the ground mesh); colliders of units don't count, those are handled above
        float solidT = float.MaxValue;
        RaycastHit rh;
        if (Physics.Raycast(from, dir, out rh, segLen, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            && !(rh.collider is CharacterController) && rh.collider.GetComponentInParent<Health>() == null)
            solidT = rh.distance / segLen;

        if (hit != null && hitT <= solidT)
        {
            Vector3 p = from + seg * hitT;
            transform.position = p;
            if (!hit.targetable)
            {
                // a rock or other thing that is not an enemy: the shot just stops on it
                Burst(p, dir, 0.6f);
                Die();
                return;
            }
            HitEnemy(hit, p, dir);
            return;
        }
        if (solidT < float.MaxValue)
        {
            transform.position = from + seg * solidT;
            Explode(transform.position, dir, null);
            Die();
            return;
        }

        // 3) under the ground surface (terrain without a collider)
        Vector3 to = from + seg;
        float gy = GroundUtil.Height(to, float.MinValue);
        if (gy > float.MinValue && to.y < gy)
        {
            to.y = gy;
            transform.position = to;
            Explode(to, dir, null);
            Die();
            return;
        }

        transform.position = to;
        transform.rotation = Quaternion.LookRotation(dir);
    }

    void HitEnemy(Health h, Vector3 p, Vector3 dir)
    {
        if (owner != null) owner.DealHit(def, h, def.damage, p, dir, start, def.knockback);
        else h.Damage(def.damage);
        Explode(p, dir, h);

        if (pierceLeft > 0)
        {
            // flies on, level with the ground, and can't hit the same enemy twice
            pierceLeft--;
            done.Add(h);
            if (target == h) target = null;
            float speed = velocity.magnitude;
            velocity.y = 0f;
            velocity = velocity.sqrMagnitude > 0.0001f ? velocity.normalized * speed : dir * speed;
            return;
        }
        Die();
    }

    /// The impact effect, and the splash damage if the weapon has any. direct = the enemy that took the full hit.
    void Explode(Vector3 p, Vector3 dir, Health direct)
    {
        Burst(p, dir, 1f);
        if (def.splashRadius <= 0f || owner == null) return;
        // the blast is centred on the enemy that was hit (the impact point is on its near side)
        if (direct != null) p = direct.transform.position;
        Targeting.FindAll(Team.Enemy, p, def.splashRadius, around);
        for (int i = 0; i < around.Count; i++)
        {
            var h = around[i];
            if (h == null || h == direct) continue;
            Vector3 away = h.transform.position - p; away.y = 0f;
            owner.DealHit(def, h, def.splashDamage, h.AimPoint, away.sqrMagnitude > 0.0001f ? away.normalized : dir, p, def.knockback * 0.6f);
        }
    }

    void Burst(Vector3 p, Vector3 dir, float size)
    {
        if (def.impactFx != null) FxPool.Play(def.impactFx, p, Quaternion.LookRotation(-dir), def.impactScale * size);
        else CombatFX.Hit(p, -dir, def.color, size);
    }

    /// The bright streak behind the shot (WeaponDef.tracerLength), so its line can be read at a glance.
    void SetTracer()
    {
        if (def.tracerLength <= 0f)
        {
            if (tracer != null) tracer.emitting = false;
            return;
        }
        if (tracer == null)
        {
            tracer = gameObject.AddComponent<TrailRenderer>();
            tracer.sharedMaterial = Resources.Load<Material>("FX/ParticleSoft");
            tracer.minVertexDistance = 0.05f;
            tracer.numCapVertices = 2;
            tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tracer.receiveShadows = false;
            tracer.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.1f));
        }
        tracer.time = def.tracerLength / Mathf.Max(1f, def.speed);
        tracer.widthMultiplier = def.tracerWidth;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.Lerp(def.color, Color.white, 0.75f), 0f), new GradientColorKey(def.color, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        tracer.colorGradient = g;
        tracer.Clear();
        tracer.emitting = true;
    }

    void Die()
    {
        dying = true;
        dieT = 0.35f;
        if (tracer != null) tracer.emitting = false;
        for (int i = 0; i < systems.Length; i++)
        {
            var ps = systems[i];
            // the head (local space) disappears at once, the sparks it left behind (world space) fade out
            bool local = ps.main.simulationSpace == ParticleSystemSimulationSpace.Local;
            ps.Stop(false, local ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting);
        }
        if (systems.Length == 0) Release();
    }

    void Release()
    {
        dying = false;
        target = null;
        owner = null;
        gameObject.SetActive(false);
        Stack<WeaponProjectile> pool;
        if (def != null && pools.TryGetValue(def, out pool)) pool.Push(this);
        else Destroy(gameObject);
    }
}
