using UnityEditor;
using UnityEngine;

/// SAND PROTOTYPE - shows how big a sand field is in gameplay terms, to help balancing the reward.
[CustomEditor(typeof(SandField))]
[CanEditMultipleObjects]
public class SandFieldEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        if (targets.Length != 1) return;

        var f = (SandField)target;
        var gun = Object.FindFirstObjectByType<SuctionGun>();
        float vol = f.FullVolume;
        EditorGUILayout.Space();
        if (vol <= 0f) { EditorGUILayout.HelpBox("Field not built yet.", MessageType.None); return; }

        string text = "Sand: " + vol.ToString("0") + " m³";
        if (gun != null && gun.capacity > 0)
        {
            float units = vol * gun.unitsPerCubicMetre;
            float tanks = units / gun.capacity;
            float perTank = tanks > 0f ? f.materialsTotal / tanks : 0f;
            text += "  =  " + units.ToString("0") + " tank units\n"
                  + "≈ " + tanks.ToString("0.0") + " full tanks (capacity " + gun.capacity + ")\n"
                  + "≈ " + (perTank * gun.materialMultiplier).ToString("0") + " materials per full tank"
                  + (Mathf.Approximately(gun.materialMultiplier, 1f) ? "" : "  (gun multiplier x" + gun.materialMultiplier.ToString("0.##") + ")");
        }
        if (Application.isPlaying) text += "\nLeft: " + f.VolumeLeft.ToString("0") + " m³  (" + Mathf.RoundToInt(f.Progress * 100f) + "% cleared)";
        EditorGUILayout.HelpBox(text, MessageType.Info);
    }
}
