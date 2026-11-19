using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// A Base: takes trash from the player and turns each piece into materials. Every Base opens the BASE popup,
/// and Bases whose pipe circles overlap form one pipe network (see PipeTether) the player can walk around in.
public class HomeBase : MonoBehaviour
{
    public static readonly List<HomeBase> All = new List<HomeBase>();

    public float radius = 3.5f;
    [Tooltip("Where dropped trash flies to")]
    public Transform intake;
    [Tooltip("Seconds to convert one trash into materials (upgradeable)")]
    public float processTime = 0.15f;
    public int materialsPerTrash = 1;

    [Header("Visuals")]
    public Transform body;          // the cube, punches when fed
    public TMP_Text label;
    public GroundRing ring;
    public string idleText = "Base";

    int queued;
    float timer, punch;
    Vector3 bodyScale = Vector3.one;
    int shownQueued = -1;

    public Vector3 IntakePoint => intake != null ? intake.position : transform.position + Vector3.up * 2f;

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Awake()
    {
        if (body != null) bodyScale = body.localScale;
    }

    public bool InRange(Vector3 worldPos)
    {
        Vector3 d = worldPos - transform.position; d.y = 0f;
        return d.sqrMagnitude <= radius * radius;
    }

    public void SetHighlight(bool on) { if (ring != null) ring.SetHighlight(on); }

    public void Receive(int count)
    {
        queued += count;
        punch = 1f;
    }

    void Update()
    {
        if (queued > 0)
        {
            timer += Time.deltaTime;
            while (timer >= processTime && queued > 0)
            {
                timer -= processTime;
                queued--;
                if (Economy.Instance != null) Economy.Instance.AddMaterials(materialsPerTrash);
            }
        }
        else timer = 0f;

        if (body != null && punch > 0f)
        {
            punch = Mathf.MoveTowards(punch, 0f, Time.deltaTime * 5f);
            float s = Mathf.Sin(punch * Mathf.PI) * 0.12f;
            body.localScale = new Vector3(bodyScale.x * (1 + s), bodyScale.y * (1 - s * 0.7f), bodyScale.z * (1 + s));
        }

        if (label != null && shownQueued != queued)
        {
            shownQueued = queued;
            label.text = queued > 0 ? "Recycling " + queued : idleText;
        }
    }
}
