using System.Collections;
using UnityEngine;

// Stasis-pod arrival cinematic. Plays on a fresh New Game BEFORE the cabin
// wake-up (see IntroSequenceController). Scene-placed (NOT an auto-singleton):
// drop one on an object in the gameplay scene and wire it to
// IntroSequenceController._podArrival.
//
// Reuses the real player camera (it carries the atmosphere/planet post-effects),
// reparenting it under a runtime-built pod rig that flies a scripted path toward
// Humble Abode. Restores the camera + PlayerController on impact/skip/abort.
//
// Design: docs/superpowers/specs/2026-06-18-stasis-pod-arrival-intro-design.md
public class PodArrivalSequence : MonoBehaviour
{
    // Entry point, called by IntroSequenceController.Start() while the black
    // overlay is up. No-op skeleton for now — fleshed out in later tasks.
    public IEnumerator Play()
    {
        yield break;
    }
}
