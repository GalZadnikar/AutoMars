using UnityEngine;
using UnityEngine.UI;

/// The weapon tile in the HUD (left side, under the tools): the gun's icon with one pip per bullet in the magazine.
///  - A shot: its pip goes dark with a small drop, the icon kicks.
///  - Reload: a dark clock-wipe runs over the icon and the pips come back one by one; a flash when it is done.
///  - The last bullets turn red. Yellow frame while the gun is in the player's hands.
///  - Tap = reload now.
/// Hidden while the player doesn't own the weapon.
public class WeaponSlotView : MonoBehaviour
{
    public Image icon;
    [Tooltip("One per bullet, in the order they are fired (the last one is used first)")]
    public Image[] pips;
    [Tooltip("Radial wipe over the tile during a reload")]
    public Image reloadFill;
    [Tooltip("White flash when the reload is done")]
    public Image flash;
    public GameObject activeFrame;
    public Button button;
    public CanvasGroup group;

    public Color pipFull = new Color(1f, 0.84f, 0.3f);
    public Color pipLow = new Color(1f, 0.38f, 0.28f);
    public Color pipEmpty = new Color(0f, 0f, 0f, 0.5f);
    [Tooltip("Pips turn red when this many bullets (or fewer) are left")]
    public int lowAmmo = 2;

    PlayerPistol pistol;
    PlayerTools tools;
    bool[] lit;
    float[] punch;
    float iconKick, flashT;
    bool wasReloading;
    Vector2 iconPos;

    void Awake()
    {
        if (button != null) button.onClick.AddListener(() => { if (pistol != null) pistol.Reload(); });
        if (icon != null) iconPos = icon.rectTransform.anchoredPosition;
        int n = pips != null ? pips.Length : 0;
        lit = new bool[n];
        punch = new float[n];
        for (int i = 0; i < n; i++) lit[i] = true;
        if (flash != null) flash.gameObject.SetActive(false);
        if (reloadFill != null) reloadFill.gameObject.SetActive(false);
    }

    void Update()
    {
        if (pistol == null)
        {
            pistol = PlayerPistol.Instance;
            if (pistol != null) tools = pistol.GetComponent<PlayerTools>();
        }
        bool owned = pistol != null && pistol.owned;
        if (group != null)
        {
            group.alpha = owned ? 1f : 0f;
            group.blocksRaycasts = owned;
        }
        if (!owned) return;

        float dt = Time.unscaledDeltaTime;
        int n = pips != null ? pips.Length : 0;
        bool reloading = pistol.Reloading;
        // during a reload the bullets come back one by one
        int shown = reloading ? Mathf.FloorToInt(pistol.ReloadProgress * pistol.magazine) : pistol.Ammo;
        if (n > 0 && pistol.magazine != n) shown = Mathf.RoundToInt(shown * (float)n / pistol.magazine);
        bool low = !reloading && pistol.Ammo <= lowAmmo;

        for (int i = 0; i < n; i++)
        {
            var p = pips[i];
            if (p == null) continue;
            bool on = i < shown;
            if (on != lit[i])
            {
                lit[i] = on;
                punch[i] = 1f;
                if (!on && !reloading) iconKick = 1f;   // a shot
            }
            if (punch[i] > 0f) punch[i] = Mathf.MoveTowards(punch[i], 0f, dt * 7f);
            p.color = on ? (low ? pipLow : pipFull) : pipEmpty;
            // a loaded bullet pops in bigger, a fired one drops smaller, both settle to 1
            float s = on ? 1f + 0.7f * punch[i] : 1f - 0.45f * punch[i];
            p.rectTransform.localScale = new Vector3(s, s, 1f);
        }

        if (icon != null)
        {
            if (iconKick > 0f) iconKick = Mathf.MoveTowards(iconKick, 0f, dt * 9f);
            float k = iconKick * iconKick;
            icon.rectTransform.anchoredPosition = iconPos + new Vector2(-10f, 6f) * k;
            icon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 14f * k);
            float a = reloading ? 0.55f : 1f;
            if (!Mathf.Approximately(icon.color.a, a)) icon.color = new Color(1f, 1f, 1f, a);
        }

        if (reloadFill != null)
        {
            if (reloadFill.gameObject.activeSelf != reloading) reloadFill.gameObject.SetActive(reloading);
            if (reloading) reloadFill.fillAmount = 1f - pistol.ReloadProgress;
        }

        if (wasReloading && !reloading) flashT = 1f;   // reload done
        wasReloading = reloading;
        if (flash != null)
        {
            if (flashT > 0f)
            {
                flashT = Mathf.MoveTowards(flashT, 0f, dt * 5f);
                if (!flash.gameObject.activeSelf) flash.gameObject.SetActive(true);
                flash.color = new Color(1f, 1f, 1f, 0.75f * flashT);
            }
            else if (flash.gameObject.activeSelf) flash.gameObject.SetActive(false);
        }
        float tile = 1f + 0.1f * flashT * flashT;
        transform.localScale = new Vector3(tile, tile, 1f);

        if (activeFrame != null)
        {
            bool inHands = tools != null ? tools.tool == PlayerTool.Pistol : pistol.InHands;
            if (activeFrame.activeSelf != inHands) activeFrame.SetActive(inHands);
        }
    }
}
