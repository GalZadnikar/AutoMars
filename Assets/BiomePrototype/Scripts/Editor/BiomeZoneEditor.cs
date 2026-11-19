using UnityEditor;
using UnityEngine;

/// BIOME PROTOTYPE - Scene view handles for BiomeZone: drag Path / Polygon points, see the shape outline.
[CustomEditor(typeof(BiomeZone))]
public class BiomeZoneEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var z = (BiomeZone)target;
        if (z.shape == BiomeZone.Shape.Path || z.shape == BiomeZone.Shape.Polygon)
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Drag the points in the Scene view. Add a point to extend the shape.", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Point"))
                {
                    Undo.RecordObject(z, "Add Point");
                    Vector3 last = z.points.Count > 0 ? z.points[z.points.Count - 1] : Vector3.zero;
                    Vector3 prev = z.points.Count > 1 ? z.points[z.points.Count - 2] : last - Vector3.forward * 3f;
                    z.points.Add(last + (last - prev).normalized * 3f);
                    BiomeZone.NotifyChanged();
                }
                if (GUILayout.Button("Remove Last") && z.points.Count > 0)
                {
                    Undo.RecordObject(z, "Remove Point");
                    z.points.RemoveAt(z.points.Count - 1);
                    BiomeZone.NotifyChanged();
                }
            }
        }
    }

    void OnSceneGUI()
    {
        var z = (BiomeZone)target;
        var t = z.transform;
        var rot = Quaternion.Euler(0f, t.eulerAngles.y, 0f);
        Handles.color = new Color(1f, 0.9f, 0.3f, 0.9f);

        switch (z.shape)
        {
            case BiomeZone.Shape.Circle:
                Handles.DrawWireDisc(t.position, Vector3.up, z.radius);
                break;
            case BiomeZone.Shape.Box:
            {
                Vector3 h = new Vector3(z.boxSize.x * 0.5f, 0f, z.boxSize.y * 0.5f);
                Vector3 a = t.position + rot * new Vector3(-h.x, 0f, -h.z), b = t.position + rot * new Vector3(-h.x, 0f, h.z);
                Vector3 c = t.position + rot * new Vector3(h.x, 0f, h.z), d = t.position + rot * new Vector3(h.x, 0f, -h.z);
                Handles.DrawPolyLine(a, b, c, d, a);
                break;
            }
            default:
            {
                int n = z.points.Count;
                for (int i = 0; i < n; i++)
                {
                    Vector3 w = t.position + rot * z.points[i];
                    if (i + 1 < n) Handles.DrawLine(w, t.position + rot * z.points[i + 1], 2f);
                    else if (z.shape == BiomeZone.Shape.Polygon && n > 2) Handles.DrawLine(w, t.position + rot * z.points[0], 2f);

                    float size = HandleUtility.GetHandleSize(w) * 0.12f;
                    EditorGUI.BeginChangeCheck();
                    Vector3 moved = Handles.FreeMoveHandle(w, size, Vector3.zero, Handles.SphereHandleCap);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(z, "Move Biome Point");
                        Vector3 local = Quaternion.Inverse(rot) * (moved - t.position);
                        local.y = 0f;
                        z.points[i] = local;
                        BiomeZone.NotifyChanged();
                    }
                }
                break;
            }
        }
    }
}
