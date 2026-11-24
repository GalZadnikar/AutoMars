using TMPro;
using UnityEngine;

/// A weapon lying on the ground. Walk into it and you get it; the weapon you had drops in its place
/// (this same object turns into a pickup of the old weapon, so nothing is ever lost).
/// The dropped weapon can't be taken again until you have stepped away from it, so the two don't swap back and forth.
/// Place one by dragging a prefab from Assets/Prefabs/Weapons into the scene, or put this component on an empty
/// object and set `weapon`: the model, ring, name tag and glow are made from the WeaponDef.
public class WeaponPickup : MonoBehaviour
{
    public WeaponDef weapon;
    [Tooltip("Walk this close to take it (m)")]
    public float pickupRadius = 1.3f;
    [Tooltip("The spinning model. Built from the weapon if empty")]
    public Transform visual;
    [SerializeField, HideInInspector] WeaponDef visualOf;
    public float hoverHeight = 0.9f;
    public float spinSpeed = 80f;
    public float bobHeight = 0.12f;
    [Tooltip("Size of the model on the ground, on top of the weapon's own model scale")]
    public float modelScale = 1.7f;
    public bool showLabel = true;
    public bool showRing = true;
    [Tooltip("Only shown while inside somebody's sight right now. Off = always shown")]
    public bool hideOutOfSight = true;

    bool armed = true;
    float phase, tossT = 1f;
    Vector3 tossFrom;
    Transform cam;
    LineRenderer ring;
    TextMeshPro label;

    /// Makes a pickup of this weapon at a point on the ground.
    public static WeaponPickup Spawn(WeaponDef def, Vector3 pos)
    {
        var go = new GameObject("Weapon Pickup (" + (def != null ? def.displayName : "?") + ")");
        go.transform.position = pos;
        var p = go.AddComponent<WeaponPickup>();
        p.weapon = def;
        return p;
    }

    /// The spinning model of a weapon under `parent` (also used by the editor builder for the prefabs).
    public static Transform BuildVisual(Transform parent, WeaponDef def, float hover, float scale)
    {
        var v = new GameObject("Visual").transform;
        v.SetParent(parent, false);
        v.localPosition = Vector3.up * hover;
        if (def == null || def.model == null) return v;
        var m = Instantiate(def.model, v);
        m.name = "Model";
        float s = def.modelScale * scale;
        m.transform.localScale = def.model.transform.localScale * s;
        m.transform.localRotation = Quaternion.Euler(0f, 0f, 70f);   // lying on its side (a little tilted), so the top-down camera sees its shape
        // the middle of the gun on the pivot, so it spins in place
        m.transform.localPosition = -(m.transform.localRotation * ((def.grip + def.muzzle) * 0.5f * s));
        foreach (var c in m.GetComponentsInChildren<Collider>(true))
        {
            if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
        }
        return v;
    }

    void Start()
    {
        phase = Random.value;
        if (visual == null)
        {
            var t = transform.Find("Visual");
            if (t != null) visual = t;
        }
        Rebuild(visual == null || visualOf != weapon);

        var pw = PlayerWeapon.Instance;
        if (pw != null && pw.pickupGlowFx != null)
        {
            var glow = FxPool.Create(pw.pickupGlowFx, transform, pw.pickupFxScale);
            glow.transform.localPosition = Vector3.up * 0.15f;
        }
        if (hideOutOfSight && GetComponent<FogHideable>() == null)
            gameObject.AddComponent<FogHideable>().hide = FogHideable.Mode.WhenNotVisible;
    }

    /// Model (if asked), ring colour and name tag for the weapon it holds now.
    void Rebuild(bool model)
    {
        if (model)
        {
            if (visual != null) Destroy(visual.gameObject);
            visual = BuildVisual(transform, weapon, hoverHeight, modelScale);
            visualOf = weapon;
        }
        if (weapon == null) return;
        gameObject.name = "Weapon Pickup (" + weapon.displayName + ")";

        if (showRing)
        {
            if (ring == null)
            {
                var rgo = new GameObject("Ring");
                rgo.transform.SetParent(transform, false);
                rgo.transform.localPosition = Vector3.up * 0.06f;
                rgo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                ring = rgo.AddComponent<LineRenderer>();
                ring.sharedMaterial = Resources.Load<Material>("FX/ParticleSolid");
                ring.useWorldSpace = false;
                ring.loop = true;
                ring.alignment = LineAlignment.TransformZ;
                const int N = 36;
                ring.positionCount = N;
                for (int i = 0; i < N; i++)
                {
                    float a = i * Mathf.PI * 2f / N;
                    ring.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.8f);
                }
                ring.widthMultiplier = 0.07f;
                ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ring.receiveShadows = false;
            }
            SetRingAlpha(armed ? 0.85f : 0.3f);
        }

