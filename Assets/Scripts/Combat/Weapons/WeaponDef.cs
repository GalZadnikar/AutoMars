using UnityEngine;

public enum WeaponMode { Projectile, Beam }

/// One weapon the player can hold: its model, its numbers, how its shot behaves and which effects it uses.
/// The assets live in Resources/Weapons (made by Tools > AutoMars > Weapon > Build Weapons); change the numbers there.
[CreateAssetMenu(menuName = "AutoMars/Weapon", fileName = "Weapon")]
public class WeaponDef : ScriptableObject
{
    [Header("Identity")]
    public string displayName = "PISTOL";
    [Tooltip("Shown on the HUD tile")]
    public Sprite icon;
    [Tooltip("Colour of the muzzle light, the pickup ring and the toast")]
    public Color color = new Color(1f, 0.78f, 0.3f);

    [Tooltip("Projectile = it fires shots. Beam = a continuous beam on one enemy (see the Beam section)")]
    public WeaponMode mode;

    [Header("Model")]
    public GameObject model;
    public float modelScale = 1.4f;
    [Tooltip("Where the hand holds it, in the model's own space")]
    public Vector3 grip;
    [Tooltip("End of the barrel, in the model's own space")]
    public Vector3 muzzle;

    [Header("Shooting")]
    [Tooltip("Damage of one projectile")]
    public float damage = 4f;
    [Tooltip("How far he shoots (m). Keep it inside the player's sight (about 6.5)")]
    public float range = 6f;
    [Tooltip("Shots per magazine")]
    [Min(1)] public int magazine = 6;
    [Tooltip("Seconds between two shots")]
    public float fireInterval = 0.26f;
    [Tooltip("Seconds to reload")]
    public float reloadTime = 1.1f;
    [Tooltip("Projectiles per shot (a scatter gun fires several)")]
    [Min(1)] public int pellets = 1;
    [Tooltip("Width of the fan the projectiles are spread over (degrees)")]
    public float spread;

    [Header("Projectile")]
    [Tooltip("Flight speed (m/s)")]
    public float speed = 30f;
    [Tooltip("How hard it turns toward the target it was fired at (degrees per second, 0 = straight line)")]
    public float homing = 120f;
    [Tooltip("Size of the projectile for hits (m), added to the enemy's own hit radius")]
    public float radius = 0.15f;
    [Tooltip("How many more enemies it flies on through after the first one")]
    public int pierce;
    [Tooltip("Above 0: it explodes, and every other enemy within this distance (m) takes Splash Damage")]
    public float splashRadius;
    public float splashDamage;
    [Tooltip("How much of an enemy's normal knockback one hit gives (1 = same as a pickaxe blow)")]
    [Range(0f, 1.5f)] public float knockback = 0.3f;

    [Header("Beam (only when Mode = Beam)")]
    [Tooltip("Laser prefab (Hovl 3D Lasers Pack: a LineRenderer with Flash and Hit children)")]
    public GameObject beamFx;
    public float beamScale = 0.5f;
    [Tooltip("Seconds between two damage ticks. Every tick uses one round of the magazine and deals Damage")]
    public float beamTick = 0.2f;
    [Tooltip("Damage of one tick after the beam has stayed on the same enemy for Ramp Time (it starts at Damage)")]
    public float damageMax = 4f;
    [Tooltip("Seconds on the same enemy until the damage is at Damage Max. A new enemy starts over")]
    public float rampTime = 2.4f;

    [Header("Feel")]
    [Tooltip("How far the arm kicks up on a shot (degrees)")]
    public float recoilAngle = 24f;
    [Tooltip("How far the gun slides back on a shot (m)")]
    public float recoilBack = 0.1f;
    [Tooltip("Seconds the recoil takes to settle")]
    public float recoilTime = 0.16f;
    [Tooltip("Camera kick on a shot / a hit / the kill (0 = none)")]
    public float shotShake = 0.05f;
    public float hitShake = 0.08f;
    public float killShake = 0.3f;
    [Tooltip("Brightness of the light flash at the muzzle (0 = none)")]
    public float flashLight = 5f;
    [Tooltip("A brass casing flies out on every shot")]
    public bool casings = true;

    [Header("Effects (Epic Toon FX prefabs)")]
    [Tooltip("The flying projectile")]
    public GameObject projectileFx;
    public float projectileScale = 0.45f;
    [Tooltip("At the muzzle on every shot")]
    public GameObject muzzleFx;
    public float muzzleScale = 0.4f;
    [Tooltip("Where the projectile lands")]
    public GameObject impactFx;
    public float impactScale = 0.4f;

    [Header("Extra looks")]
    [Tooltip("Above 0: every projectile drags a bright streak of this length (m) behind it")]
    public float tracerLength;
    public float tracerWidth = 0.14f;
    [Tooltip("While he shoots, a faint fan on the ground shows how far and how wide the weapon reaches")]
    public bool rangeArc;

    public static WeaponDef[] LoadAll() => Resources.LoadAll<WeaponDef>("Weapons");
}
