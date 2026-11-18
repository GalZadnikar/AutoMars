using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// A disk in the bot popup. Drag it onto the program slot / inventory, or just tap it.
[RequireComponent(typeof(CanvasGroup))]
public class DiskIcon : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
{
    public Image body;
    public Image label;
    public TMP_Text nameText;

    [System.NonSerialized] public bool fromRobot;
    [System.NonSerialized] public int index;
    [System.NonSerialized] public DiskProgram program;
    [System.NonSerialized] public DiskDropZone droppedOn;

    BotProgramPopup popup;
    CanvasGroup group;
    Transform homeParent;
    int homeIndex;
    Vector3 homeScale;

    TMP_Text countText;

    public void Setup(BotProgramPopup owner, DiskProgram disk, bool inRobot, int idx, int count = 1)
    {
        popup = owner;
        program = disk;
        fromRobot = inRobot;
        index = idx;
        group = GetComponent<CanvasGroup>();
        if (label != null) label.color = disk.color;
        if (nameText != null) nameText.text = disk.shortName;
        SetCount(count);
    }

    /// "x2" badge in the bottom-right corner; the text object is created on first use.
    void SetCount(int count)
    {
        if (count <= 1) { if (countText != null) countText.gameObject.SetActive(false); return; }
        if (countText == null)
        {
            var go = new GameObject("Count", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-4f, 4f);
            rt.sizeDelta = new Vector2(60f, 30f);
            countText = go.AddComponent<TextMeshProUGUI>();
            countText.alignment = TextAlignmentOptions.BottomRight;
            countText.fontSize = nameText != null ? nameText.fontSize : 22f;
            countText.fontStyle = FontStyles.Bold;
            countText.color = Color.white;
            countText.raycastTarget = false;
            if (nameText != null) countText.font = nameText.font;
        }
        countText.gameObject.SetActive(true);
        countText.text = "x" + count;
    }

    public void OnBeginDrag(PointerEventData e)
    {
        droppedOn = null;
        homeParent = transform.parent;
        homeIndex = transform.GetSiblingIndex();
        homeScale = transform.localScale;
        transform.SetParent(popup.dragLayer, true);
        transform.SetAsLastSibling();
        group.blocksRaycasts = false;          // let the drop zone under the finger receive OnDrop
        transform.localScale = Vector3.one * 1.15f;
    }

    public void OnDrag(PointerEventData e)
    {
        transform.position = e.position;
    }

    public void OnEndDrag(PointerEventData e)
    {
        group.blocksRaycasts = true;
        if (droppedOn != null) { popup.HandleDrop(this, droppedOn); return; }
        // not dropped anywhere useful: snap back
        transform.SetParent(homeParent, false);
        transform.SetSiblingIndex(homeIndex);
        transform.localScale = homeScale;
        ((RectTransform)transform).anchoredPosition = Vector2.zero;
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (e.dragging) return;
        popup.Tap(this);
    }
}
