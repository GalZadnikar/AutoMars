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

        clips.Add(BuildDig("Dig", DigQuickTimes, 0.08f, 0.17f, 0.29f, 0.40f));
        clips.Add(BuildDig("DigHeavy", DigHeavyTimes, 0.46f, 0.42f, 0.53f, 0.64f));
        BuildController(clips);
        AssetDatabase.SaveAssets();
        Debug.Log("MeleeClipBuilder: rebuilt " + clips.Count + " swing clips in " + OutFolder);
    }

    // ------------------------------------------------------------------ shovel dig (keyed in code, no Mixamo clip)
    //
    // The astronaut's arms are two short bones, so stock dig clips put the shovel under the ground or make him crouch
    // to the floor. This one is posed here instead: feet planted in the idle pose, head kept up, the right arm (the shovel
    // runs along it) thrusts forward-down into the sand and then flicks up. Dig.anim starts and ends in the same pose.
    //   pitch = body leans forward (degrees), yaw = body twists (+ = right shoulder back), drop = hips lower (m),
    //   right / left = where that arm points (x right, y up, z forward).

    class DigKey { public float t, pitch, yaw, drop; public Vector3 right, left; }

    static readonly DigKey[] DigKeys =
    {
        new DigKey { t = 0.00f, pitch = 4f,  yaw = 0f,   drop = 0.00f, right = new Vector3(0.15f, -0.20f, 1f),   left = new Vector3(0.45f, -0.45f, 0.8f) },   // ready
        new DigKey { t = 0.08f, pitch = 0f,  yaw = 22f,  drop = 0.00f, right = new Vector3(0.30f, -0.05f, 0.85f), left = new Vector3(0.55f, -0.35f, 0.7f) },   // pull back
        new DigKey { t = 0.17f, pitch = 16f, yaw = -20f, drop = 0.10f, right = new Vector3(0.00f, -0.55f, 1f),   left = new Vector3(0.30f, -0.70f, 0.8f) },   // thrust in = CONTACT
        new DigKey { t = 0.22f, pitch = 18f, yaw = -22f, drop = 0.12f, right = new Vector3(0.00f, -0.50f, 1f),   left = new Vector3(0.30f, -0.65f, 0.8f) },   // pressed in
        new DigKey { t = 0.32f, pitch = -7f, yaw = 8f,   drop = 0.00f, right = new Vector3(0.10f, 0.55f, 1f),    left = new Vector3(0.40f, 0.15f, 0.9f) },    // flick up
        new DigKey { t = 0.40f, pitch = -9f, yaw = 12f,  drop = 0.00f, right = new Vector3(0.15f, 0.70f, 0.9f),  left = new Vector3(0.45f, 0.25f, 0.85f) },   // top of the flick
        new DigKey { t = 0.60f, pitch = 4f,  yaw = 0f,   drop = 0.00f, right = new Vector3(0.15f, -0.20f, 1f),   left = new Vector3(0.45f, -0.45f, 0.8f) },   // back to ready
    };
    // key times: ready, pull back, CONTACT, pressed in, flick up, top of flick, ready
    static readonly float[] DigQuickTimes = { 0f, 0.08f, 0.17f, 0.22f, 0.32f, 0.40f, 0.60f };
    // heavy: slow press down (0.16 -> 0.42), short hold, then a fast snap up (0.06 s) - the sand goes with the snap
    static readonly float[] DigHeavyTimes = { 0f, 0.16f, 0.42f, 0.49f, 0.55f, 0.61f, 0.84f };

    static AnimationClip BuildDig(string clipName, float[] times, float trailOn, float hit, float toss, float trailOff)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(RigModel);
        AnimationClip idle = null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(RigModel))
            if (o is AnimationClip c && c.name == "Idle") idle = c;

        string path = OutFolder + "/" + clipName + ".anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        bool isNew = clip == null;
        if (isNew) clip = new AnimationClip();
        else clip.ClearCurves();
        clip.name = clipName;
        clip.frameRate = OutFps;

        var go = Object.Instantiate(model);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        try
        {
            if (idle != null) idle.SampleAnimation(go, 0f);
            Transform arm = go.transform.Find("Armature");
            Transform body = arm.Find("Root/Body_Upper"), head = body.Find("Head");
            Transform r1 = body.Find("Arm_1_Right"), r2 = r1.Find("Arm_2_Right"), l1 = body.Find("Arm_1_Left"), l2 = l1.Find("Arm_2_Left");

            var bones = new List<Transform>();
            foreach (var t in arm.GetComponentsInChildren<Transform>())
                if (!t.name.EndsWith("_end")) bones.Add(t);
            var baseRot = new Quaternion[bones.Count];
            for (int i = 0; i < bones.Count; i++) baseRot[i] = bones[i].localRotation;
            Vector3 basePos = arm.position;
            Quaternion headWorld = head.rotation;

            var rot = new AnimationCurve[bones.Count, 4];
            for (int i = 0; i < bones.Count; i++) for (int k = 0; k < 4; k++) rot[i, k] = new AnimationCurve();
            var pos = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            var lastQ = (Quaternion[])baseRot.Clone();

            for (int ki = 0; ki < DigKeys.Length; ki++)
            {
                var key = DigKeys[ki];
                float kt = times[ki];
                for (int i = 0; i < bones.Count; i++) bones[i].localRotation = baseRot[i];
                arm.position = basePos + Vector3.down * key.drop;
                body.rotation = Quaternion.AngleAxis(key.yaw, Vector3.up) * Quaternion.AngleAxis(key.pitch, Vector3.right) * body.rotation;
                head.rotation = Quaternion.Slerp(head.rotation, headWorld, 0.8f);   // the head stays up and looks ahead
                PointBone(r1, key.right); PointBone(r2, key.right);
                PointBone(l1, key.left); PointBone(l2, key.left);

                for (int i = 0; i < bones.Count; i++)
                {
                    Quaternion q = bones[i].localRotation;
                    if (Quaternion.Dot(lastQ[i], q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);   // same sign as the key before
                    lastQ[i] = q;
                    rot[i, 0].AddKey(kt, q.x); rot[i, 1].AddKey(kt, q.y); rot[i, 2].AddKey(kt, q.z); rot[i, 3].AddKey(kt, q.w);
                }
                Vector3 lp = arm.localPosition;
                pos[0].AddKey(kt, lp.x); pos[1].AddKey(kt, lp.y); pos[2].AddKey(kt, lp.z);
            }

            string[] rn = { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
            string[] pn = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z" };
            for (int i = 0; i < bones.Count; i++)
            {
                string p = AnimationUtility.CalculateTransformPath(bones[i], go.transform);
                for (int k = 0; k < 4; k++)
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(p, typeof(Transform), rn[k]), Smooth(rot[i, k]));
            }
            for (int k = 0; k < 3; k++)
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Armature", typeof(Transform), pn[k]), Smooth(pos[k]));
            clip.EnsureQuaternionContinuity();
        }
        finally { Object.DestroyImmediate(go); }

        AnimationUtility.SetAnimationEvents(clip, new[]
        {
            new AnimationEvent { time = trailOn, functionName = "MeleeTrail", intParameter = 1 },
            new AnimationEvent { time = hit, functionName = "MeleeHit" },
            new AnimationEvent { time = toss, functionName = "DigToss" },
            new AnimationEvent { time = trailOff, functionName = "MeleeTrail", intParameter = 0 },
        });
        if (isNew) AssetDatabase.CreateAsset(clip, path);
        else EditorUtility.SetDirty(clip);
        return clip;
    }

    /// Turns a bone so its length (its Y axis) points along `dir`.
    static void PointBone(Transform bone, Vector3 dir)
    {
        bone.rotation = Quaternion.FromToRotation(bone.up, dir.normalized) * bone.rotation;
    }

    static AnimationCurve Smooth(AnimationCurve c)
    {
        for (int i = 0; i < c.length; i++) c.SmoothTangents(i, 0f);
        return c;
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
