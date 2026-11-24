using Unity.Cinemachine;
using UnityEngine;

/// Short camera kick for impacts (melee hits, the player getting hit, something dying).
/// Uses Cinemachine Impulse: the source and the listener on every CinemachineCamera are made at runtime,
/// so no scene needs setting up. CameraShake.Kick(0.2f) = light tap, 0.6f = heavy blow.
public static class CameraShake
{
    /// Global strength (0 = no shake at all).
    public static float Scale = 1f;

    static CinemachineImpulseSource source;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { source = null; Scale = 1f; }

    /// strength = roughly how many metres the camera jumps. dir = which way (default: a random sideways / up direction).
    public static void Kick(float strength, Vector3? dir = null, float duration = 0.16f)
    {
        if (strength <= 0f || Scale <= 0f || !Application.isPlaying) return;
        Ensure();
        Vector3 v;
        if (dir.HasValue && dir.Value.sqrMagnitude > 0.0001f) v = dir.Value.normalized;
        else
        {
            Vector2 r = Random.insideUnitCircle.normalized;
            v = new Vector3(r.x, r.y * 0.6f, 0f);
        }
        source.ImpulseDefinition.ImpulseDuration = duration;
        source.GenerateImpulseWithVelocity(v * strength * Scale);
    }

    static void Ensure()
    {
        if (source != null) return;
        var go = new GameObject("Camera Shake");
        source = go.AddComponent<CinemachineImpulseSource>();
        var d = source.ImpulseDefinition;
        d.ImpulseChannel = 1;
        d.ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
        d.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump;
        d.ImpulseDuration = 0.16f;

        foreach (var cam in Object.FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (cam.GetComponent<CinemachineImpulseListener>() != null) continue;
            var l = cam.gameObject.AddComponent<CinemachineImpulseListener>();
            l.ApplyAfter = CinemachineCore.Stage.Noise;
            l.ChannelMask = 1;
            l.Gain = 1f;
            l.Use2DDistance = false;
            l.UseCameraSpace = true;
            l.ReactionSettings = new CinemachineImpulseListener.ImpulseReaction { AmplitudeGain = 0f, FrequencyGain = 1f, Duration = 0.5f };
        }
    }
}
