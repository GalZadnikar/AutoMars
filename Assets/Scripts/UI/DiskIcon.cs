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

    public void Setup(BotProgramPopup owner, DiskProgram disk, bool inRobot, int idx)
    {
        popup = owner;
        program = disk;
        fromRobot = inRobot;
        index = idx;
        group = GetComponent<CanvasGroup>();
        if (label != null) label.color = disk.color;
        if (nameText != null) nameText.text = disk.shortName;
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
