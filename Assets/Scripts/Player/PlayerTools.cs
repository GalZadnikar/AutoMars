using UnityEngine;

public enum PlayerTool { Pickaxe, Gun }

/// What the player holds: the pickaxe or the suction gun. Only one is out at a time.
///  - Auto switch (default): the pickaxe comes out next to something to smash, the gun next to sand.
///    Away from both, the last tool stays in the hands.
///  - Toggle() / Equip() switch by hand (for a button); that turns auto switching off until SetAuto(true).
/// While the gun is out both arms are pointed at it after the Animator has run, so it is held in the hands
/// and stays steady in front of the body while the legs keep their idle / run animation.
public class PlayerTools : MonoBehaviour
{
    public PlayerTool tool = PlayerTool.Pickaxe;
    public bool autoSwitch = true;
    [Tooltip("The pickaxe comes out this far before a melee target is in reach (m)")]
    public float pickaxeNear = 2.5f;
    [Tooltip("The gun comes out this far before sand is in its range (m)")]
    public float gunNear = 1.5f;
    [Tooltip("Shortest time between two automatic switches (s)")]
    public float minSwitchTime = 0.35f;

    [Header("Parts")]
    public GameObject pickaxe;
    public SuctionGun gun;
    public Transform rightUpperArm, rightForearm, leftUpperArm, leftForearm;

    [Header("Gun hold")]
    [Tooltip("Where the right / left hand goes, in the gun's own space")]
    public Vector3 rightGrip = new Vector3(0.12f, 0f, -0.2f);
    public Vector3 leftGrip = new Vector3(-0.12f, -0.02f, -0.1f);
    [Tooltip("Seconds the arms take to move onto / off the gun")]
    public float holdBlendTime = 0.15f;

    PlayerMelee melee;
    float hold, sinceSwitch = 99f;

    void Awake()
    {
        melee = GetComponent<PlayerMelee>();
        if (gun == null) gun = FindFirstObjectByType<SuctionGun>();
        Apply();
    }

    public void Equip(PlayerTool t) { autoSwitch = false; Set(t); }
    public void Toggle() => Equip(tool == PlayerTool.Gun ? PlayerTool.Pickaxe : PlayerTool.Gun);
    public void SetAuto(bool on) => autoSwitch = on;

    void Set(PlayerTool t)
    {
        if (tool == t) return;
        tool = t;
        sinceSwitch = 0f;
        Apply();
    }

    void Apply()
    {
        bool gunOut = tool == PlayerTool.Gun;
        if (pickaxe != null && pickaxe.activeSelf == gunOut) pickaxe.SetActive(!gunOut);
        if (gun != null) gun.Holstered = !gunOut;
        if (melee != null && melee.enabled == gunOut) melee.enabled = !gunOut;
    }

    void Update()
    {
        sinceSwitch += Time.deltaTime;
        if (autoSwitch && sinceSwitch >= minSwitchTime)
        {
            if (TargetNear()) Set(PlayerTool.Pickaxe);
            else if (SandNear()) Set(PlayerTool.Gun);
        }
        Apply();   // also keeps a gun that was (re)built later in the right state
    }

    bool TargetNear()
    {
        if (melee == null) return false;
        var all = MeleeTarget.All;
        for (int i = 0; i < all.Count; i++)
        {
            var t = all[i];
            if (t == null || !t.Alive) continue;
            Vector3 d = t.transform.position - transform.position; d.y = 0f;
            if (d.magnitude - t.radius <= melee.range + pickaxeNear) return true;
        }
        return false;
    }

    bool SandNear()
    {
        if (gun == null || gun.IsFull) return false;
        float reach = gun.range + gunNear;
        for (int i = 0; i < SandField.All.Count; i++)
        {
            var f = SandField.All[i];
            if (f != null && !f.IsCleared && f.DistanceTo(transform.position) <= reach) return true;
        }
        return false;
    }

    // after the Animator: point the arms at the gun
    void LateUpdate()
    {
        Transform g = gun != null ? gun.GunTransform : null;
        bool holding = tool == PlayerTool.Gun && g != null && g.gameObject.activeInHierarchy;
        hold = Mathf.MoveTowards(hold, holding ? 1f : 0f, holdBlendTime > 0f ? Time.deltaTime / holdBlendTime : 1f);
        if (hold <= 0f || g == null) return;
        Aim(rightUpperArm, rightForearm, g.TransformPoint(rightGrip));
        Aim(leftUpperArm, leftForearm, g.TransformPoint(leftGrip));
    }

    /// Straight arm from the shoulder to the point (the bones' Y axis runs along the arm).
    void Aim(Transform upper, Transform fore, Vector3 point)
    {
        if (upper == null) return;
        Point(upper, point);
        if (fore != null) Point(fore, point);
    }

    void Point(Transform bone, Vector3 point)
    {
        Vector3 to = point - bone.position;
        if (to.sqrMagnitude < 1e-6f) return;
        Quaternion aimed = Quaternion.FromToRotation(bone.up, to) * bone.rotation;
        bone.rotation = Quaternion.Slerp(bone.rotation, aimed, hold);
    }
}
