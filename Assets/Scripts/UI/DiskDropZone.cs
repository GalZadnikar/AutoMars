using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// Area a DiskIcon can be dropped on: the bot's program slot or the inventory.
public class DiskDropZone : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
{
    public bool isRobotSlot;
    public Image highlight;
    public Color highlightColor = new Color(1f, 0.8f, 0.25f);

    Color baseColor;
    bool hasBase;

    void Awake()
    {
        if (highlight == null) highlight = GetComponent<Image>();
        if (highlight != null) { baseColor = highlight.color; hasBase = true; }
    }

    void OnDisable() => SetHighlight(false);

    public void OnDrop(PointerEventData e)
    {
        var icon = e.pointerDrag != null ? e.pointerDrag.GetComponent<DiskIcon>() : null;
        if (icon != null) icon.droppedOn = this;
        SetHighlight(false);
    }

    public void OnPointerEnter(PointerEventData e)
    {
        if (e.dragging && e.pointerDrag != null && e.pointerDrag.GetComponent<DiskIcon>() != null) SetHighlight(true);
    }

    public void OnPointerExit(PointerEventData e) => SetHighlight(false);

    void SetHighlight(bool on)
    {
        if (!hasBase) return;
        highlight.color = on ? Color.Lerp(baseColor, highlightColor, 0.4f) : baseColor;
    }
}
