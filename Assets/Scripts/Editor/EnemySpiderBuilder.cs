using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// Builds the spider enemy from the "Polygonal Alien Horror" pack:
/// - Assets/Animations/Enemies/EnemySpider.controller (states only; EnemySpider.cs cross-fades between them)
/// - Assets/Resources/Enemies/Enemy Spider.prefab
/// Tools > Trash Game > 13. Enemies (Spider Prefab) makes both (an existing prefab keeps your edits; delete it to rebuild).
/// Tools > Trash Game > 13. Enemies (Add Spider) puts one in the scene at the Scene view pivot.
public static class EnemySpiderBuilder
{
    const string Pack = "Assets/Polygonal Alien Horror";
    const string ModelPrefab = Pack + "/Prefabs/Polygonal Alien Horror Brown.prefab";
    const string ControllerFolder = "Assets/Animations/Enemies";
    const string ControllerPath = ControllerFolder + "/EnemySpider.controller";
    const string PrefabFolder = "Assets/Resources/Enemies";
    public const string PrefabPath = PrefabFolder + "/Enemy Spider.prefab";
    public const float ModelScale = 0.7f;

    static AnimationClip Clip(string name) =>
        AssetDatabase.LoadAssetAtPath<AnimationClip>(Pack + "/FBX/Polygonal Alien Horror@" + name + ".FBX");

    [MenuItem("Tools/Trash Game/13. Enemies (Spider Prefab)")]
    public static void Build() => Build(false);

    /// overwrite = rebuild the prefab from the values in this file even if it exists (edits made on the prefab are lost).
    public static void Build(bool overwrite)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefab);
        if (model == null) { Debug.LogError("[Spider] model prefab not found: " + ModelPrefab); return; }
        var controller = BuildController();

        if (!overwrite && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            Debug.Log("[Spider] prefab already exists (delete it to rebuild): " + PrefabPath);
            return;
        }
        if (!Directory.Exists(PrefabFolder)) Directory.CreateDirectory(PrefabFolder);

        var go = new GameObject("Enemy Spider");
        try
        {
            var visual = new GameObject("Visual").transform;
            visual.SetParent(go.transform, false);
            var m = (GameObject)PrefabUtility.InstantiatePrefab(model, visual);
            m.name = "Model";
            m.transform.localPosition = Vector3.zero;
            m.transform.localRotation = Quaternion.identity;
            m.transform.localScale = Vector3.one * ModelScale;
            var anim = m.GetComponent<Animator>();
            anim.runtimeAnimatorController = controller;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var r in m.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.updateWhenOffscreen = true;

            var hp = go.AddComponent<Health>();
            hp.team = Team.Enemy;
            hp.maxHP = 30f;
            hp.aimHeight = 0.85f;
            hp.hitRadius = 0.9f;
            hp.barHeight = 2.8f;
            hp.barWidth = 1.4f;

            var mt = go.AddComponent<MeleeTarget>();
            mt.visual = visual;
            mt.radius = 0.85f;
            mt.breakOnDeath = false;
            mt.respawnDelay = 0f;
            mt.punchScale = 0.12f;
            mt.tiltDegrees = 7f;
            mt.debrisColor = new Color(0.55f, 0.85f, 0.2f);
            mt.fxSize = 1.1f;

            var cc = go.AddComponent<CharacterController>();
            cc.radius = 0.75f;
            cc.height = 1.6f;
            cc.center = new Vector3(0f, 0.83f, 0f);
            cc.stepOffset = 0.3f;
            cc.slopeLimit = 50f;

            var s = go.AddComponent<EnemySpider>();
            s.walkClipSpeed = RootSpeed("Walk Forward Slow W Root") * ModelScale;
            s.runClipSpeed = RootSpeed("Walk Forward Fast W Root") * ModelScale;
            foreach (var a in s.attacks)
            {
                var clip = Clip(a.state == "AttackTail" ? "Tail Stab Attack" : "Front Legs Attack");
                if (clip != null) a.clipLength = clip.length;
            }

            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Debug.Log("[Spider] created " + PrefabPath + "  (walk clip " + s.walkClipSpeed.ToString("F2") + " m/s, run clip " + s.runClipSpeed.ToString("F2") + " m/s)");
        }
        finally { Object.DestroyImmediate(go); }
        AssetDatabase.SaveAssets();
    }

    /// Metres per second the "W Root" version of a walk clip travels (model scale 1).
    static float RootSpeed(string clipName)
    {
        var clip = Clip(clipName);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefab);
        if (clip == null || model == null) return 1f;
        var inst = Object.Instantiate(model);
        inst.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            float best = 0f;
            var bones = inst.GetComponentsInChildren<Transform>();
            var names = new[] { "RigPelvis", "Rig" };
            foreach (var n in names)
            {
                Transform b = null;
                foreach (var t in bones) if (t.name == n) { b = t; break; }
                if (b == null) continue;
                clip.SampleAnimation(inst, 0f);
                Vector3 p0 = b.position;
                clip.SampleAnimation(inst, clip.length);
                float d = Vector3.Distance(p0, b.position);
                if (d > best) best = d;
            }
            return best > 0.05f ? best / clip.length : 1f;
        }
        finally { Object.DestroyImmediate(inst); }
    }

    static AnimatorController BuildController()
    {
        if (!Directory.Exists(ControllerFolder)) { Directory.CreateDirectory(ControllerFolder); AssetDatabase.Refresh(); }
        var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (c != null) return c;
        c = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var sm = c.layers[0].stateMachine;
        AddState(sm, "Idle", "Idle", true);
        AddState(sm, "Look", "Look Around");
        AddState(sm, "Walk", "Walk Forward Slow WO Root");
        AddState(sm, "Run", "Walk Forward Fast WO Root");
        AddState(sm, "AttackLegs", "Front Legs Attack");
        AddState(sm, "AttackTail", "Tail Stab Attack");
        AddState(sm, "Hit", "Take Damage");
        AddState(sm, "Die", "Die");
        EditorUtility.SetDirty(c);
        AssetDatabase.SaveAssets();
        Debug.Log("[Spider] created " + ControllerPath);
        return c;
    }

    static void AddState(AnimatorStateMachine sm, string state, string clip, bool isDefault = false)
    {
        var st = sm.AddState(state);
        st.motion = Clip(clip);
        if (st.motion == null) Debug.LogWarning("[Spider] clip not found: " + clip);
        if (isDefault) sm.defaultState = st;
    }

    [MenuItem("Tools/Trash Game/13. Enemies (Add Spider)")]
    public static void AddToScene()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) { Build(); prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath); }
        if (prefab == null) return;
        var sv = SceneView.lastActiveSceneView;
        Vector3 pos = sv != null ? sv.pivot : Vector3.zero;
        pos.y = GroundUtil.Height(pos, 0f) + 0.05f;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.position = pos;
        Undo.RegisterCreatedObjectUndo(go, "Add Spider");
        Selection.activeGameObject = go;
    }
}
