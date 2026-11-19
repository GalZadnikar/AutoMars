using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static StatType;

/// Player upgrade tree (same system as the factory Workshop): tree asset, BuildingUpgrades + PlayerUpgrades on the
/// Player, pipe pump at the last player level. Safe to re-run: an existing tree asset keeps your tweaks
/// (delete Assets/Upgrades/Player_Tree.asset to get the defaults back). Builder 8 runs this too.
public static class PlayerUpgradesBuilder
{
    const string TreePath = "Assets/Upgrades/Player_Tree.asset";
    const string AstronautPrefab = "Assets/Stylized_Astronaut/Stylized Astronaut.prefab";

    [MenuItem("Tools/Trash Game/10. Player Upgrade Tree")]
    public static void Build()
    {
        Setup();
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Player upgrade tree ready: " + TreePath);
    }

    public static void Setup()
    {
        var tree = TreeAsset();
        var carrier = Object.FindFirstObjectByType<PlayerCarrier>(FindObjectsInactive.Include);
        if (carrier == null) { Debug.LogWarning("PlayerUpgradesBuilder: no PlayerCarrier in the scene."); return; }
        var go = carrier.gameObject;
        var up = go.GetComponent<BuildingUpgrades>();
        if (up == null) up = Undo.AddComponent<BuildingUpgrades>(go);
        up.tree = tree;
        EditorUtility.SetDirty(up);
        var pu = go.GetComponent<PlayerUpgrades>();
        if (pu == null) pu = Undo.AddComponent<PlayerUpgrades>(go);
        if (pu.previewPrefab == null) pu.previewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AstronautPrefab);
        EditorUtility.SetDirty(pu);

        // pipe level = player level: ranges for levels 1-5, the H2O pump at the max player level
        var pt = Object.FindFirstObjectByType<PipeTether>(FindObjectsInactive.Include);
        if (pt != null)
        {
            pt.pumpAtLevel = tree.MaxLevel;
            pt.level = 1;
            EditorUtility.SetDirty(pt);
        }
    }

    static BuildingUpgradeTree TreeAsset()
    {
        var tree = AssetDatabase.LoadAssetAtPath<BuildingUpgradeTree>(TreePath);
        if (tree != null) return tree;

        tree = ScriptableObject.CreateInstance<BuildingUpgradeTree>();
        tree.displayName = "PLAYER";
        tree.category = "ASTRONAUT SUIT";
        tree.outputPrefix = "";
        tree.outputName = "ASTRONAUT";
        tree.levelLabel = "PLAYER LEVEL";
        tree.levelRowLabel = "Player Level";
        tree.hasSlots = false;
        tree.masterDiskUnlockLevel = 0;
        tree.shownStats = new List<StatType> { CarryCapacity, CollectRate, UnloadRate, MoveSpeed, PipeRange };
        tree.levelCosts = new List<int> { 25, 50, 90, 140, 200 };   // max level 6
        tree.perLevel = new List<StatMod> { Flat(CarryCapacity, 1), Pct(CollectRate, 8), Pct(UnloadRate, 10), Flat(MoveSpeed, 0.15f) };
        tree.tiers = new List<UpgradeTier>
        {
            Tier(Perk("Bigger Backpack", "BAG", "More trash per trip.", Flat(CarryCapacity, 2)),
                 Perk("Quick Hands", "QH", "Grab trash faster.", Pct(CollectRate, 25)),
                 Perk("Long Hose", "HOS", "More pipe on the reel (no effect with the H2O pump).", Flat(PipeRange, 4))),
            Tier(Perk("Light Boots", "SPD", "Walk faster.", Flat(MoveSpeed, 0.5f)),
                 Perk("Dump Chute", "DMP", "Empty the bag at the base faster.", Pct(UnloadRate, 50)),
                 Perk("Stack Frame", "STK", "Carry more, a bit slower.", Flat(CarryCapacity, 3), Flat(MoveSpeed, -0.2f))),
            Tier(Perk("Grabber Arm", "GRB", "Much faster pickup, smaller bag.", Pct(CollectRate, 40), Flat(CarryCapacity, -1)),
                 Perk("Servo Legs", "SRV", "Faster trips to far-off piles.", Pct(MoveSpeed, 12)),
                 Perk("Extra Hose", "HOS", "Even more pipe (no effect with the H2O pump).", Flat(PipeRange, 6))),
            Tier(Perk("Cargo Rack", "CAP", "Big bag, heavy steps.", Flat(CarryCapacity, 4), Pct(MoveSpeed, -8)),
                 Perk("Twin Grabbers", "TWN", "Two hands are better than one.", Pct(CollectRate, 35)),
                 Perk("Turbo Unload", "TRB", "Dump everything in a flash.", Pct(UnloadRate, 80))),
            Tier(Perk("Mule Mode", "MUL", "Carry a mountain of trash.", Pct(CarryCapacity, 40)),
                 Perk("Jet Steps", "JET", "Small thrusters in the boots.", Pct(MoveSpeed, 20)),
                 Perk("Vacuum Nozzle", "VAC", "Suck it up, blow it out.", Pct(CollectRate, 50), Pct(UnloadRate, 50))),
        };
        if (!AssetDatabase.IsValidFolder("Assets/Upgrades")) AssetDatabase.CreateFolder("Assets", "Upgrades");
        AssetDatabase.CreateAsset(tree, TreePath);
        AssetDatabase.SaveAssets();
        return tree;
    }

    static StatMod Flat(StatType s, float v) => new StatMod(s, ModOp.AddFlat, v);
    static StatMod Pct(StatType s, float v) => new StatMod(s, ModOp.AddPercent, v);
    static UpgradeTier Tier(params Perk[] p) => new UpgradeTier { options = new List<Perk>(p) };
    static Perk Perk(string title, string glyph, string flavor, params StatMod[] mods) =>
        new Perk { title = title, glyph = glyph, flavor = flavor, mods = new List<StatMod>(mods) };
}
