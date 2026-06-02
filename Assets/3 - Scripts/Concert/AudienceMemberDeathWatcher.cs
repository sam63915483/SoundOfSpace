using UnityEngine;

// One-shot watcher that fires AudienceSpawner.NotifyMemberKilled() the moment
// the host audience member's AlienNPCDamageable.IsDying flips true. Lives on
// the audience-member GameObject (not on the spawner), so it ticks regardless
// of whether the spawner is enabled — kills that happen while the concert is
// in day-mode (spawner disabled) still get counted, so the member doesn't
// respawn when the concert next switches back to night-mode.
public class AudienceMemberDeathWatcher : MonoBehaviour
{
    public AudienceSpawner spawner;
    AlienNPCDamageable _dmg;
    bool _notified;

    void Start()
    {
        _dmg = GetComponent<AlienNPCDamageable>();
    }

    void Update()
    {
        if (_notified) return;
        if (_dmg == null) { _dmg = GetComponent<AlienNPCDamageable>(); if (_dmg == null) return; }
        if (_dmg.IsDying)
        {
            _notified = true;
            // Spawner may be null if the scene is tearing down. Either way,
            // we never fire twice for the same member.
            if (spawner != null) spawner.NotifyMemberKilled(gameObject);
        }
    }
}
