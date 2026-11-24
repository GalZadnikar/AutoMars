using System.Collections.Generic;
using UnityEngine;

/// Something the player can smash with the melee weapon (rock, crate, door ...). Uses Health for the hit points
/// and the HP bar, and adds the hit feel: the whole object flashes white for a moment, squashes and rocks away
/// from the blow, sparks fly from the contact point and a damage number pops up. The last hit shatters it.
/// For testing it comes back after `respawnDelay` seconds.
[RequireComponent(typeof(Health))]
public class MeleeTarget : MonoBehaviour
{
    public static readonly List<MeleeTarget> All = new List<MeleeTarget>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { All.Clear(); flashMat = null; }

    [Tooltip("The part that flashes, squashes and disappears when broken (default: the first child)")]
    public Transform visual;
    [Tooltip("Rough radius of the object: the player hits it from (his reach + this) away")]
    public float radius = 0.7f;

    [Header("Hit feel")]
    [Tooltip("Seconds the object is pure white after a hit")]
    public float flashTime = 0.07f;
    [ColorUsage(false, true)] public Color flashColor = new Color(2.2f, 2.2f, 2.2f);
    [Tooltip("How much it squashes on a hit (0.15 = 15%)")]
    [Range(0f, 0.5f)] public float punchScale = 0.16f;
    [Tooltip("Seconds the squash / wobble takes to settle")]
    public float punchTime = 0.22f;
    [Tooltip("How far it rocks away from the blow")]
    public float tiltDegrees = 8f;
    [Tooltip("Colour of the chips and debris")]
    public Color debrisColor = new Color(0.6f, 0.5f, 0.42f);
    public float fxSize = 1f;

    [Header("Reward")]
    [Tooltip("Stone the player gets when this breaks (0 = nothing)")]
    public int stoneYield;

    [Header("Break")]
    [Tooltip("Off = it doesn't shatter and vanish at 0 HP: another script on the object plays the death (enemies)")]
    public bool breakOnDeath = true;
    public float breakFXSize = 1.3f;
    [Tooltip("Seconds until it comes back after being destroyed (0 = it stays destroyed)")]
    public float respawnDelay = 2.5f;

    public Health HP => hp;
    public bool Alive => !broken && hp != null && !hp.IsDead && isActiveAndEnabled;
    public Vector3 Center => transform.position + Vector3.up * (hp != null ? hp.aimHeight : 0.5f);

    static Material flashMat;
    static MaterialPropertyBlock mpb;

    Health hp;
    Renderer[] renderers;
    Collider[] colliders;
    Material[][] normalMats, flashMats;
    MaterialPropertyBlock[] savedBlocks;
    bool[] hadBlock;
    Vector3 baseScale, punchDir;
    Quaternion baseRot;
    bool flashing, punching, broken, barWasShown;
    float flashT, punchAge, respawnT;

    void Awake()
    {
        hp = GetComponent<Health>();
        if (visual == null) visual = transform.childCount > 0 ? transform.GetChild(0) : transform;
        baseScale = visual.localScale;
        baseRot = visual.localRotation;

        var list = new List<Renderer>();
        foreach (var r in visual.GetComponentsInChildren<Renderer>(true))
            if (r is MeshRenderer || r is SkinnedMeshRenderer) list.Add(r);
        renderers = list.ToArray();
        colliders = visual.GetComponentsInChildren<Collider>(true);

        if (flashMat == null) flashMat = Resources.Load<Material>("FX/HitFlash");
        normalMats = new Material[renderers.Length][];
        flashMats = new Material[renderers.Length][];
        savedBlocks = new MaterialPropertyBlock[renderers.Length];
        hadBlock = new bool[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            normalMats[i] = renderers[i].sharedMaterials;
            flashMats[i] = new Material[normalMats[i].Length];
            for (int k = 0; k < flashMats[i].Length; k++) flashMats[i][k] = flashMat;
            savedBlocks[i] = new MaterialPropertyBlock();
        }
        hp.OnDied += Break;
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }
    void OnDestroy() { if (hp != null) hp.OnDied -= Break; }

