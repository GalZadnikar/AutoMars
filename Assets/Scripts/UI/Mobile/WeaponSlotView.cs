using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// The weapon tile in the HUD (left side, under the tools): the icon of the weapon the player holds and its
/// ammo as a number, "4/6".
///  - A shot: the number jumps, the icon kicks. The last third of the magazine is red.
///  - Reload: a dark clock-wipe runs over the icon and the number counts back up; a flash when it is done.
///  - A new weapon (pickup): the icon changes and the tile pops.
///  - Yellow frame while the weapon is in the player's hands. Tap = reload now.
/// Hidden while the player has no weapon.
public class WeaponSlotView : MonoBehaviour
{
    public Image icon;
    [Tooltip("\"4/6\"")]
    public TMP_Text ammoText;
    [Tooltip("Radial wipe over the tile during a reload")]
    public Image reloadFill;
    [Tooltip("White flash when the reload is done or the weapon changes")]
    public Image flash;
    public GameObject activeFrame;
    public Button button;
    public CanvasGroup group;

    public Color ammoColor = Color.white;
    public Color lowColor = new Color(1f, 0.38f, 0.28f);
    public Color reloadColor = new Color(1f, 0.84f, 0.3f);
    [Tooltip("The number turns red when this part of the magazine (or less) is left")]
    [Range(0f, 1f)] public float lowAmmo = 0.34f;

    PlayerWeapon pw;
    PlayerTools tools;
    WeaponDef shownWeapon;
    int shownAmmo = -1, shownMag = -1;
    float iconKick, textPunch, flashT;
    bool wasReloading;
    Vector2 iconPos;

    void Awake()
    {
        if (button != null) button.onClick.AddListener(() => { if (pw != null) pw.Reload(); });
        if (icon != null) iconPos = icon.rectTransform.anchoredPosition;
        if (flash != null) flash.gameObject.SetActive(false);
        if (reloadFill != null) reloadFill.gameObject.SetActive(false);
    }

    void Update()
    {
        if (pw == null)
        {
            pw = PlayerWeapon.Instance;
            if (pw != null) tools = pw.GetComponent<PlayerTools>();
        }
        var def = pw != null ? pw.weapon : null;
        bool owned = def != null;
        if (group != null)
        {
            group.alpha = owned ? 1f : 0f;
            group.blocksRaycasts = owned;
        }
        if (!owned) { shownWeapon = null; return; }

        float dt = Time.unscaledDeltaTime;
        bool reloading = pw.Reloading;

        if (def != shownWeapon)
        {
            bool first = shownWeapon == null;
            shownWeapon = def;
            shownAmmo = -1;
            if (icon != null)
            {
                icon.sprite = def.icon;
                icon.enabled = def.icon != null;
            }
            if (!first) flashT = 1f;   // a new weapon: pop
        }

        // during a reload the number counts back up
        int ammo = reloading ? Mathf.FloorToInt(pw.ReloadProgress * def.magazine) : pw.Ammo;
        if (ammo != shownAmmo || def.magazine != shownMag)
        {
            if (shownAmmo >= 0)
            {
                textPunch = 1f;
                if (ammo < shownAmmo && !reloading) iconKick = 1f;   // a shot
            }
            shownAmmo = ammo;
            shownMag = def.magazine;
            if (ammoText != null) ammoText.text = ammo + "/" + def.magazine;
        }
        if (ammoText != null)
        {
            if (textPunch > 0f) textPunch = Mathf.MoveTowards(textPunch, 0f, dt * 7f);
            float s = 1f + 0.35f * textPunch * textPunch;
            ammoText.rectTransform.localScale = new Vector3(s, s, 1f);
            ammoText.color = reloading ? reloadColor : (pw.Ammo <= Mathf.Max(1f, def.magazine * lowAmmo) ? lowColor : ammoColor);
        }

        if (icon != null)
        {
            if (iconKick > 0f) iconKick = Mathf.MoveTowards(iconKick, 0f, dt * 9f);
            float k = iconKick * iconKick;
            icon.rectTransform.anchoredPosition = iconPos + new Vector2(10f, 6f) * k;
            icon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -14f * k);
            float a = reloading ? 0.55f : 1f;
            if (!Mathf.Approximately(icon.color.a, a)) icon.color = new Color(1f, 1f, 1f, a);
        }

        if (reloadFill != null)
        {
            if (reloadFill.gameObject.activeSelf != reloading) reloadFill.gameObject.SetActive(reloading);
            if (reloading) reloadFill.fillAmount = 1f - pw.ReloadProgress;
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
            bool inHands = tools != null ? tools.tool == PlayerTool.Weapon : pw.InHands;
            if (activeFrame.activeSelf != inHands) activeFrame.SetActive(inHands);
        }
    }
}
