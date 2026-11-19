using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// The Trade Post popup: two tabs, BATTERIES and DISKS, a grid of offers (icon, name, info, owned count, BUY).
/// Builds itself under "UI Canvas" the first time it opens (same look as the BASE popup), so no builder step.
/// TradePostPopup.Open(post) / Close(). Tap outside the panel or CLOSE to close.
public class TradePostPopup : MonoBehaviour
{
    public const int BatteryTab = 0, DiskTab = 1;
    static readonly string[] TabNames = { "BATTERIES", "DISKS" };

    static TradePostPopup instance;
    public static TradePostPopup Instance => instance;
    public static bool IsOpen => instance != null && instance.gameObject.activeSelf;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; }

    TradePost post;
    int tab = BatteryTab;
    TMP_Text materialsText, subText;
    readonly Button[] tabButtons = new Button[2];
    RectTransform grid;
    ScrollRect scroll;
    readonly List<OfferCard> cards = new List<OfferCard>();
    float refreshT;

    class OfferCard
    {
        public GameObject go;
        public Image stripe, icon, buyBg;
        public TMP_Text name, info, owned, buyLabel;
        public Button buy;
        public TradePost.BatteryOffer battery;
        public TradePost.DiskOffer disk;
    }

    // ------------------------------------------------------------------ open / close

    public static void Open(TradePost tp)
    {
        if (tp == null || UIState.ModalOpen || UIState.BuildMode) return;
        if (instance == null) instance = Create();
        if (instance == null) return;
        instance.Show(tp);
    }

    public static void Close()
    {
        if (instance != null) instance.Hide();
    }

    void Show(TradePost tp)
    {
        post = tp;
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        UIState.ModalOpen = true;
        SetTab(tab);
    }

    void Hide()
    {
        if (!gameObject.activeSelf) return;
        gameObject.SetActive(false);
        UIState.ModalOpen = false;
        post = null;
    }

    void Update()
    {
        if (post == null) { Hide(); return; }
        refreshT -= Time.unscaledDeltaTime;
        if (refreshT <= 0f) { refreshT = 0.25f; Refresh(); }
    }

    void SetTab(int t)
    {
        tab = Mathf.Clamp(t, 0, 1);
        for (int i = 0; i < tabButtons.Length; i++)
        {
            bool on = i == tab;
            tabButtons[i].targetGraphic.color = on ? WorkshopTheme.Orange : WorkshopTheme.ButtonOff;
            var l = tabButtons[i].GetComponentInChildren<TMP_Text>();
            if (l != null) l.color = on ? WorkshopTheme.DarkText : WorkshopTheme.White;
        }
        RebuildCards();
        if (scroll != null) scroll.verticalNormalizedPosition = 1f;
        Refresh();
    }

    // ------------------------------------------------------------------ cards

    void RebuildCards()
    {
        foreach (var c in cards) Destroy(c.go);
        cards.Clear();
        if (post == null) return;
        if (tab == BatteryTab)
        {
            foreach (var o in post.batteries)
                if (o != null && o.item != null) cards.Add(MakeCard(o, null));
        }
        else
        {
            foreach (var o in post.disks)
                if (o != null && o.disk != null) cards.Add(MakeCard(null, o));
        }
    }

    void Refresh()
    {
        var inv = PlayerInventory.Instance;
        if (materialsText != null)
            materialsText.text = "MATERIALS  " + (Economy.Instance != null ? Economy.Instance.materials : 0);
        if (subText != null)
        {
            if (tab == BatteryTab)
                subText.text = "A battery in a factory powers every robot it builds.   Battery slots  " +
                               (inv != null ? inv.BatterySlotsUsed + " / " + inv.batterySlots : "-") + "  (same kind stacks)";
            else
                subText.text = "Disks program your robots.   <color=" + WorkshopTheme.Hex(RobotTypes.Color(RobotType.Gatherer)) + ">G</color> = gatherer bot,  <color=" +
                               WorkshopTheme.Hex(RobotTypes.Color(RobotType.Drone)) + ">D</color> = combat drone";
        }

        foreach (var c in cards)
        {
            int price;
            string block;
            int owned;
            if (c.battery != null)
            {
                price = c.battery.Price;
                block = TradePost.BatteryBlock(c.battery);
                owned = inv != null ? inv.CountOf(c.battery.item) : 0;
            }
            else
            {
                price = c.disk.Price;
                block = TradePost.DiskBlock(c.disk);
                owned = inv != null ? inv.CountOf(c.disk.disk) : 0;
            }
            bool ok = block == "";
            string cost = BuildMode.FreeBuild ? "FREE" : price.ToString();
            c.buyLabel.text = ok ? "BUY   <size=80%>" + cost + " MATS</size>"
                            : block == "NOT ENOUGH" ? "<size=80%>NEED " + price + " MATS</size>" : "<size=80%>" + block + "</size>";
            c.buyLabel.color = ok ? Color.white : WorkshopTheme.Muted;
            c.buyBg.color = ok ? WorkshopTheme.ButtonGreen : WorkshopTheme.ButtonOff;
            c.owned.text = owned > 0 ? "OWNED  <color=#FFFFFF>x" + owned + "</color>" : "NOT OWNED";
        }
    }

    void Buy(OfferCard c)
    {
        if (post == null) return;
        bool done = c.battery != null ? post.BuyBattery(c.battery) : post.BuyDisk(c.disk);
        if (done) StartCoroutine(Punch(c.go.transform));
        Refresh();
    }

    static System.Collections.IEnumerator Punch(Transform t)
    {
        for (float k = 0f; k < 1f; k += Time.unscaledDeltaTime * 5f)
        {
            if (t == null) yield break;
            t.localScale = Vector3.one * (1f + Mathf.Sin(k * Mathf.PI) * 0.06f);
            yield return null;
        }
        if (t != null) t.localScale = Vector3.one;
    }

    OfferCard MakeCard(TradePost.BatteryOffer b, TradePost.DiskOffer d)
    {
        var c = new OfferCard { battery = b, disk = d };
        var rt = NewRect("Offer", grid);
        c.go = rt.gameObject;
        AddImage(rt, WorkshopTheme.Panel, false);
        Color col = b != null ? b.item.Color : d.disk.color;

        c.stripe = AddImage(Rect("Stripe", rt, 0, 0, CardW, 6), col, false);
        var iconBg = Rect("IconBg", rt, -CardW * 0.5f + 16f + 36f, 20, 72, 72);
        AddImage(iconBg, WorkshopTheme.PanelDark, false);
        c.icon = AddImage(Rect("Icon", iconBg, 0, 8, 56, 56), col, false);
        c.icon.sprite = b != null ? post.batteryIcon : post.diskIcon;
        c.icon.preserveAspect = true;
        if (c.icon.sprite == null) c.icon.color = new Color(col.r, col.g, col.b, 0.8f);

        float tx = -CardW * 0.5f + 100f + (CardW - 112f) * 0.5f, tw = CardW - 112f;
        c.name = Txt("Name", rt, tx, 18, tw, 34, (b != null ? b.item.displayName : d.disk.displayName).ToUpper(), 22, col, TextAlignmentOptions.Left);
        c.name.enableAutoSizing = true; c.name.fontSizeMin = 14; c.name.fontSizeMax = 22;
        string info;
        if (b != null) info = Stats.Clock(b.item.runtimeSeconds) + " RUNTIME\n<color=#A39885>" + b.item.rarity.ToString().ToUpper() + "</color>";
        else
        {
            var t = d.disk.compatibleWith;
            info = "<color=" + WorkshopTheme.Hex(RobotTypes.Color(t)) + ">" + RobotTypes.Glyph(t) + "</color>  FOR " +
                   (t == RobotType.Any ? "ANY ROBOT" : RobotTypes.Name(t) + (t == RobotType.Drone ? "S" : " BOTS"));
        }
        c.info = Txt("Info", rt, tx, 52, tw, 46, info, 16, WorkshopTheme.White, TextAlignmentOptions.TopLeft, FontStyles.Normal);
        c.owned = Txt("Owned", rt, 0, 104, CardW - 32f, 24, "", 17, WorkshopTheme.Muted, TextAlignmentOptions.Left);

        var btnRt = Rect("Buy", rt, 0, 134, CardW - 24f, 58);
        c.buyBg = AddImage(btnRt, WorkshopTheme.ButtonGreen, true);
        c.buy = btnRt.gameObject.AddComponent<Button>();
        c.buy.targetGraphic = c.buyBg;
        var cb = c.buy.colors; cb.pressedColor = new Color(0.78f, 0.78f, 0.78f); c.buy.colors = cb;
        var lr = NewRect("Label", btnRt); Stretch(lr, 4f);
        c.buyLabel = AddText(lr, "BUY", 26, Color.white);
        c.buy.onClick.AddListener(() => Buy(c));
        return c;
    }

    // ------------------------------------------------------------------ build (once)

    const float W = 1180f, H = 664f, CardW = 254f, CardH = 204f;

    static TradePostPopup Create()
    {
        var canvasGo = GameObject.Find("UI Canvas");
        if (canvasGo == null) { Debug.LogError("[Trade] no 'UI Canvas' in the scene"); return null; }
        var root = NewRect("TradePostPopup", canvasGo.transform);
        Stretch(root);
        var p = root.gameObject.AddComponent<TradePostPopup>();
        p.BuildUI(root);
        root.gameObject.SetActive(false);
        return p;
    }

    void BuildUI(RectTransform root)
    {
        var dim = AddImage(root, new Color(0f, 0f, 0f, 0.6f), true);
        var backdrop = root.gameObject.AddComponent<Button>();
        backdrop.targetGraphic = dim;
        backdrop.transition = Selectable.Transition.None;
        backdrop.onClick.AddListener(Hide);

        var panel = NewRect("Panel", root);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(W, H);
        AddImage(panel, new Color(0.10f, 0.086f, 0.067f, 0.97f), true);   // swallows taps on the panel
        AddImage(Rect("Rule", panel, 0, 0, W, 5), WorkshopTheme.Orange, false);
        Txt("Title", panel, -362, 14, 400, 52, "TRADE POST", 42, WorkshopTheme.Orange, TextAlignmentOptions.Left).characterSpacing = 4f;
        materialsText = Txt("Materials", panel, 190, 20, 360, 44, "MATERIALS  0", 30, WorkshopTheme.Yellow, TextAlignmentOptions.Right);
        var close = Btn("Close", panel, "CLOSE", WorkshopTheme.ButtonOff, Color.white, 30, new Vector2(480f, -46f), new Vector2(170f, 60f));
        close.onClick.AddListener(Hide);

        for (int i = 0; i < TabNames.Length; i++)
        {
            int t = i;
            tabButtons[i] = Btn("Tab " + TabNames[i], panel, TabNames[i], WorkshopTheme.ButtonOff, Color.white, 30,
                                new Vector2(-W * 0.5f + 45f + 118f + i * 252f, -121f), new Vector2(236f, 66f));
            tabButtons[i].onClick.AddListener(() => SetTab(t));
        }
        AddImage(Rect("TabRule", panel, 0, 160, W - 90f, 3), new Color(WorkshopTheme.Orange.r, WorkshopTheme.Orange.g, WorkshopTheme.Orange.b, 0.45f), false);
        subText = Txt("Sub", panel, 0, 172, W - 90f, 34, "", 22, WorkshopTheme.Muted, TextAlignmentOptions.Left, FontStyles.Normal);

        // offer grid: 4 per row, scrolls up / down
        var view = Rect("Offers View", panel, 0, 216, 1090, H - 216 - 22);
        view.gameObject.AddComponent<RectMask2D>();
        AddImage(view, new Color(0f, 0f, 0f, 0f), true);
        grid = NewRect("Offers", view);
        grid.anchorMin = new Vector2(0f, 1f); grid.anchorMax = new Vector2(1f, 1f);
        grid.pivot = new Vector2(0.5f, 1f);
        grid.anchoredPosition = Vector2.zero;
        grid.sizeDelta = Vector2.zero;
        var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
        gl.cellSize = new Vector2(CardW, CardH);
        gl.spacing = new Vector2(16f, 16f);
        gl.padding = new RectOffset(13, 13, 0, 8);
        gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        gl.constraintCount = 4;
        var fit = grid.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll = view.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = view; scroll.content = grid;
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.decelerationRate = 0.1f;
        scroll.scrollSensitivity = 30f;
    }

    // ------------------------------------------------------------------ helpers (x = centre, yTop = from the parent's top)

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static RectTransform Rect(string name, Transform parent, float x, float yTop, float w, float h)
    {
        var rt = NewRect(name, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, -(yTop + h * 0.5f));
        rt.sizeDelta = new Vector2(w, h);
        return rt;
    }

    static void Stretch(RectTransform rt, float pad = 0f)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(pad, pad); rt.offsetMax = new Vector2(-pad, -pad);
    }

    static Image AddImage(RectTransform rt, Color c, bool raycast)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = raycast;
        return img;
    }

    static TextMeshProUGUI AddText(RectTransform rt, string text, float size, Color c, FontStyles style = FontStyles.Bold)
    {
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.color = c; t.fontStyle = style;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        return t;
    }

    static TextMeshProUGUI Txt(string name, Transform parent, float x, float yTop, float w, float h, string text, float size, Color c,
                               TextAlignmentOptions align = TextAlignmentOptions.Center, FontStyles style = FontStyles.Bold)
    {
        var t = AddText(Rect(name, parent, x, yTop, w, h), text, size, c, style);
        t.alignment = align;
        return t;
    }

    static Button Btn(string name, Transform parent, string label, Color bg, Color fg, float fontSize, Vector2 pos, Vector2 size)
    {
        var rt = NewRect(name, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = AddImage(rt, bg, true);
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        var cb = b.colors; cb.pressedColor = new Color(0.78f, 0.78f, 0.78f); b.colors = cb;
        var lt = NewRect("Label", rt); Stretch(lt, 4f);
        AddText(lt, label, fontSize, fg);
        return b;
    }
}
