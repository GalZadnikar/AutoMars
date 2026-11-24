using UnityEngine;

/// Sits on the "FX Pool" object and returns finished effects to FxPool. Made by FxPool, never added by hand.
public class FxPoolRunner : MonoBehaviour
{
    void Update() => FxPool.Tick();
}
