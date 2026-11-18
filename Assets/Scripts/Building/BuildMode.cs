using System.Collections.Generic;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// Base construction. BUILD → the player stops, the camera goes top-down over a build cursor and the
/// joystick moves that cursor (WASD in the Editor). Pick a building → its blueprint snaps to the grid at
/// the cursor → ROTATE / PLACE / CANCEL. Placing flattens the ground under it and starts a construction
/// site that finishes after the building's build time.
public class BuildMode : MonoBehaviour
{
    /// Debug: building costs nothing.
    public static bool FreeBuild;

    public List<BuildingDef> catalog = new List<BuildingDef>();

    [Header("UI")]
    public Button buildButton;
    public GameObject catalogPanel;
    public RectTransform cardRow;
    public BuildCard cardTemplate;
    public Button exitButton;
    public GameObject placePanel;
    public TMP_Text placeTitle, placeStatus, hintText;
    public Button rotateButton, cancelButton, placeButton;

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

    readonly List<BuildCard> cards = new List<BuildCard>();
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
        buildButton.onClick.AddListener(Enter);
        exitButton.onClick.AddListener(Exit);
        rotateButton.onClick.AddListener(() => { rot = (rot + 1) & 3; dirty = true; });
        cancelButton.onClick.AddListener(CancelPlacement);
        placeButton.onClick.AddListener(Place);

        foreach (var def in catalog)
        {
            if (def == null || def.prefab == null) continue;
            var c = Instantiate(cardTemplate, cardRow);
            c.gameObject.SetActive(true);
            var d = def;
            c.Setup(def, () => Select(d));
            cards.Add(c);
        }

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
        catalogPanel.SetActive(false);
        placePanel.SetActive(false);
        hintText.gameObject.SetActive(false);
    }

    public void Enter()
    {
        if (UIState.ModalOpen) return;
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
        catalogPanel.SetActive(true);
        placePanel.SetActive(false);
        hintText.gameObject.SetActive(true);
        hintText.text = "Joystick moves the view. Pick a building.";
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
        catalogPanel.SetActive(false);
        placePanel.SetActive(true);
        hintText.text = "Joystick moves the building. It snaps to the grid.";
        placeTitle.text = def.displayName.ToUpper();
    }

    void CancelPlacement()
    {
        ClearGhost();
        catalogPanel.SetActive(true);
        placePanel.SetActive(false);
        hintText.text = "Joystick moves the view. Pick a building.";
        dirty = true;
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
        bool showButton = !UIState.ModalOpen && !UIState.BuildMode;
        if (buildButton.gameObject.activeSelf != showButton) buildButton.gameObject.SetActive(showButton);

        if (UIState.BuildMode && UIState.ModalOpen) Exit();   // a popup opened on top: leave build mode
        if (!UIState.BuildMode) return;

        MoveCursor();

        refreshT -= Time.unscaledDeltaTime;
        if (refreshT <= 0f)
        {
            refreshT = 0.25f;
            for (int i = 0; i < cards.Count && i < catalog.Count; i++) cards[i].SetCost(catalog[i].cost, Affordable(catalog[i]));
        }

        // fog clears / loot gets picked up / a site finishes: re-check now and then
        recheckT -= Time.unscaledDeltaTime;
        if (recheckT <= 0f)
        {
            recheckT = 0.5f;
            BuildGrid.RefreshItems();
            dirty = true;
        }

        if (current != null) UpdatePlacement();
        else UpdateBrowseGrid();
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

    // no building picked yet: just show the grid around the cursor
    void UpdateBrowseGrid()
    {
        var cell = BuildGrid.WorldToCell(cursor.position);
        if (cell == lastBl && !dirty) return;
        lastBl = cell;
        dirty = false;
        states.Clear();
        overlay.Show(cell, Vector2Int.zero, states);
    }

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
        dirty = true;   // the spot is taken now
    }
}
