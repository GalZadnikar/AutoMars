using System.Collections.Generic;
using UnityEngine;

/// Sits on the Player. Shows the level tag only on the few robots closest to the player
/// (within showRadius); everything else stays hidden. Slightly larger hideRadius avoids flicker.
public class ProximityTags : MonoBehaviour
{
    public float showRadius = 6f;
    public float hideRadius = 7.5f;
    [Tooltip("At most this many tags at once (nearest first)")]
    public int maxShown = 3;
    [Tooltip("Seconds between re-checks")]
    public float interval = 0.1f;

    readonly List<KeyValuePair<float, BotLevelTag>> candidates = new List<KeyValuePair<float, BotLevelTag>>();
    HashSet<BotLevelTag> shown = new HashSet<BotLevelTag>();
    HashSet<BotLevelTag> next = new HashSet<BotLevelTag>();
    float timer;

    void Update()
    {
        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = interval;

        candidates.Clear();
        next.Clear();
        if (!UIState.ModalOpen)
        {
            Vector3 me = transform.position;
            foreach (var b in ScoutBot.All)
            {
                var tag = b.levelTag;
                if (tag == null) continue;
                Vector3 d = b.transform.position - me; d.y = 0f;
                float r = shown.Contains(tag) ? hideRadius : showRadius;
                float m = d.sqrMagnitude;
                if (m <= r * r) candidates.Add(new KeyValuePair<float, BotLevelTag>(m, tag));
            }
            candidates.Sort((a, b) => a.Key.CompareTo(b.Key));
            for (int i = 0; i < candidates.Count && i < maxShown; i++) next.Add(candidates[i].Value);
        }

        foreach (var t in shown) if (t != null && !next.Contains(t)) t.SetVisible(false);
        foreach (var t in next) if (!shown.Contains(t)) t.SetVisible(true);
        (shown, next) = (next, shown);
    }

    void OnDisable()
    {
        foreach (var t in shown) if (t != null) t.SetVisible(false);
        shown.Clear();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, showRadius);
    }
}
