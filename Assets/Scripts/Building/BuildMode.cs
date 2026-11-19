using System.Collections.Generic;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// Base popup + construction. Stand at the base and tap its floating BASE button → a popup with two tabs:
/// PLAYER (player level, stats, LEVEL UP, UPGRADE TREE = the Workshop with the player's perk tree) and
/// BUILD (a grid of small building tiles with a spinning 3D preview; tiles you can't afford are faded).
/// Pick an affordable building → build mode: the player stops, the camera goes top-down over a build cursor and the
/// joystick moves that cursor (WASD in the Editor) → its blueprint snaps to the grid. A bar at the bottom lists every
/// building (name + cost) so you can switch without leaving, plus ROTATE / EXIT / PLACE.
/// Placing flattens the ground and starts a construction site; you stay in build mode until EXIT.
public class BuildMode : MonoBehaviour
{
    /// Debug: building costs nothing.
    public static bool FreeBuild;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { FreeBuild = false; }

    public List<BuildingDef> catalog = new List<BuildingDef>();

    [Header("Base popup (opened from the BASE button at the base)")]
    public GameObject menuPanel;
    public Button menuCloseButton, menuBackdrop;
    public TMP_Text menuMaterialsText;
    public RectTransform cardRow;
    [Tooltip("Old building card template (fallback)")]
    public BuildCard cardTemplate;
    [Tooltip("Building card template (with a 3D preview); falls back to cardTemplate")]
    public BuildCard buildingCardTemplate;

    [Header("PLAYER tab")]
    public ModelPreview playerPreview;
    public TMP_Text playerLevelText, playerPointsText, playerStatsText, playerNextText;
    public Button playerLevelUpButton, playerTreeButton;
    public TMP_Text playerLevelUpLabel, playerTreeLabel;
    [Tooltip("The Workshop screen (found automatically)")]
    public WorkshopScreen workshop;

    [Header("Tabs (0 = PLAYER, 1 = BUILD)")]
    public Button[] tabButtons;
    public GameObject[] tabPages;

    [Header("Placement UI")]
    public GameObject placePanel;
    public TMP_Text placeTitle, placeStatus, hintText;
    [Tooltip("cancelButton = EXIT (leaves build mode)")]
    public Button rotateButton, cancelButton, placeButton;
    [Tooltip("Bottom bar: one chip per building, tap to switch")]
    public RectTransform chipRow;
    public BuildChip chipTemplate;

    [Header("Camera + cursor")]
    [Tooltip("Top-down camera that follows the build cursor (enabled only in build mode)")]
    public CinemachineCamera buildCamera;
    [Tooltip("Metres per second at full joystick")]
    public float cursorSpeed = 14f;

    [Header("World")]
    public Material ghostMaterial;
    public Material overlayMaterial;
    public Color ghostOk = new Color(0.35f, 1f, 0.45f, 0.45f);
    public Color ghostBad = new Color(1f, 0.3f, 0.25f, 0.45f);
    public Color constructionColor = new Color(1f, 0.7f, 0.2f, 0.25f);
    [Tooltip("Width of the slope that blends the flattened pad back into the terrain")]
    public float levelMargin = 2.5f;

    public const int PlayerTab = 0, BuildTab = 1;
    static readonly string[] TabNames = { "PLAYER", "BUILD" };

    readonly List<BuildCard> cards = new List<BuildCard>();
    readonly List<BuildChip> chips = new List<BuildChip>();
    int tab = PlayerTab;
    readonly Dictionary<Vector2Int, BuildGrid.CellState> states = new Dictionary<Vector2Int, BuildGrid.CellState>();
    BuildingDef current;
    GameObject ghost;
    Material ghostMat;
    GridOverlay overlay;
    Transform cursor;
    Joystick joystick;
    int rot;
    Vector2Int lastBl = new Vector2Int(int.MinValue, int.MinValue);
    int lastRot = -1;
    bool dirty, valid;
    string reason = "";
    Vector3 ghostPos;
    Quaternion ghostRot;
    Vector2Int footBl, footDims;
    float refreshT, recheckT;
    GameObject zoomSlider;   // the normal camera's zoom slider: useless while building, hidden

