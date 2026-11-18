using TMPro;
using UnityEngine;

/// A placed building under construction: reserves its grid cells, shows the building rising out of the
/// ground inside an orange hologram with a progress bar + countdown, then swaps in the real building.
public class ConstructionSite : MonoBehaviour
{
    public BuildingDef def;
    public float duration = 5f;

    Transform rising;
    Vector3 risingScale;
    Material holoMat;
    TMP_Text label;
    Transform barFill;
    float t;
    int shownSeconds = -1;
    const float BarWidth = 2.6f;

    static Sprite white;
    static Sprite White => white != null ? white : (white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f));

    /// spriteMaterial = an unlit transparent material (Sprites/Default), e.g. BuildMode.ghostMaterial.
    public static ConstructionSite Create(BuildingDef def, Vector3 pos, Quaternion rot, Material spriteMaterial, Color holoColor)
    {
        var go = new GameObject(def.displayName + " (construction)");
        go.SetActive(false);   // set everything up before the footprint registers
        go.transform.SetPositionAndRotation(pos, rot);

        var src = def.Footprint;
        var fp = go.AddComponent<GridFootprint>();
        if (src != null) { fp.size = src.size; fp.centerOffset = src.centerOffset; }

        var site = go.AddComponent<ConstructionSite>();
        site.def = def;
        site.duration = Mathf.Max(0.1f, def.buildTime);

        // full-size orange hologram of the finished building
        var holo = VisualCopy.Make(def.prefab, go.transform);
        holo.name = "Hologram";
        site.holoMat = new Material(spriteMaterial) { color = holoColor };
        foreach (var r in holo.GetComponentsInChildren<Renderer>())
        {
            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = site.holoMat;
            r.sharedMaterials = mats;
        }

        // the real-looking building rising out of the ground
        var real = VisualCopy.Make(def.prefab, go.transform);
        real.name = "Rising";
        site.rising = real.transform;
        site.risingScale = real.transform.localScale;
        real.transform.localScale = new Vector3(site.risingScale.x, 0.02f, site.risingScale.z);

        // label + progress bar above it
        float top = 4.2f;
        var hud = new GameObject("Progress").transform;
        hud.SetParent(go.transform, false);
        hud.localPosition = new Vector3(0f, top, 0f);
        hud.gameObject.AddComponent<Billboard>();

        Bar("BarBg", hud, spriteMaterial, new Color(0.08f, 0.06f, 0.05f, 0.85f), BarWidth + 0.12f, 0.3f, 0f, 0);
        site.barFill = Bar("BarFill", hud, spriteMaterial, new Color(1f, 0.72f, 0.2f, 1f), BarWidth, 0.2f, -0.01f, 1);

        go.SetActive(true);

        // TextMeshPro needs an active object before its outline can be set
        var lbl = new GameObject("Label");
        lbl.transform.SetParent(hud, false);
        lbl.transform.localPosition = new Vector3(0f, 0.55f, 0f);
        var tmp = lbl.AddComponent<TextMeshPro>();
        tmp.fontSize = 6f; tmp.fontStyle = FontStyles.Bold; tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.rectTransform.sizeDelta = new Vector2(10f, 2f);
        tmp.outlineWidth = 0.25f; tmp.outlineColor = new Color32(40, 25, 20, 255);
        tmp.color = new Color(1f, 0.8f, 0.3f);
        site.label = tmp;

        return site;
    }

    static Transform Bar(string name, Transform parent, Material mat, Color c, float w, float h, float z, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, 0f, z);
        go.transform.localScale = new Vector3(w, h, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = White;
        sr.sharedMaterial = mat;
        sr.color = c;
        sr.sortingOrder = order;
        return go.transform;
    }

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / duration);

        float e = 1f - (1f - k) * (1f - k);   // ease out
        rising.localScale = new Vector3(risingScale.x, risingScale.y * Mathf.Max(0.02f, e), risingScale.z);

        barFill.localScale = new Vector3(BarWidth * k, barFill.localScale.y, 1f);
        barFill.localPosition = new Vector3(-BarWidth * (1f - k) * 0.5f, 0f, -0.01f);

        if (holoMat != null)
        {
            var c = holoMat.color;
            c.a = 0.18f + 0.1f * Mathf.Sin(Time.time * 5f);
            holoMat.color = c;
        }

        int sec = Mathf.CeilToInt(duration - t);
        if (sec != shownSeconds) { shownSeconds = sec; label.text = "BUILDING  " + Mathf.Max(0, sec) + "s"; }

        if (k >= 1f) Complete();
    }

    void Complete()
    {
        enabled = false;
        PushPlayerOut();
        var go = Instantiate(def.prefab, transform.position, transform.rotation);
        go.name = def.displayName;
        go.AddComponent<BuildPop>();
        GameToast.Show(def.displayName.ToUpper() + " READY", def.color);
        Destroy(gameObject);   // our footprint frees its cells; the building has claimed them already
    }

    // the player may be standing where the building appears (you can't walk in build mode): step them out
    void PushPlayerOut()
    {
        var player = GameObject.FindWithTag("Player");
        var fp = GetComponent<GridFootprint>();
        if (player == null || fp == null) return;
        var dims = BuildGrid.Dims(fp.size, fp.QuarterTurns);
        Vector3 c = fp.Center;
        float hx = dims.x * BuildGrid.CellSize * 0.5f, hz = dims.y * BuildGrid.CellSize * 0.5f;
        Vector3 pp = player.transform.position;
        float dx = pp.x - c.x, dz = pp.z - c.z;
        if (Mathf.Abs(dx) >= hx || Mathf.Abs(dz) >= hz) return;
        // nearest edge
        float toX = hx - Mathf.Abs(dx), toZ = hz - Mathf.Abs(dz);
        if (toX < toZ) pp.x = c.x + Mathf.Sign(dx == 0f ? 1f : dx) * (hx + 0.8f);
        else pp.z = c.z + Mathf.Sign(dz == 0f ? -1f : dz) * (hz + 0.8f);
        pp.y = GroundUtil.Height(pp, pp.y) + 0.1f;
        var cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        player.transform.position = pp;
        if (cc != null) cc.enabled = true;
    }

    void OnDestroy()
    {
        if (holoMat != null) Destroy(holoMat);
    }
}
