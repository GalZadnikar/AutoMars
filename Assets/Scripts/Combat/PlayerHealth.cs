using System;
using UnityEngine;

/// The player's HP rules (the HP itself is the Health component next to it, team Player):
/// - heals over time while standing inside the home base radius (Base_Home), after a short delay since the last hit;
/// - at 0 HP: knocked out (input off, body tips over, carried trash dropped), then respawns at the base at full HP.
/// Creates itself (and the Health) on the player at scene load if the scene doesn't have them yet.
[RequireComponent(typeof(Health))]
public class PlayerHealth : MonoBehaviour
{
    public static PlayerHealth Instance { get; private set; }
    /// Debug GOD MODE: the player takes no damage.
    public static bool GodMode;
    /// True while knocked out (JoystickPlayerController ignores input).
    public static bool KnockedOut => Instance != null && Instance.ko;

    public static event Action KnockedOutEvent;
    public static event Action Respawned;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; GodMode = false; KnockedOutEvent = null; Respawned = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<PlayerHealth>() != null) return;
        var carrier = FindFirstObjectByType<PlayerCarrier>();
        if (carrier == null) return;
        carrier.gameObject.AddComponent<PlayerHealth>();
    }

    [Header("HP")]
    public float maxHP = 100f;

    [Header("Healing at the base")]
    [Tooltip("HP per second while inside the home base radius")]
    public float healRate = 10f;
    [Tooltip("Seconds after the last hit before healing starts")]
    public float healDelay = 1f;

    [Header("Knock-out")]
    public float knockOutTime = 3f;
    public bool dropTrashOnKnockOut = true;
    [Tooltip("Respawn this far from the base centre (fraction of its radius), on the side where the player went down")]
    [Range(0f, 1f)] public float respawnRadiusFraction = 0.6f;

    public Health Health { get; private set; }
    /// Healing right now (inside the base, not full).
    public bool IsHealing { get; private set; }
    /// 0..1 progress of the knock-out timer.
    public float KnockOut01 => ko ? Mathf.Clamp01(koT / Mathf.Max(0.01f, knockOutTime)) : 0f;
    public float KnockOutLeft => ko ? Mathf.Max(0f, knockOutTime - koT) : 0f;

    bool ko;
    float koT, tilt;
    Transform armature;
    Quaternion armatureRot;
    Vector3 armaturePos;
    Animator anim;
    CharacterController controller;

    void Awake()
    {
        Instance = this;
        Health = GetComponent<Health>();
        Health.SetTeam(Team.Player);
        Health.SetMax(maxHP);
        Health.showBar = false;            // the player has the HUD bar instead
        Health.aimHeight = 1.3f;
        Health.hitRadius = 0.75f;
        Health.OnDied += KnockOut;
        anim = GetComponent<Animator>();
        controller = GetComponent<CharacterController>();
        armature = transform.Find("Armature");
        if (armature != null) { armatureRot = armature.localRotation; armaturePos = armature.localPosition; }
    }

    void OnDestroy()
    {
        if (Health != null) Health.OnDied -= KnockOut;
        if (Instance == this) Instance = null;
    }

    /// The base that heals (the one with the pipe), or the first HomeBase.
    public static HomeBase HomeBaseForHealing()
    {
        var pt = PipeTether.Instance;
        if (pt != null)
        {
            var hb = pt.GetComponent<HomeBase>();
            if (hb != null) return hb;
        }
        return HomeBase.All.Count > 0 ? HomeBase.All[0] : null;
    }

    void Update()
    {
        Health.invulnerable = GodMode || ko;

        if (ko)
        {
            koT += Time.deltaTime;
            tilt = Mathf.MoveTowards(tilt, 1f, Time.deltaTime * 3f);
            if (koT >= knockOutTime) Respawn();
            return;
        }
        tilt = Mathf.MoveTowards(tilt, 0f, Time.deltaTime * 4f);

        IsHealing = false;
        if (!Health.IsFull && Health.SinceDamaged >= healDelay)
        {
            bool atBase = false;   // every Base heals
            foreach (var hb in HomeBase.All) if (hb != null && hb.InRange(transform.position)) { atBase = true; break; }
            if (atBase)
            {
                Health.Heal(healRate * Time.deltaTime);
                IsHealing = true;
            }
        }
    }

    // tip the body over while knocked out (after the Animator wrote the pose)
    void LateUpdate()
    {
        if (armature == null) return;
        if (tilt <= 0.001f)
        {
            if (armature.localRotation != armatureRot) { armature.localRotation = armatureRot; armature.localPosition = armaturePos; }
            return;
        }
        float e = tilt * tilt * (3f - 2f * tilt);
        armature.localRotation = Quaternion.AngleAxis(80f * e, Vector3.right) * armatureRot;   // falls forward (backpack stays up)
        armature.localPosition = armaturePos + new Vector3(0f, -0.45f * e, 0f);
    }

    void KnockOut()
    {
        if (ko) return;
        ko = true;
        koT = 0f;
        if (anim != null) anim.SetInteger("AnimationPar", 0);
        int dropped = 0;
        if (dropTrashOnKnockOut)
        {
            var carrier = GetComponent<PlayerCarrier>();
            if (carrier != null) dropped = carrier.DropAll();
        }
        CombatFX.Hit(transform.position + Vector3.up * 1.2f, Vector3.up, new Color(1f, 0.35f, 0.25f), 1.6f);
        GameToast.Show(dropped > 0 ? "KNOCKED OUT! TRASH BAG LOST (" + dropped + ")" : "KNOCKED OUT!", new Color(1f, 0.35f, 0.3f));
        KnockedOutEvent?.Invoke();
    }

    void Respawn()
    {
        var home = HomeBaseForHealing();
        if (home != null)
        {
            Vector3 c = home.transform.position;
            Vector3 dir = transform.position - c; dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.back;
            Vector3 p = c + dir.normalized * home.radius * respawnRadiusFraction;
            p.y = GroundUtil.Height(p, transform.position.y) + 0.05f;
            Teleport(p);
            Vector3 look = c - p; look.y = 0f;
            if (look.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(-look);
        }
        ko = false;
        tilt = 0f;
        Health.HealFull();
        GameToast.Show("BACK AT THE BASE", new Color(0.45f, 0.95f, 1f));
        Respawned?.Invoke();
    }

    public void Teleport(Vector3 p)
    {
        if (controller != null) controller.enabled = false;
        transform.position = p;
        if (controller != null) controller.enabled = true;
    }
}
