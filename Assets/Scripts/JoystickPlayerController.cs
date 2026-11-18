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

    CharacterController controller;
    Animator anim;
    float verticalVelocity;
    static readonly int AnimationPar = Animator.StringToHash("AnimationPar");

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        anim = GetComponentInChildren<Animator>();
        if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
    }

    Vector2 ReadInput()
    {
        if (UIState.ModalOpen || UIState.BuildMode) return Vector2.zero;   // popup open / build mode (joystick moves the blueprint): don't move
        Vector2 input = joystick != null ? joystick.Direction : Vector2.zero;
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

        Vector3 velocity = move * moveSpeed;
        velocity.y = verticalVelocity;
        controller.Move(velocity * Time.deltaTime);

        if (anim != null) anim.SetInteger(AnimationPar, moving ? 1 : 0);
    }
}
