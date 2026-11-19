using UnityEngine;

/// SAND PROTOTYPE - sand blocks walking.
/// Remembers where the player was before it moved this frame (runs early), and after everything moved
/// (LateUpdate) pushes it back out of deep sand, sliding along the wall when only one direction is blocked.
/// Lives on the prototype object, so the player's own controller stays untouched.
[DefaultExecutionOrder(-500)]
public class SandBlocker : MonoBehaviour
{
    [Tooltip("The player (found automatically)")]
    public Transform player;
    [Tooltip("Radius of the player's feet against sand (smaller = fits into narrower paths)")]
    public float footRadius = 0.45f;

    CharacterController cc;
    Vector3 prev;

    void OnEnable()
    {
        if (player == null)
        {
            var pc = FindFirstObjectByType<JoystickPlayerController>();
            if (pc != null) player = pc.transform;
        }
        if (player != null) { cc = player.GetComponent<CharacterController>(); prev = player.position; }
    }

    void Update()
    {
        if (player != null) prev = player.position;
    }

    void LateUpdate()
    {
        if (player == null || SandField.All.Count == 0) return;
        Vector3 now = player.position;
        Vector3 delta = now - prev; delta.y = 0f;
        if (delta.sqrMagnitude < 1e-8f || !Blocked(now)) return;
        if (Blocked(prev)) return;   // already standing in sand (spawned / knocked in): let it walk out

        Vector3 target = prev;
        Vector3 tryX = new Vector3(prev.x + delta.x, now.y, prev.z);
        Vector3 tryZ = new Vector3(prev.x, now.y, prev.z + delta.z);
        if (Mathf.Abs(delta.x) > 1e-5f && !Blocked(tryX)) target = tryX;
        else if (Mathf.Abs(delta.z) > 1e-5f && !Blocked(tryZ)) target = tryZ;

        Vector3 fix = target - now;
        fix.y = 0f;
        if (cc != null && cc.enabled) cc.Move(fix + Vector3.down * 0.05f);   // small push down keeps isGrounded true
        else player.position += fix;
    }

    bool Blocked(Vector3 p)
    {
        if (SandField.BlockedAt(p)) return true;
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI * 0.25f;
            if (SandField.BlockedAt(p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * footRadius)) return true;
        }
        return false;
    }
}
