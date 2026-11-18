using UnityEngine;

/// The H2O pump that replaces the pipe after the last pipe upgrade: spins its rotor and pulses its water light.
public class H2OPump : MonoBehaviour
{
    public Transform rotor;
    public Renderer waterLight;
    public float spinSpeed = 120f;

    MaterialPropertyBlock mpb;
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    static readonly Color Water = new Color(0.2f, 0.75f, 1f);

    void Update()
    {
        if (rotor != null) rotor.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.Self);
        if (waterLight != null)
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();
            float k = 1.2f + Mathf.Sin(Time.time * 3f) * 0.6f;
            waterLight.GetPropertyBlock(mpb);
            mpb.SetColor(EmissionId, Water * k);
            waterLight.SetPropertyBlock(mpb);
        }
    }
}
