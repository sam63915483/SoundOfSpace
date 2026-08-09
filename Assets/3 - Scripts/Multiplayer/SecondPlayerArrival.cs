using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// How a joining player arrives: out of the stasis pod, with the DOWNLOADING
/// screen — the same way every other arrival in this game happens.
///
/// Dropping a guest at the host's feet would be the easy thing and the wrong
/// one. Loading a save wakes you in the pod. Dying wakes you in the pod. So
/// joining wakes you in the pod, and the fiction stays whole: you were always
/// in the machine, you just got downloaded later.
///
/// ── Sequence ─────────────────────────────────────────────────────────────
///   1. Cover the screen in black the moment the gameplay scene loads, before
///      anything can be seen.
///   2. Wait until the world is ready to be walked into — the shuttle ramp
///      being down is the signal, matching what the intro itself waits for.
///   3. Seat the player inside the pod.
///   4. Hand off to the pod's own DOWNLOADING ritual and fade the black away
///      underneath it.
///
/// ── Why the statics are safe here ────────────────────────────────────────
/// TutorialGate, PlayerController.isInDialogue and the pod's own single-player
/// assumptions all look like blockers for a second player, and would be under
/// split-screen. They are not, because every player runs their own client with
/// exactly one real PlayerController and render-only puppets for everyone else.
/// This ritual freezes and blacks out THIS machine only; the host sees nothing.
/// </summary>
public class SecondPlayerArrival : MonoBehaviour
{
    const string GameplayScene = "1.6.7.7.7";
    /// How long to wait for the ramp before giving up and waking anyway, so a
    /// guest can never be trapped behind a black screen forever.
    const float ReadyTimeout = 45f;
    const float FadeSeconds = 0.9f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        if (!FeatureVault.Multiplayer) return;
        SceneManager.sceneLoaded += (scene, mode) =>
        {
            if (scene.name != GameplayScene) return;
            // Consumed, not merely read — see MultiplayerSession.TakeGuestArrival.
            if (!MultiplayerSession.TakeGuestArrival()) return;
            var go = new GameObject("SecondPlayerArrival");
            go.AddComponent<SecondPlayerArrival>();
        };
    }

    Image _cover;

    void Start() => StartCoroutine(Arrive());

    IEnumerator Arrive()
    {
        BuildCover();

        // Freeze this client's player while they are "still downloading".
        TutorialGate.LockAll();
        TutorialGate.Unlock(TutorialAbility.MouseLook);

        // Let the scene finish waking up before we go looking for anything.
        yield return null;
        yield return new WaitForFixedUpdate();

        // The guest skipped the intro, which means nothing opened the shuttle
        // ramp on THIS machine — the cinematic path never ran and there is no
        // save being restored. Open it, or the guest wakes up sealed inside.
        var exit = FindObjectOfType<ShuttleExitDoor>();
        if (exit != null) exit.OpenInstant();

        float deadline = Time.realtimeSinceStartup + ReadyTimeout;
        while (!WorldIsWalkable() && Time.realtimeSinceStartup < deadline)
            yield return null;

        SeatInPod();

        // Hand off to the pod's real ritual rather than a lookalike.
        var pod = FindObjectOfType<StasisPodSave>();
        if (pod != null) pod.PlayDownloadWake();

        // Fade the black out from under the DOWNLOADING overlay, which sorts
        // above this cover and is already running.
        yield return Fade(1f, 0f, FadeSeconds);
        if (_cover != null) Destroy(_cover.canvas.gameObject);

        // The pod's ritual calls UnlockAll when it finishes; this is belt and
        // braces for the case where no pod was found at all.
        if (pod == null) TutorialGate.UnlockAll();
    }

    /// The ramp being down is the same signal the opening waits for, and it is
    /// set on both the cinematic path and the load path.
    static bool WorldIsWalkable()
    {
        if (ShuttleArrivalSequence.IsPlaying) return false;
        return ShuttleExitDoor.HasOpened;
    }

    /// Put the player inside the pod. Positions are body-relative because the
    /// planet is orbiting — a world-space constant would be metres off by the
    /// time it is applied.
    static void SeatInPod()
    {
        var pc = FindObjectOfType<PlayerController>();
        if (pc == null) return;

        Transform pod = FindPod();
        if (pod == null) return;

        // Matches ShuttleArrivalSequence.standOffset: feet just above the
        // plinth, clear of the back glass, so PhysX depenetration has nothing
        // to fight when the player becomes solid again.
        Vector3 seat = pod.TransformPoint(new Vector3(0f, 1.02f, 0f));

        var rb = pc.Rigidbody;
        if (rb != null)
        {
            rb.position = seat;
            rb.rotation = pod.rotation;
            rb.angularVelocity = Vector3.zero;
        }
        pc.transform.position = seat;
        pc.transform.rotation = pod.rotation;

        // Inherit the planet's orbital velocity or the player is instantly
        // moving at several hundred m/s relative to the world under them.
        var body = pod.GetComponentInParent<CelestialBody>();
        pc.SetVelocity(body != null ? body.velocity : Vector3.zero);
        Physics.SyncTransforms();
    }

    static Transform FindPod()
    {
        var pod = FindObjectOfType<StasisPodSave>();
        if (pod != null) return pod.transform;
        // Fall back to the named group inside the lander prefab.
        var lander = GameObject.Find("Shuttle_Lander");
        if (lander == null) return null;
        foreach (var t in lander.GetComponentsInChildren<Transform>(true))
            if (t.name == "StasisPod") return t;
        return null;
    }

    void BuildCover()
    {
        var go = new GameObject("GuestArrivalCover");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Below the pod's DOWNLOADING overlay (32766) so the ritual reads on
        // top of the black, exactly like a save load does.
        canvas.sortingOrder = 32700;
        var group = go.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        var rt = new GameObject("Black", typeof(RectTransform));
        rt.transform.SetParent(go.transform, false);
        var r = (RectTransform)rt.transform;
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        // Oversized so no aspect ratio can show an edge.
        r.offsetMin = new Vector2(-200f, -200f);
        r.offsetMax = new Vector2(200f, 200f);
        _cover = rt.AddComponent<Image>();
        _cover.color = Color.black;
        _cover.raycastTarget = false;
    }

    IEnumerator Fade(float from, float to, float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            if (_cover != null)
                _cover.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, t / seconds));
            yield return null;
        }
        if (_cover != null) _cover.color = new Color(0f, 0f, 0f, to);
    }
}
