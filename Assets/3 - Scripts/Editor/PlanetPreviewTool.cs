#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor-only convenience: show the REAL procedural terrain of a celestial body in
/// the Scene view at edit time, so surface placement / tweaking doesn't require play
/// mode.
///
/// Why this is needed: the bodies in the scene only carry a <see cref="BodyPlaceholder"/>
/// (a crude smooth sphere). The detailed terrain is built at runtime by
/// <see cref="SolarSystemSpawner"/>, which reads each placeholder's bodySettings, deletes
/// the placeholder, and adds a <see cref="CelestialBodyGenerator"/> child scaled to the
/// body radius. This tool replicates exactly that — in edit mode, non-destructively
/// (the placeholder is hidden, not deleted) — and triggers the generator's built-in
/// [ExecuteInEditMode] generation.
///
/// IMPORTANT: this does NOT modify any generation/shading code (trap #2). It only USES
/// the same path the game uses at runtime.
///
/// Safety — the preview can never end up in the saved scene:
///   • Preview objects are flagged <see cref="HideFlags.DontSave"/> (recursively), so
///     Unity excludes them from scene serialization.
///   • A <see cref="EditorSceneManager.sceneSaving"/> hook re-asserts that flag on the
///     whole preview branch right before any save.
///   • "Clear" fully removes the preview and restores the placeholders.
///
/// Note: while previewing, the scene may show as "modified" — that's just the transient
/// preview objects; they are not written to disk.
/// </summary>
[InitializeOnLoad]
public static class PlanetPreviewTool
{
    const string PreviewName = "Body Generator (Preview)";

    static PlanetPreviewTool()
    {
        EditorSceneManager.sceneSaving -= OnSceneSaving;
        EditorSceneManager.sceneSaving += OnSceneSaving;
    }

