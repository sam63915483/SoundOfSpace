using UnityEditor;
using UnityEngine;

// One-shot fix-up for the DuNguyn loot box materials. The package ships with
// a URP/toon-shader-derived .mat YAML — even after the shader is swapped to
// Built-in Standard, leftover float properties (_UseEmission, _Surface, etc.)
// confuse Unity's material reconciliation. Compounding that: DiagnoseScene-
// Lighting showed the gameplay scene has ambientIntensity=0 and reflection-
// Intensity=0 (intentional — custom atmosphere shaders own the look), so
// Standard PBR materials with a metallic map have NOTHING to reflect and
// the metal pixels render black in builds. Editor prefab preview hides
// this because the prefab-preview window has its own bright HDRI.
//
// Fix: make the loot box self-emit its own colour by binding the base
// colour texture to BOTH _MainTex and _EmissionMap (with a white
// _EmissionColor at intensity 1). Standard fully supports this — the
// surface then renders as base * 1 (diffuse if any direct light hits)
// PLUS base * emission (always, regardless of lighting). Net effect:
// the loot box's full albedo texture is always visible at full
// brightness, no matter how dim the scene's ambient / reflection
// environment is. Loses PBR shine on the metal bits but is
// guaranteed identical across editor prefab-preview, editor play
// mode, and the built .exe.
//
// This menu item resets each loot box material IN PLACE (preserving the
// asset GUID so SHIP44.prefab keeps its material references). After running
// it once, rebuild the .exe.
public static class FixLootBoxMaterials
{
    static readonly string[] MaterialPaths =
    {
        "Assets/DuNguyn/Loot Box/Materials/LootBox_v01_M.mat",
        "Assets/DuNguyn/Loot Box/Materials/LootBox_v02_M.mat",
        "Assets/DuNguyn/Loot Box/Materials/LootBox_v03_M.mat",
    };

    [MenuItem("Tools/Fix/Rebuild LootBox Materials")]
    public static void Rebuild()
    {
        Shader standard = Shader.Find("Standard");
        if (standard == null)
        {
            Debug.LogError("[FixLootBox] Built-in 'Standard' shader not found.");
            return;
        }

        int fixedCount = 0;
        foreach (string path in MaterialPaths)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Debug.LogWarning($"[FixLootBox] Could not load material at '{path}' — skipping.");
                continue;
            }

            // Capture textures from current state. URP-derived mats have both
            // _MainTex and _BaseMap aliased to the same texture; either works.
            Texture albedo    = mat.GetTexture("_MainTex");
            if (albedo == null) albedo = mat.GetTexture("_BaseMap");
            Texture metallic  = mat.GetTexture("_MetallicGlossMap");
            Texture occlusion = mat.GetTexture("_OcclusionMap");
            Texture emission  = mat.GetTexture("_EmissionMap");
            Texture parallax  = mat.GetTexture("_ParallaxMap");
            Texture bumpMap   = mat.GetTexture("_BumpMap");

            // Re-assigning the shader prunes properties that don't exist on
            // Standard and forces the inspector path that synchronises the
            // keyword list against the shader's declared shader_features.
            mat.shader = standard;

            // Standard's defaults — non-metallic workflow so the material
            // never needs to sample reflection probes / cubemaps (scene has
            // reflectionIntensity=0). _Glossiness stays low so any specular
            // highlight that DOES occur from a direct light isn't a hard
            // glare. _GlossyReflections=0 belt-and-braces: even if a probe
            // existed, we don't sample it.
            mat.SetColor("_Color",            Color.white);
            mat.SetFloat("_Metallic",         0f);
            mat.SetFloat("_Glossiness",       0.5f);
            mat.SetFloat("_GlossMapScale",    1f);
            mat.SetFloat("_BumpScale",        1f);
            mat.SetFloat("_OcclusionStrength", 1f);
            mat.SetFloat("_Parallax",         0.02f);
            mat.SetFloat("_Cutoff",           0.5f);
            mat.SetFloat("_Mode",             0f);  // Opaque
            mat.SetFloat("_SrcBlend",         1f);
            mat.SetFloat("_DstBlend",         0f);
            mat.SetFloat("_ZWrite",           1f);
            mat.SetFloat("_SmoothnessTextureChannel", 0f);
            mat.SetFloat("_SpecularHighlights",       1f);
            mat.SetFloat("_GlossyReflections",        0f);

