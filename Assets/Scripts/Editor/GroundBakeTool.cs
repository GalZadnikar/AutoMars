using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Levels the ground under hand-placed buildings (GridFootprint.levelGround) and trash piles (TrashPile.levelGround)
/// in the editor and stores the pads on the ground, so the game doesn't have to do it at start.
/// Run it again after moving one of them or changing Level Ground / Level Radius (a moved one is still leveled at play, as a fallback).
public static class GroundBakeTool
{
    const float Margin = 2.5f;

    [MenuItem("Tools/Trash Game/Bake Ground Leveling")]
    public static void Bake()
    {
        var ground = Object.FindFirstObjectByType<LowPolyGround>();
        if (ground == null) { Debug.LogError("No LowPolyGround in the scene"); return; }

        // clean ground first, so pad heights come from the original terrain
        ground.bakedPads = new List<LowPolyGround.BakedPad>();
        ground.Build();

        var pads = new List<LowPolyGround.BakedPad>();
        var buildings = new List<GridFootprint>();
        var piles = new List<TrashPile>();
        foreach (var fp in Object.FindObjectsByType<GridFootprint>(FindObjectsSortMode.None))
        {
            fp.groundBaked = false;
            if (!fp.levelGround) continue;
            var dims = BuildGrid.Dims(fp.size, fp.QuarterTurns);
            var half = new Vector2(dims.x, dims.y) * BuildGrid.CellSize * 0.5f;
            var c = fp.Center;
            pads.Add(new LowPolyGround.BakedPad { center = c, half = half, target = GroundLeveler.PadHeight(c, half) });
            buildings.Add(fp);
        }
        foreach (var tp in Object.FindObjectsByType<TrashPile>(FindObjectsSortMode.None))
        {
            tp.groundBaked = false;
            if (!tp.levelGround) continue;
            float r = tp.LevelRadius;
            var c = tp.transform.position;
            pads.Add(new LowPolyGround.BakedPad { center = c, half = new Vector2(r, r), round = true, target = GroundLeveler.PadHeightRound(c, r) });
            piles.Add(tp);
        }

        ground.bakedPads = pads;
        ground.Build();   // applies the pads
        EditorUtility.SetDirty(ground);

        for (int i = 0; i < buildings.Count; i++)
        {
            var fp = buildings[i];
            var t = fp.transform;
            Undo.RecordObject(t, "Bake Ground");
            t.position = new Vector3(t.position.x, pads[i].target, t.position.z);
            fp.groundBaked = true;
            fp.bakedPosition = t.position;
            Dirty(fp); Dirty(t);
        }
        foreach (var tp in piles)
        {
            var b = tp.GetComponent<TrashPileBuilder>();
            if (b != null && b.mainMounds != null && b.mainMounds.Length > 0 && b.pieces != null && b.pieces.Length > 0)
                b.Build();   // heap rebuilt on the flat pad (same seed = same look)
            else
            {
                var t = tp.transform;
                t.position = new Vector3(t.position.x, GroundUtil.Height(t.position, t.position.y), t.position.z);
            }
            tp.groundBaked = true;
            tp.bakedPosition = tp.transform.position;
            Dirty(tp); Dirty(tp.transform);
            if (b != null) Dirty(b);
        }
        foreach (var ring in Object.FindObjectsByType<GroundRing>(FindObjectsInactive.Include, FindObjectsSortMode.None)) ring.Rebuild();

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Ground leveling baked: " + buildings.Count + " building(s), " + piles.Count + " trash pile(s). Run it again after moving one.");
    }

    [MenuItem("Tools/Trash Game/Clear Baked Ground Leveling")]
    public static void Clear()
    {
        var ground = Object.FindFirstObjectByType<LowPolyGround>();
        if (ground == null) return;
        ground.bakedPads = new List<LowPolyGround.BakedPad>();
        ground.Build();
        EditorUtility.SetDirty(ground);
        foreach (var fp in Object.FindObjectsByType<GridFootprint>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { fp.groundBaked = false; Dirty(fp); }
        foreach (var tp in Object.FindObjectsByType<TrashPile>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { tp.groundBaked = false; Dirty(tp); }
        foreach (var ring in Object.FindObjectsByType<GroundRing>(FindObjectsInactive.Include, FindObjectsSortMode.None)) ring.Rebuild();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Baked ground leveling cleared (leveling happens at play again).");
    }

    static void Dirty(Object o)
    {
        EditorUtility.SetDirty(o);
        if (PrefabUtility.IsPartOfPrefabInstance(o)) PrefabUtility.RecordPrefabInstancePropertyModifications(o);
    }
}
