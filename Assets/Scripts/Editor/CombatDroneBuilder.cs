using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// 12. Combat Drone (C3): drone disks (GUARD / ESCORT), the CombatDrone prefab, hooks it into the Drone Bay,
/// and drops one of each drone disk on the ground near the base. Safe to re-run (assets keep tweaks; delete the
/// prefab to regenerate it).
public static class CombatDroneBuilder
{
    const string PrefabPath = "Assets/Prefabs/CombatDrone.prefab";
    const string ModelPath = "Assets/VoodooPlay/Prefab/Drone.prefab";
    const string ScoutPath = "Assets/Prefabs/ScoutBot.prefab";
    const string BayPath = "Assets/Prefabs/Buildings/DroneBay.prefab";
    const string DiskPrefabPath = "Assets/Prefabs/Disk.prefab";

    /// Model yaw so the drone's nose points along +Z (the root's forward).
    public static float ModelYaw = 0f;
    /// Width of the drone model in metres.
    public static float ModelWidth = 1.5f;
    /// Labels / bars are moved up by this much compared to the Scout Bot (the drone hovers).
    const float LabelLift = 1.15f;

    [MenuItem("Tools/Trash Game/12. Combat Drone (Unit + Drone Disks)")]
    public static void Build()
    {
        var guard = Disk<GuardProgram>("Assets/Programs/Disk_Guard.asset", "Guard", "GRD",
            "Drone stays where you give it this disk and shoots every enemy that comes near.", new Color(1f, 0.42f, 0.3f));
        var escort = Disk<EscortProgram>("Assets/Programs/Disk_Escort.asset", "Escort", "ESC",
            "Drone stays close to you and shoots anything that threatens you.", new Color(0.45f, 0.75f, 1f));
        var patrol = Disk<PatrolProgram>("Assets/Programs/Disk_Patrol.asset", "Patrol", "PTL",
            "Drone flies between spots around the base and the explored map, hovers a few seconds at each, and shoots enemies it meets.", new Color(0.55f, 1f, 0.45f));

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) prefab = MakePrefab();

        var bay = AssetDatabase.LoadAssetAtPath<GameObject>(BayPath);
        if (bay != null)
        {
            var f = bay.GetComponent<BotFactory>();
            f.botPrefab = prefab;
            EditorUtility.SetDirty(bay);
            PrefabUtility.SavePrefabAsset(bay);
        }
        else Debug.LogWarning("Drone Bay prefab not found: run builder 11 first");

