using System.Collections;
using UnityEngine;

// The valve wheel beside the stasis pod, wired as a push button: look at it,
// press interact → the wheel pushes in and back out, and the stasis pod door
// opens for openSeconds (then closes — deferred while the player is inside;
// see StasisPodDoor). Interaction mirrors LootBox: range gate + the shared
// InteractPromptUI pill + InteractGaze at the press.
public class StasisValveButton : MonoBehaviour
{
    public float interactRange = 3.5f;
    [Tooltip("How far the wheel pushes in (parent-local units).")]
    public float pressDepth = 0.08f;
    public float pressSeconds = 0.15f;
    [Tooltip("Seconds the stasis door stays open after a press.")]
    public float openSeconds = 5f;

    static string Prompt => $"Press {PromptGlyphs.Interact} to open stasis pod";

    StasisPodDoor _door;
    PlayerController _pc;
    Vector3 _restLocal;
    Vector3 _pushDirLocal;
    bool _pressing;
    float _refind;

    void Awake()
    {
        var seq = GetComponentInParent<ShuttleArrivalSequence>();
        if (seq != null) _door = seq.GetComponentInChildren<StasisPodDoor>(true);
        if (_door == null) _door = GetComponentInParent<StasisPodDoor>();

        _restLocal = transform.localPosition;
        // Push INTO the pipe housing behind the wheel; -Z fallback.
        Transform pipe = null;
        if (transform.parent != null)
            foreach (Transform s in transform.parent)
                if (s.name == "PipeValve") { pipe = s; break; }
        _pushDirLocal = pipe != null && (pipe.localPosition - _restLocal).sqrMagnitude > 0.0001f
            ? (pipe.localPosition - _restLocal).normalized
            : new Vector3(0f, 0f, -1f);
    }

    void Update()
    {
        if (_pc == null)
        {
            _refind -= Time.deltaTime;
            if (_refind > 0f) return;
            _refind = 0.5f;
            _pc = FindObjectOfType<PlayerController>();
            if (_pc == null) return;
        }

        float rangeSq = interactRange * interactRange;
        if ((transform.position - _pc.transform.position).sqrMagnitude > rangeSq || !CanInteract())
        {
            InteractPromptUI.Clear(this);
            return;
        }

        InteractPromptUI.Show(this, Prompt);

        if (!_pressing
            && (Input.GetKeyDown(KeyCode.F) || TutorialGate.PadPressed(TutorialGate.PadButton.X))
            && InteractGaze.IsLookingAt(this))
        {
            StartCoroutine(Press());
        }
    }

    bool CanInteract()
    {
        if (StorageUI.Instance != null && StorageUI.Instance.IsOpen) return false;
        if (PlayerController.isInDialogue) return false;
        if (PlayerController.isMapOpen) return false;
        if (PlayerPhoneUI.IsOpen) return false;
        if (Ship.FindPilotedShip() != null) return false;
        return true;
    }

    IEnumerator Press()
    {
        _pressing = true;
        if (_door != null) _door.OpenForSeconds(openSeconds);

        Vector3 pressed = _restLocal + _pushDirLocal * pressDepth;
        for (float t = 0f; t < pressSeconds; t += Time.deltaTime)
        {
            transform.localPosition = Vector3.Lerp(_restLocal, pressed, t / pressSeconds);
            yield return null;
        }
        for (float t = 0f; t < pressSeconds; t += Time.deltaTime)
        {
            transform.localPosition = Vector3.Lerp(pressed, _restLocal, t / pressSeconds);
            yield return null;
        }
        transform.localPosition = _restLocal;
        _pressing = false;
    }
}
