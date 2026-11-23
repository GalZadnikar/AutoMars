using UnityEngine;

/// Stage 1 sand tool: the shovel. No suction - every swing takes one bite out of the sand in front of the player.
/// Dig by walking into sand and holding the joystick (he digs his way forward), or stand next to sand and let go
/// (he turns to it and digs). Uses the same swing clips and Animation Events as the pickaxe (MeleeHit = the bite).
/// The sand goes into the same tank as the suction gun's, so the HUD and the pay-out at the base stay the same.
[RequireComponent(typeof(Animator))]
public class PlayerShovel : MonoBehaviour
{
    [Header("Dig")]
    [Tooltip("How far in front of the player a bite reaches (m)")]
    public float reach = 1.5f;
    [Tooltip("How much sand one swing removes at the feet (1 = a full-height layer; it fades out towards the far end and the sides)")]
    public float bite = 1f;
    [Range(20f, 90f)] public float halfAngle = 55f;
    [Tooltip("Tool level against tough (crusted) sand. A shovel is 0: it only scratches crust.")]
    public int power = 0;
    [Tooltip("Swing animation speed (1 = normal)")]
    public float attackSpeed = 1f;
    [Tooltip("Seconds into a swing when the next one may start")]
    public float comboTime = 0.5f;
    public float crossFade = 0.07f;
    [Tooltip("How fast the player turns to the sand when the joystick is released (degrees per second)")]
    public float turnSpeed = 540f;
    public float faceAngle = 40f;
    [Tooltip("Seconds the swing freezes when the shovel bites")]
    public float hitStop = 0.02f;
    [Tooltip("Walking speed while digging forward (share of normal). Steady, so he doesn't dash ahead after every bite and stop at the next one.")]
    [Range(0.05f, 1f)] public float digMoveSpeed = 0.25f;
    [Tooltip("A/B: on = steady slow digging walk (the shovel paces the walk). Off = the old way: the sand right ahead slows the player (the gun's sand resistance), so he speeds up after each bite and stops at the next.")]
    public bool steadyWalk = true;
    [Tooltip("Pushing into sand: he keeps digging (and walking slowly) while there is sand this far ahead (m)")]
    public float digLookAhead = 2.2f;

    [Header("Heavy style (A/B test)")]
    [Tooltip("Slow press down into the sand, then a fast snap up: the sand is taken (and flies) on the snap, not on contact")]
    public bool heavyStyle;
    [Tooltip("Combo time for the heavy dig clip (seconds of clip time)")]
    public float heavyComboTime = 0.78f;

    [Header("Parts")]
    public TrailRenderer trail;
    [Tooltip("End of the blade: the tossed sand flies from here")]
    public Transform tip;
    [Tooltip("Where the sand goes (found automatically)")]
    public SuctionGun tank;

    /// True while there is sand to dig in front and the player is digging it.
    public bool Digging { get; private set; }

    static readonly int DigState = Animator.StringToHash("Dig");
    static readonly int DigHeavyState = Animator.StringToHash("DigHeavy");
    static readonly int AttackSpeedId = Animator.StringToHash("AttackSpeed");
    const float Back = 0.35f;   // the bite starts a little behind the player, so it also clears beside the feet

    Animator anim;
    JoystickPlayerController pc;
    bool swinging, hasSpeedParam;
    float swingT, stopT, tossSide = 0.35f;
    bool loaded, pendingBite;
    float fxSize = 1f, moveMul = 1f;
    Color loadColor;

    void Awake()
    {
        anim = GetComponent<Animator>();
        pc = GetComponent<JoystickPlayerController>();
        if (tank == null) tank = FindFirstObjectByType<SuctionGun>();
        foreach (var p in anim.parameters)
            if (p.nameHash == AttackSpeedId) hasSpeedParam = true;
        if (trail != null) trail.emitting = false;
    }

    void OnDisable()
    {
        Digging = false;
        pendingBite = loaded = false;
        swinging = false;
        stopT = 0f;
        if (anim != null) anim.speed = 1f;
        if (pc != null) { pc.suppressRunAnim = false; pc.toolSpeedMultiplier = 1f; }
        moveMul = 1f;
        if (trail != null) trail.emitting = false;
    }

