using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One power switch of the "Cold Company" base-door puzzle. Attach to each switch/breaker prop
/// at the base entrance. Look at it + press F to flip it ON (one-way). When ALL switches in the
/// scene are on, the <see cref="MoonBaseDoor"/> powers up and opens.
///
/// Mirrors the vendor interaction pattern (gaze gate + F + InteractPromptUI); bare empties drop
/// the gaze requirement automatically. Optional on/off indicator child objects give feedback.
/// </summary>
public class MoonBasePowerSwitch : MonoBehaviour
{
    public static readonly List<MoonBasePowerSwitch> All = new List<MoonBasePowerSwitch>();

    [Header("Interaction")]
    [Tooltip("Proximity radius. A trigger SphereCollider is auto-added at this radius if the object has no collider.")]
    public float interactRadius = 2.5f;

    [Header("Lever (optional — rotates when flipped)")]
    [Tooltip("Handle transform that rotates from the OFF angle to the ON angle when flipped.")]
    public Transform leverHandle;
    public Vector3 leverOffLocalEuler = new Vector3(45f, 0f, 0f);
    public Vector3 leverOnLocalEuler = new Vector3(-45f, 0f, 0f);
    public float leverFlipTime = 0.35f;

    [Header("Feedback (optional)")]
    [Tooltip("Child object shown while this switch is ON (e.g. a green light).")]
    public GameObject onIndicator;
    [Tooltip("Child object shown while this switch is OFF (e.g. a red light).")]
    public GameObject offIndicator;
    public AudioClip flipSound;

    public bool IsOn { get; private set; }
    bool _playerInRange;
    bool _requireGaze;

    /// <summary>True only when at least one switch exists and every switch is on.</summary>
    public static bool AllOn()
    {
        if (All.Count == 0) return false;
        for (int i = 0; i < All.Count; i++)
            if (All[i] == null || !All[i].IsOn) return false;
        return true;
    }

    void Reset()
    {
        var sc = GetComponent<SphereCollider>();
        if (sc == null) sc = gameObject.AddComponent<SphereCollider>();
        sc.isTrigger = true;
        sc.radius = interactRadius;
    }

    void Awake()
    {
        if (GetComponent<Collider>() == null)
        {
            var sc = gameObject.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = interactRadius;
        }

        // Give the visible switch body a SOLID (non-trigger) collider so the gaze
        // crosshair (InteractGaze) can actually hit it: occlusion-respecting and easy
        // to aim. Without this the switch has only its trigger sphere, so InteractGaze
        // falls back to a through-wall silhouette cone — you could flip it from the far
        // side of a wall, and had to aim precisely from up close. Fit the box to the
        // mesh's local bounds (runtime AddComponent does NOT auto-fit). Mirrors how the
        // trigger sphere is auto-added above: scene stays clean, every switch fixed.
        var body = GetComponentInChildren<MeshRenderer>();
        if (body != null && body.GetComponent<Collider>() == null)
        {
            var bc = body.gameObject.AddComponent<BoxCollider>();
            var mf = body.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                bc.center = mf.sharedMesh.bounds.center;
                bc.size = mf.sharedMesh.bounds.size;
            }
        }

        _requireGaze = GetComponentInChildren<Renderer>() != null;
        if (leverHandle != null) leverHandle.localRotation = Quaternion.Euler(leverOffLocalEuler);
        RefreshIndicators();
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); InteractPromptUI.Clear(this); }

    void OnTriggerEnter(Collider other) { if (other.CompareTag("Player")) _playerInRange = true; }
    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInRange = false;
        InteractPromptUI.Clear(this);
    }

    void Update()
    {
        if (!_playerInRange) return;

        InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to {(IsOn ? "cut" : "restore")} power");

        if ((!_requireGaze || InteractGaze.IsLookingAt(this)) && TutorialGate.InteractPressed(TutorialAbility.TalkToNPC))
            Toggle();
    }

    // Toggleable so the airlock can be cycled both ways (enter AND leave). The AirlockController
    // polls AllOn() and runs the seal/unseal sequence when every switch matches.
    void Toggle()
    {
        IsOn = !IsOn;
        InteractPromptUI.Clear(this);
        if (flipSound != null) AudioSource.PlayClipAtPoint(flipSound, transform.position);
        RefreshIndicators();
        if (leverHandle != null) StartCoroutine(SwingLever(IsOn));
    }

    IEnumerator SwingLever(bool toOn)
    {
        Quaternion from = leverHandle.localRotation;
        Quaternion to = Quaternion.Euler(toOn ? leverOnLocalEuler : leverOffLocalEuler);
        float t = 0f;
        float dur = Mathf.Max(0.01f, leverFlipTime);
        while (t < dur)
        {
            t += Time.deltaTime;
            leverHandle.localRotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, t / dur));
            yield return null;
        }
        leverHandle.localRotation = to;
    }

    void RefreshIndicators()
    {
        if (onIndicator != null) onIndicator.SetActive(IsOn);
        if (offIndicator != null) offIndicator.SetActive(!IsOn);
    }
}
