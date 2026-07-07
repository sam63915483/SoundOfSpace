using UnityEngine;

/// <summary>
/// A valid "Face Down" location (N-1, docs/story-drafts/staging-scripts.md §1).
/// Drop on an empty GameObject with the trigger sphere somewhere no one goes —
/// a cave interior, the moon's far side. Several can exist; whichever the
/// player uses first wins.
///
/// Zero-new-UI variant: once HAL's request is accepted (FaceDown_Accepted),
/// standing inside the volume with the phone CLOSED for the full wait counts
/// as placing it screen-down. Opening the phone or leaving resets the timer.
/// On success it queues conv_face_down_after — no chime, no toast; the next
/// phone open is the payoff. Deliberately does nothing observable during the
/// wait (see the anti-staging rule).
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public class FaceDownSpot : MonoBehaviour
{
    [Tooltip("Seconds the player must wait inside with the phone closed.")]
    [SerializeField] float requiredSeconds = 60f;

    bool _playerInside;
    float _timer;

    void Reset()
    {
        var c = GetComponent<SphereCollider>();
        c.isTrigger = true;
        c.radius = 8f;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player")) { _playerInside = true; _timer = 0f; }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player")) { _playerInside = false; _timer = 0f; }
    }

    void Update()
    {
        if (!_playerInside) return;
        if (!Mission2.Get(Mission2.FlagFaceDownAccepted) ||
            Mission2.Get(Mission2.FlagFaceDownWaitDone) ||
            Mission2.Get(Mission2.FlagFaceDownDone)) return;

        // The fiction is "phone placed screen-down": phone open = watching = reset.
        if (PlayerPhoneUI.IsOpen) { _timer = 0f; return; }

        _timer += Time.deltaTime;
        if (_timer < requiredSeconds) return;

        var sd = StoryDirector.Instance;
        if (sd == null) return;
        if (StoryContent.GetConversation("conv_face_down_after") == null)
        {
            Debug.LogWarning("[FaceDownSpot] conv_face_down_after not in StreamingAssets/Story yet — wait not consumed.");
            enabled = false;
            return;
        }

        Mission2.Set(Mission2.FlagFaceDownWaitDone);
        sd.QueueConversation("conv_face_down_after");
        // No notification, no HAL line. The staging rule: the game does not
        // acknowledge the minute. The player opens the phone; HAL says thank you.
        enabled = false;
    }
}