            // Re-bind textures. Two key tricks combined:
            //   1. Albedo texture also bound to _EmissionMap with white
            //      _EmissionColor — gives the surface a constant self-lit
            //      brightness equal to its base colour, independent of
            //      scene ambient/reflection. Metallic pixels can no longer
            //      go black because emission adds base colour on top.
            //   2. Metallic-gloss map IS re-bound — now safe because the
            //      "black metal in zero-reflection scene" failure is masked
            //      by (1). The metallic workflow still produces specular
            //      highlights from direct lights (the Sun), restoring the
            //      lock/hinge shine.
            if (albedo    != null) mat.SetTexture("_MainTex",          albedo);
            if (bumpMap   != null) mat.SetTexture("_BumpMap",          bumpMap);
            if (metallic  != null) mat.SetTexture("_MetallicGlossMap", metallic);
            if (occlusion != null) mat.SetTexture("_OcclusionMap",     occlusion);
            if (albedo    != null) mat.SetTexture("_EmissionMap",      albedo); // self-emit
            if (parallax  != null) mat.SetTexture("_ParallaxMap",      parallax);

            // Keyword bookkeeping. _METALLICGLOSSMAP enabled if the texture
            // is present (specular shine from direct Sun light). _EMISSION
            // always on (we bound the albedo as emission texture).
            // _PARALLAXMAP / _NORMALMAP track their respective textures.
            // Occlusion auto-samples without a keyword.
            if (metallic != null) mat.EnableKeyword("_METALLICGLOSSMAP");
            else                  mat.DisableKeyword("_METALLICGLOSSMAP");

            mat.EnableKeyword("_EMISSION");
            // 0.5 grey emission tint = 50% less vibrant than white. The
            // surface still self-lights its base texture (so no black
            // metal pixels) but reads less neon against the ship's dark
            // interior. Bump back toward 1.0 if it feels too dim.
            mat.SetColor("_EmissionColor", new Color(0.5f, 0.5f, 0.5f, 1f));
            // RealtimeEmissive (not EmissiveIsBlack) — with EmissiveIsBlack
            // Unity strips the _EMISSION keyword on reimport when the
            // emission colour is HDR, killing the self-lit effect.
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            if (parallax != null) mat.EnableKeyword("_PARALLAXMAP");
            else                  mat.DisableKeyword("_PARALLAXMAP");

            if (bumpMap != null)  mat.EnableKeyword("_NORMALMAP");
            else                  mat.DisableKeyword("_NORMALMAP");

            // emission variable retained only for the log line below
            _ = emission;

            mat.renderQueue = -1; // From shader

