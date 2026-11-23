using UnityEngine;

public enum PlayerTool { Pickaxe, Gun, Shovel }

/// What the player holds: the pickaxe (rocks), or his sand tool - the shovel (stage 1) or the suction gun (later).
/// Only one is out at a time.
///  - `sandTool` is the progression state: which tool he owns for sand.
///  - Auto switch (default): the pickaxe comes out next to something to smash, the sand tool next to sand.
///    Away from both, the last tool stays in the hands.
///  - Toggle() / Equip() switch by hand (for a button); that turns auto switching off until SetAuto(true).
/// While the gun is out both arms are pointed at it after the Animator has run, so it is held in the hands
/// and stays steady in front of the body while the legs keep their idle / run animation.
public class PlayerTools : MonoBehaviour
{
    public PlayerTool tool = PlayerTool.Pickaxe;
    [Tooltip("The tool used on sand: Shovel at the start of the game, Gun once it is unlocked")]
    public PlayerTool sandTool = PlayerTool.Gun;
    public bool autoSwitch = true;
    [Tooltip("The pickaxe comes out this far before a melee target is in reach (m)")]
    public float pickaxeNear = 2.5f;
    [Tooltip("The gun comes out this far before sand is in its range (m)")]
    public float gunNear = 1.5f;
    [Tooltip("The shovel comes out this far before sand is in its reach (m)")]
    public float shovelNear = 2f;
    [Tooltip("Shortest time between two automatic switches (s)")]
    public float minSwitchTime = 0.35f;

    [Header("Parts")]
    public GameObject pickaxe;
    public GameObject shovelModel;
    public SuctionGun gun;
    public Transform rightUpperArm, rightForearm, leftUpperArm, leftForearm;

    [Header("Gun hold")]
    [Tooltip("Where the right / left hand goes, in the gun's own space")]
    public Vector3 rightGrip = new Vector3(0.12f, 0f, -0.2f);
    public Vector3 leftGrip = new Vector3(-0.12f, -0.02f, -0.1f);
    [Tooltip("Seconds the arms take to move onto / off the gun")]
    public float holdBlendTime = 0.15f;

    PlayerMelee melee;
    PlayerShovel shovel;
    float hold, sinceSwitch = 99f;

    void Awake()
    {
        melee = GetComponent<PlayerMelee>();
        shovel = GetComponent<PlayerShovel>();
        if (gun == null) gun = FindFirstObjectByType<SuctionGun>();
        Apply();
    }

    public void Equip(PlayerTool t) { autoSwitch = false; Set(t); }
    public void Toggle() => Equip(tool == PlayerTool.Pickaxe ? sandTool : PlayerTool.Pickaxe);
    public void SetAuto(bool on) => autoSwitch = on;

    /// Progression: which tool the player has for sand. If he is holding the old one it is swapped right away.
    public void SetSandTool(PlayerTool t)
    {
        if (t == PlayerTool.Pickaxe) return;
        sandTool = t;
        if (tool != PlayerTool.Pickaxe) Set(t);
    }

    void Set(PlayerTool t)
    {
        if (tool == t) return;
        tool = t;
        sinceSwitch = 0f;
        Apply();
    }

    void Apply()
    {
        if (pickaxe != null && pickaxe.activeSelf != (tool == PlayerTool.Pickaxe)) pickaxe.SetActive(tool == PlayerTool.Pickaxe);
        if (shovelModel != null && shovelModel.activeSelf != (tool == PlayerTool.Shovel)) shovelModel.SetActive(tool == PlayerTool.Shovel);
        if (gun != null) gun.Holstered = tool != PlayerTool.Gun;
        if (melee != null && melee.enabled != (tool == PlayerTool.Pickaxe)) melee.enabled = tool == PlayerTool.Pickaxe;
        if (shovel != null && shovel.enabled != (tool == PlayerTool.Shovel)) shovel.enabled = tool == PlayerTool.Shovel;
    }

    void Update()
    {
        sinceSwitch += Time.deltaTime;
        if (autoSwitch)
        {
            if (tool != PlayerTool.Pickaxe && tool != sandTool) Set(sandTool);   // holding a sand tool he no longer uses
            else if (sinceSwitch >= minSwitchTime)
            {
                if (TargetNear()) Set(PlayerTool.Pickaxe);
                else if (SandNear()) Set(sandTool);
            }
        }
        Apply();   // also keeps a gun that was (re)built later in the right state
        // tough sand shows its lock while the sand tool he owns is too weak for it
        SandField.ToolPower = sandTool == PlayerTool.Gun ? (gun != null ? gun.power : 0) : (shovel != null ? shovel.power : 0);
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
        if (gun != null && gun.IsFull) return false;
        float reach;
        if (sandTool == PlayerTool.Shovel) { if (shovel == null) return false; reach = shovel.reach + shovelNear; }
        else { if (gun == null) return false; reach = gun.range + gunNear; }
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