        PlaceDisks(guard, escort, patrol);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("Combat Drone ready: " + PrefabPath + ", disks GUARD + ESCORT + PATROL, Drone Bay hooked up");
    }

    static T Disk<T>(string path, string name, string shortName, string desc, Color c) where T : DiskProgram
    {
        var d = AssetDatabase.LoadAssetAtPath<T>(path);
        if (d != null) return d;
        d = ScriptableObject.CreateInstance<T>();
        d.displayName = name;
        d.shortName = shortName;
        d.description = desc;
        d.color = c;
        d.compatibleWith = RobotType.Drone;
        AssetDatabase.CreateAsset(d, path);
        return d;
    }

    static GameObject MakePrefab()
    {
        var root = new GameObject("CombatDrone");
        var drone = root.AddComponent<CombatDrone>();

        // model: an empty pivot (moved by the hover code) + the Voodoo drone mesh scaled down
        var pivot = new GameObject("Model").transform;
        pivot.SetParent(root.transform, false);
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var mesh = (GameObject)PrefabUtility.InstantiatePrefab(src, pivot);
        var b = Bounds(mesh);
        float s = ModelWidth / Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z));
        mesh.transform.localScale = Vector3.one * s;
        mesh.transform.localRotation = Quaternion.Euler(0f, ModelYaw, 0f);
        mesh.transform.localPosition = -b.center * s;   // centre the mesh on the pivot
        drone.model = pivot;

        // status label, level tag, battery bar: copied from the Scout Bot and lifted above the hovering drone
        var scoutPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ScoutPath);
        var scout = (GameObject)PrefabUtility.InstantiatePrefab(scoutPrefab);
        PrefabUtility.UnpackPrefabInstance(scout, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        var sb = scout.GetComponent<ScoutBot>();
        foreach (var name in new[] { "Status", "LevelTag", "BatteryBar" })
        {
            var t = scout.transform.Find(name);
            if (t == null) continue;
            t.SetParent(root.transform, false);
            t.localPosition += Vector3.up * LabelLift;
        }
        drone.statusLabel = root.transform.Find("Status").GetComponent<TMPro.TMP_Text>();
        drone.levelTag = root.transform.Find("LevelTag").GetComponent<BotLevelTag>();
        drone.batteryBar = root.transform.Find("BatteryBar").GetComponent<BatteryBar>();
        var fog = scout.GetComponent<FogRevealer>();
        if (fog != null)
        {
            var fr = root.AddComponent<FogRevealer>();
            EditorUtility.CopySerialized(fog, fr);
            fr.radius = 8f;   // sees a bit further than a gatherer (gun range 12; black fog targets are ignored)
        }

        // power numbers like the gatherer; stats come from the Drone Bay at spawn
        drone.jumpStartRadius = sb.jumpStartRadius;
        drone.jumpStartRate = sb.jumpStartRate;
        drone.jumpStartTo = sb.jumpStartTo;
        drone.lowBatteryFraction = sb.lowBatteryFraction;
        drone.reserveSeconds = sb.reserveSeconds;
        drone.batteryRuntime = 90f;
        drone.moveSpeed = 4.5f;
        drone.turnSpeed = 300f;
        drone.capacity = 0;
        drone.maxHP = 40f;
        drone.hpBarHeight = 3.2f + LabelLift;
        drone.robotType = RobotType.Drone;
        Object.DestroyImmediate(scout);

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs.Length > 0 ? rs[0].bounds : new Bounds(go.transform.position, Vector3.one);
        foreach (var r in rs) b.Encapsulate(r.bounds);
        b.center -= go.transform.position;
        return b;
    }

    // one GUARD, one ESCORT and one PATROL disk on the ground near the base (inside the start pipe range)
    static void PlaceDisks(DiskProgram guard, DiskProgram escort, DiskProgram patrol)
    {
        var diskPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DiskPrefabPath);
        var home = GameObject.Find("Base_Home");
        if (diskPrefab == null || home == null) { Debug.LogWarning("Disk prefab or Base_Home missing: no drone disks placed"); return; }
        var parent = GameObject.Find("Disks");
        Place(diskPrefab, guard, home.transform.position + new Vector3(6.5f, 0f, 3.5f), parent);
        Place(diskPrefab, escort, home.transform.position + new Vector3(-6.5f, 0f, 3.5f), parent);
        Place(diskPrefab, patrol, home.transform.position + new Vector3(0f, 0f, -7f), parent);
    }

    static void Place(GameObject prefab, DiskProgram program, Vector3 pos, GameObject parent)
    {
        string name = "Disk " + program.displayName + " (drone)";
        var existing = GameObject.Find(name);
        if (existing != null) return;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent != null ? parent.transform : null);
        go.name = name;
        pos.y = Physics.Raycast(pos + Vector3.up * 50f, Vector3.down, out var hit, 100f) ? hit.point.y : pos.y;
        go.transform.position = pos;
        go.GetComponent<DiskPickup>().program = program;
        var label = go.transform.Find("Visual/Label");
        if (label != null)
        {
            string path = "Assets/Materials/DiskLabel_" + program.shortName + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m.SetColor("_BaseColor", program.color);
                AssetDatabase.CreateAsset(m, path);
            }
            label.GetComponent<Renderer>().sharedMaterial = m;
        }
        EditorUtility.SetDirty(go);
    }
}
