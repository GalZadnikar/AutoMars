using UnityEngine;

/// Global UI flags. While a popup is open the player doesn't move and world buttons hide.
public static class UIState
{
    public static bool ModalOpen;
    /// Placing buildings: world prompts hide, the player can still walk.
    public static bool BuildMode;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { ModalOpen = false; BuildMode = false; }
}
