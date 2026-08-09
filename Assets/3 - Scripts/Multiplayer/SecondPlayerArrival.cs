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
    /// Roughly how long StasisPodSave's DOWNLOADING ritual runs on the download
    /// path — hold (holdSeconds + 0.9) plus clear (clearSeconds). Only used to
    /// time the door broadcast; being a little out is cosmetic.
    const float PodWakeSeconds = 2.8f;

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

        // Tell the sync layer we're placed, so it starts publishing our position
        // from the POD rather than from wherever the scene dropped us. Until
        // this happens the host cannot see us at all. Runs alongside — the
        // connection may not have finished yet.
        StartCoroutine(ReleasePoseHoldWhenSpawned());

        // Hand off to the pod's real ritual rather than a lookalike.
        var pod = FindObjectOfType<StasisPodSave>();
        if (pod != null) pod.PlayDownloadWake();

        // Fade the black out from under the DOWNLOADING overlay, which sorts
        // above this cover and is already running.
        yield return Fade(1f, 0f, FadeSeconds);
        if (_cover != null) Destroy(_cover.canvas.gameObject);

        // The pod's ritual opens its own door locally when the download
        // completes. Mirror that to everyone else at the same MOMENT, so the
        // host sees the door hold shut over an occupied pod and then open —
        // broadcasting on arrival instead would swing it open before the
        // player had finished materialising.
        if (pod != null) yield return new WaitForSecondsRealtime(PodWakeSeconds);
        // Ask the host to open it — a client isn't allowed to decide, and the
        // host's periodic state broadcast would just shut it again if we did.
        StasisDoorSync.RequestOpen();

        // The pod's ritual calls UnlockAll when it finishes; this is belt and
        // braces for the case where no pod was found at all.
        if (pod == null) TutorialGate.UnlockAll();
    }

    /// Release the publish hold on our own networked avatar, once it exists.
    ///
    /// PlanetRelativeSync deliberately holds a joining client's position
    /// broadcast until it has been seated, so the host doesn't watch them pop
    /// from the default spawn into the pod.
    ///
    /// POLLS, because the seating can easily happen before the Relay connection
    /// finishes and the player object spawns — a one-shot check would miss it
    /// and leave the guest invisible until the hold expired on its own.
    IEnumerator ReleasePoseHoldWhenSpawned()
    {
        float deadline = Time.realtimeSinceStartup + 20f;
        while (Time.realtimeSinceStartup < deadline)
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            var player = nm != null && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
            if (player != null)
            {
                var sync = player.GetComponent<PlanetRelativeSync>();
                if (sync != null) { sync.MarkOwnerPlaced(); yield break; }
            }
            yield return null;
        }
        Debug.LogWarning("[SecondPlayerArrival] Never saw our player object — the "
                       + "pose hold will expire on its own.");
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
    /// Public because MultiplayerDeathRespawn reuses it: respawning after a
    /// death and arriving as a guest put the player in exactly the same place,
    /// and that should stay one implementation.
    public static void SeatInPod()
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
