using UnityEngine;

/// One-shot particle burst when a trash pile is fully cleared: a dust puff, flying scraps and a few sparkles.
/// Built in code (no prefab). Material / texture come from Resources/FX.
public static class PileClearFX
{
    static Material mat, solid;
    static Mesh cube;

    static Material Mat
    {
        get
        {
            if (mat == null) mat = Resources.Load<Material>("FX/ParticleSoft");
            return mat;
        }
    }

    /// size = radius of the pile in metres.
    public static void Play(Vector3 pos, float size)
    {
        size = Mathf.Clamp(size, 1.5f, 10f);
        var root = new GameObject("Pile Cleared FX");
        root.transform.position = pos + Vector3.up * 0.2f;

        // dust puff: big soft tan clouds rolling outwards
        var dust = System(root.transform, "Dust", 1.6f);
        {
            var m = dust.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(size * 0.4f, size * 0.9f);
            m.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size * 0.95f);
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.78f, 0.58f, 0.40f, 0.65f), new Color(0.62f, 0.44f, 0.30f, 0.5f));
            m.gravityModifier = -0.05f;
            var em = dust.emission; em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.RoundToInt(10 + size * 3)) });
            var sh = dust.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = size * 0.5f; sh.rotation = new Vector3(-90f, 0f, 0f);
            var vel = dust.limitVelocityOverLifetime; vel.enabled = true; vel.limit = 0.6f; vel.dampen = 0.12f;
            Fade(dust, 1.6f);
        }

        // scraps: little cubes (bags / boxes bits) thrown up and falling back
        var bits = System(root.transform, "Scraps", 2f);
        {
            var m = bits.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.5f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(4f, 7f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.32f);
            m.startRotation3D = true;
            m.startRotationX = m.startRotationY = m.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.gravityModifier = 1.6f;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(0.25f, 0.3f, 0.22f), 0f), new GradientColorKey(new Color(0.55f, 0.42f, 0.28f), 0.5f), new GradientColorKey(new Color(0.85f, 0.8f, 0.7f), 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            m.startColor = new ParticleSystem.MinMaxGradient(g) { mode = ParticleSystemGradientMode.RandomColor };
            var em = bits.emission; em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.RoundToInt(14 + size * 5)) });
            var sh = bits.shape; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = size * 0.4f; sh.rotation = new Vector3(-90f, 0f, 0f);
            var rot = bits.rotationOverLifetime; rot.enabled = true; rot.separateAxes = true;
            rot.x = rot.y = rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            var col = bits.collision; col.enabled = true; col.type = ParticleSystemCollisionType.World; col.dampen = 0.5f; col.bounce = 0.3f; col.lifetimeLoss = 0.2f;
            var sol = bits.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.75f, 1f), new Keyframe(1f, 0f)));
            var r = bits.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            if (cube == null) cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            r.mesh = cube;
            if (solid == null) solid = Resources.Load<Material>("FX/ParticleSolid");
            if (solid != null) r.sharedMaterial = solid;
        }

        // sparkles: a quick "done!" pop of bright points going up
        var spark = System(root.transform, "Sparkles", 1.2f);
        {
            var m = spark.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4.5f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.3f);
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.45f, 1f), new Color(0.5f, 1f, 0.55f, 1f));
            m.gravityModifier = 0.3f;
            var em = spark.emission; em.SetBursts(new[] { new ParticleSystem.Burst(0.05f, 18) });
            var sh = spark.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 30f; sh.radius = size * 0.3f; sh.rotation = new Vector3(-90f, 0f, 0f);
            Fade(spark, 1f);
        }

        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>()) ps.Play();
        Object.Destroy(root, 3f);
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
        m.maxParticles = 200;
        var em = ps.emission; em.rateOverTime = 0f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = Mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return ps;
    }

    static void Fade(ParticleSystem ps, float _)
    {
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
    }
}