    void Start()
    {
        menuCloseButton.onClick.AddListener(CloseMenu);
        if (menuBackdrop != null) menuBackdrop.onClick.AddListener(CloseMenu);
        rotateButton.onClick.AddListener(() => { rot = (rot + 1) & 3; dirty = true; });
        cancelButton.onClick.AddListener(Exit);
        placeButton.onClick.AddListener(Place);

        if (tabButtons != null)
            for (int i = 0; i < tabButtons.Length; i++)
            {
                int t = i;
                if (tabButtons[i] != null) tabButtons[i].onClick.AddListener(() => SetTab(t));
            }

        var bTemplate = buildingCardTemplate != null ? buildingCardTemplate : cardTemplate;
        int n = 0;
        foreach (var def in catalog)
        {
            if (def == null || def.prefab == null) continue;
            var c = Instantiate(bTemplate, cardRow);
            c.gameObject.SetActive(true);
            // every preview gets its own spot high above the base: the fog of war darkens anything over unexplored ground,
            // so the rigs are stacked in height over the (always explored) base instead of spread sideways
            if (c.preview != null) c.preview.rigPosition = new Vector3(0f, 700f + 100f * n, 0f);
            n++;
            var d = def;
            c.Setup(def, () => Begin(d));
            cards.Add(c);
            if (chipTemplate != null && chipRow != null)
            {
                var chip = Instantiate(chipTemplate, chipRow);
                chip.gameObject.SetActive(true);
                chip.Setup(def, () => Switch(d));
                chips.Add(chip);
            }
        }

        PlayerUpgrades.Ensure();
        if (workshop == null) workshop = FindFirstObjectByType<WorkshopScreen>(FindObjectsInactive.Include);
        if (playerLevelUpButton != null) playerLevelUpButton.onClick.AddListener(PlayerLevelUp);
        if (playerTreeButton != null) playerTreeButton.onClick.AddListener(OpenPlayerTree);

        var ov = new GameObject("Build Grid Overlay", typeof(MeshFilter), typeof(MeshRenderer));
        ov.GetComponent<MeshRenderer>().sharedMaterial = overlayMaterial;
        overlay = ov.AddComponent<GridOverlay>();
        overlay.Hide();

        cursor = new GameObject("Build Cursor").transform;
        var pc = FindFirstObjectByType<JoystickPlayerController>();
        if (pc != null) joystick = pc.joystick;
        if (buildCamera != null) buildCamera.gameObject.SetActive(false);
        var zs = transform.parent != null ? transform.parent.Find("CameraDistanceSlider") : null;
        if (zs != null) zoomSlider = zs.gameObject;

        if (ghostMaterial != null) ghostMat = new Material(ghostMaterial);
        BuildGrid.Changed += MarkDirty;
        ShowNothing();
    }

    void OnDestroy()
    {
        BuildGrid.Changed -= MarkDirty;
        UIState.BuildMode = false;
    }

    void MarkDirty() => dirty = true;

    // ------------------------------------------------------------------ modes

    void ShowNothing()
    {
        menuPanel.SetActive(false);
        placePanel.SetActive(false);
        hintText.gameObject.SetActive(false);
    }

    /// The BASE button calls this. Opens on the last used tab (or the given one).
    public void OpenMenu() => OpenMenu(-1);

    public void OpenMenu(int openTab)
    {
        if (UIState.ModalOpen || UIState.BuildMode) return;
        menuPanel.SetActive(true);
        menuPanel.transform.SetAsLastSibling();
        UIState.ModalOpen = true;
        refreshT = 0f;
        SetTab(openTab >= 0 ? openTab : tab);
        RefreshMenu();
    }

    public void CloseMenu()
    {
        if (!menuPanel.activeSelf) return;
        foreach (var c in cards) c.ShowPreview(false);
        if (playerPreview != null) playerPreview.Hide();
        menuPanel.SetActive(false);
        UIState.ModalOpen = false;
    }

    public void SetTab(int t)
    {
        int count = tabPages != null ? tabPages.Length : 0;
        if (count == 0) return;
        tab = Mathf.Clamp(t, 0, count - 1);
        for (int i = 0; i < count; i++)
        {
            if (tabPages[i] != null) tabPages[i].SetActive(i == tab);
            if (tabButtons == null || i >= tabButtons.Length || tabButtons[i] == null) continue;
            bool on = i == tab;
            tabButtons[i].targetGraphic.color = on ? WorkshopTheme.Orange : WorkshopTheme.ButtonOff;
            var label = tabButtons[i].GetComponentInChildren<TMP_Text>();
            if (label != null) label.color = on ? WorkshopTheme.DarkText : WorkshopTheme.White;
        }
        // previews only render while the BUILD tab is showing
        Canvas.ForceUpdateCanvases();
        foreach (var c in cards) c.ShowPreview(menuPanel.activeSelf && tab == BuildTab);
        if (playerPreview != null)
        {
            var pu = PlayerUpgrades.Instance;
            if (menuPanel.activeSelf && tab == PlayerTab && pu != null) playerPreview.Show(PlayerModel(pu));
            else playerPreview.Hide();
        }
        RefreshMenu();
    }

