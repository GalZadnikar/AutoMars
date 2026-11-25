using UnityEngine;

/// The visible beam of a beam weapon (WeaponDef.mode = Beam): a laser prefab from the Hovl "3D Lasers Pack"
/// (a LineRenderer with a "Flash" child at the start and a "Hit" child at the end), driven from PlayerWeapon.
/// The pack's own HS_Laser script is removed (FxPool does that); this only moves the two ends and makes the
/// beam thicker and its hit effect bigger as `power` goes from 0 to 1, so the damage ramp can be seen.
public class WeaponBeam : MonoBehaviour
{
    static readonly int MainST = Shader.PropertyToID("_MainTex_ST");

    LineRenderer line;
    Transform hit, flash;
    ParticleSystem[] hitFx = new ParticleSystem[0], flashFx = new ParticleSystem[0];
    MaterialPropertyBlock mpb;
    Vector4 mainST = new Vector4(1f, 1f, 0f, 0f);
    float baseWidth = 0.2f, scale = 1f;
    bool on;

    public static WeaponBeam Create(GameObject prefab, float scale)
    {
        var holder = new GameObject("Weapon Beam");
        var b = holder.AddComponent<WeaponBeam>();
        b.scale = scale;
        b.mpb = new MaterialPropertyBlock();
        var go = FxPool.Create(prefab, holder.transform);
        if (go == null) return b;
        b.line = go.GetComponentInChildren<LineRenderer>(true);
        b.hit = go.transform.Find("Hit");
        b.flash = go.transform.Find("Flash");
        if (b.line != null)
        {
            b.baseWidth = b.line.widthMultiplier;
            b.line.useWorldSpace = true;
            b.line.positionCount = 2;
            b.line.enabled = false;
            var m = b.line.sharedMaterial;
            if (m != null && m.HasProperty(MainST)) b.mainST = m.GetVector(MainST);
        }
        if (b.hit != null) b.hitFx = b.hit.GetComponentsInChildren<ParticleSystem>(true);
        if (b.flash != null) b.flashFx = b.flash.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        return b;
    }

    /// Beam on, from the muzzle to the target. power 0..1 = how far the damage has ramped up.
    public void Set(Vector3 from, Vector3 to, float power)
    {
        Vector3 d = to - from;
        float len = d.magnitude;
        if (len < 0.01f) return;
        if (!on)
        {
            on = true;
            if (line != null) line.enabled = true;
            foreach (var ps in flashFx) ps.Play(false);
            foreach (var ps in hitFx) ps.Play(false);
        }
        if (line != null)
        {
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            float flicker = 1f + 0.1f * Mathf.Sin(Time.time * 47f) * power;
            line.widthMultiplier = baseWidth * scale * Mathf.Lerp(0.7f, 2.4f, power) * flicker;
            // the beam texture is tiled along its length, like the pack's own script does
            Vector4 st = mainST;
            st.x = mainST.x * len / Mathf.Max(0.05f, scale);
            line.GetPropertyBlock(mpb);
            mpb.SetVector(MainST, st);
            line.SetPropertyBlock(mpb);
        }
        if (flash != null)
        {
            flash.SetPositionAndRotation(from, Quaternion.LookRotation(d));
            flash.localScale = Vector3.one * scale * Mathf.Lerp(0.7f, 1.3f, power);
        }
        if (hit != null)
        {
            hit.SetPositionAndRotation(to, Quaternion.LookRotation(-d));
            hit.localScale = Vector3.one * scale * Mathf.Lerp(0.45f, 1.1f, power);
        }
    }

    public void Off()
    {
        if (!on) return;
        on = false;
        if (line != null) line.enabled = false;
        foreach (var ps in flashFx) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        foreach (var ps in hitFx) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
    }
}