    // Guarantee a saved scene is always in a clean, runtime-safe state: no preview object
    // serialised in, and every placeholder ACTIVE + visible (a deactivated placeholder breaks
    // SolarSystemSpawner's GetComponentInChildren and makes the player fall through terrain).
    static void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path)
    {
        MarkPreviewsDontSave();
        RestoreAllPlaceholders();
    }

    // ── menu ──────────────────────────────────────────────────────────────

    [MenuItem("Tools/Planet Preview/Show Selected Body")]
    static void ShowSelected()
    {
        var body = FindSelectedBody();
        if (body == null)
        {
            EditorUtility.DisplayDialog(
                "Planet Preview",
                "Select a celestial body (or any of its children) in the Hierarchy first — e.g.\n\n" +
                "--- Celestial ---/Body Simulation/Humble Abode",
                "OK");
            return;
        }
        ShowForBody(body);
    }

    [MenuItem("Tools/Planet Preview/Show All Bodies")]
    static void ShowAll()
    {
        int n = 0;
        foreach (var body in Object.FindObjectsOfType<CelestialBody>(true))
        {
            if (body.bodyType == CelestialBody.BodyType.Sun) continue;
            if (ShowForBody(body)) n++;
        }
        Debug.Log($"[PlanetPreview] Generated terrain preview for {n} bodies. Use Tools ▸ Planet Preview ▸ Clear when done (saving also keeps the file clean automatically).");
    }

    [MenuItem("Tools/Planet Preview/Clear")]
    static void ClearMenu() => ClearAll(silent: false);

    // ── core ──────────────────────────────────────────────────────────────

    static CelestialBody FindSelectedBody()
    {
        var go = Selection.activeGameObject;
        return go != null ? go.GetComponentInParent<CelestialBody>() : null;
    }

    static bool ShowForBody(CelestialBody body)
    {
        if (body == null || body.bodyType == CelestialBody.BodyType.Sun) return false;

        // Re-showing: drop the existing preview generator for this body first.
        var existing = body.GetComponentInChildren<CelestialBodyGenerator>(true);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);

        var placeholder = body.GetComponentInChildren<BodyPlaceholder>(true);
        if (placeholder == null || placeholder.bodySettings == null)
        {
            Debug.LogWarning($"[PlanetPreview] '{body.bodyName}' has no BodyPlaceholder with bodySettings — can't preview it.");
            return false;
        }

        var spawner = Object.FindObjectOfType<SolarSystemSpawner>();
        if (spawner == null || spawner.resolutionSettings == null)
        {
            Debug.LogWarning("[PlanetPreview] No SolarSystemSpawner with resolutionSettings found in the scene — can't determine terrain resolution.");
            return false;
        }

        // Hide the crude placeholder sphere so it doesn't z-fight the real terrain — by
        // disabling its RENDERERS only. NEVER deactivate the GameObject: SolarSystemSpawner
        // finds it via GetComponentInChildren (which skips inactive objects), so a deactivated
        // placeholder makes terrain generation fail at runtime and the player falls through.
        foreach (var r in placeholder.GetComponentsInChildren<Renderer>(true))
            r.enabled = false;

        // Replicate SolarSystemSpawner.Spawn(), but WITHOUT destroying the placeholder.
        var holder = new GameObject(PreviewName);
        holder.hideFlags = HideFlags.DontSave;            // never serialised into the scene
        var generator = holder.AddComponent<CelestialBodyGenerator>();
        var t = holder.transform;
        t.parent = body.transform;
        holder.layer = body.gameObject.layer;
        t.localRotation = Quaternion.identity;
        t.localPosition = Vector3.zero;
        t.localScale = Vector3.one * body.radius;
        generator.resolutionSettings = spawner.resolutionSettings;
        generator.body = placeholder.bodySettings;
        generator.previewMode = CelestialBodyGenerator.PreviewMode.LOD0;

        // Kick the generator's edit-mode generation (runs on its next [ExecuteInEditMode] tick).
        generator.OnShapeSettingChanged();
        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();

        // Once the generator has spawned its "Terrain Mesh" child, make sure that's
        // DontSave too.
        EditorApplication.delayCall += MarkPreviewsDontSave;

        Debug.Log($"[PlanetPreview] Showing terrain for '{body.bodyName}'. (Clear when done — saving auto-keeps the file clean.)");
        return true;
    }

    static void ClearAll(bool silent)
    {
        if (Application.isPlaying) return;   // at runtime these are the REAL generators — never touch them

        int removed = 0;
        foreach (var gen in Object.FindObjectsOfType<CelestialBodyGenerator>(true))
        {
            Object.DestroyImmediate(gen.gameObject);
            removed++;
        }
        // Restore the placeholders we hid (and defensively reactivate any that an older
        // version of this tool may have left deactivated).
        RestoreAllPlaceholders();

        if (!silent)
        {
            SceneView.RepaintAll();
            Debug.Log($"[PlanetPreview] Cleared {removed} preview(s); placeholders restored.");
        }
    }

    // Flag every preview generator + all its descendants DontSave so none of it is
    // written to the scene file. Idempotent; safe to call repeatedly.
    static void MarkPreviewsDontSave()
    {
        if (Application.isPlaying) return;
        foreach (var gen in Object.FindObjectsOfType<CelestialBodyGenerator>(true))
            SetDontSaveRecursive(gen.transform);
    }

    // Ensure every placeholder is ACTIVE and its renderers enabled — the authored, runtime-safe
    // state. A deactivated placeholder breaks SolarSystemSpawner (GetComponentInChildren skips
    // inactive) and the player falls through the planet, so we never leave one off.
    static void RestoreAllPlaceholders()
    {
        if (Application.isPlaying) return;
        foreach (var ph in Object.FindObjectsOfType<BodyPlaceholder>(true))
        {
            if (!ph.gameObject.activeSelf) ph.gameObject.SetActive(true);
            foreach (var r in ph.GetComponentsInChildren<Renderer>(true))
                r.enabled = true;
        }
    }

    static void SetDontSaveRecursive(Transform t)
    {
        if (t == null) return;
        t.gameObject.hideFlags = HideFlags.DontSave;
        for (int i = 0; i < t.childCount; i++)
            SetDontSaveRecursive(t.GetChild(i));
    }
}
#endif