    // popup card tapped: leave the popup, switch to the top-down view and start placing (only if affordable)
    void Begin(BuildingDef def)
    {
        if (!Affordable(def)) { NeedMore(def); return; }
        CloseMenu();
        EnterBuild();
        Select(def);
    }

    // bottom-bar chip tapped while placing: swap the blueprint, keep cursor and rotation
    void Switch(BuildingDef def)
    {
        if (!UIState.BuildMode || def == current) return;
        if (!Affordable(def)) { NeedMore(def); return; }
        int keepRot = rot;
        Select(def);
        rot = keepRot;
    }

    static void NeedMore(BuildingDef def)
    {
        int have = Economy.Instance != null ? Economy.Instance.materials : 0;
        GameToast.Show("NEED " + (def.cost - have) + " MORE MATERIALS FOR " + def.displayName.ToUpper(), WorkshopTheme.Red);
    }

    void EnterBuild()
    {
        UIState.BuildMode = true;
        BuildGrid.RefreshItems();

        // cursor starts a little ahead of the player; the top-down camera follows it
        var player = GameObject.FindWithTag("Player");
        Vector3 start = player != null ? player.transform.position + CameraForward() * 3f : Vector3.zero;
        cursor.position = ClampToArea(start);
        if (buildCamera != null)
        {
            buildCamera.Target.TrackingTarget = cursor;
            buildCamera.Target.LookAtTarget = cursor;
            buildCamera.gameObject.SetActive(true);
            buildCamera.PreviousStateIsValid = false;
        }

        if (zoomSlider != null) zoomSlider.SetActive(false);
        hintText.gameObject.SetActive(true);
        refreshT = 0f;
        dirty = true;
    }

    public void Exit()
    {
        ClearGhost();
        UIState.BuildMode = false;
        if (buildCamera != null) buildCamera.gameObject.SetActive(false);
        if (overlay != null) overlay.Hide();
        if (zoomSlider != null) zoomSlider.SetActive(true);
        ShowNothing();
    }

