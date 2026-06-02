using System.Collections;
using UnityEngine;

/// <summary>
/// Brief Time.timeScale dip when an enemy dies. Duration scales by 1.2x per
/// streak tier — chained kills extend the same dip via end-time bookkeeping
/// instead of starting overlapping coroutines (which would fight each other
/// over Time.timeScale and end the slow-mo prematurely on the FIRST routine's
/// 0.45 s timer regardless of later kills).
///
/// Subscribes to KillstreakManager.OnKillRegistered (which fires AFTER the
/// streak count is incremented) rather than EnemyController.OnAnyEnemyDeath
/// directly, so we always see the post-increment count — solo kill is x1,
/// DOUBLE is x2, etc. The lazy-hook in Update handles the auto-create-order
/// race between the manager and this component.
/// </summary>
public class SlowmoOnKill : MonoBehaviour
{
    const float kBaseDuration   = 0.45f;
    const float kStackMultiplier = 1.2f;
    const float kSlowTimeScale  = 0.15f;

    float _slowmoEndTime;
    bool  _routineRunning;
    bool  _subscribed;

    void OnDisable()
    {
        var mgr = KillstreakManager.Instance;
        if (mgr != null) mgr.OnKillRegistered -= Handle;
        _subscribed = false;
    }

    void Update()
    {
        // Lazy hook — KillstreakManager may auto-create after us this scene.
        if (!_subscribed && KillstreakManager.Instance != null)
        {
            KillstreakManager.Instance.OnKillRegistered += Handle;
            _subscribed = true;
        }
    }

    void Handle(int newStreak)
    {
        var mgr = CameraEffectsManager.Instance;
        if (mgr == null || !mgr.MasterEnabled) return;
        if (mgr.Input != null && !mgr.Input.fxSlowmoOnKill) return;

        // Streak 1 (solo kill) → baseDuration; each tier above multiplies by 1.2.
        int exp = Mathf.Max(0, newStreak - 1);
        float duration = kBaseDuration * Mathf.Pow(kStackMultiplier, exp);

        float candidateEnd = Time.unscaledTime + duration;
        if (candidateEnd > _slowmoEndTime) _slowmoEndTime = candidateEnd;

        if (!_routineRunning) StartCoroutine(Routine());
    }

    IEnumerator Routine()
    {
        _routineRunning = true;
        Time.timeScale = kSlowTimeScale;
        while (Time.unscaledTime < _slowmoEndTime) yield return null;
        Time.timeScale = 1f;
        _routineRunning = false;
    }
}