    /// Where a blow coming from `from` lands on this object.
    public Vector3 HitPoint(Vector3 from)
    {
        Vector3 best = Center;
        float bestD = float.MaxValue;
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] == null || !colliders[i].enabled) continue;
            Vector3 p = colliders[i].bounds.ClosestPoint(from);
            float d = (p - from).sqrMagnitude;
            if (d < bestD) { bestD = d; best = p; }
        }
        if (bestD == float.MaxValue)
        {
            // no collider on the visual (creatures): the side of the body that faces the blow
            Vector3 to = from - Center; to.y = 0f;
            if (to.sqrMagnitude > 0.0001f) best = Center + to.normalized * radius * 0.8f;
        }
        return Vector3.Lerp(best, Center, 0.25f);
    }

    /// One blow. point = where it lands, dir = from the attacker to this object. Returns true if it destroyed it.
    public bool TakeHit(float damage, Vector3 point, Vector3 dir, GameObject source = null)
    {
        if (!Alive) return false;
        float done = hp.Damage(damage, source);   // the last hit runs Break() from inside here
        if (done <= 0f) return false;
        DamageNumber.Spawn(transform.position + Vector3.up * (hp.barHeight + 0.4f), done, broken);
        CombatFX.MeleeHit(point, -dir, debrisColor, broken ? fxSize * 1.6f : fxSize);
        if (broken && breakOnDeath) return true;

        flashT = flashTime;
        SetFlash(true);
        punching = true;
        punchAge = 0f;
        punchDir = dir;
        return broken;
    }

    /// Alive again at full HP (for objects with breakOnDeath off, whose own script decides when).
    public void Revive()
    {
        if (!broken) return;
        broken = false;
        hp.HealFull();
        hp.showBar = barWasShown;
    }

    void Break()
    {
        broken = true;
        if (!breakOnDeath)
        {
            // the killing blow still flashes and squashes; the owner script takes it from here
            barWasShown = hp.showBar;
            hp.showBar = false;
            return;
        }
        SetFlash(false);
        ResetPose();
        CombatFX.Shatter(Center, breakFXSize, debrisColor);
        if (stoneYield > 0 && Economy.Instance != null) Economy.Instance.Add(ResourceKind.Stone, stoneYield);
        barWasShown = hp.showBar;
        hp.showBar = false;
        SetShown(false);
        if (respawnDelay > 0f) respawnT = respawnDelay;
        else Destroy(gameObject);
    }

    void Respawn()
    {
        broken = false;
        hp.HealFull();
        hp.showBar = barWasShown;
        SetShown(true);
        punching = true;   // pops back in with a little bounce
        punchAge = 0f;
        punchDir = Vector3.zero;
    }

    void Update()
    {
        if (flashing)
        {
            flashT -= Time.unscaledDeltaTime;
            if (flashT <= 0f) SetFlash(false);
        }

        if (punching)
        {
            punchAge += Time.deltaTime;
            float k = punchTime > 0f ? punchAge / punchTime : 1f;
            if (k >= 1f) ResetPose();
            else
            {
                // squashed most on the contact frame, then springs back with a fading wobble
                float wave = Mathf.Cos(k * Mathf.PI * 3f) * (1f - k);
                float s = punchScale * wave;
                visual.localScale = Vector3.Scale(baseScale, new Vector3(1f + s, 1f - s, 1f + s));
                Quaternion parentRot = visual.parent != null ? visual.parent.rotation : Quaternion.identity;
                Quaternion rest = visual == transform ? baseRot : parentRot * baseRot;
                if (punchDir.sqrMagnitude > 0.0001f)
                {
                    Vector3 axis = Vector3.Cross(Vector3.up, punchDir);
                    visual.rotation = Quaternion.AngleAxis(tiltDegrees * wave, axis) * rest;
                }
            }
        }

        if (broken && respawnT > 0f)
        {
            respawnT -= Time.deltaTime;
            if (respawnT <= 0f) Respawn();
        }
    }

    void ResetPose()
    {
        punching = false;
        visual.localScale = baseScale;
        if (visual != transform) visual.localRotation = baseRot;
    }

    void SetShown(bool on)
    {
        if (visual != transform) { visual.gameObject.SetActive(on); return; }
        foreach (var r in renderers) if (r != null) r.enabled = on;
        foreach (var c in colliders) if (c != null) c.enabled = on;
    }

    /// White flash: every renderer gets the unlit flash material for a moment (works with any shader),
    /// then its own materials and colour block come back.
    void SetFlash(bool on)
    {
        if (flashing == on || flashMat == null) { if (!on) flashing = false; return; }
        flashing = on;
        if (mpb == null) mpb = new MaterialPropertyBlock();
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;
            if (on)
            {
                hadBlock[i] = r.HasPropertyBlock();
                if (hadBlock[i]) r.GetPropertyBlock(savedBlocks[i]);
                r.sharedMaterials = flashMats[i];
                mpb.Clear();
                mpb.SetColor("_BaseColor", flashColor);
                r.SetPropertyBlock(mpb);
            }
            else
            {
                r.sharedMaterials = normalMats[i];
                r.SetPropertyBlock(hadBlock[i] ? savedBlocks[i] : null);
            }
        }
    }
}
