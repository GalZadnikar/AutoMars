using UnityEngine;

/// Code-built one-shot particle effects for combat (same approach as PileClearFX, no prefabs):
/// muzzle flash, hit sparks, death explosion and a looping "broken" smoke.
public static class CombatFX
{
    static Material soft, solid;
    static Mesh cube;

    static Material Soft => soft != null ? soft : (soft = Resources.Load<Material>("FX/ParticleSoft"));
    static Material Solid => solid != null ? solid : (solid = Resources.Load<Material>("FX/ParticleSolid"));

    /// Quick flash at a gun muzzle, pointing along `dir`.
    public static void Muzzle(Vector3 pos, Vector3 dir, Color color)
    {
        var root = Root("Muzzle FX", pos, dir);
        var flash = System(root, "Flash", 0.15f);
        {
            var m = flash.main;
            m.startLifetime = 0.08f;
            m.startSpeed = 0f;
            m.startSize = new ParticleSystem.MinMaxCurve(0.55f, 0.75f);
            m.startColor = Color.Lerp(color, Color.white, 0.5f);
            Burst(flash, 0f, 2);
            m.simulationSpace = ParticleSystemSimulationSpace.Local;
        }
        var sparks = System(root, "Sparks", 0.3f);
        {
            var m = sparks.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(6f, 11f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
            m.startColor = color;
            Burst(sparks, 0f, 6);
            var sh = sparks.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 18f; sh.radius = 0.02f;
            Stretch(sparks, 0.06f);
        }
        Finish(root, 0.6f);
    }

    /// Sparks + small puff where a projectile hits. normal = direction the sparks fly (usually back toward the shooter).
    public static void Hit(Vector3 pos, Vector3 normal, Color color, float size = 1f)
    {
        var root = Root("Hit FX", pos, normal);
        var flash = System(root, "Flash", 0.2f);
        {
            var m = flash.main;
            m.startLifetime = 0.1f;
            m.startSpeed = 0f;
            m.startSize = 0.7f * size;
            m.startColor = Color.Lerp(color, Color.white, 0.6f);
            Burst(flash, 0f, 1);
        }
        var sparks = System(root, "Sparks", 0.5f);
        {
            var m = sparks.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(3f * size, 7f * size);
            m.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
            m.startColor = new ParticleSystem.MinMaxGradient(color, new Color(1f, 0.95f, 0.7f));
            m.gravityModifier = 1.2f;
            Burst(sparks, 0f, (int)(10 * size));
            var sh = sparks.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 55f; sh.radius = 0.05f;
            Stretch(sparks, 0.05f);
        }
        var puff = System(root, "Puff", 0.6f);
        {
            var m = puff.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.35f * size, 0.6f * size);
            m.startColor = new Color(0.55f, 0.5f, 0.45f, 0.45f);
            Burst(puff, 0f, 3);
            Fade(puff);
        }
        Finish(root, 1f);
    }

