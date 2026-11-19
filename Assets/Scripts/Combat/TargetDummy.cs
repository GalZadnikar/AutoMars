using UnityEngine;

/// Enemy-team test target (debug menu: SPAWN DUMMY / SPAWN SHOOTER). Built in code: a post with a target board.
/// Takes damage, shows its HP bar, explodes at 0 HP. With `shoots` on it fires at the nearest player-team unit
/// (the player or robots) in range, so the enemy side of combat can be tested before real enemies exist.
[RequireComponent(typeof(Health))]
public class TargetDummy : MonoBehaviour
{
    public const string PrefabResource = "Enemies/Target Dummy";

    [Tooltip("Turns and fires at the nearest player-team unit (the player or robots)")]
    public bool shoots;
    public float range = 12f;
    public float fireInterval = 1.2f;
    public ShotSpec shot = new ShotSpec { damage = 8f, speed = 16f, color = new Color(1f, 0.3f, 0.2f), radius = 0.14f };

    Health hp;
    // built once (in code or when the prefab is made) and saved with the object
    [SerializeField, HideInInspector] Transform board, gun;
    [SerializeField, HideInInspector] MeshTint ringOuter, ringCenter;
    float fireT, wobble;
    Health target;

    /// Spawns a dummy on the ground at `pos` (from the prefab Assets/Resources/Enemies/Target Dummy.prefab if it exists).
    public static TargetDummy Spawn(Vector3 pos, bool shoots, float maxHP = 100f)
    {
        pos.y = GroundUtil.Height(pos, pos.y);
        TargetDummy d;
        var prefab = Resources.Load<GameObject>(PrefabResource);
        if (prefab != null)
        {
            d = Instantiate(prefab, pos, Quaternion.identity).GetComponent<TargetDummy>();
            d.GetComponent<Health>().SetMax(maxHP);
        }
        else
        {
            var go = new GameObject();
            go.transform.position = pos;
            d = Create(go, maxHP);
        }
        d.shoots = shoots;
        d.gameObject.name = shoots ? "Target Dummy (Shooter)" : "Target Dummy";
        d.ApplyLook();
        return d;
    }

    /// Adds and sets up all components on `go` (Spawn without a prefab, and the prefab builder in Edit mode).
    public static TargetDummy Create(GameObject go, float maxHP = 100f)
    {
        var h = go.AddComponent<Health>();
        h.SetTeam(Team.Enemy);   // AddComponent already ran OnEnable with the default team
        h.SetMax(maxHP);
        h.aimHeight = 1.3f;
        h.hitRadius = 0.7f;
        h.barHeight = 2.6f;
        go.AddComponent<DeathFX>().size = 1.1f;
        var d = go.AddComponent<TargetDummy>();
        d.Build();
        d.ApplyLook();
        return d;
    }

    void Awake()
    {
        hp = GetComponent<Health>();
        hp.OnDamaged += (a, s) => wobble = 1f;
        if (board == null) board = transform.Find("Board");
        if (board == null) Build();
        ApplyLook();
    }

    /// Builds post, base, target board and gun. Does nothing if it is already built.
    public void Build()
    {
        if (board != null) return;
        Part(PrimitiveType.Cylinder, transform, new Vector3(0f, 0.6f, 0f), new Vector3(0.18f, 0.6f, 0.18f), new Color(0.35f, 0.32f, 0.3f)).name = "Post";
        Part(PrimitiveType.Cylinder, transform, new Vector3(0f, 0.05f, 0f), new Vector3(0.9f, 0.05f, 0.9f), new Color(0.25f, 0.23f, 0.22f)).name = "Foot";
        board = new GameObject("Board").transform;
        board.SetParent(transform, false);
        board.localPosition = new Vector3(0f, 1.3f, 0f);
        var outer = Part(PrimitiveType.Cylinder, board, Vector3.zero, new Vector3(1.1f, 0.05f, 1.1f), Color.white);
        outer.name = "Ring"; outer.localRotation = Quaternion.Euler(90f, 0f, 0f);
        var white = Part(PrimitiveType.Cylinder, board, new Vector3(0f, 0f, -0.03f), new Vector3(0.7f, 0.05f, 0.7f), new Color(0.92f, 0.9f, 0.85f));
        white.name = "White"; white.localRotation = Quaternion.Euler(90f, 0f, 0f);
        var center = Part(PrimitiveType.Cylinder, board, new Vector3(0f, 0f, -0.06f), new Vector3(0.3f, 0.05f, 0.3f), Color.white);
        center.name = "Bull"; center.localRotation = Quaternion.Euler(90f, 0f, 0f);
        ringOuter = outer.GetComponent<MeshTint>();
        ringCenter = center.GetComponent<MeshTint>();
        gun = Part(PrimitiveType.Cube, board, new Vector3(0f, 0.75f, 0f), new Vector3(0.25f, 0.25f, 0.7f), new Color(0.2f, 0.2f, 0.22f));
        gun.name = "Gun";
    }

    /// Gun on/off and ring colour (red = shooter, orange = plain dummy) from `shoots`.
    public void ApplyLook()
    {
        if (gun == null && board != null) gun = board.Find("Gun");
        if (gun != null && gun.gameObject.activeSelf != shoots) gun.gameObject.SetActive(shoots);
        var ring = shoots ? new Color(0.85f, 0.25f, 0.2f) : new Color(0.95f, 0.55f, 0.15f);
        if (ringOuter != null) ringOuter.Set(ring);
        if (ringCenter != null) ringCenter.Set(ring);
    }

#if UNITY_EDITOR
    // ticking "shoots" in the Inspector shows / hides the gun right away (SetActive isn't allowed inside OnValidate itself)
    void OnValidate()
    {
        UnityEditor.EditorApplication.delayCall += () => { if (this != null) ApplyLook(); };
    }
#endif

    static Transform Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color c)
        => EnemyParts.Part(parent, type, pos, scale, c);

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
                if (fireT <= 0f && gun != null && gun.gameObject.activeInHierarchy)
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
