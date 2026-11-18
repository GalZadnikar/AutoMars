using UnityEngine;

/// Global UI flags. While a popup is open the player doesn't move and world buttons hide.
public static class UIState
{
    public static bool ModalOpen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => ModalOpen = false;
}
