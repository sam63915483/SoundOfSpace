using System;
using UnityEngine;

/// <summary>
/// Sits on the spawned body of an authored NPC (added by AuthoredNPCSpawner).
/// Relays the talk trigger to whichever AuthoredNPCTalk lives on the spawner
/// empty, and is the component InteractGaze is asked about (gaze resolves the
/// silhouette of the object the component sits on -- this one, the body).
/// </summary>
public class AuthoredNPCBody : MonoBehaviour
{
    public AuthoredNPCSpawner Owner { get; internal set; }
    public bool PlayerInRange { get; private set; }
    public event Action OnPlayerEnter;
    public event Action OnPlayerExit;

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (PlayerInRange) return;
        PlayerInRange = true;
        OnPlayerEnter?.Invoke();
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (!PlayerInRange) return;
        PlayerInRange = false;
        OnPlayerExit?.Invoke();
    }

    void OnDisable()
    {
        if (!PlayerInRange) return;
        PlayerInRange = false;
        OnPlayerExit?.Invoke();
    }
}
