using System.Collections;
using UnityEngine;

// Aligns each planet's "Water"-tagged trigger sphere to the generator's
// VISIBLE ocean radius (2026-08-27, playtest 11: Icey Twin's hand-sized water
// trigger sat higher than the visible ice sea, so dry land counted as water —
// swim physics on solid ground). The visible ocean is the source of truth
// (CelestialBodyGenerator.GetOceanRadius — read-only call into the protected
// zone, never an edit); the trigger is scene data that can drift from it.
//
// Runs a pass shortly after every gameplay-scene load (delayed so the
// generators have built), logs every adjustment, and no-ops on planets whose
// trigger already matches. Deliberately does NOT skip MainMenu in AutoCreate,
// so it never needs seeding in EnsureGameplaySingletons (CLAUDE.md trap #1).
public class WaterlineAlign : MonoBehaviour
{
    public static WaterlineAlign Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("WaterlineAlign");
        DontDestroyOnLoad(go);
        go.AddComponent<WaterlineAlign>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        StartCoroutine(AlignSoon());
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
    {
        StartCoroutine(AlignSoon());
    }

    IEnumerator AlignSoon()
    {
        // Two passes: an early one, and a late one in case a generator builds
        // its ocean radius lazily during the first seconds.
        yield return new WaitForSeconds(1f);
        Align();
        yield return new WaitForSeconds(4f);
        Align();
    }

    static void Align()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu") return;
        foreach (var body in FindObjectsOfType<CelestialBody>())
        {
            var gen = body.GetComponentInChildren<CelestialBodyGenerator>();
            if (gen == null) continue;
            float oceanR;
            try { oceanR = gen.GetOceanRadius(); }
            catch { continue; }
            if (oceanR <= 0f) continue;

            foreach (var col in body.GetComponentsInChildren<SphereCollider>(true))
            {
                if (!col.isTrigger || !col.CompareTag("Water")) continue;
                float scale = Mathf.Max(0.0001f, Mathf.Abs(col.transform.lossyScale.x));
                float worldR = col.radius * scale;
                if (Mathf.Abs(worldR - oceanR) <= 0.5f) continue;   // already true to the water
                Debug.Log("[WaterlineAlign] " + body.bodyName + ": water trigger radius "
                    + worldR.ToString("0.0") + " -> visible ocean " + oceanR.ToString("0.0"));
                col.radius = oceanR / scale;
            }
        }
    }
}
