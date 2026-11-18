using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// One hexagon in the Workshop upgrade tree. Tap = inspect, tap again = equip.
public class PerkHex : MonoBehaviour, IPointerClickHandler
{
    public enum State { Locked, Available, Equipped }

    public Image fill;
    public Image ring;
    public Image icon;
    public TMP_Text glyph;

    public float inspectedScale = 1.14f;

    Action onTap;
    State state;
    bool inspected;
    float scale = 1f, punch;

    public void Setup(Perk perk, Action tap)
    {
        onTap = tap;
        bool hasIcon = perk.icon != null;
        if (icon != null)
        {
            icon.gameObject.SetActive(hasIcon);
            if (hasIcon) icon.sprite = perk.icon;
        }
        if (glyph != null)
        {
            glyph.gameObject.SetActive(!hasIcon);
            glyph.text = perk.glyph;
        }
    }

    public void SetState(State s, bool isInspected)
    {
        if (s == State.Equipped && state != State.Equipped) punch = 1f;
        state = s;
        inspected = isInspected;

        Color f, g;
        switch (s)
        {
            case State.Equipped: f = WorkshopTheme.Yellow; g = WorkshopTheme.DarkText; break;
            case State.Available: f = WorkshopTheme.Orange; g = WorkshopTheme.DarkText; break;
            default: f = WorkshopTheme.LockedFill; g = WorkshopTheme.LockedGlyph; break;
        }
        if (fill != null) fill.color = f;
        if (glyph != null) glyph.color = g;
        if (icon != null) icon.color = g;
    }

    public void OnPointerClick(PointerEventData e) => onTap?.Invoke();

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        scale = Mathf.MoveTowards(scale, inspected ? inspectedScale : 1f, dt * 2.5f);
        punch = Mathf.MoveTowards(punch, 0f, dt * 4f);
        transform.localScale = Vector3.one * (scale + Mathf.Sin(punch * Mathf.PI) * 0.18f);

        if (ring != null)
        {
            bool show = inspected || state == State.Equipped;
            ring.enabled = show;
            if (show)
            {
                var c = inspected ? WorkshopTheme.White : new Color(1f, 1f, 1f, 0.55f);
                if (inspected) c.a = 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 6f);
                ring.color = c;
            }
        }
    }
}
