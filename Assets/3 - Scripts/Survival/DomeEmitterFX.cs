using UnityEngine;

/// <summary>
/// Animates a bubble-dome emitter device: spins a ring of nodes and pulses the
/// emissive parts so it reads as a live piece of technology. Wired up by
/// DomeBuildRegistrar when it builds the placeholder emitter.
/// </summary>
public class DomeEmitterFX : MonoBehaviour
{
    public Transform spinner;          // ring of nodes, rotated about its up axis
    public Renderer[] pulseRenderers;  // emissive parts that breathe
    public float spinSpeed = 45f;      // deg/sec
    public float pulseSpeed = 2.5f;
    public Color emitColor = new Color(0.4f, 0.85f, 1f, 1f);
    public float emitMin = 0.6f;
    public float emitMax = 2.4f;

    MaterialPropertyBlock _mpb;

    void Awake() { _mpb = new MaterialPropertyBlock(); }

    void Update()
    {
        if (spinner != null) spinner.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.Self);

        if (pulseRenderers == null || _mpb == null) return;
        float k = Mathf.Lerp(emitMin, emitMax, 0.5f + 0.5f * Mathf.Sin(Time.time * pulseSpeed));
        Color e = emitColor * k;
        for (int i = 0; i < pulseRenderers.Length; i++)
        {
            var r = pulseRenderers[i];
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor("_EmissionColor", e);
            r.SetPropertyBlock(_mpb);
        }
    }
}
