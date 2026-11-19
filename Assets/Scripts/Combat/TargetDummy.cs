using UnityEngine;

/// Enemy-team test target (debug menu: SPAWN DUMMY / SPAWN SHOOTER). Built in code: a post with a target board.
/// Takes damage, shows its HP bar, explodes at 0 HP. With `shoots` on it fires at the nearest player-team unit
/// (the player or robots) in range, so the enemy side of combat can be tested before real enemies exist.
[RequireComponent(typeof(Health))]
public class TargetDummy : MonoBehaviour
{
    public bool shoots;
    public float range = 12f;
    public float fireInterval = 1.2f;
    public ShotSpec shot = new ShotSpec { damage = 8f, speed = 16f, color = new Color(1f, 0.3f, 0.2f), radius = 0.14f };

    Health hp;
    Transform board, gun;
    float fireT, wobble;
    Health target;

    static Material lit;

    /// Spawns a dummy on the ground at `pos`.
    public static TargetDummy Spawn(Vector3 pos, bool shoots, float maxHP = 100f)
    {
        var go = new GameObject(shoots ? "Target Dummy (Shooter)" : "Target Dummy");
        pos.y = GroundUtil.Height(pos, pos.y);
        go.transform.position = pos;
        var h = go.AddComponent<Health>();
        h.SetTeam(Team.Enemy);   // AddComponent already ran OnEnable with the default team
        h.SetMax(maxHP);
        h.aimHeight = 1.3f;
        h.hitRadius = 0.7f;
        h.barHeight = 2.6f;
        go.AddComponent<DeathFX>().size = 1.1f;
        var d = go.AddComponent<TargetDummy>();
        d.shoots = shoots;
        d.Build();
        return d;
    }

    void Awake()
    {
        hp = GetComponent<Health>();
        hp.OnDamaged += (a, s) => wobble = 1f;
    }

    void Build()
    {
        if (lit == null) lit = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        Part(PrimitiveType.Cylinder, transform, new Vector3(0f, 0.6f, 0f), new Vector3(0.18f, 0.6f, 0.18f), new Color(0.35f, 0.32f, 0.3f));
        Part(PrimitiveType.Cylinder, transform, new Vector3(0f, 0.05f, 0f), new Vector3(0.9f, 0.05f, 0.9f), new Color(0.25f, 0.23f, 0.22f));
        board = new GameObject("Board").transform;
        board.SetParent(transform, false);
        board.localPosition = new Vector3(0f, 1.3f, 0f);
        var ring = shoots ? new Color(0.85f, 0.25f, 0.2f) : new Color(0.95f, 0.55f, 0.15f);
        Part(PrimitiveType.Cylinder, board, Vector3.zero, new Vector3(1.1f, 0.05f, 1.1f), ring).localRotation = Quaternion.Euler(90f, 0f, 0f);
        Part(PrimitiveType.Cylinder, board, new Vector3(0f, 0f, -0.03f), new Vector3(0.7f, 0.05f, 0.7f), new Color(0.92f, 0.9f, 0.85f)).localRotation = Quaternion.Euler(90f, 0f, 0f);
        Part(PrimitiveType.Cylinder, board, new Vector3(0f, 0f, -0.06f), new Vector3(0.3f, 0.05f, 0.3f), ring).localRotation = Quaternion.Euler(90f, 0f, 0f);
        if (shoots)
        {
            gun = Part(PrimitiveType.Cube, board, new Vector3(0f, 0.75f, 0f), new Vector3(0.25f, 0.25f, 0.7f), new Color(0.2f, 0.2f, 0.22f));
        }
    }

    Transform Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color c)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());   // projectiles hit the Health sphere, not colliders
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = lit;
        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_BaseColor", c);
        r.SetPropertyBlock(mpb);
        return go.transform;
    }

    void Update()
    {
        if (hp.IsDead) return;
        wobble = Mathf.MoveTowards(wobble, 0f, Time.deltaTime * 2.5f);
        float a = Mathf.Sin(Time.time * 28f) * 12f * wobble;

        if (shoots)
        {
            if (!Targeting.StillValid(target, transform.position, range))
                target = Targeting.FindNearest(Team.Player, transform.position, range, null, false, false);
            if (target != null)
            {
                Vector3 d = target.transform.position - transform.position; d.y = 0f;
                if (d.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 360f * Time.deltaTime);
                fireT -= Time.deltaTime;
                if (fireT <= 0f && gun != null)
                {
                    fireT = fireInterval;
                    Vector3 muzzle = gun.position + gun.forward * 0.4f;
                    Projectile.Fire(muzzle, target, shot, Team.Enemy, gameObject);
                }
            }
        }
        if (board != null) board.localRotation = Quaternion.Euler(a, 0f, 0f);
    }
}
