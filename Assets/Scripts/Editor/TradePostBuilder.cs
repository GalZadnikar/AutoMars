using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// 14. Trade Post: a shop building with the Red Planet model. Walk up to it → TRADE button → popup with BATTERIES and
/// DISKS tabs (buy with materials). Makes the prefab (only if missing: delete TradePost.prefab to rebuild it; your
/// price / stock edits on the prefab survive re-runs otherwise), the catalog entry and adds it to the BUILD tab.
/// Re-run 8. Build Mode afterwards if the new card doesn't show up.
public static class TradePostBuilder
{
    const string PrefabPath = "Assets/Prefabs/Buildings/TradePost.prefab";
    const string DefPath = "Assets/Buildings/Build_TradePost.asset";
    const string PlanetPrefab = "Assets/CartoonSpaceForce/Prefabs/RedPlanet.prefab";
    const string BayPrefab = "Assets/Prefabs/Buildings/DroneBay.prefab";

    [MenuItem("Tools/Trash Game/14. Trade Post (Shop Building)")]
    public static void Build()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) prefab = MakePrefab();
        if (prefab == null) return;
        var def = Def(prefab);

        var bm = Object.FindFirstObjectByType<BuildMode>(FindObjectsInactive.Include);
        if (bm != null && !bm.catalog.Contains(def))
        {
            bm.catalog.Add(def);
            EditorUtility.SetDirty(bm);
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Trade Post ready: " + PrefabPath + " (BUILD tab of the base popup).");
    }

    static GameObject MakePrefab()
    {
        var bay = AssetDatabase.LoadAssetAtPath<GameObject>(BayPrefab);
        var planetSrc = AssetDatabase.LoadAssetAtPath<GameObject>(PlanetPrefab);
        if (planetSrc == null) { Debug.LogError("[Trade] " + PlanetPrefab + " not found"); return null; }

        var root = new GameObject("TradePost");
        try
        {
            var fp = root.AddComponent<GridFootprint>();
            fp.size = new Vector2Int(3, 3);
            fp.centerOffset = Vector2.zero;
            var fogSrc = bay != null ? bay.GetComponent<FogRevealer>() : null;
            var fog = root.AddComponent<FogRevealer>();
            if (fogSrc != null) EditorUtility.CopySerialized(fogSrc, fog);

            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);
            var padMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/FactorySphere.mat");
            var pillarMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/ChargerBody.mat") ?? padMat;

            var pad = Prim(PrimitiveType.Cylinder, "Pad", body, new Vector3(0f, 0.1f, 0f), new Vector3(4.6f, 0.1f, 4.6f), padMat, false);
            var pillar = Prim(PrimitiveType.Cylinder, "Pillar", body, new Vector3(0f, 0.9f, 0f), new Vector3(0.7f, 0.8f, 0.7f), pillarMat, true);
            Prim(PrimitiveType.Cylinder, "Cradle", body, new Vector3(0f, 1.75f, 0f), new Vector3(1.5f, 0.06f, 1.5f), pillarMat, false);

            // the planet floats above the pillar and turns slowly (TradePost.spinner)
            var holder = new GameObject("Planet").transform;
            holder.SetParent(body, false);
            holder.localPosition = new Vector3(0f, 3.25f, 0f);
            var planet = (GameObject)PrefabUtility.InstantiatePrefab(planetSrc, holder);
            PrefabUtility.UnpackPrefabInstance(planet, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            planet.transform.localPosition = Vector3.zero;
            planet.transform.localScale = Vector3.one * 0.95f;
            foreach (var c in planet.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            var ringT = planet.transform.Find("PlanetRing");
            if (ringT != null) ringT.localRotation = Quaternion.Euler(16f, 0f, 8f);
            float top = 0f;
            foreach (var r in planet.GetComponentsInChildren<Renderer>()) top = Mathf.Max(top, r.bounds.max.y);

            // label + ground ring: copies of the Drone Bay's (same look as every building)
            var tp = root.AddComponent<TradePost>();
            tp.spinner = holder;
            if (bay != null)
            {
                var lbl = bay.transform.Find("Label");
                if (lbl != null)
                {
                    var l = Object.Instantiate(lbl.gameObject, root.transform);
                    l.name = "Label";
                    l.transform.localPosition = new Vector3(0f, top + 1.1f, 0f);
                    tp.label = l.GetComponent<TMPro.TMP_Text>();
                    if (tp.label != null) tp.label.text = tp.idleText;
                }
                var ring = bay.transform.Find("Ring");
                if (ring != null)
                {
                    var g = Object.Instantiate(ring.gameObject, root.transform);
                    g.name = "Ring";
                    tp.ring = g.GetComponent<GroundRing>();
                    if (tp.ring != null) tp.ring.radius = tp.radius;
                }
            }

            // stock: every battery rarity (default prices) + the four disks
            foreach (var b in BatteryItem.LoadAll().OrderBy(x => (int)x.rarity))
                tp.batteries.Add(new TradePost.BatteryOffer { item = b, price = 0 });
            AddDisk(tp, "Assets/Programs/Disk_CollectTrash.asset", 40);
            AddDisk(tp, "Assets/Programs/Disk_Patrol.asset", 60);
            AddDisk(tp, "Assets/Programs/Disk_Guard.asset", 60);
            AddDisk(tp, "Assets/Programs/Disk_Escort.asset", 80);
            tp.batteryIcon = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Generated/Battery.png");
            tp.diskIcon = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Generated/Disk.png");

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[Trade] created " + PrefabPath);
            return prefab;
        }
        finally { Object.DestroyImmediate(root); }
    }

    static void AddDisk(TradePost tp, string path, int price)
    {
        var d = AssetDatabase.LoadAssetAtPath<DiskProgram>(path);
        if (d != null) tp.disks.Add(new TradePost.DiskOffer { disk = d, price = price });
    }

    static Transform Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material m, bool keepCollider)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        if (m != null) go.GetComponent<Renderer>().sharedMaterial = m;
        return go.transform;
    }

    static BuildingDef Def(GameObject prefab)
    {
        var d = AssetDatabase.LoadAssetAtPath<BuildingDef>(DefPath);
        if (d == null)
        {
            d = ScriptableObject.CreateInstance<BuildingDef>();
            AssetDatabase.CreateAsset(d, DefPath);
            d.displayName = "Trade Post";
            d.description = "Buy batteries and disks with materials";
            d.cost = 60;
            d.buildTime = 5f;
            d.color = new Color(0.95f, 0.45f, 0.32f);
            d.glyph = "T";
        }
        d.prefab = prefab;
        EditorUtility.SetDirty(d);
        return d;
    }
}
