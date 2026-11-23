using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// Builds the player's melee swings from the two Mixamo slashes (Tools > AutoMars > Rebuild Melee Clips).
///  - converts them to the astronaut's own rig (the Mixamo copy is 10x bigger and its clips start at the armature)
///  - cuts out only the swing and RETIMES it: normal wind-up, very fast strike, short follow-through, slow settle
///  - puts the Animation Events on it: MeleeTrail(1) -> MeleeHit (contact frame) -> MeleeTrail(0)
/// and adds the two states to a copy of the astronaut's Animator controller.
/// To change the feel, edit the numbers in Swings below and run the menu item again.
public static class MeleeClipBuilder
{
    const string SrcFolder = "Assets/Stylized_Astronaut/Animations/";
    const string RigModel = "Assets/Stylized_Astronaut/Character/Astronaut.fbx";
    const string MixamoModel = SrcFolder + "T-Pose.fbx";
    const string BaseController = "Assets/Stylized_Astronaut/Character/AstronautCharacterController.controller";
    public const string OutFolder = "Assets/Animations/Player";
    public const string ControllerPath = OutFolder + "/AstronautCombat.controller";
    const float SrcFps = 30f, OutFps = 60f;

    class Swing
    {
        public string name, file;
        // time in the new clip (seconds) -> frame of the Mixamo clip (30 fps)
        // knots: 0 start of wind-up, 1 start of strike, 2 CONTACT, 3 end of follow-through, 4 end of settle
        public float[] time, frame;
    }

    static readonly Swing[] Swings =
    {
        // right -> left
        new Swing { name = "Attack_L", file = "Stable Sword Outward Slash",
                    time = new[] { 0f, 0.16f, 0.22f, 0.34f, 0.62f }, frame = new[] { 18f, 23f, 26f, 31f, 33f } },
        // left -> right (starts where Attack_L ends, so they chain)
        new Swing { name = "Attack_R", file = "Stable Sword Inward Slash",
                    time = new[] { 0f, 0.16f, 0.22f, 0.34f, 0.62f }, frame = new[] { 28f, 32.5f, 36f, 39.5f, 41.5f } },
    };

    [MenuItem("Tools/AutoMars/Rebuild Melee Clips")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Animations")) AssetDatabase.CreateFolder("Assets", "Animations");
        if (!AssetDatabase.IsValidFolder(OutFolder)) AssetDatabase.CreateFolder("Assets/Animations", "Player");

        // rest position of the armature in both rigs + the size ratio between them
        Transform rig = AssetDatabase.LoadAssetAtPath<GameObject>(RigModel).transform.Find("Armature");
        Transform mix = AssetDatabase.LoadAssetAtPath<GameObject>(MixamoModel).transform.Find("Armature");
        float ratio = rig.Find("Root/Body_Upper").localPosition.magnitude / mix.Find("Root/Body_Upper").localPosition.magnitude;
        Vector3 rigRest = rig.localPosition, mixRest = mix.localPosition;

        var clips = new List<AnimationClip>();
        foreach (var sw in Swings)
        {
            AnimationClip src = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(SrcFolder + sw.file + ".fbx"))
                if (o is AnimationClip c && !c.name.StartsWith("__preview")) src = c;
            if (src == null) { Debug.LogError("MeleeClipBuilder: no clip in " + sw.file); return; }

            string path = OutFolder + "/" + sw.name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            bool isNew = clip == null;
            if (isNew) clip = new AnimationClip();
            else clip.ClearCurves();
            clip.name = sw.name;
            clip.frameRate = OutFps;

            float length = sw.time[sw.time.Length - 1];
            int n = Mathf.RoundToInt(length * OutFps);
            foreach (var b in AnimationUtility.GetCurveBindings(src))
            {
                var curve = AnimationUtility.GetEditorCurve(src, b);
                bool rootPos = b.path == "" && b.propertyName.StartsWith("m_LocalPosition");
                int axis = b.propertyName.EndsWith(".x") ? 0 : b.propertyName.EndsWith(".y") ? 1 : 2;
                var keys = new Keyframe[n + 1];
                for (int i = 0; i <= n; i++)
                {
                    float t = i / OutFps;
                    float v = curve.Evaluate(Map(sw, t) / SrcFps);
                    if (rootPos) v = rigRest[axis] + (v - mixRest[axis]) * ratio;
                    keys[i] = new Keyframe(t, v);
                }
                var outCurve = new AnimationCurve(keys);
                for (int i = 0; i <= n; i++) outCurve.SmoothTangents(i, 0f);
                string p = b.path == "" ? "Armature" : "Armature/" + b.path;
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(p, typeof(Transform), b.propertyName), outCurve);
            }
            clip.EnsureQuaternionContinuity();