    /// Generic explode / scrap burst: flash, fireball, smoke, metal bits.
    public static void Explode(Vector3 pos, float size, Color color)
    {
        size = Mathf.Clamp(size, 0.4f, 6f);
        var root = Root("Explosion FX", pos + Vector3.up * 0.5f * size, Vector3.up);
        var flash = System(root, "Flash", 0.2f);
        {
            var m = flash.main;
            m.startLifetime = 0.14f; m.startSpeed = 0f; m.startSize = 3.2f * size;
            m.startColor = new Color(1f, 0.92f, 0.7f, 0.9f);
            Burst(flash, 0f, 1);
        }
        var fire = System(root, "Fireball", 0.8f);
        {
            var m = fire.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(1.5f * size, 4f * size);
            m.startSize = new ParticleSystem.MinMaxCurve(0.7f * size, 1.4f * size);
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.3f), Color.Lerp(new Color(1f, 0.4f, 0.15f), color, 0.35f));
            Burst(fire, 0f, (int)(10 + 6 * size));
            var sh = fire.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.3f * size;
            var vel = fire.limitVelocityOverLifetime; vel.enabled = true; vel.limit = 0.5f; vel.dampen = 0.2f;
            Fade(fire);
        }
        var smoke = System(root, "Smoke", 2f);
        {
            var m = smoke.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(1f, 1.8f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.8f * size, 2f * size);
            m.startSize = new ParticleSystem.MinMaxCurve(0.9f * size, 1.6f * size);
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.22f, 0.2f, 0.19f, 0.7f), new Color(0.4f, 0.36f, 0.32f, 0.55f));
            m.gravityModifier = -0.15f;
            Burst(smoke, 0.05f, (int)(8 + 4 * size));
            var sh = smoke.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.4f * size;
            var vel = smoke.limitVelocityOverLifetime; vel.enabled = true; vel.limit = 0.4f; vel.dampen = 0.1f;
            Fade(smoke);
        }
        var bits = System(root, "Bits", 2f);
        {
            var m = bits.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(4f * Mathf.Sqrt(size), 9f * Mathf.Sqrt(size));
            m.startSize = new ParticleSystem.MinMaxCurve(0.08f * size, 0.22f * size);
            m.startRotation3D = true;
            m.startRotationX = m.startRotationY = m.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.3f, 0.3f, 0.32f), Color.Lerp(new Color(0.6f, 0.58f, 0.55f), color, 0.4f));
            m.gravityModifier = 1.8f;
            Burst(bits, 0f, (int)(10 + 6 * size));
            var sh = bits.shape; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 0.3f * size; sh.rotation = new Vector3(-90f, 0f, 0f);
            var rot = bits.rotationOverLifetime; rot.enabled = true; rot.separateAxes = true;
            rot.x = rot.y = rot.z = new ParticleSystem.MinMaxCurve(-8f, 8f);
            var col = bits.collision; col.enabled = true; col.type = ParticleSystemCollisionType.World; col.dampen = 0.5f; col.bounce = 0.3f; col.lifetimeLoss = 0.2f;
            var sol = bits.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.75f, 1f), new Keyframe(1f, 0f)));
            var r = bits.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            if (cube == null) cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            r.mesh = cube;
            if (Solid != null) r.sharedMaterial = Solid;
        }
        var sparks = System(root, "Sparks", 1f);
        {
            var m = sparks.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(5f * size, 10f * size);
            m.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
            m.startColor = new Color(1f, 0.85f, 0.45f);
            m.gravityModifier = 1f;
            Burst(sparks, 0f, (int)(14 + 6 * size));
            var sh = sparks.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.2f * size;
            Stretch(sparks, 0.04f);
        }
        Finish(root, 3f);
    }

    /// Looping dark smoke for a broken (0 HP) unit. Parent it to the unit; Stop + destroy it when repaired.
    public static ParticleSystem BrokenSmoke(Transform parent, float height)
    {
        var go = new GameObject("Broken Smoke");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.up * height;
        go.transform.rotation = Quaternion.LookRotation(Vector3.up);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main;
        m.loop = true; m.playOnAwake = false; m.duration = 1f;
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.15f, 0.14f, 0.13f, 0.6f), new Color(0.3f, 0.28f, 0.26f, 0.45f));
        m.maxParticles = 40;
        var em = ps.emission; em.rateOverTime = 7f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 12f; sh.radius = 0.15f;
        var sol = ps.sizeOverLifetime; sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.6f)));
        Fade(ps);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = Soft;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        return ps;
    }

    // ------------------------------------------------------------------ helpers

    static Transform Root(string name, Vector3 pos, Vector3 dir)
    {
        var go = new GameObject(name);
        go.transform.position = pos;
        if (dir.sqrMagnitude > 1e-4f) go.transform.rotation = Quaternion.LookRotation(dir);
        return go.transform;
    }

    static void Finish(Transform root, float life)
    {
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>()) ps.Play();
        Object.Destroy(root.gameObject, life);
    }

    static ParticleSystem System(Transform parent, string name, float duration)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main;
        m.duration = duration;
        m.loop = false;
        m.playOnAwake = false;
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.maxParticles = 100;
        var em = ps.emission; em.rateOverTime = 0f;
        var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.01f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = Soft;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return ps;
    }

    static void Burst(ParticleSystem ps, float t, int count)
    {
        var em = ps.emission;
        em.SetBursts(new[] { new ParticleSystem.Burst(t, (short)Mathf.Max(1, count)) });
    }

    static void Stretch(ParticleSystem ps, float velocityScale)
    {
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = velocityScale;
        r.lengthScale = 1f;
    }

    static void Fade(ParticleSystem ps)
    {
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
    }
}
