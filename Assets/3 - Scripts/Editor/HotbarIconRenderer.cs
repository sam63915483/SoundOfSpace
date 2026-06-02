using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// One-shot editor utility: renders each equippable's prefab to a transparent
// 256×256 PNG, writes to Assets/2 - Materials/HotbarIcons/, imports as a Sprite,
// and auto-wires the resulting Sprite into the controller's hotbarIcon field.
// Run via menu Tools > Generate Hotbar Icons with the gameplay scene loaded
// (the controllers live on the Player in the active scene).
public static class HotbarIconRenderer
{
    [MenuItem("Tools/Generate Hotbar Icons")]
    public static void Execute()
    {
        var water  = Object.FindObjectOfType<WaterBottleController>(true);
        var rod    = Object.FindObjectOfType<FishingRodController>(true);
        var guitar = Object.FindObjectOfType<GuitarController>(true);
        var axe    = Object.FindObjectOfType<AxeController>(true);
        var pistol = Object.FindObjectOfType<PistolController>(true);

        var dir = "Assets/2 - Materials/HotbarIcons";
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        // (controller, source-prefab, filename, rotation, zoom, frameBias) — zoom > 1
        // crops the bounds-based framing; frameBias shifts the camera look-at as a
        // normalized fraction of bounds size (negative = toward bounds.min, positive
        // = toward bounds.max), so long props can put one end in view while the
        // other bleeds off the opposite edge.
        var jobs = new List<(MonoBehaviour ctrl, GameObject prefab, string filename, Vector3 rotation, float zoom, Vector2 frameBias)>
        {
            (water,  water  != null ? water.waterBottlePrefab  : null, "water_bottle_icon.png", Vector3.zero, 1f, Vector2.zero),
            // Rod: rotated -50° on Z so it reads as a diagonal silhouette. Bias the
            // camera DOWN-LEFT in screen space so the handle (rod pivot end) sits
            // near the lower-left of the frame and the tip extends off the upper-right.
            (rod,    rod    != null ? rod.fishingRodPrefab     : null, "fishing_rod_icon.png", new Vector3(0f, 0f, -50f), 2.0f, new Vector2(-0.20f, -0.20f)),
            (guitar, guitar != null ? guitar.guitarPrefab      : null, "guitar_icon.png", Vector3.zero, 1f, Vector2.zero),
            (axe,    axe    != null ? axe.axePrefab            : null, "axe_icon.png", Vector3.zero, 1f, Vector2.zero),
            (pistol, pistol != null ? pistol.pistolPrefab      : null, "pistol_icon.png", Vector3.zero, 1f, Vector2.zero),
        };

        int rendered = 0;
        foreach (var job in jobs)
        {
            if (job.prefab == null)
            {
                Debug.LogWarning($"[HotbarIconRenderer] Skipping {job.filename} — controller or prefab missing in scene.");
                continue;
            }
            string path = $"{dir}/{job.filename}";
            if (!RenderPrefabToFile(job.prefab, path, 256, job.rotation, job.zoom, job.frameBias))
            {
                Debug.LogWarning($"[HotbarIconRenderer] Failed to render {job.filename}");
                continue;
            }
            rendered++;

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }

            // Auto-wire to controller.hotbarIcon — covers both scene-only refs
            // and prefab-instance overrides (PrefabUtility records the modification).
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null && job.ctrl != null)
            {
                var so = new SerializedObject(job.ctrl);
                var prop = so.FindProperty("hotbarIcon");
                if (prop != null)
                {
                    prop.objectReferenceValue = sprite;
                    so.ApplyModifiedProperties();

                    if (PrefabUtility.IsPartOfPrefabInstance(job.ctrl))
                        PrefabUtility.RecordPrefabInstancePropertyModifications(job.ctrl);
                }
                EditorUtility.SetDirty(job.ctrl);
            }
        }

        if (UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isDirty)
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[HotbarIconRenderer] Done. Rendered {rendered}/{jobs.Count} icons.");
    }

    static bool RenderPrefabToFile(GameObject prefab, string outPath, int size, Vector3 eulerOverride, float zoom, Vector2 frameBias)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        if (instance == null) return false;
        instance.hideFlags = HideFlags.HideAndDontSave;

        // Park the instance far from world origin so it doesn't interact with
        // anything in the open gameplay scene.
        var stage = new Vector3(10000f, 10000f, 10000f);
        instance.transform.position = stage;
        instance.transform.rotation = Quaternion.Euler(eulerOverride);

        // Combined renderer bounds for framing.
        var renderers = instance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Object.DestroyImmediate(instance);
            return false;
        }
        Bounds bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);

        var camGO = new GameObject("__IconCam") { hideFlags = HideFlags.HideAndDontSave };
        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.orthographic = true;
        float maxDim = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, 0.001f);
        float zoomFactor = (zoom > 0f) ? zoom : 1f;
        cam.orthographicSize = (maxDim * 0.55f) / zoomFactor;
        // Three-quarter angle: front-right, slightly above. Reads nicer than dead-on.
        var camOffset = new Vector3(0.7f, 0.45f, -1f).normalized * (maxDim * 2.5f);
        // frameBias shifts the look-at as a fraction of bounds size — useful for
        // long props (rod) where we want one end in the frame and the other off.
        var lookAt = bounds.center + new Vector3(frameBias.x * bounds.size.x, frameBias.y * bounds.size.y, 0f);
        cam.transform.position = lookAt + camOffset;
        cam.transform.LookAt(lookAt);
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = maxDim * 20f;
        cam.allowMSAA = true;
        cam.cullingMask = ~0;

        var keyLightGO = new GameObject("__IconKeyLight") { hideFlags = HideFlags.HideAndDontSave };
        var keyLight = keyLightGO.AddComponent<Light>();
        keyLight.type = LightType.Directional;
        keyLight.intensity = 1.1f;
        keyLight.color = Color.white;
        keyLight.transform.rotation = Quaternion.Euler(35f, -30f, 0f);

        var fillLightGO = new GameObject("__IconFillLight") { hideFlags = HideFlags.HideAndDontSave };
        var fillLight = fillLightGO.AddComponent<Light>();
        fillLight.type = LightType.Directional;
        fillLight.intensity = 0.55f;
        fillLight.color = new Color(0.7f, 0.85f, 1f);
        fillLight.transform.rotation = Quaternion.Euler(-20f, 150f, 0f);

        var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 8;
        cam.targetTexture = rt;
        cam.Render();

        var prevActive = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
        tex.Apply();
        RenderTexture.active = prevActive;

        try
        {
            File.WriteAllBytes(outPath, tex.EncodeToPNG());
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[HotbarIconRenderer] Failed to write {outPath}: {ex.Message}");
            cam.targetTexture = null;
            Object.DestroyImmediate(instance);
            Object.DestroyImmediate(camGO);
            Object.DestroyImmediate(keyLightGO);
            Object.DestroyImmediate(fillLightGO);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            return false;
        }

        cam.targetTexture = null;
        Object.DestroyImmediate(instance);
        Object.DestroyImmediate(camGO);
        Object.DestroyImmediate(keyLightGO);
        Object.DestroyImmediate(fillLightGO);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);

        return true;
    }
}