            AnimationUtility.SetAnimationEvents(clip, new[]
            {
                new AnimationEvent { time = sw.time[1], functionName = "MeleeTrail", intParameter = 1 },
                new AnimationEvent { time = sw.time[2], functionName = "MeleeHit" },
                new AnimationEvent { time = sw.time[3], functionName = "MeleeTrail", intParameter = 0 },
            });

            if (isNew) AssetDatabase.CreateAsset(clip, path);
            else EditorUtility.SetDirty(clip);
            clips.Add(clip);
        }

        BuildController(clips);
        AssetDatabase.SaveAssets();
        Debug.Log("MeleeClipBuilder: rebuilt " + clips.Count + " swing clips in " + OutFolder);
    }

    /// Mixamo frame for a time in the new clip (straight lines between the knots).
    static float Map(Swing sw, float t)
    {
        for (int i = 1; i < sw.time.Length; i++)
            if (t <= sw.time[i] || i == sw.time.Length - 1)
            {
                float k = Mathf.Clamp01(Mathf.InverseLerp(sw.time[i - 1], sw.time[i], t));
                if (i == sw.time.Length - 1) k = 1f - (1f - k) * (1f - k);   // settle eases out
                return Mathf.Lerp(sw.frame[i - 1], sw.frame[i], k);
            }
        return sw.frame[sw.frame.Length - 1];
    }

    static void BuildController(List<AnimationClip> clips)
    {
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (ctrl == null)
        {
            AssetDatabase.CopyAsset(BaseController, ControllerPath);
            ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        }
        bool hasSpeed = false;
        foreach (var p in ctrl.parameters) if (p.name == "AttackSpeed") hasSpeed = true;
        if (!hasSpeed)
            ctrl.AddParameter(new AnimatorControllerParameter { name = "AttackSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });

        var sm = ctrl.layers[0].stateMachine;
        AnimatorState idle = null, run = null;
        foreach (var s in sm.states)
        {
            if (s.state.name == "Idle") idle = s.state;
            if (s.state.name == "Run") run = s.state;
        }
        for (int i = 0; i < clips.Count; i++)
        {
            AnimatorState st = null;
            foreach (var s in sm.states) if (s.state.name == clips[i].name) st = s.state;
            if (st == null) st = sm.AddState(clips[i].name, new Vector3(520f, 60f + i * 70f, 0f));
            st.motion = clips[i];
            st.speedParameterActive = true;
            st.speedParameter = "AttackSpeed";
            foreach (var tr in st.transitions) st.RemoveTransition(tr);
            if (run != null)
            {
                // walking cancels the swing right away
                var t = st.AddTransition(run);
                t.hasExitTime = false; t.hasFixedDuration = true; t.duration = 0.08f;
                t.AddCondition(AnimatorConditionMode.Equals, 1f, "AnimationPar");
            }
            if (idle != null)
            {
                // nothing chained: relax back to idle at the end of the settle
                var t = st.AddTransition(idle);
                t.hasExitTime = true; t.exitTime = 0.85f; t.hasFixedDuration = true; t.duration = 0.2f;
            }
        }
        EditorUtility.SetDirty(ctrl);
    }
}