            EditorUtility.SetDirty(mat);
            fixedCount++;
            Debug.Log($"[FixLootBox] Rebuilt '{path}' — albedo={Name(albedo)}, metallic={Name(metallic)}, occlusion={Name(occlusion)}, emission={Name(emission)}, parallax={Name(parallax)}.");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[FixLootBox] DONE — rebuilt {fixedCount}/{MaterialPaths.Length} loot box material(s). Rebuild the .exe to test.");
    }

    static string Name(Texture t) => t == null ? "(none)" : t.name;

    // ─── Reactor-themed variant ───────────────────────────────────────
    // Creates a copy of SM_LootBox_03.prefab + its material, retinted with
    // the ship reactor's electric-blue + red palette (per ReactorGlow.cs:
    // glowColor = (0.15, 0.45, 1.0), redColor = (1.0, 0.15, 0.1)). The
    // user wanted a variant whose colours match the ship interior instead
    // of the warm wooden look of the base loot box.

    const string SrcPrefab   = "Assets/DuNguyn/Loot Box/Prefabs/SM_LootBox_03.prefab";
    const string SrcMaterial = "Assets/DuNguyn/Loot Box/Materials/LootBox_v03_M.mat";
    const string DstPrefab   = "Assets/DuNguyn/Loot Box/Prefabs/SM_LootBox_ReactorBlue.prefab";
    const string DstMaterial = "Assets/DuNguyn/Loot Box/Materials/LootBox_ReactorBlue_M.mat";

    // Reactor-blue palette for the loot box variant.
    //
    //   BaseTint      — multiplied with the albedo texture. Near-black
    //                   navy crushes the wooden warm hue down to grey-
    //                   black while preserving brightness variation.
    //   EmissionTint  — the visible glow colour. ReactorBlue from
    //                   ReactorGlow.cs — matches the live reactor's
    //                   idle glow exactly.
    //   EmissionBoost — HDR multiplier on EmissionTint. Tuned for
    //                   visibility in the scene (ambient=0,
    //                   reflection=0) so the surface reads as
    //                   self-lit, around the brightness of the
    //                   surrounding ship hull.
    // Near-black albedo — the texture's saturated cool variation is
    // suppressed almost entirely so direct-light contribution is dim
    // and uniform, letting the emission contribution drive the look.
    static readonly Color BaseTint      = new Color(0.14f, 0.14f, 0.14f, 1f);
    // Warm amber emission tint × low boost = subtle copper glow that
    // matches the ship interior's warm (1, 0.83, 0.27) lighting. The
    // texture's brightness pattern still reads through the emission
    // map binding so the box isn't a uniform slab, but every pixel is
    // forced into the warm hue family.
    static readonly Color EmissionTint  = new Color(1.0f, 0.6f, 0.25f, 1f);
    const  float EmissionBoost = 0.32f;

    [MenuItem("Tools/Storage/Create Reactor-Themed LootBox Variant")]
    public static void CreateReactorVariant()
    {
        Shader standard = Shader.Find("Standard");
        if (standard == null)
        {
            Debug.LogError("[ReactorVariant] Standard shader not found.");
            return;
        }

        // 1. Copy the source material to a new file so the variant prefab
        //    has its own mat to retint without touching the original.
        if (AssetDatabase.LoadAssetAtPath<Material>(DstMaterial) != null)
        {
            // Already exists — delete and recreate so a re-run gives a
            // fresh material (in case the user tweaked the tint in code
            // and wants to refresh).
            AssetDatabase.DeleteAsset(DstMaterial);
        }
        if (!AssetDatabase.CopyAsset(SrcMaterial, DstMaterial))
        {
            Debug.LogError($"[ReactorVariant] Failed to copy {SrcMaterial} -> {DstMaterial}.");
            return;
        }

        // 2. Retint the new material.
        Material newMat = AssetDatabase.LoadAssetAtPath<Material>(DstMaterial);
        if (newMat == null) { Debug.LogError("[ReactorVariant] Copied material would not load."); return; }

        // Recolour-with-detail approach. Keep the albedo + occlusion +
        // (optional) parallax textures so the loot box's MESH DETAIL
        // (planks, lock outlines, decorative trim, AO crevices) still
        // reads. But crush the wooden HUE by multiplying the albedo
        // with near-black navy — bright spots in the texture become
        // dark navy, dark spots become near-pure-black. Then the
        // EMISSION pass re-colours those same brightness variations
        // in bright reactor blue, so the texture pattern reads as
        // glowing electric blue against a near-black navy hull.
        //
        // Drop the metallic map — in a zero-reflection scene metallic
        // pixels go matte regardless, so it's just dead weight that
        // could complicate the look.
        Texture albedo   = newMat.GetTexture("_MainTex");
        if (albedo == null) albedo = newMat.GetTexture("_BaseMap");
        Texture occlusion = newMat.GetTexture("_OcclusionMap");
        Texture parallax  = newMat.GetTexture("_ParallaxMap");

        newMat.shader = standard;
        newMat.SetTexture("_MainTex",          albedo);     // keep — pattern detail
        newMat.SetTexture("_BumpMap",          null);
        newMat.SetTexture("_MetallicGlossMap", null);       // strip — useless in zero-reflection
        newMat.SetTexture("_OcclusionMap",     occlusion);  // keep — AO depth
        newMat.SetTexture("_EmissionMap",      albedo);     // self-emit the SAME pattern
        newMat.SetTexture("_ParallaxMap",      parallax);   // keep if it was there

        // Heavy navy multiplier — kills wooden warmth, keeps the
        // texture's brightness pattern so trim/lock outlines still read.
        newMat.SetColor("_Color",          BaseTint);
        newMat.SetFloat("_Metallic",       0f);
        newMat.SetFloat("_Glossiness",     0.5f);
        newMat.SetFloat("_GlossMapScale",  1f);
        newMat.SetFloat("_BumpScale",      1f);
        newMat.SetFloat("_OcclusionStrength", 1f);
        newMat.SetFloat("_Parallax",       0.02f);
        newMat.SetFloat("_Cutoff",         0.5f);
        newMat.SetFloat("_Mode",           0f);
        newMat.SetFloat("_SrcBlend",       1f);
        newMat.SetFloat("_DstBlend",       0f);
        newMat.SetFloat("_ZWrite",         1f);
        newMat.SetFloat("_SmoothnessTextureChannel", 0f);
        newMat.SetFloat("_SpecularHighlights",       1f);
        newMat.SetFloat("_GlossyReflections",        0f);

        newMat.DisableKeyword("_METALLICGLOSSMAP");
        newMat.DisableKeyword("_NORMALMAP");
        if (parallax != null) newMat.EnableKeyword("_PARALLAXMAP");
        else                  newMat.DisableKeyword("_PARALLAXMAP");

        // Emission re-colours the texture's brightness variation in
        // bright reactor blue. albedoBrightness × ReactorBlue × HDR
        // gives a glowing-blue pattern where the wooden grain used to
        // be — looks like illuminated circuit etching on a navy panel.
        newMat.EnableKeyword("_EMISSION");
        newMat.SetColor("_EmissionColor", EmissionTint * EmissionBoost);
        // RealtimeEmissive (not EmissiveIsBlack) — with EmissiveIsBlack
        // Unity strips the _EMISSION keyword on reimport when the
        // emission colour is HDR, killing the self-lit effect.
        newMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(newMat);

        // 3. Copy the source prefab to a new file so the variant has its
        //    own asset that can sit alongside SM_LootBox_03 in the prefabs
        //    folder, available for the user to drop in scenes.
        if (AssetDatabase.LoadAssetAtPath<GameObject>(DstPrefab) != null)
            AssetDatabase.DeleteAsset(DstPrefab);
        if (!AssetDatabase.CopyAsset(SrcPrefab, DstPrefab))
        {
            Debug.LogError($"[ReactorVariant] Failed to copy {SrcPrefab} -> {DstPrefab}.");
            return;
        }

        // 4. Load the new prefab's contents, swap every renderer's
        //    material reference to the new tinted material, then save the
        //    prefab back. EditPrefabContentsScope handles the open/save
        //    cleanly without dirtying the project.
        using (var scope = new PrefabUtility.EditPrefabContentsScope(DstPrefab))
        {
            var renderers = scope.prefabContentsRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                var mats = r.sharedMaterials;
                bool any = false;
                for (int j = 0; j < mats.Length; j++)
                {
                    // Replace whichever slot referenced the source material.
                    if (mats[j] != null && AssetDatabase.GetAssetPath(mats[j]) == SrcMaterial)
                    {
                        mats[j] = newMat;
                        any = true;
                    }
                }
                if (any) r.sharedMaterials = mats;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[ReactorVariant] Created {DstPrefab} (material {DstMaterial}).");
    }

    // ─── Swap the active loot box's material to the reactor-blue one ──
    // The LootBox attached to SHIP44 is SM_LootBox_03 — its BoxId
    // ("OriginalShip/SM_LootBox_03") is baked into save files via
    // StorageSave.boxId. Swapping the WHOLE prefab would change the
    // hierarchy path and orphan existing saves; swapping only the
    // material on the existing renderer preserves the BoxId and all
    // saved storage contents while changing the visible look to the
    // reactor-blue variant.

    const string Ship44Prefab = "Assets/1 - samsPrefabs/SHIP44.prefab";

    [MenuItem("Tools/Storage/Apply Reactor-Blue Material To Ship Loot Box")]
    public static void ApplyReactorBlueToShipLootBox()
    {
        Material reactorMat = AssetDatabase.LoadAssetAtPath<Material>(DstMaterial);
        if (reactorMat == null)
        {
            Debug.LogError($"[SwapMat] Reactor-blue material missing at {DstMaterial}. Run 'Create Reactor-Themed LootBox Variant' first.");
            return;
        }

        int swapped = 0;
        using (var scope = new PrefabUtility.EditPrefabContentsScope(Ship44Prefab))
        {
            // Walk the prefab to find the LootBox component. There's only
            // one (it's the SM_LootBox_03 child under SHIP44 root). Then
            // swap every material slot on its MeshRenderer.
            var lootBoxes = scope.prefabContentsRoot.GetComponentsInChildren<LootBox>(true);
            for (int i = 0; i < lootBoxes.Length; i++)
            {
                var renderers = lootBoxes[i].GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                {
                    var mats = renderers[r].sharedMaterials;
                    for (int m = 0; m < mats.Length; m++)
                    {
                        mats[m] = reactorMat;
                        swapped++;
                    }
                    renderers[r].sharedMaterials = mats;
                }
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[SwapMat] Swapped {swapped} material slot(s) on SHIP44's LootBox to {DstMaterial}.");
    }
}
