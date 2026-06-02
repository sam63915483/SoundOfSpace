using System.Collections.Generic;
using UnityEngine;

// Per-torch protection aura. Mirrors the LebronLight pattern but multi-instance:
// every placed torch registers itself in a static list, so the spawner can ask
// "is this candidate spawn point inside ANY torch's radius?" with a single
// static call, and each torch independently damages enemies inside its own
// radius each frame.
//
// Add this component to the Torch prefab (Assets/1 - samsPrefabs/Props/Torch.prefab).
// Every torch placed via the build menu — and every torch restored from a save —
// will then automatically have an aura, with no per-instance setup.
public class TorchAura : MonoBehaviour
{
    [Header("Aura")]
    [Tooltip("Radius in metres. Enemies cannot spawn inside this radius, and any enemy that enters takes damage per second.")]
    public float radius = 15f;

    [Tooltip("Damage applied per second to enemies inside the radius. Set to 0 to make the torch only block spawns without damaging.")]
    public float damagePerSecond = 20f;

    static readonly List<TorchAura> s_active = new List<TorchAura>();

    void OnEnable()  { if (!s_active.Contains(this)) s_active.Add(this); }
    void OnDisable() { s_active.Remove(this); }

    void Update()
    {
        if (damagePerSecond <= 0f) return;
        var enemies = EnemyController.ActiveEnemies;
        if (enemies == null || enemies.Count == 0) return;

        Vector3 myPos = transform.position;
        float r2 = radius * radius;
        float dmg = damagePerSecond * Time.deltaTime;

        // Iterate backwards so an enemy dying mid-iteration (TakeDamage may
        // destroy / unregister) doesn't shift the list under our feet.
        for (int i = enemies.Count - 1; i >= 0; i--)
        {
            var e = enemies[i];
            if (e == null) continue;
            if ((e.transform.position - myPos).sqrMagnitude > r2) continue;
            e.TakeDamage(dmg, creditPlayer: false);
        }
    }

    // True if the world position is inside ANY active torch's protection radius.
    // Used by EnemySpawner to reject candidate spawn points near torches.
    public static bool IsPositionProtected(Vector3 worldPos)
    {
        for (int i = 0; i < s_active.Count; i++)
        {
            var t = s_active[i];
            if (t == null) continue;
            float r2 = t.radius * t.radius;
            if ((worldPos - t.transform.position).sqrMagnitude <= r2) return true;
        }
        return false;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
