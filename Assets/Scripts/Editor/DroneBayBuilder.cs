using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static StatType;

/// Module C2: the Drone Bay. Makes the upgrade tree (DroneBay_Tree.asset), the placeable prefab (a copy of the
/// Gatherer Factory prefab with the Satelite model; delete DroneBay.prefab to regenerate it), the catalog entry
/// and adds it to the base popup's BUILD tab. Safe to re-run: existing assets keep your tweaks.
/// The bay has no drone prefab yet (Module C3): the popup says "NO DRONE DESIGN YET" until BotFactory.botPrefab is set.
public static class DroneBayBuilder
{
    const string TreePath = "Assets/Upgrades/DroneBay_Tree.asset";
    const string PrefabPath = "Assets/Prefabs/Buildings/DroneBay.prefab";
    const string DefPath = "Assets/Buildings/Build_DroneBay.asset";
    const string FactoryPrefab = "Assets/Prefabs/Buildings/GathererFactory.prefab";
    const string SatelitePrefab = "Assets/CartoonSpaceForce/Prefabs/Satelite.prefab";
    const string DronePrefab = "Assets/VoodooPlay/Prefab/Drone.prefab";

    [MenuItem("Tools/Trash Game/11. Drone Bay (Building + Master Disk)")]
    public static void Build()
    {
        var tree = TreeAsset();
        var prefab = BayPrefab(tree);
        var def = Def(prefab);

        var bm = Object.FindFirstObjectByType<BuildMode>(FindObjectsInactive.Include);
        if (bm != null && def != null && !bm.catalog.Contains(def))
        {
            bm.catalog.Add(def);
            EditorUtility.SetDirty(bm);
            Debug.Log("Drone Bay added to the catalog. Re-run '8. Build Mode' once so the BUILD tab row scrolls (4+ cards).");
        }
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Drone Bay ready: " + PrefabPath);
    }

