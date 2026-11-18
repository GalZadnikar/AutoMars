using TMPro;
using UnityEngine;

/// Top-of-screen counters: materials and carried trash.
public class GameHUD : MonoBehaviour
{
    public TMP_Text materialsText;
    public TMP_Text carryText;
    public TMP_Text disksText;
    public PlayerCarrier player;

    int shownMaterials = -1, shownCarry = -1, shownCap = -1, shownDisks = -1;
    float diskPunch;
    Vector3 diskBaseScale = Vector3.one;
    float punch;
    Vector3 baseScale = Vector3.one;

    [Header("Layout")]
    [Tooltip("Stack the counters in a panel in the top-left corner (player stats style)")]
    public bool statsPanel = true;
    public Vector2 panelPosition = new Vector2(24f, -24f);
    public float panelWidth = 360f;
    public float rowHeight = 62f;

    void Awake() { if (statsPanel) ApplyLayout(); }

    /// Puts the counters in a compact panel in the top-left corner. Also runs from the context menu (edit mode).
    [ContextMenu("Apply Stats Panel Layout")]
    public void ApplyLayout()
    {
        var rt = (RectTransform)transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = panelPosition;
        rt.sizeDelta = new Vector2(panelWidth, rowHeight * 3f + 24f);
        var bg = GetComponent<UnityEngine.UI.Image>();
        if (bg == null) bg = gameObject.AddComponent<UnityEngine.UI.Image>();
        bg.color = new Color(0.10f, 0.086f, 0.067f, 0.72f);
        bg.raycastTarget = false;
        Row("MaterialsCaption", materialsText, 0, "MATERIALS");
        Row("DisksCaption", disksText, 1, "DISKS");
        Row("CarryCaption", carryText, 2, "TRASH BAG");
    }

    void Row(string captionName, TMP_Text value, int index, string caption)
    {
        float y = -12f - rowHeight * (index + 0.5f);
        var cap = transform.Find(captionName);
        if (cap != null)
        {
            var c = (RectTransform)cap;
            c.anchorMin = c.anchorMax = new Vector2(0f, 1f);
            c.pivot = new Vector2(0f, 0.5f);
            c.anchoredPosition = new Vector2(20f, y);
            c.sizeDelta = new Vector2(panelWidth * 0.5f, rowHeight);
            var ct = cap.GetComponent<TMP_Text>();
            if (ct != null) { ct.text = caption; ct.fontSize = 24f; ct.alignment = TextAlignmentOptions.MidlineLeft; ct.color = new Color(1f, 1f, 1f, 0.7f); ct.enableAutoSizing = false; }
        }
        if (value != null)
        {
            var v = value.rectTransform;
            v.anchorMin = v.anchorMax = new Vector2(1f, 1f);
            v.pivot = new Vector2(1f, 0.5f);   // punch grows from the right edge
            v.anchoredPosition = new Vector2(-20f, y);
            v.sizeDelta = new Vector2(panelWidth * 0.5f, rowHeight);
            value.fontSize = 42f;
            value.enableAutoSizing = false;
            value.alignment = TextAlignmentOptions.MidlineRight;
        }
    }

    void Start()
    {
        if (materialsText != null) baseScale = materialsText.transform.localScale;
        if (disksText != null) diskBaseScale = disksText.transform.localScale;
    }

    void Update()
    {
        int m = Economy.Instance != null ? Economy.Instance.materials : 0;
        if (m != shownMaterials)
        {
            if (shownMaterials >= 0) punch = 1f;
            shownMaterials = m;
            if (materialsText != null) materialsText.text = m.ToString();
        }
        if (materialsText != null && punch > 0f)
        {
            punch = Mathf.MoveTowards(punch, 0f, Time.deltaTime * 6f);
            materialsText.transform.localScale = baseScale * (1f + 0.2f * punch);
        }

        int disks = PlayerInventory.Instance != null ? PlayerInventory.Instance.DiskCount : 0;
        if (disksText != null && disks != shownDisks)
        {
            if (shownDisks >= 0 && disks > shownDisks) diskPunch = 1f;
            shownDisks = disks;
            disksText.text = disks.ToString();
        }
        if (disksText != null && diskPunch > 0f)
        {
            diskPunch = Mathf.MoveTowards(diskPunch, 0f, Time.deltaTime * 5f);
            disksText.transform.localScale = diskBaseScale * (1f + 0.3f * diskPunch);
        }

        if (player != null && carryText != null && (player.Carried != shownCarry || player.capacity != shownCap))
        {
            shownCarry = player.Carried; shownCap = player.capacity;
            carryText.text = shownCarry + " / " + shownCap;
            carryText.color = player.IsFull ? new Color(1f, 0.4f, 0.35f) : Color.white;
        }
    }
}