        if (showLabel)
        {
            if (label == null)
            {
                var lgo = new GameObject("Label");
                lgo.transform.SetParent(transform, false);
                lgo.transform.localPosition = Vector3.up * (hoverHeight + 0.85f);
                label = lgo.AddComponent<TextMeshPro>();
                label.fontSize = 3.2f;
                label.fontStyle = FontStyles.Bold;
                label.alignment = TextAlignmentOptions.Center;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.outlineWidth = 0.25f;
                label.outlineColor = new Color32(30, 22, 30, 255);
                label.rectTransform.sizeDelta = new Vector2(6f, 1.5f);
                label.sortingOrder = 15;
            }
            label.text = weapon.displayName;
            label.color = Color.Lerp(weapon.color, Color.white, 0.5f);
        }
    }

    void SetRingAlpha(float a)
    {
        if (ring == null || weapon == null) return;
        Color c = weapon.color;
        c.a = a;
        ring.startColor = ring.endColor = c;
    }

    void Update()
    {
        if (visual != null)
        {
            visual.localRotation = Quaternion.Euler(0f, Time.time * spinSpeed + phase * 360f, 0f);
            Vector3 rest = Vector3.up * (hoverHeight + Mathf.Sin(Time.time * 2f + phase * 6f) * bobHeight);
            if (tossT < 1f)
            {
                // the weapon the player just let go of: flies from his hand in an arc and lands here
                tossT = Mathf.Min(1f, tossT + Time.deltaTime / 0.4f);
                float e = tossT * tossT * (3f - 2f * tossT);
                visual.localPosition = Vector3.Lerp(transform.InverseTransformPoint(tossFrom), rest, e) + Vector3.up * Mathf.Sin(tossT * Mathf.PI) * 0.8f;
                visual.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, e);
            }
            else
            {
                visual.localPosition = rest;
                visual.localScale = Vector3.one;
            }
        }
        if (label != null)
        {
            if (cam == null && Camera.main != null) cam = Camera.main.transform;
            if (cam != null) label.transform.rotation = cam.rotation;
        }

        var pw = PlayerWeapon.Instance;
        if (pw == null || weapon == null) return;
        Vector3 d = pw.transform.position - transform.position; d.y = 0f;
        float dist = d.magnitude;
        if (!armed)
        {
            // he has to step away from the weapon he dropped before he can take it back
            if (dist > pickupRadius + 0.5f)
            {
                armed = true;
                SetRingAlpha(0.85f);
            }
            return;
        }
        if (dist > pickupRadius || UIState.ModalOpen || UIState.BuildMode || PlayerHealth.KnockedOut || EnemyLeech.PlayerHeld) return;
        Take(pw);
    }

    void Take(PlayerWeapon pw)
    {
        var mine = weapon;
        var old = pw.Equip(mine);
        GameToast.Show("+ " + mine.displayName, Color.Lerp(mine.color, Color.white, 0.3f));
        if (pw.pickupTakeFx != null) FxPool.Play(pw.pickupTakeFx, pw.transform.position + Vector3.up * 0.1f, Quaternion.identity, pw.pickupFxScale, 2f);
        CameraShake.Kick(0.06f);

        if (old == null || old == mine)
        {
            Destroy(gameObject);   // he had no weapon (or the same one): nothing to leave behind
            return;
        }
        // the weapon he had falls down here
        weapon = old;
        armed = false;
        Rebuild(true);
        tossFrom = pw.transform.position + Vector3.up * 1.2f;
        tossT = 0f;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = weapon != null ? weapon.color : Color.yellow;
        Gizmos.DrawWireSphere(transform.position, pickupRadius);
    }
}
