using System;
using System.Collections.Generic;
using UnityEngine;

public enum Team { Player, Enemy }

/// Hit points for anything that can be shot: the player, robots, drones, enemies, dummies.
/// One per unit. Keeps a static list per team for targeting (Targeting.FindNearest).
/// The small billboard HP bar (HealthBar) is created at runtime and only shows after damage.
public class Health : MonoBehaviour
{
    static readonly List<Health> playerTeam = new List<Health>();
    static readonly List<Health> enemyTeam = new List<Health>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { playerTeam.Clear(); enemyTeam.Clear(); }

    /// Every living-or-down unit of a team (check Targetable before shooting).
    public static List<Health> All(Team team) => team == Team.Player ? playerTeam : enemyTeam;
    public static Team Opponent(Team team) => team == Team.Player ? Team.Enemy : Team.Player;

    public Team team = Team.Player;
    public float maxHP = 100f;
    [Tooltip("Height above the pivot that projectiles aim at")]
    public float aimHeight = 1f;
    [Tooltip("Projectiles within this distance of the aim point count as a hit")]
    public float hitRadius = 0.6f;
    [Tooltip("No damage taken (GOD MODE, knocked-out player, ...)")]
    public bool invulnerable;
    [Tooltip("Can enemies pick this as a target (still takes damage either way)")]
    public bool targetable = true;

    [Header("HP bar")]
    public bool showBar = true;
    [Tooltip("Height of the billboard bar above the pivot")]
    public float barHeight = 2.4f;
    public float barWidth = 1.3f;

    /// amount, source (the shooter, may be null)
    public event Action<float, GameObject> OnDamaged;
    public event Action OnDied;
    public event Action OnHealed;

    public float Current { get; private set; } = -1f;
    public float Fraction => maxHP > 0f ? Mathf.Clamp01(Current / maxHP) : 0f;
    public bool IsDead => Current <= 0f;
    public bool IsFull => Current >= maxHP;
    /// Time.time of the last hit that did damage (-inf if never).
    public float LastDamageTime { get; private set; } = -999f;
    public float SinceDamaged => Time.time - LastDamageTime;
    public Vector3 AimPoint => transform.position + Vector3.up * aimHeight;
    /// Alive, targetable and active: what targeting is allowed to pick.
    public bool Targetable => targetable && !IsDead && isActiveAndEnabled;

    HealthBar bar;

    void Awake()
    {
        if (Current < 0f) Current = maxHP;
    }

    void Start()
    {
        if (showBar && bar == null) bar = HealthBar.Create(this);
    }

    void OnEnable()
    {
        var list = All(team);
        if (!list.Contains(this)) list.Add(this);
    }

    void OnDisable()
    {
        playerTeam.Remove(this);
        enemyTeam.Remove(this);
    }

    /// Change team at runtime (moves it to the other list).
    public void SetTeam(Team t)
    {
        if (team == t) return;
        OnDisable();
        team = t;
        if (isActiveAndEnabled) OnEnable();
    }

    /// Sets max HP. keepFraction = scale current HP with it, otherwise fill up.
    public void SetMax(float max, bool keepFraction = false)
    {
        float f = Current < 0f ? 1f : Fraction;
        maxHP = Mathf.Max(1f, max);
        Current = keepFraction ? maxHP * f : maxHP;
    }

    /// Deals damage. Returns the damage actually done (0 if dead / invulnerable).
    public float Damage(float amount, GameObject source = null)
    {
        if (Current < 0f) Current = maxHP;
        if (amount <= 0f || IsDead || invulnerable) return 0f;
        float done = Mathf.Min(amount, Current);
        Current -= done;
        LastDamageTime = Time.time;
        OnDamaged?.Invoke(done, source);
        if (Current <= 0f)
        {
            Current = 0f;
            OnDied?.Invoke();
        }
        return done;
    }

    public void Heal(float amount)
    {
        if (Current < 0f) Current = maxHP;
        if (amount <= 0f || Current >= maxHP) return;
        bool wasDead = IsDead;
        Current = Mathf.Min(maxHP, Current + amount);
        if (!wasDead || Current > 0f) OnHealed?.Invoke();
    }

    /// Back to full HP (also brings a dead unit back).
    public void HealFull() => Heal(maxHP);

    public void Kill(GameObject source = null)
    {
        bool inv = invulnerable;
        invulnerable = false;
        Damage(Mathf.Max(1f, Current), source);
        invulnerable = inv;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = team == Team.Player ? Color.cyan : Color.red;
        Gizmos.DrawWireSphere(AimPoint, hitRadius);
    }
}