    void Update()
    {
        if (stopT > 0f)
        {
            stopT -= Time.unscaledDeltaTime;
            if (stopT <= 0f) anim.speed = 1f;
        }
        if (hasSpeedParam) anim.SetFloat(AttackSpeedId, attackSpeed);

        bool blocked = UIState.ModalOpen || UIState.BuildMode || PlayerHealth.KnockedOut || EnemyLeech.PlayerHeld;
        bool pushing = pc != null && pc.RawInput.magnitude > pc.deadZone;
        bool full = tank != null && tank.IsFull;
        Vector3 dir = Vector3.zero;
        Digging = !blocked && !full && FindSand(pushing, out dir);
        // pushing into sand: keep the dig swing instead of the run animation
        if (pc != null) pc.suppressRunAnim = Digging && pushing;

        // steady digging walk: down quickly when the digging starts, back up gently when he is through
        float wantMul = steadyWalk && Digging && pushing ? digMoveSpeed : 1f;
        if (tank != null) tank.resistWhileHolstered = !steadyWalk;   // old way: the gun's sand resistance paces the shovel
        moveMul = Mathf.MoveTowards(moveMul, wantMul, Time.deltaTime * (wantMul < moveMul ? 6f : 2.5f));
        if (pc != null) pc.toolSpeedMultiplier = moveMul;

        if (swinging)
        {
            swingT += Time.deltaTime * anim.speed * attackSpeed;
            if (swingT >= (heavyStyle ? heavyComboTime : comboTime)) swinging = false;
        }
        if (!Digging)
        {
            if (trail != null && !swinging) trail.emitting = false;
            return;
        }

        float angle = 0f;
        if (!pushing)
        {
            var look = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
            angle = Quaternion.Angle(transform.rotation, look);
        }
        if (!swinging && angle <= faceAngle)
        {
            anim.CrossFadeInFixedTime(heavyStyle ? DigHeavyState : DigState, crossFade, 0, 0f);
            swingT = 0f;
            swinging = true;
        }
    }

    /// Pushing the joystick: sand straight ahead. Joystick released: the direction with the most sand in reach,
    /// preferring what is in front.
    bool FindSand(bool pushing, out Vector3 dir)
    {
        Vector3 fwd = transform.forward; fwd.y = 0f;
        fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
        dir = fwd;
        if (SandField.All.Count == 0) return false;
        if (pushing) return SandAhead(fwd) > 0.2f || (steadyWalk && Digging && SandFar(fwd) > 0.2f);

        float best = 0.2f;
        bool found = false;
        for (int i = 0; i < 16; i++)
        {
            Vector3 d = Quaternion.Euler(0f, i * 22.5f, 0f) * fwd;
            float off = Vector3.Angle(fwd, d) / 180f;
            float s = SandAhead(d) * Mathf.Lerp(1f, 0.5f, off);
            if (s > best) { best = s; dir = d; found = true; }
        }
        return found;
    }

    float SandAhead(Vector3 d)
    {
        Vector3 p = transform.position;
        float a = 0f;
        for (int k = -1; k <= 1; k++)
        {
            Vector3 dd = Quaternion.Euler(0f, k * 25f, 0f) * d;
            a = Mathf.Max(a, SandAt(p + dd * 0.7f));
            a = Mathf.Max(a, SandAt(p + dd * Mathf.Min(1.1f, reach * 0.75f)));
        }
        return a;
    }

    /// Sand straight ahead further out: he keeps digging (swinging and walking slowly) through the gaps a bite leaves.
    float SandFar(Vector3 d)
    {
        Vector3 p = transform.position;
        float a = 0f;
        for (float r = 1.2f; r <= digLookAhead + 0.01f; r += 0.5f)
            for (int k = -1; k <= 1; k++)
                a = Mathf.Max(a, SandAt(p + Quaternion.Euler(0f, k * 20f, 0f) * d * r));
        return a;
    }

    static float SandAt(Vector3 p)
    {
        float a = 0f;
        for (int i = 0; i < SandField.All.Count; i++) a = Mathf.Max(a, SandField.All[i].AmountAt(p));
        return a;
    }