    void Select(BuildingDef def)
    {
        ClearGhost();
        current = def;
        rot = 0;
        lastRot = -1;
        dirty = true;
        ghost = VisualCopy.Make(def.prefab, null);
        if (ghostMat != null)
            foreach (var r in ghost.GetComponentsInChildren<Renderer>())
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = ghostMat;
                r.sharedMaterials = mats;
            }
        placePanel.SetActive(true);
        hintText.text = "Joystick moves the building. It snaps to the grid.";
        placeTitle.text = def.displayName.ToUpper();
        RefreshChips();
    }

    void RefreshChips()
    {
        foreach (var c in chips)
            if (c != null && c.Def != null) c.SetState(c.Def == current, Affordable(c.Def), FreeBuild);
    }

    void ClearGhost()
    {
        current = null;
        if (ghost != null) Destroy(ghost);
        ghost = null;
    }

    // ------------------------------------------------------------------ per frame

    void Update()
    {
        // card costs / materials in the popup
        if (menuPanel.activeSelf || UIState.BuildMode)
        {
            refreshT -= Time.unscaledDeltaTime;
            if (refreshT <= 0f) { refreshT = 0.25f; RefreshMenu(); }
        }

        if (UIState.BuildMode && UIState.ModalOpen) Exit();   // another popup opened on top: leave build mode
        if (!UIState.BuildMode) return;

        MoveCursor();

        // fog clears / loot gets picked up / a site finishes: re-check now and then
        recheckT -= Time.unscaledDeltaTime;
        if (recheckT <= 0f)
        {
            recheckT = 0.5f;
            BuildGrid.RefreshItems();
            dirty = true;
        }

        if (current != null) UpdatePlacement();
    }

    static GameObject PlayerModel(PlayerUpgrades pu) => pu.previewPrefab != null ? pu.previewPrefab : pu.gameObject;

    void PlayerLevelUp()
    {
        var pu = PlayerUpgrades.Instance;
        if (pu == null) return;
        var up = pu.Upgrades;
        if (up.IsMaxLevel) { GameToast.Show("PLAYER IS AT MAX LEVEL", WorkshopTheme.Orange); return; }
        int cost = up.NextLevelCost;
        if (up.TryLevelUp())
        {
            var pt = PipeTether.Instance;
            string pipe = pt == null ? "" : pt.IsPumped ? "  -  H2O PUMP" : "  -  PIPE " + Mathf.RoundToInt(pt.Range) + " m";
            GameToast.Show("PLAYER LEVEL " + up.Level + "!" + pipe + (up.UnspentPoints > 0 ? "  -  +1 SKILL POINT" : ""), WorkshopTheme.Green);
        }
        else GameToast.Show("NEED " + cost + " MATERIALS", WorkshopTheme.Red);
        RefreshMenu();
    }

    void OpenPlayerTree()
    {
        var pu = PlayerUpgrades.Instance;
        if (pu == null || workshop == null || pu.Upgrades.tree == null) return;
        CloseMenu();
        workshop.Open(pu.Upgrades, PlayerModel(pu), () => OpenMenu(PlayerTab));   // CLOSE in the Workshop = back here
    }

    void RefreshPlayer()
    {
        var pu = PlayerUpgrades.Instance;
        var up = pu != null ? pu.Upgrades : null;
        bool badge = false;
        if (up != null && up.tree != null && playerLevelText != null)
        {
            string orange = WorkshopTheme.Hex(WorkshopTheme.Orange), green = WorkshopTheme.Hex(WorkshopTheme.Green);
            int pts = up.UnspentPoints;
            playerLevelText.text = "PLAYER LEVEL  <color=" + orange + ">" + up.Level + "</color> / " + up.MaxLevel;
            playerPointsText.text = pts > 0 ? (pts == 1 ? "1 SKILL POINT" : pts + " SKILL POINTS") + " TO SPEND IN THE TREE" : "";

            var cur = up.Current;
            var sb = new System.Text.StringBuilder();
            var list = up.tree.shownStats != null && up.tree.shownStats.Count > 0 ? up.tree.shownStats.ToArray() : Stats.FactoryStats;
            foreach (var st in list)
                sb.Append(Stats.Label(st)).Append("<pos=58%><color=#FFFFFF>").Append(Stats.Format(st, cur[st])).Append("</color>\n");
            playerStatsText.text = sb.ToString();

            var img = playerLevelUpButton.targetGraphic as Image;
            if (up.IsMaxLevel)
            {
                playerLevelUpLabel.text = "MAX LEVEL";
                if (img != null) img.color = WorkshopTheme.ButtonOff;
                playerNextText.text = "Fully upgraded";
            }
            else
            {
                bool afford = up.CanAffordLevelUp;
                badge |= afford;
                playerLevelUpLabel.text = "LEVEL UP\n<size=65%>" + (FreeBuild ? "FREE (debug)" : up.NextLevelCost + " MATERIALS") + "</size>";
                playerLevelUpLabel.color = afford ? Color.white : WorkshopTheme.Muted;
                if (img != null) img.color = afford ? WorkshopTheme.ButtonGreen : WorkshopTheme.ButtonOff;
                // what the next level brings: longer pipe (or the pump), +1 skill point
                var pt = PipeTether.Instance;
                string pipe = "";
                if (pt != null && !pt.IsPumped)
                {
                    float next = pt.RangeAt(up.Level + 1);
                    pipe = next >= Stats.NoLimit * 0.5f ? "Pipe > <color=" + green + ">H2O PUMP (no limit)</color>"
                         : "Pipe " + Mathf.RoundToInt(pt.Range) + " > <color=" + green + ">" + Mathf.RoundToInt(next + pt.bonusRange) + " m</color>";
                }
                bool point = up.Level - 1 < up.TierCount;
                playerNextText.text = "Next level:\n" + pipe + (point ? "\n<color=" + green + ">+1 skill point</color>" : "") + "\nBetter stats";
            }
            if (playerTreeLabel != null)
                playerTreeLabel.text = "UPGRADE TREE" + (pts > 0 ? "\n<size=65%><color=" + orange + ">" + pts + " POINT" + (pts > 1 ? "S" : "") + " !</color></size>" : "");
            badge |= pts > 0;
        }
        // "!" on the PLAYER tab when there is something to do there
        if (tabButtons != null && tabButtons.Length > PlayerTab && tabButtons[PlayerTab] != null)
        {
            var l = tabButtons[PlayerTab].GetComponentInChildren<TMP_Text>();
            if (l != null) l.text = TabNames[PlayerTab] + (badge ? "  !" : "");
        }
    }

    void RefreshMenu()
    {
        RefreshPlayer();
        foreach (var c in cards) if (c.Def != null) c.SetCost(c.Def.cost, Affordable(c.Def));
        if (UIState.BuildMode) RefreshChips();
        if (menuMaterialsText != null)
            menuMaterialsText.text = "MATERIALS  " + (Economy.Instance != null ? Economy.Instance.materials : 0);
    }

    Vector3 CameraForward()
    {
        var cam = Camera.main;
        float yaw = cam != null ? cam.transform.eulerAngles.y : 0f;
        return Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
    }

    Vector2 ReadInput()
    {
        Vector2 input = joystick != null ? joystick.Direction : Vector2.zero;
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (input.sqrMagnitude < 0.0001f && kb != null)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) input.y += 1;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) input.y -= 1;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) input.x += 1;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) input.x -= 1;
        }