    // ------------------------------------------------------------------ tree
    static BuildingUpgradeTree TreeAsset()
    {
        var tree = AssetDatabase.LoadAssetAtPath<BuildingUpgradeTree>(TreePath);
        if (tree != null) return tree;

        tree = ScriptableObject.CreateInstance<BuildingUpgradeTree>();
        tree.displayName = "DRONE BAY";
        tree.category = "PRODUCTION BUILDING";
        tree.outputName = "COMBAT DRONE";
        tree.levelLabel = "BAY LEVEL";
        tree.levelRowLabel = "Drone Level";
        tree.robotType = RobotType.Drone;
        tree.unitWord = "DRONE";
        tree.hasSlots = true;
        tree.masterDiskUnlockLevel = 4;
        tree.shownStats = new List<StatType> { Damage, FireRate, AttackRange, MaxHealth, MoveSpeed, BatteryRuntime, BuildTime, BotCost };
        // level-1 drone values (C3 may move these onto the drone prefab)
        tree.baseStats = new List<StatMod> { Flat(Damage, 8), Flat(FireRate, 1.5f), Flat(AttackRange, 12), Flat(MaxHealth, 40), Flat(MoveSpeed, 4.5f), Flat(BatteryRuntime, 90) };
        tree.levelCosts = new List<int> { 50, 100, 180, 280, 420 };   // max level 6
        tree.perLevel = new List<StatMod> { Pct(Damage, 8), Pct(MaxHealth, 8) };
        tree.tiers = new List<UpgradeTier>
        {
            Tier(Perk("Rapid Fire", "RPD", "Spray more shots.", Pct(FireRate, 25)),
                 Perk("Heavy Rounds", "HVY", "Bigger bang, slower trigger.", Pct(Damage, 25), Pct(FireRate, -10))),
            Tier(Perk("Armor Plating", "ARM", "Take more hits.", Pct(MaxHealth, 30)),
                 Perk("Long Barrel", "LNG", "Shoot from farther away.", Pct(AttackRange, 25))),
            Tier(Perk("Thrusters", "THR", "Fly faster.", Pct(MoveSpeed, 20)),
                 Perk("Efficient Core", "ECO", "Fight longer per charge.", Pct(BatteryRuntime, 30))),
            Tier(Perk("Mass Production", "MAS", "Cheaper drones.", Pct(BotCost, -20)),
                 Perk("Fast Assembly", "FST", "Drones roll out sooner.", Pct(BuildTime, -25))),
            Tier(Perk("Overcharge", "OVR", "Hit much harder, drain the cell.", Pct(Damage, 30), Pct(BatteryRuntime, -10)),
                 Perk("Fortress", "FRT", "Tank mode: tough but slow.", Pct(MaxHealth, 45), Pct(MoveSpeed, -10))),
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

    // ------------------------------------------------------------------ prefab
    static GameObject BayPrefab(BuildingUpgradeTree tree)
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing == null)
        {
            if (!AssetDatabase.CopyAsset(FactoryPrefab, PrefabPath)) { Debug.LogError("Can't copy " + FactoryPrefab); return null; }
            AssetDatabase.Refresh();
            existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            ReshapeFromFactory();
        }

        // always: link the tree
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var up = root.GetComponent<BuildingUpgrades>();
            if (up != null && up.tree != tree) { up.tree = tree; PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); }
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return existing;
    }

    // factory copy -> drone bay: new model, 3x3, drone settings
    static void ReshapeFromFactory()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            root.name = "DroneBay";
            var body = root.transform.Find("Body");
            if (body != null)
                for (int i = body.childCount - 1; i >= 0; i--) Object.DestroyImmediate(body.GetChild(i).gameObject);
            else { body = new GameObject("Body").transform; body.SetParent(root.transform, false); }

            // landing pad
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = "Pad";
            Object.DestroyImmediate(pad.GetComponent<Collider>());
            pad.transform.SetParent(body, false);
            pad.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            pad.transform.localScale = new Vector3(4.6f, 0.1f, 4.6f);
            var padMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/FactorySphere.mat");
            if (padMat != null) pad.GetComponent<Renderer>().sharedMaterial = padMat;

            // the Satelite model hovering over the pad
            var sat = PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SatelitePrefab), body) as GameObject;
            sat.name = "Satelite";
            sat.transform.localPosition = Vector3.zero;
            sat.transform.localScale = Vector3.one * 1.6f;
            var r = sat.GetComponentInChildren<Renderer>();
            if (r != null)
            {
                var b = r.bounds;   // root is at the origin, so world bounds = local bounds
                sat.transform.localPosition = new Vector3(-b.center.x, 1.0f - b.min.y, -b.center.z);
            }

            // label floats above the model (the factory's label height cuts through the taller satellite)
            float top = 0f;
            foreach (var rr in sat.GetComponentsInChildren<Renderer>()) top = Mathf.Max(top, rr.bounds.max.y);
            var lbl = root.transform.Find("Label");
            if (lbl != null) lbl.localPosition = new Vector3(0f, top + 1.0f, 0f);

            var fp = root.GetComponent<GridFootprint>();
            if (fp != null) { fp.size = new Vector2Int(3, 3); fp.centerOffset = Vector2.zero; }

            var bf = root.GetComponent<BotFactory>();
            if (bf != null)
            {
                bf.botPrefab = null;   // Combat Drone arrives with C3
                bf.previewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DronePrefab);
                bf.costPerBot = 30;
                bf.buildTime = 8f;
                bf.radius = 3.6f;
                bf.body = body;
                bf.idleText = "Drone Bay";
                if (bf.label != null) bf.label.text = bf.idleText;
                if (bf.spawnPoint != null) bf.spawnPoint.localPosition = new Vector3(0f, 0f, -3.4f);
                if (bf.ring != null) bf.ring.radius = bf.radius;
            }
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // ------------------------------------------------------------------ catalog entry
    static BuildingDef Def(GameObject prefab)
    {
        var d = AssetDatabase.LoadAssetAtPath<BuildingDef>(DefPath);
        if (d == null)
        {
            d = ScriptableObject.CreateInstance<BuildingDef>();
            AssetDatabase.CreateAsset(d, DefPath);
            d.displayName = "Drone Bay";
            d.description = "Builds Combat Drones. Own upgrades and Master Disk.";
            d.cost = 120;
            d.color = RobotTypes.Color(RobotType.Drone);
            d.glyph = "D";
        }
        d.prefab = prefab;
        EditorUtility.SetDirty(d);
        return d;
    }
}
