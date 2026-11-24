using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// Mobile-style movement: joystick direction is relative to the camera,
/// character turns to face where it moves. WASD/arrows also work in the editor.
[RequireComponent(typeof(CharacterController))]
public class JoystickPlayerController : MonoBehaviour
{
    public Joystick joystick;
    public Transform cameraTransform;

    [Header("Movement")]
    public float moveSpeed = 5f;
    public float turnSpeed = 720f;       // degrees per second
    public float gravity = 20f;
    [Range(0f, 0.5f)] public float deadZone = 0.1f;

    /// Multiplies moveSpeed; other systems set it (e.g. the suction gun slows you while sucking). 1 = normal.
    [System.NonSerialized] public float speedMultiplier = 1f;
    /// A second multiplier for the tool in the hands (the shovel walks slowly while digging). 1 = normal.
    [System.NonSerialized] public float toolSpeedMultiplier = 1f;
    /// Set from code to steer the player without a joystick (automated tests). Zero = off.
    [System.NonSerialized] public Vector2 scriptedInput;
    /// Keeps the run animation off while another animation plays during movement (digging into sand with the shovel).
    [System.NonSerialized] public bool suppressRunAnim;

    CharacterController controller;
    Animator anim;
    float verticalVelocity;
    Vector3 pushVelocity;

    /// Shoves the player (an enemy's blow). velocity in m/s on the ground plane; it fades out in a fraction of a second.
    public void Push(Vector3 velocity)
    {
        velocity.y = 0f;
        pushVelocity = velocity;
    }
    static readonly int AnimationPar = Animator.StringToHash("AnimationPar");

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        anim = GetComponentInChildren<Animator>();
        if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
    }

    /// Joystick / keys this frame, even while movement is blocked (the leech's struggle option reads it).
    public Vector2 RawInput { get; private set; }

    Vector2 ReadInput()
    {
        RawInput = ReadRaw();
        if (UIState.ModalOpen || UIState.BuildMode || PlayerHealth.KnockedOut || EnemyLeech.PlayerHeld) return Vector2.zero;   // popup open / build mode (joystick moves the blueprint) / held by a leech: don't move
        return RawInput;
    }

    Vector2 ReadRaw()
    {
        Vector2 input = joystick != null ? joystick.Direction : Vector2.zero;
        if (scriptedInput.sqrMagnitude > 0.0001f) input = scriptedInput;
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (input.sqrMagnitude < 0.0001f && kb != null)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) input.y += 1;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) input.y -= 1;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) input.x += 1;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) input.x -= 1;
        }
#endif
        return Vector2.ClampMagnitude(input, 1f);
    }

    void Update()
    {
        Vector2 input = ReadInput();
        if (EnemyLeech.PlayerHeld)
        {
            // a leech holds (and moves) the player; its CharacterController is off meanwhile
            verticalVelocity = 0f;
            if (anim != null) anim.SetInteger(AnimationPar, 0);
            return;
        }
        bool moving = input.magnitude > deadZone;

        // Camera-relative direction on the ground plane
        Vector3 fwd = Vector3.forward, right = Vector3.right;
        if (cameraTransform != null)
        {
            fwd = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        }
        Vector3 move = (fwd * input.y + right * input.x);

        if (moving)
        {
            Quaternion target = Quaternion.LookRotation(move.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
        }

        if (controller.isGrounded && verticalVelocity < 0) verticalVelocity = -2f;
        verticalVelocity -= gravity * Time.deltaTime;

        Vector3 velocity = move * moveSpeed * speedMultiplier * toolSpeedMultiplier;
        if (pushVelocity.sqrMagnitude > 0.0001f)
        {
            velocity += pushVelocity;
            pushVelocity = Vector3.MoveTowards(pushVelocity, Vector3.zero, 22f * Time.deltaTime);
        }
        velocity.y = verticalVelocity;
        if (PipeTether.Instance != null) velocity = PipeTether.Instance.ConstrainVelocity(transform.position, velocity, Time.deltaTime);   // pipe to the base: can't walk past its end
        controller.Move(velocity * Time.deltaTime);

        if (anim != null) anim.SetInteger(AnimationPar, moving && !suppressRunAnim ? 1 : 0);
    }
}