#endif
        return Vector2.ClampMagnitude(input, 1f);
    }

    void MoveCursor()
    {
        Vector2 input = ReadInput();
        if (input.sqrMagnitude < 0.01f) return;
        Vector3 fwd = CameraForward();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Vector3 p = cursor.position + (fwd * input.y + right * input.x) * cursorSpeed * Time.unscaledDeltaTime;
        cursor.position = ClampToArea(p);
    }

    static Vector3 ClampToArea(Vector3 p)
    {
        p.x = Mathf.Clamp(p.x, BuildGrid.Min.x, BuildGrid.Max.x);
        p.z = Mathf.Clamp(p.z, BuildGrid.Min.y, BuildGrid.Max.y);
        p.y = GroundUtil.Height(p, p.y);
        return p;
    }

    static bool Affordable(BuildingDef def) =>
        FreeBuild || (Economy.Instance != null && Economy.Instance.materials >= def.cost);

    void UpdatePlacement()
    {
        var fp = current.Footprint;
        if (fp == null) return;
        var dims = BuildGrid.Dims(fp.size, rot);
        var bl = BuildGrid.SnapBottomLeft(cursor.position, dims);
        if (bl != lastBl || rot != lastRot || dirty)
        {
            lastBl = bl; lastRot = rot; dirty = false;
            Evaluate(fp, bl, dims);
        }

        float bob = Mathf.Sin(Time.unscaledTime * 3f) * 0.08f + 0.15f;
        ghost.transform.SetPositionAndRotation(ghostPos + Vector3.up * bob, ghostRot);

        bool afford = Affordable(current);
        if (ghostMat != null) ghostMat.color = valid && afford ? ghostOk : ghostBad;
        placeButton.interactable = valid && afford;
        placeStatus.text = !valid ? "<color=#FF6A55>" + reason + "</color>"
                         : !afford ? "<color=#FF6A55>Need " + current.cost + " materials</color>"
                         : "<color=#8BE07A>Ready to build</color>  ·  " + (FreeBuild ? "FREE (debug)" : current.cost + " materials") +
                           "  ·  " + current.buildTime.ToString("0.#") + "s";
    }

    void Evaluate(GridFootprint fp, Vector2Int bl, Vector2Int dims)
    {
        states.Clear();
        valid = true;
        reason = "";
        for (int x = 0; x < dims.x; x++)
            for (int z = 0; z < dims.y; z++)
            {
                var c = new Vector2Int(bl.x + x, bl.y + z);
                var st = BuildGrid.Check(c);
                states[c] = st;
                if (st != BuildGrid.CellState.Free && valid)
                {
                    valid = false;
                    reason = BuildGrid.Describe(st);
                }
            }

        footBl = bl;
        footDims = dims;
        ghostRot = Quaternion.Euler(0f, rot * 90f, 0f);
        Vector3 center = BuildGrid.FootprintCenter(bl, dims);
        Vector3 pos = center - ghostRot * new Vector3(fp.centerOffset.x, 0f, fp.centerOffset.y);
        pos.y = GroundLeveler.PadHeight(center, HalfSize(dims));   // the height the leveled pad will get
        ghostPos = pos;
        overlay.Show(bl, dims, states);
    }

    static Vector2 HalfSize(Vector2Int dims) => new Vector2(dims.x, dims.y) * BuildGrid.CellSize * 0.5f;

    void Place()
    {
        if (current == null || !valid) return;
        if (!FreeBuild)
        {
            if (Economy.Instance == null || !Economy.Instance.TrySpend(current.cost))
            {
                GameToast.Show("NOT ENOUGH MATERIALS", WorkshopTheme.Red);
                return;
            }
        }

        // level the ground (AoE2 style), then start construction on the flat pad
        Vector3 center = BuildGrid.FootprintCenter(footBl, footDims);
        float padY = GroundLeveler.Flatten(center, HalfSize(footDims), levelMargin);
        var pos = new Vector3(ghostPos.x, padY, ghostPos.z);
        ConstructionSite.Create(current, pos, ghostRot, ghostMaterial, constructionColor);
        GameToast.Show("BUILDING " + current.displayName.ToUpper() + "  (" + current.buildTime.ToString("0.#") + "s)", current.color);

        // stay in build mode: place another one, switch building in the bottom bar, or EXIT
        dirty = true;
        RefreshMenu();
    }
}
