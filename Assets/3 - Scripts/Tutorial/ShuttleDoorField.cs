using UnityEngine;

// The transparent oxygen force-field membrane across the shuttle's exit
// doorway — revealed when the exit door folds down into the ramp; it's what
// keeps the atmosphere inside the cabin. Visuals come from the bubble-dome kit
// (Resources/DomeFX/DomeShield.mat on a doorway-sized quad, assigned in the
// prefab); this component adds the dome membrane whoosh when the player passes
// through, matching DomeAudio's boundary-cross stinger (same clip, 2D, same
// default volume). Trigger-based, with a cooldown so a multi-collider player
// fires once per crossing.
public class ShuttleDoorField : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)] float crossVolume = 0.7f;

    AudioClip _clip;
    AudioSource _src;
    float _lastPlayAt = -10f;

    void Awake()
    {
        _clip = Resources.Load<AudioClip>("DomeFX/dome_enter");   // same whoosh as the domes
        _src = gameObject.AddComponent<AudioSource>();
        _src.playOnAwake = false;
        _src.spatialBlend = 0f;
    }

    void OnTriggerEnter(Collider other)
    {
        if (_clip == null) return;
        if (other.GetComponentInParent<PlayerController>() == null) return;
        if (Time.unscaledTime - _lastPlayAt < 0.6f) return;
        _lastPlayAt = Time.unscaledTime;
        _src.PlayOneShot(_clip, crossVolume);
    }
}
