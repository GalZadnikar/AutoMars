using TMPro;
using UnityEngine;

/// Top-of-screen counters: materials and carried trash.
public class GameHUD : MonoBehaviour
{
    public TMP_Text materialsText;
    public TMP_Text carryText;
    public TMP_Text disksText;
    public PlayerCarrier player;

    int shownMaterials = -1, shownCarry = -1, shownCap = -1, shownDisks = -1;
    float diskPunch;
    Vector3 diskBaseScale = Vector3.one;
    float punch;
    Vector3 baseScale = Vector3.one;

    void Start()
    {
        if (materialsText != null) baseScale = materialsText.transform.localScale;
        if (disksText != null) diskBaseScale = disksText.transform.localScale;
    }

    void Update()
    {
        int m = Economy.Instance != null ? Economy.Instance.materials : 0;
        if (m != shownMaterials)
        {
            if (shownMaterials >= 0) punch = 1f;
            shownMaterials = m;
            if (materialsText != null) materialsText.text = m.ToString();
        }
        if (materialsText != null && punch > 0f)
        {
            punch = Mathf.MoveTowards(punch, 0f, Time.deltaTime * 6f);
            materialsText.transform.localScale = baseScale * (1f + 0.2f * punch);
        }

        int disks = PlayerInventory.Instance != null ? PlayerInventory.Instance.DiskCount : 0;
        if (disksText != null && disks != shownDisks)
        {
            if (shownDisks >= 0 && disks > shownDisks) diskPunch = 1f;
            shownDisks = disks;
            disksText.text = disks.ToString();
        }
        if (disksText != null && diskPunch > 0f)
        {
            diskPunch = Mathf.MoveTowards(diskPunch, 0f, Time.deltaTime * 5f);
            disksText.transform.localScale = diskBaseScale * (1f + 0.3f * diskPunch);
        }

        if (player != null && carryText != null && (player.Carried != shownCarry || player.capacity != shownCap))
        {
            shownCarry = player.Carried; shownCap = player.capacity;
            carryText.text = shownCarry + " / " + shownCap;
            carryText.color = player.IsFull ? new Color(1f, 0.4f, 0.35f) : Color.white;
        }
    }
}
