using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// Renders a robot model into the Workshop (like DRG's weapon showcase). Drag to spin it.
/// The model is a stripped copy of the bot prefab (no scripts, no labels), placed on its own layer
/// high above the map and rendered by a private camera only while the Workshop is open.
[RequireComponent(typeof(RawImage))]
public class ModelPreview : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public string layerName = "WorkshopPreview";
    public Vector3 rigPosition = new Vector3(0f, 600f, 0f);
    public float fieldOfView = 26f;
    public float elevation = 14f;
    public float padding = 1.1f;
    public float autoSpin = 22f;
    public float dragSpeed = 0.35f;
    public int maxTextureSize = 1024;

    RawImage image;
    Camera cam;
    Transform rig, turntable;
    RenderTexture rt;
    GameObject model, shownPrefab;
    int layer = -1;
    float yaw = -35f, spin;
    bool dragging;

    public void Show(GameObject prefab)
    {
        EnsureRig();
        rig.gameObject.SetActive(true);
        if (prefab != shownPrefab)
        {
            if (model != null) Destroy(model);
            model = prefab != null ? BuildModel(prefab) : null;
            shownPrefab = prefab;
            Frame();
        }
        spin = autoSpin;
    }

    public void Hide()
    {
        if (rig != null) rig.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (rig != null) Destroy(rig.gameObject);
        if (rt != null) { rt.Release(); Destroy(rt); }
    }

    void EnsureRig()
    {
        if (rig != null) return;
        image = GetComponent<RawImage>();
        layer = LayerMask.NameToLayer(layerName);
        if (layer < 0) layer = 31;

        rig = new GameObject("Workshop Preview Rig").transform;
        rig.position = rigPosition;
        turntable = new GameObject("Turntable").transform;
        turntable.SetParent(rig, false);

        var camGo = new GameObject("Preview Camera");
        camGo.transform.SetParent(rig, false);
        cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.cullingMask = 1 << layer;
        cam.fieldOfView = fieldOfView;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 60f;
        var data = cam.GetUniversalAdditionalCameraData();
        data.renderShadows = false;
        data.renderPostProcessing = false;

        // soft fill light so the model doesn't depend only on the map's sun angle
        var lightGo = new GameObject("Preview Fill Light");
        lightGo.transform.SetParent(rig, false);
        lightGo.transform.localPosition = new Vector3(-3f, 5f, 6f);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = 25f;
        l.intensity = 3f;
        l.color = new Color(1f, 0.92f, 0.82f);
        l.shadows = LightShadows.None;
        l.cullingMask = 1 << layer;

        // render texture matching the on-screen size of the RawImage
        // overlay canvas: world corners are screen pixels
        var corners = new Vector3[4];
        ((RectTransform)transform).GetWorldCorners(corners);
        int w = Mathf.Clamp(Mathf.CeilToInt(corners[2].x - corners[0].x), 64, maxTextureSize);
        int h = Mathf.Clamp(Mathf.CeilToInt(corners[2].y - corners[0].y), 64, maxTextureSize);
        rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2, name = "WorkshopPreviewRT" };
        cam.targetTexture = rt;
        image.texture = rt;
        image.color = Color.white;
    }

    GameObject BuildModel(GameObject prefab)
    {
        // instantiate under an inactive holder so no Awake/OnEnable runs (no bot logic, no registration)
        var holder = new GameObject("holder");
        holder.SetActive(false);
        var go = Instantiate(prefab, holder.transform);
        go.name = "Preview " + prefab.name;

        foreach (var t in go.GetComponentsInChildren<TMP_Text>(true)) DestroyImmediate(t.gameObject);
        // world-space HUD bits (battery bar sprites, rings) don't belong in the showcase
        foreach (var r in go.GetComponentsInChildren<SpriteRenderer>(true)) if (r != null) DestroyImmediate(r.gameObject);
        foreach (var r in go.GetComponentsInChildren<LineRenderer>(true)) if (r != null) DestroyImmediate(r.gameObject);
        var behaviours = go.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = behaviours.Length - 1; i >= 0; i--) if (behaviours[i] != null) DestroyImmediate(behaviours[i]);
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
        }
        foreach (var a in go.GetComponentsInChildren<Animator>(true)) a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

        go.transform.SetParent(turntable, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = prefab.transform.localScale;
        Destroy(holder);
        return go;
    }

    // point the camera so the whole model fits
    void Frame()
    {
        if (model == null) return;
        var renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        var saved = turntable.localRotation;
        turntable.localRotation = Quaternion.identity;
        var b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        turntable.localRotation = saved;

        Vector3 center = b.center;
        float radius = Mathf.Max(0.1f, b.extents.magnitude);
        float halfFov = fieldOfView * 0.5f * Mathf.Deg2Rad;
        float aspect = cam.aspect > 0f ? cam.aspect : 1f;
        float fit = Mathf.Min(Mathf.Sin(halfFov), Mathf.Sin(Mathf.Atan(Mathf.Tan(halfFov) * aspect)));
        float dist = radius / fit * padding;
        // the bot faces +Z, so look at it from the front
        var dir = Quaternion.Euler(elevation, 180f, 0f) * Vector3.forward;
        cam.transform.position = center - dir * dist;
        cam.transform.rotation = Quaternion.LookRotation(dir);
    }

    void LateUpdate()
    {
        if (rig == null || !rig.gameObject.activeSelf) return;
        float dt = Time.unscaledDeltaTime;
        if (!dragging)
        {
            spin = Mathf.Lerp(spin, autoSpin, dt * 1.5f);
            yaw += spin * dt;
        }
        turntable.localRotation = Quaternion.Euler(0f, yaw, 0f);
    }

    public void OnBeginDrag(PointerEventData e) => dragging = true;

    public void OnDrag(PointerEventData e)
    {
        float d = -e.delta.x * dragSpeed;
        yaw += d;
        float dt = Mathf.Max(0.001f, Time.unscaledDeltaTime);
        spin = Mathf.Clamp(d / dt, -720f, 720f);
    }

    public void OnEndDrag(PointerEventData e) => dragging = false;
}
