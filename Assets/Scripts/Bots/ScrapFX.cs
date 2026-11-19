using UnityEngine;

/// Small "scrap pop" when a robot is scrapped: a puff of smoke and a few metal bits.
/// Reuses PileClearFX at a small size (dust + flying scraps + sparkles).
public static class ScrapFX
{
    public static void Play(Vector3 pos) => PileClearFX.Play(pos, 1.5f);
}
