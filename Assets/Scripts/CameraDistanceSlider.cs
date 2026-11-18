using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UI;

/// Lets the player zoom the follow camera in/out with a UI slider.
/// Keeps the camera's original angle and only scales the follow offset.
/// The chosen value is remembered between sessions.
public class CameraDistanceSlider : MonoBehaviour
{
    public Slider slider;
    public CinemachineFollow follow;

    [Header("Distance (multiplier of the camera's original distance)")]
    public float minMultiplier = 0.5f;
    public float maxMultiplier = 2f;
    public float defaultMultiplier = 1f;

    const string PrefKey = "CameraDistanceMultiplier";
    Vector3 baseOffset;

    void Start()
    {
        if (follow == null) follow = FindFirstObjectByType<CinemachineFollow>();
        if (slider == null) slider = GetComponent<Slider>();
        if (follow == null || slider == null)
        {
            Debug.LogWarning("CameraDistanceSlider: missing slider or CinemachineFollow.");
            enabled = false;
            return;
        }

        baseOffset = follow.FollowOffset;

        slider.minValue = minMultiplier;
        slider.maxValue = maxMultiplier;
        slider.wholeNumbers = false;
        slider.SetValueWithoutNotify(Mathf.Clamp(PlayerPrefs.GetFloat(PrefKey, defaultMultiplier), minMultiplier, maxMultiplier));
        slider.onValueChanged.AddListener(Apply);
        Apply(slider.value);
    }

    void OnDestroy()
    {
        if (slider != null) slider.onValueChanged.RemoveListener(Apply);
    }

    void Apply(float multiplier)
    {
        follow.FollowOffset = baseOffset * multiplier;
        PlayerPrefs.SetFloat(PrefKey, multiplier);
    }
}
