using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// HUD layout B: a bar at the bottom of the screen with four tiles - shovel, pickaxe, weapon 1, weapon 2.
/// Nothing switches by itself in this layout: tap a tile to take that tool in the hands.
///  - The tile of the tool in the hands is lifted, framed and its strip turns yellow.
///  - Weapon tiles show the ammo ("4/6"); tapping the weapon that is already out reloads it.
///  - An empty weapon slot shows EMPTY; a pickup fills an empty slot first, otherwise it replaces the
///    weapon slot that is selected (see PlayerWeapon.Give).
/// Layout A (tiles on the left, automatic switching) is the old one; MobileHUD.SetBarLayout switches between them.
public class ToolBarView : MonoBehaviour
{
    [Serializable]
    public class Slot
    {
        public RectTransform root;
        public Button button;
        public Image icon;
        public TMP_Text label;
        [Tooltip("Band along the bottom of the tile, behind the label")]
        public Image strip;
        public GameObject selected;
        [Tooltip("Radial wipe during a reload (weapon tiles)")]
        public Image reload;
    }

    [Tooltip("0 = shovel, 1 = pickaxe, 2 = weapon 1, 3 = weapon 2")]
    public Slot[] slots;
    public Color stripNormal = new Color(0.16f, 0.13f, 0.24f, 0.92f);
    public Color stripSelected = new Color(1f, 0.82f, 0.2f);
    public Color textNormal = Color.white;
    public Color textSelected = new Color(0.17f, 0.12f, 0.1f);
    public Color lowColor = new Color(1f, 0.38f, 0.28f);
    [Tooltip("How far the selected tile is lifted (px) and how much bigger it is")]
    public float selectedLift = 18f;
    public float selectedScale = 1.08f;

    PlayerTools tools;
    PlayerWeapon pw;
    ToolProgress tp;
    Vector2[] basePos;
    float[] sel;
    string[] shown;

    void Awake()
    {
        int n = slots != null ? slots.Length : 0;
        basePos = new Vector2[n];
        sel = new float[n];
        shown = new string[n];
        for (int i = 0; i < n; i++)
        {
            int k = i;
            if (slots[i].root != null) basePos[i] = slots[i].root.anchoredPosition;
            if (slots[i].button != null) slots[i].button.onClick.AddListener(() => Tap(k));
            if (slots[i].reload != null) slots[i].reload.gameObject.SetActive(false);
        }
    }

    void Find()
    {
        if (tools != null) return;
        tools = FindFirstObjectByType<PlayerTools>();
        if (tools == null) return;
        pw = tools.GetComponent<PlayerWeapon>();
        tp = tools.GetComponent<ToolProgress>();
    }

    void Tap(int i)
    {
        Find();
        if (tools == null || UIState.ModalOpen || UIState.BuildMode) return;
        if (i == 0) tools.Equip(tools.sandTool);
        else if (i == 1) tools.Equip(PlayerTool.Pickaxe);
        else
        {
            if (pw == null) return;
            int s = i - 2;
            if (pw.SlotWeapon(s) == null) { GameToast.Show("EMPTY - PICK UP A WEAPON", Color.white); return; }
            if (tools.tool == PlayerTool.Weapon && pw.Slot == s) { pw.Reload(); return; }   // already out: reload
            pw.SelectSlot(s);
            tools.Equip(PlayerTool.Weapon);
        }
    }

    void Update()
    {
        Find();
        if (tools == null || slots == null) return;
        float dt = Time.unscaledDeltaTime;
        for (int i = 0; i < slots.Length; i++)
        {
            var sl = slots[i];
            bool isSel = false, low = false, reloading = false;
            Sprite icon = null;
            string text = "";
            if (i == 0)
            {
                isSel = tools.tool == PlayerTool.Shovel || tools.tool == PlayerTool.Gun;
                if (tp != null) { icon = tp.Icon(ToolKind.Shovel); text = "LVL " + tp.Level(ToolKind.Shovel); }
            }
            else if (i == 1)
            {
                isSel = tools.tool == PlayerTool.Pickaxe;
                if (tp != null) { icon = tp.Icon(ToolKind.Pickaxe); text = "LVL " + tp.Level(ToolKind.Pickaxe); }
            }
            else if (pw != null)
            {
                int s = i - 2;
                var def = pw.SlotWeapon(s);
                isSel = def != null && tools.tool == PlayerTool.Weapon && pw.Slot == s;
                if (def == null) text = "EMPTY";
                else
                {
                    icon = def.icon;
                    reloading = pw.Slot == s && pw.Reloading;
                    int ammo = reloading ? Mathf.FloorToInt(pw.ReloadProgress * def.magazine) : pw.SlotAmmo(s);
                    text = ammo + "/" + def.magazine;
                    low = !reloading && ammo <= Mathf.Max(1f, def.magazine * 0.34f);
                }
            }

            if (sl.icon != null)
            {
                if (sl.icon.sprite != icon) sl.icon.sprite = icon;
                if (sl.icon.enabled != (icon != null)) sl.icon.enabled = icon != null;
            }
            if (sl.label != null)
            {
                if (shown[i] != text) { shown[i] = text; sl.label.text = text; }
                sl.label.color = low ? lowColor : (isSel ? textSelected : textNormal);
            }
            if (sl.strip != null) sl.strip.color = isSel ? stripSelected : stripNormal;
            if (sl.selected != null && sl.selected.activeSelf != isSel) sl.selected.SetActive(isSel);
            if (sl.reload != null)
            {
                if (sl.reload.gameObject.activeSelf != reloading) sl.reload.gameObject.SetActive(reloading);
                if (reloading) sl.reload.fillAmount = 1f - pw.ReloadProgress;
            }
            if (sl.root != null)
            {
                sel[i] = Mathf.MoveTowards(sel[i], isSel ? 1f : 0f, dt * 9f);
                float e = sel[i] * sel[i] * (3f - 2f * sel[i]);
                sl.root.anchoredPosition = basePos[i] + Vector2.up * (selectedLift * e);
                float sc = Mathf.Lerp(1f, selectedScale, e);
                sl.root.localScale = new Vector3(sc, sc, 1f);
            }
        }
    }
}