    // ------------------------------------------------------------------ animation events (shared swing clips)

    /// Contact frame: the blade goes into the sand. Quick style takes the bite here; heavy style only sinks in
    /// (small spray + freeze) and takes it on the snap up (DigToss).
    public void MeleeHit()
    {
        if (!enabled || !Digging) return;
        if (heavyStyle)
        {
            if (!FindBiteSpot(out Vector3 p, out Color c)) return;
            CombatFX.SandDig(p, Vector3.up * 0.6f - Fwd() * 0.6f, c, 0.6f);
            loadColor = c;
            pendingBite = true;
            Freeze();
            return;
        }
        if (!TakeBite(out Vector3 fxPos, out Color fxCol)) return;
        CombatFX.SandDig(fxPos, Vector3.up * 0.8f - Fwd(), fxCol, fxSize);
        loadColor = fxCol;
        loaded = true;
        Freeze();
    }

    /// Snap up of the scoop. Quick style: the sand that was taken flies over the shoulder.
    /// Heavy style: the chunk disappears NOW and bursts up with the shovel.
    public void DigToss()
    {
        if (!enabled) return;
        Vector3 fwd = Fwd();
        if (heavyStyle)
        {
            if (!pendingBite) return;
            pendingBite = false;
            if (!TakeBite(out Vector3 fxPos, out Color fxCol)) return;
            loadColor = fxCol;
            CombatFX.SandDig(fxPos, Vector3.up * 1.2f - fwd * 0.4f, fxCol, fxSize * 1.15f);   // the hole tears open
            loaded = true;
        }
        if (!loaded) return;
        loaded = false;
        Vector3 from = tip != null ? tip.position : transform.position + fwd * 1.5f + Vector3.up * 1.2f;
        CombatFX.SandToss(from, Vector3.up * 1.3f - fwd * 0.55f + transform.right * tossSide, loadColor, fxSize * (heavyStyle ? 1.25f : 1f));
        tossSide = -tossSide;   // left, right, left ...
    }

    Vector3 Fwd()
    {
        Vector3 f = transform.forward; f.y = 0f;
        return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
    }

    void Freeze()
    {
        if (hitStop <= 0f) return;
        anim.speed = 0f;
        stopT = hitStop;
    }

    /// The first sand surface in front of the player (where the blade goes in) and its colour.
    bool FindBiteSpot(out Vector3 fxPos, out Color fxCol)
    {
        Vector3 pos = transform.position, fwd = Fwd();
        fxPos = pos + fwd * Mathf.Min(1f, reach * 0.7f) + Vector3.up * 0.6f;
        fxCol = new Color(1f, 0.83f, 0.42f);
        for (float r = 0.6f; r <= reach; r += 0.3f)
            if (SandField.TryGetSurface(pos + fwd * r, out float y, out float amt, out SandField field) && amt > 0.1f)
            {
                fxPos = pos + fwd * r; fxPos.y = y;
                if (field != null) fxCol = field.ParticleColor;
                return true;
            }
        return false;
    }

    /// Removes one bite of sand in front of the player into the tank. False if there was nothing to take.
    bool TakeBite(out Vector3 fxPos, out Color fxCol)
    {
        FindBiteSpot(out fxPos, out fxCol);   // looked up before the sand is gone
        Vector3 fwd = Fwd();
        float vol = 0f, mats = 0f;
        Vector3 apex = transform.position - fwd * Back;
        for (int i = 0; i < SandField.All.Count; i++)
        {
            vol += SandField.All[i].Suck(apex, fwd, reach + Back, halfAngle, bite, 1f, out float m, power);
            mats += m;
        }
        if (vol <= 0f) return false;
        if (tank != null) tank.AddSand(vol, mats);
        fxSize = Mathf.Lerp(1f, 1.6f, Mathf.InverseLerp(1f, 2.5f, bite));   // bigger bites throw more sand
        return true;
    }

    public void MeleeTrail(int on)
    {
        if (!enabled) return;
        if (trail != null) trail.emitting = on != 0;
    }
}
