using System.Collections;
using UnityEngine;

/// <summary>
/// A single airlock door leaf for "Cold Company". Opens by retracting UP into the frame (the top
/// edge stays pinned so nothing ever rises past the doorway to poke/​block the floor above) and
/// closes by extending back DOWN to seal. Driven by <see cref="AirlockController"/>, not directly
/// by the switches. Auto-detects the vertical axis from the moon's gravity, so the door's odd
/// world rotation doesn't matter.
/// </summary>
public class MoonBaseDoor : MonoBehaviour
{
    [Header("Motion")]
    public float openTime = 1.2f;
    [Tooltip("Half the leaf's height in LOCAL units (the sFuture 1x1 panel is 1.5).")]
    public float doorHalfHeight = 1.5f;
    [Tooltip("If true this leaf BEGINS open (retracted + hidden) — e.g. the outer SEALDOOR.")]
    public bool startOpen = false;

    [Header("Sound")]
    public AudioClip openSound;
    public AudioClip closeSound;

    public bool IsOpen { get; private set; }
    public bool IsBusy => _busy;

    bool _busy;
    Collider[] _cols;
    Renderer[] _rends;
    Vector3 _closedLocalPos;
    Vector3 _closedScale;
    Vector3 _closedLossy;

    void Awake()
    {
        _cols = GetComponentsInChildren<Collider>();
        _rends = GetComponentsInChildren<Renderer>();
        _closedLocalPos = transform.localPosition;
        _closedScale = transform.localScale;
        _closedLossy = transform.lossyScale;
    }

    void Start()
    {
        // Apply initial state once the sim/bodies exist (gravity axis needs the moon body).
        ApplyState(startOpen ? 0f : 1f);
        SetColliders(!startOpen);
        SetRenderers(!startOpen);
        IsOpen = startOpen;
    }

    public void Open()  { if (!IsOpen && !_busy) StartCoroutine(Animate(closing: false)); }
    public void Close() { if (IsOpen && !_busy) StartCoroutine(Animate(closing: true)); }

    IEnumerator Animate(bool closing)
    {
        _busy = true;
        var clip = closing ? closeSound : openSound;
        if (clip != null) AudioSource.PlayClipAtPoint(clip, transform.position);

        if (closing) { SetColliders(true); SetRenderers(true); } // solid as it seals
        else SetColliders(false);                                // passable immediately as it opens

        float t = 0f;
        while (t < openTime)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / openTime);
            ApplyState(closing ? p : 1f - p);   // leaf fraction: 1 = closed/full, 0 = open/retracted
            yield return null;
        }
        ApplyState(closing ? 1f : 0f);
        IsOpen = !closing;
        if (!closing) SetRenderers(false);       // fully retracted → hide the zero-height sliver
        _busy = false;
    }

    // s = leaf fraction (1 closed/full … 0 open/retracted). The top edge stays pinned in place.
    void ApplyState(float s)
    {
        ComputeAxis(out Vector3 worldUp, out bool scaleY, out float halfHeightWorld);
        var sc = _closedScale;
        if (scaleY) sc.y = _closedScale.y * s; else sc.x = _closedScale.x * s;
        transform.localScale = sc;
        Vector3 moveWorld = worldUp * (halfHeightWorld * (1f - s));
        transform.localPosition = _closedLocalPos +
            (transform.parent != null ? transform.parent.InverseTransformVector(moveWorld) : moveWorld);
    }

    void ComputeAxis(out Vector3 worldUp, out bool scaleY, out float halfHeightWorld)
    {
        Vector3 gravUp = Vector3.up;
        var moon = ColdCompany.FindMoon();
        if (moon != null) gravUp = (transform.position - moon.transform.position).normalized;
        float du = Vector3.Dot(transform.up, gravUp);
        float dr = Vector3.Dot(transform.right, gravUp);
        scaleY = Mathf.Abs(du) >= Mathf.Abs(dr);
        worldUp = scaleY ? (du >= 0 ? transform.up : -transform.up)
                         : (dr >= 0 ? transform.right : -transform.right);
        halfHeightWorld = doorHalfHeight * (scaleY ? _closedLossy.y : _closedLossy.x);
    }

    void SetColliders(bool on) { if (_cols != null) foreach (var c in _cols) if (c != null && !c.isTrigger) c.enabled = on; }
    void SetRenderers(bool on) { if (_rends != null) foreach (var r in _rends) if (r != null) r.enabled = on; }
}
