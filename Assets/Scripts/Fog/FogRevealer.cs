using System.Collections.Generic;
using UnityEngine;

/// Anything with this component clears the fog of war around itself.
/// Put it on the player, buildings, bots...
public class FogRevealer : MonoBehaviour
{
    public static readonly List<FogRevealer> All = new List<FogRevealer>();

    [Tooltip("How far this object can see, in world units.")]
    public float radius = 10f;

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.9f, 0.4f, 0.8f);
        UnityEditor.Handles.color = Gizmos.color;
        UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, radius);
    }
#endif
}
