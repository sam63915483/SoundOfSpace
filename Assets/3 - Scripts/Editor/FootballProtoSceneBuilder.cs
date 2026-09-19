using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Builds Assets/4 - Scenes/Proto_Football.unity — the alien-football test
/// scene (docs/Handoff_AlienFootball_Phase1_v1.md). Step 0 (Sam, 2026-09-18):
/// the bought stadium pack (Assets/LargeFootballStadium) assembled the way its
/// own demo scene lays it out, with the gameplay player standing at midfield,
/// so Sam can walk the stands and judge stadium-vs-player scale before any
/// football code exists.
///
/// Tools ▸ Football ▸ Build Proto Football Scene. Modelled on
/// TutorialSceneBuilder: the scene is created ADDITIVELY (or rebuilt in place if
/// it is the open scene), populated, saved; the previously active scene is
/// restored. Re-running WIPES and rebuilds the scene. NOT added to Build
/// Settings — it is a dev scene, opened via Tools ▸ Football ▸ Open.
///
/// What's in the scene:
///   FieldRoot            empty at the origin. The handoff's rule: every football
///                        position is FieldRoot-local, "up" is FieldRoot.up. The
///                        stadium hangs under it so moving FieldRoot (onto Cyclops,
///                        one day) moves everything.
///     Stadium            the pack's prefabs at the demo scene's poses (minus the
///                        soccer goals and corner flags), ×1.2, every object on
///                        the Body layer (10) so the player can walk on the pitch,
///                        the stands and the stairs (walkableMask). The pitch's
///                        soccer stripes/lines are swapped out (ConvertPitchToFootball).
///     FieldMarkings      FootballFieldMarkings — the football paint (yard lines,
///                        numbers, arrows, mow stripes) from FootballField's dims.
///     FootballMatch      the game: spawns both squads + the ball at runtime and
///                        plays coin toss → kickoff → drives → final on its own.
///     Scoreboard         FootballScoreboard, dot-matrix; parked over the +Z end
///                        zone for Sam to move.
///     Ground             a 600 m slab a metre under it all, so walking out of
///                        the stadium never falls into the void.
///   Sun                  directional light + the pack's day-sky cubemap. Flat
///                        ambient (unlike the gameplay scene, there is no
///                        atmosphere post here to light the shade).
///   Player               TutorialPlayer.prefab (snapshot of the gameplay player)
///                        at midfield, active. No NBodySimulation in the scene →
///                        PlayerController's flat-gravity fallback (straight down,
///                        20 m/s²) and OxygenManager's "no bodies = breathable".
///   --- UI ---           Overlay Canvas ▸ Dot (the crosshair), EventSystem.
///
/// The pack is authored for URP: 38 of its materials use URP/Lit, which renders
/// MAGENTA in this Built-in project. Build converts them to Standard in place
/// (same GUIDs, so the prefabs keep their references) — also available alone as
/// Tools ▸ Football ▸ Convert Stadium Materials To Standard.
/// </summary>
public static class FootballProtoSceneBuilder
{
    const string SceneDir   = "Assets/4 - Scenes";
    public const string ScenePath = SceneDir + "/Proto_Football.unity";
    const string PackDir    = "Assets/LargeFootballStadium";
    const string PrefabDir  = PackDir + "/Prefabs";
    const string MatDir     = PackDir + "/Materials";
    const string SkyMatPath = MatDir + "/Sky_HDRI.mat";       // Skybox/Cubemap (built-in) — fine as-is
    const string GrassMatPath  = MatDir + "/GrassLightShader.mat";   // the pitch's lighter stripe — used for the WHOLE pitch
    const string GroundMatPath = SceneDir + "/Proto_Football_Ground.mat";

    const string PlayerSnapshotPath = "Assets/1 - samsPrefabs/TutorialPlayer.prefab";
    const string AlienPackDir = "Assets/5 - External Imports/Alien_Toys/Alien_Pack/Prefab/Built-In";

    // Where Sam put the scoreboard: read before the wipe, written back after.
    /// Two empties under FieldRoot with a dark placeholder slab the size of
    /// the screen, so the jumbotrons exist in the editor to be dragged and
    /// rotated (they are otherwise built at runtime). The broadcast builds the
    /// real screens on them and hides the placeholders.
    [MenuItem("Tools/Football/Add Jumbotron Anchors")]
    public static void AddJumbotronAnchors()
    {
        var fieldRoot = GameObject.Find("FieldRoot");
        if (fieldRoot == null) { Debug.LogError("[FootballProto] no FieldRoot in the open scene"); return; }
        var bc = fieldRoot.GetComponentInChildren<FootballBroadcast>(true);
        Vector2 size = bc != null ? bc.screenSize : new Vector2(40f, 22.5f);
        Vector3[] pos = { bc != null ? bc.screenAPos : new Vector3(64f, 30f, 0f), bc != null ? bc.screenBPos : new Vector3(-64f, 30f, 0f) };
        Vector3[] eul = { bc != null ? bc.screenAEuler : new Vector3(0f, 90f, 0f), bc != null ? bc.screenBEuler : new Vector3(0f, -90f, 0f) };
        string[] names = { FootballBroadcast.AnchorA, FootballBroadcast.AnchorB };
        for (int i = 0; i < 2; i++)
        {
            var t = fieldRoot.transform.Find(names[i]);
            if (t != null) continue;
            var go = new GameObject(names[i]);
            Undo.RegisterCreatedObjectUndo(go, "jumbotron anchor");
            go.transform.SetParent(fieldRoot.transform, false);
            go.transform.localPosition = pos[i]; go.transform.localRotation = Quaternion.Euler(eul[i]);
            var ph = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ph.name = "Placeholder";
            Object.DestroyImmediate(ph.GetComponent<Collider>());
            ph.transform.SetParent(go.transform, false);
            ph.transform.localScale = new Vector3(size.x + 1.2f, size.y + 1.2f, 0.8f);
            ph.GetComponent<Renderer>().sharedMaterial = LoadOrCreateMat(GroundMatPath.Replace("Ground", "Jumbotron"), m => { m.color = new Color(0.08f, 0.08f, 0.1f); m.SetFloat("_Glossiness", 0.3f); });
        }
        EditorSceneManager.MarkSceneDirty(fieldRoot.scene);
        Debug.Log("[FootballProto] jumbotron anchors under FieldRoot — drag them where you want the screens, then save the scene");
    }

    /// The gameplay HUD, from the same snapshot the tutorial box uses
    /// (Assets/1 - samsPrefabs/TutorialHUDCanvas.prefab — re-snapshot with
    /// Tools ▸ Solar System ▸ Snapshot Tutorial HUD Canvas Prefab when the
    /// gameplay HUD changes). Panels with nothing to drive them here are
    /// switched off.
    [MenuItem("Tools/Football/Add Gameplay HUD")]
    public static void AddGameplayHud()
    {
        var ui = GameObject.Find("--- UI ---");
        if (ui == null) { Debug.LogError("[FootballProto] no '--- UI ---' in the open scene"); return; }
        if (ui.transform.Find(TutorialSnapshots.HudCanvasName) != null) { Debug.Log("[FootballProto] HUD_Canvas already here"); return; }
        var hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TutorialSnapshots.HudPrefabPath);
        if (hudPrefab == null) { Debug.LogError("[FootballProto] " + TutorialSnapshots.HudPrefabPath + " missing"); return; }
        var hud = (GameObject)PrefabUtility.InstantiatePrefab(hudPrefab, ui.scene);
        Undo.RegisterCreatedObjectUndo(hud, "gameplay HUD");
        hud.name = TutorialSnapshots.HudCanvasName;
        hud.transform.SetParent(ui.transform, false);
        hud.SetActive(true);
        string[] off = { "SellPanel", "EarningsText", "DialogueText", "TalkPrompt", "CassetteText", "CookPanel", "BuildMenu", "FishCatch",
                         "PickupPromptText", "PlacePromptText", "BonfirePromptText", "GuitarChoicePanel", "GuitarDialogueText", "GuitarTalkPrompt", "CrashWarningText" };
        foreach (var name in off) { var t = hud.transform.Find(name); if (t != null) t.gameObject.SetActive(false); }
        AddHelmetHudConfig(ui);
        EditorSceneManager.MarkSceneDirty(ui.scene);
        Debug.Log("[FootballProto] HUD_Canvas added under --- UI ---");
    }

    /// The HelmetHudConfig prefab restyles the auto-created compass / boost /
    /// vitals clusters into the current look (the tutorial box has it too);
    /// without it they fall back to their old look.
    [MenuItem("Tools/Football/Add HUD Config")]
    public static void AddHudConfig()
    {
        var ui = GameObject.Find("--- UI ---");
        if (ui == null) { Debug.LogError("[FootballProto] no '--- UI ---' in the open scene"); return; }
        AddHelmetHudConfig(ui);
        EditorSceneManager.MarkSceneDirty(ui.scene);
    }

    static void AddHelmetHudConfig(GameObject ui)
    {
        var helmetCfg = AssetDatabase.LoadAssetAtPath<GameObject>(TutorialSnapshots.HelmetPrefabPath);
        if (helmetCfg == null) { Debug.LogWarning("[FootballProto] " + TutorialSnapshots.HelmetPrefabPath + " missing — run Tools ▸ Solar System ▸ Snapshot HelmetHudConfig Prefab"); return; }
        if (ui.transform.Find(helmetCfg.name) != null) { Debug.Log("[FootballProto] HelmetHudConfig already here"); return; }
        var cfg = (GameObject)PrefabUtility.InstantiatePrefab(helmetCfg, ui.scene);
        Undo.RegisterCreatedObjectUndo(cfg, "hud config");
        cfg.transform.SetParent(ui.transform, false);
        Debug.Log("[FootballProto] HelmetHudConfig added under --- UI ---");
    }

    struct BoardPose { public bool found; public Vector3 pos; public Quaternion rot; public Vector3 scale; }
    static BoardPose _boardPose;

    static BoardPose CaptureBoardPose()
    {
        var bp = new BoardPose();
        var open = SceneManager.GetSceneByPath(ScenePath);
        if (!open.IsValid() || !open.isLoaded) return bp;
        foreach (var root in open.GetRootGameObjects())
        {
            var board = root.GetComponentInChildren<FootballScoreboard>(true);
            if (board == null) continue;
            bp.found = true; bp.pos = board.transform.localPosition; bp.rot = board.transform.localRotation; bp.scale = board.transform.localScale;
            break;
        }
        return bp;
    }
    const string PlayerPrefabGuid   = "30d1ef01b1bfd4cce849c5888b9c1de4"; // Player.prefab (fallback if no snapshot)

    const int BodyLayer = 10;    // "Body" — in PlayerController.walkableMask
    const int UILayer   = 5;

    const float GroundSize = 600f;
    // Sam's walk (2026-09-18): the pack's stadium read 1.2× too small against the
    // player. Uniform scale on the Stadium node — the pieces' demo poses are its
    // local children, so they scale together.
    const float StadiumScale = 1.2f;
    // The ground slab's top. The pitch mesh spans y −0.033…0.004 and the pitchside
    // concrete base bottoms out at −0.66 (×StadiumScale) — a slab with its top at
    // y = 0 z-fought the grass ("grass and a greyer thing fighting", Sam). Keep it
    // clear under everything; the step down at the concrete's edge is fine here.
    const float GroundTop = -1.0f;

    // The pack's own demo scene (DemoScene/FootballStadium.unity), transcribed.
    // The pitch is a FIFA 105 × 68 m field with its long axis on Z. Soccer-only
    // dressing left out now that it's a football field: the goals (z = ±54.75)
    // and the four corner flags (±34, ±52.7). Dugouts kept.
    struct Piece { public string prefab; public Vector3 pos; public float yaw; public Piece(string p, float x, float y, float z, float yawDeg = 0f) { prefab = p; pos = new Vector3(x, y, z); yaw = yawDeg; } }
    static readonly Piece[] Layout =
    {
        new Piece("FootballPitchFifa5.3", 0f, 0f, 0f),
        new Piece("PitchSide",            0f, 0f, 0f),
        new Piece("PitchSideLEDBanner",   0f, 0.0816f, 0f),
        new Piece("UpperLevels",          0f, 0f, 0f),
        new Piece("UpperLevelsLEDBanner", 0f, 0f, 0f),
        new Piece("Roof",                 0f, 0f, 0f),
        new Piece("DugoutLong",  -41.84f, 0f,  12.41f),
        new Piece("DugoutShort", -41.84f, 0f,  -5.34f),
        new Piece("DugoutLong",  -41.84f, 0f, -13.15f),
    };

    // ── materials ───────────────────────────────────────────────────────────

    /// URP/Lit → Standard, in place. Property names differ (URP _BaseMap /
    /// _BaseColor / _Smoothness vs Standard _MainTex / _Color / _Glossiness);
    /// the normal, metallic, occlusion and emission maps share names. Surface
    /// type: URP _Surface 1 = transparent → Standard Transparent mode (queue
    /// 3000 — the glass you look THROUGH rule, CLAUDE.md); _AlphaClip 1 →
    /// Cutout. Skips materials already on a built-in shader.
    [MenuItem("Tools/Football/Convert Stadium Materials To Standard")]
    public static void ConvertMaterials()
    {
        int n = ConvertMaterialsInternal();
        Debug.Log("[FootballProto] Converted " + n + " stadium material(s) to Standard.");
    }

    static int ConvertMaterialsInternal()
    {
        var standard = Shader.Find("Standard");
        if (standard == null) { Debug.LogError("[FootballProto] Built-in 'Standard' shader not found."); return 0; }
        int converted = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { MatDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null) continue;
            // Missing URP shader shows up as the error shader; anything else that
            // isn't Standard (the two skybox mats) is left alone.
            bool broken = mat.shader.name == "Hidden/InternalErrorShader" || mat.shader.name.StartsWith("Universal Render Pipeline");
            if (!broken) continue;

            // Read the URP values off the SAVED properties (SerializedObject),
            // not Material.Get*/HasProperty: with the URP shader missing the
            // material sits on Hidden/InternalErrorShader, which declares none of
            // them, so HasProperty is false for everything and the glass and goal
            // nets would come out opaque white.
            var so = new SerializedObject(mat);
            Texture albedo    = SavedTex(so, "_BaseMap", out Vector2 tiling, out Vector2 offset);
            Color   baseColor = SavedColor(so, "_BaseColor", Color.white);
            Texture bump      = SavedTex(so, "_BumpMap", out _, out _);
            Texture metallic  = SavedTex(so, "_MetallicGlossMap", out _, out _);
            Texture occlusion = SavedTex(so, "_OcclusionMap", out _, out _);
            Texture emission  = SavedTex(so, "_EmissionMap", out _, out _);
            Color   emitColor = SavedColor(so, "_EmissionColor", Color.black);
            float smooth      = SavedFloat(so, "_Smoothness", 0.5f);
            float metal       = SavedFloat(so, "_Metallic", 0f);
            float bumpScale   = SavedFloat(so, "_BumpScale", 1f);
            float cutoff      = SavedFloat(so, "_Cutoff", 0.5f);
            bool  transparent = SavedFloat(so, "_Surface", 0f) > 0.5f;
            bool  cutout      = !transparent && SavedFloat(so, "_AlphaClip", 0f) > 0.5f;

            mat.shader = standard;
            mat.SetTexture("_MainTex", albedo);
            mat.SetTextureScale("_MainTex", tiling);
            mat.SetTextureOffset("_MainTex", offset);
            mat.SetColor("_Color", baseColor);
            mat.SetTexture("_BumpMap", bump);
            mat.SetFloat("_BumpScale", bumpScale);
            mat.SetTexture("_MetallicGlossMap", metallic);
            mat.SetTexture("_OcclusionMap", occlusion);
            mat.SetTexture("_EmissionMap", emission);
            mat.SetColor("_EmissionColor", emitColor);
            mat.SetFloat("_Metallic", metal);
            mat.SetFloat("_Glossiness", smooth);
            mat.SetFloat("_GlossMapScale", smooth);
            mat.SetFloat("_Cutoff", cutoff);
            mat.SetFloat("_SmoothnessTextureChannel", 0f);

            SetKeyword(mat, "_NORMALMAP", bump != null);
            SetKeyword(mat, "_METALLICGLOSSMAP", metallic != null);
            bool emits = emission != null || emitColor.maxColorComponent > 0.001f;
            SetKeyword(mat, "_EMISSION", emits);
            mat.globalIlluminationFlags = emits ? MaterialGlobalIlluminationFlags.RealtimeEmissive
                                                : MaterialGlobalIlluminationFlags.EmissiveIsBlack;

            if (transparent)      SetStandardMode(mat, 3);   // Transparent — queue 3000
            else if (cutout)      SetStandardMode(mat, 1);   // Cutout
            else                  SetStandardMode(mat, 0);   // Opaque

            EditorUtility.SetDirty(mat);
            converted++;
        }
        if (converted > 0) AssetDatabase.SaveAssets();
        return converted;
    }

    // ── saved-property readers (work whatever shader the material is on) ────

    static SerializedProperty FindSaved(SerializedObject so, string list, string name)
    {
        var arr = so.FindProperty("m_SavedProperties." + list);
        if (arr == null) return null;
        for (int i = 0; i < arr.arraySize; i++)
        {
            var el = arr.GetArrayElementAtIndex(i);
            if (el.FindPropertyRelative("first").stringValue == name) return el.FindPropertyRelative("second");
        }
        return null;
    }

    static float SavedFloat(SerializedObject so, string name, float fallback)
    {
        var p = FindSaved(so, "m_Floats", name);
        return p != null ? p.floatValue : fallback;
    }

    static Color SavedColor(SerializedObject so, string name, Color fallback)
    {
        var p = FindSaved(so, "m_Colors", name);
        return p != null ? p.colorValue : fallback;
    }

    static Texture SavedTex(SerializedObject so, string name, out Vector2 tiling, out Vector2 offset)
    {
        tiling = Vector2.one; offset = Vector2.zero;
        var p = FindSaved(so, "m_TexEnvs", name);
        if (p == null) return null;
        tiling = p.FindPropertyRelative("m_Scale").vector2Value;
        offset = p.FindPropertyRelative("m_Offset").vector2Value;
        return p.FindPropertyRelative("m_Texture").objectReferenceValue as Texture;
    }

    /// The same switches the Standard shader inspector flips for its Rendering
    /// Mode dropdown (StandardShaderGUI.SetupMaterialWithBlendMode).
    static void SetStandardMode(Material m, int mode)
    {
        m.SetFloat("_Mode", mode);
        switch (mode)
        {
            case 0: // Opaque
                m.SetOverrideTag("RenderType", "");
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                m.SetInt("_ZWrite", 1);
                m.DisableKeyword("_ALPHATEST_ON"); m.DisableKeyword("_ALPHABLEND_ON"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                m.renderQueue = -1;
                break;
            case 1: // Cutout
                m.SetOverrideTag("RenderType", "TransparentCutout");
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                m.SetInt("_ZWrite", 1);
                m.EnableKeyword("_ALPHATEST_ON"); m.DisableKeyword("_ALPHABLEND_ON"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                break;
            default: // Transparent
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.DisableKeyword("_ALPHATEST_ON"); m.DisableKeyword("_ALPHABLEND_ON"); m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                break;
        }
    }

    static void SetKeyword(Material m, string kw, bool on)
    {
        if (on) m.EnableKeyword(kw); else m.DisableKeyword(kw);
    }

    // ── the scene ───────────────────────────────────────────────────────────

    [MenuItem("Tools/Football/Build Proto Football Scene")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(PrefabDir))
        {
            Debug.LogError("[FootballProto] " + PrefabDir + " not found — is the LargeFootballStadium pack imported?");
            return;
        }
        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(PlayerPrefabGuid));
        var snapshot     = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerSnapshotPath);
        if (playerPrefab == null && snapshot == null)
        {
            Debug.LogError("[FootballProto] Neither TutorialPlayer.prefab nor Player.prefab found — nothing built.");
            return;
        }
        if (!AssetDatabase.IsValidFolder(SceneDir)) AssetDatabase.CreateFolder("Assets", "4 - Scenes");

        _boardPose = CaptureBoardPose();
        int fixedMats = ConvertMaterialsInternal();
        if (fixedMats > 0) Debug.Log("[FootballProto] Converted " + fixedMats + " URP material(s) to Standard first.");

        var prevActive  = SceneManager.GetActiveScene();
        var alreadyOpen = SceneManager.GetSceneByPath(ScenePath);
        if (alreadyOpen.IsValid() && alreadyOpen.isLoaded)
        {
            SceneManager.SetActiveScene(alreadyOpen);
            try
            {
                foreach (var go in alreadyOpen.GetRootGameObjects()) Object.DestroyImmediate(go);
                Populate(alreadyOpen, snapshot != null ? snapshot : playerPrefab);
                EditorSceneManager.MarkSceneDirty(alreadyOpen);
                if (!EditorSceneManager.SaveScene(alreadyOpen))
                    Debug.LogError("[FootballProto] SaveScene failed for " + ScenePath);
            }
            finally
            {
                if (prevActive.IsValid() && prevActive != alreadyOpen) SceneManager.SetActiveScene(prevActive);
            }
        }
        else
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                Populate(scene, snapshot != null ? snapshot : playerPrefab);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    Debug.LogError("[FootballProto] SaveScene failed for " + ScenePath);
            }
            finally
            {
                if (prevActive.IsValid()) SceneManager.SetActiveScene(prevActive);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[FootballProto] Built → " + ScenePath + ". Tools ▸ Football ▸ Open Proto Football Scene, then Play. " +
                  "The game starts by itself (coin toss → kickoff); F8 = debug panel, [ ] = sim speed, F7 = play-by-play.");
    }

    [MenuItem("Tools/Football/Open Proto Football Scene")]
    public static void Open()
    {
        if (!System.IO.File.Exists(ScenePath)) { Debug.LogError("[FootballProto] Not built yet — run Build Proto Football Scene first."); return; }
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    static void Populate(Scene scene, GameObject playerPrefab)
    {
        // Lighting: the pack's own day sky, ambient from it, a sun at a
        // mid-afternoon angle. Nothing here is the gameplay look (that comes from
        // the atmosphere post on Cyclops) — it just has to read well for a walk.
        var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyMatPath);
        if (sky != null) RenderSettings.skybox = sky;
        else Debug.LogWarning("[FootballProto] " + SkyMatPath + " missing — sky falls back to the default.");
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.48f);
        RenderSettings.fog = false;

        var sunGo = new GameObject("Sun");
        sunGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.96f, 0.88f);
        sun.intensity = 1.1f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.85f;
        sun.shadowResolution = UnityEngine.Rendering.LightShadowResolution.VeryHigh;
        sun.shadowBias = 0.05f;
        sun.shadowNormalBias = 0.4f;
        RenderSettings.sun = sun;

        // FieldRoot — the handoff's one non-negotiable: the sim's frame.
        var fieldRoot = new GameObject("FieldRoot");

        var stadium = new GameObject("Stadium");
        stadium.transform.SetParent(fieldRoot.transform, false);
        stadium.transform.localScale = Vector3.one * StadiumScale;
        var counts = new Dictionary<string, int>();
        foreach (var piece in Layout)
        {
            string path = PrefabDir + "/" + piece.prefab + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Debug.LogWarning("[FootballProto] " + path + " missing — skipped."); continue; }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            inst.transform.SetParent(stadium.transform, false);
            inst.transform.localPosition = piece.pos;
            inst.transform.localRotation = Quaternion.Euler(0f, piece.yaw, 0f);
            counts.TryGetValue(piece.prefab, out int k);
            counts[piece.prefab] = k + 1;
            if (k > 0) inst.name = piece.prefab + " (" + k + ")";
            SetLayerRecursive(inst, BodyLayer);
            if (piece.prefab == "FootballPitchFifa5.3") ConvertPitchToFootball(inst);
        }

        // The football field: dimensions on FieldRoot, paint under it, the
        // match (teams, ball, twelve players at runtime) beside it.
        fieldRoot.AddComponent<FootballField>();
        var markings = new GameObject("FieldMarkings");
        markings.transform.SetParent(fieldRoot.transform, false);
        markings.AddComponent<FootballFieldMarkings>();
        var match = new GameObject("FootballMatch");
        match.transform.SetParent(fieldRoot.transform, false);
        var fm = match.AddComponent<FootballMatch>();
        var aliens = new List<GameObject>();
        for (int i = 1; i <= 10; i++)
        {
            var a = AssetDatabase.LoadAssetAtPath<GameObject>(AlienPackDir + "/Alien" + i + ".prefab");
            if (a != null) aliens.Add(a);
        }
        fm.alienPrefabs = aliens.ToArray();
        if (aliens.Count == 0) Debug.LogWarning("[FootballProto] no Alien_Pack prefabs found at " + AlienPackDir + " — capsules.");

        // The scoreboard: over the +Z end zone, facing the field. Sam moves it;
        // a rebuild keeps wherever he put it.
        var board = new GameObject("Scoreboard");
        board.transform.SetParent(fieldRoot.transform, false);
        if (_boardPose.found) { board.transform.localPosition = _boardPose.pos; board.transform.localRotation = _boardPose.rot; board.transform.localScale = _boardPose.scale; }
        else board.transform.localPosition = new Vector3(0f, 22f, 97f);   // clear of the tier's seats
        board.AddComponent<FootballScoreboard>();

        // Ground slab under everything, so stepping outside the stands never falls.
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Ground";
        ground.layer = BodyLayer;
        ground.transform.SetParent(fieldRoot.transform, false);
        ground.transform.localPosition = new Vector3(0f, GroundTop - 0.5f, 0f);   // top face at GroundTop
        ground.transform.localScale = new Vector3(GroundSize, 1f, GroundSize);
        // A scene can't hold a material of its own — an inline `new Material`
        // is gone (pink) the next time the scene loads — so it's an asset.
        ground.GetComponent<Renderer>().sharedMaterial = LoadOrCreateMat(GroundMatPath, m =>
        {
            m.color = new Color(0.30f, 0.32f, 0.28f);
            m.SetFloat("_Glossiness", 0.1f);
        });

        // The player on the home sideline at the 50, facing the field — off the
        // grass the game is played on, a few steps from the stands. Snapshot of the gameplay
        // player (audio, every equippable) when it exists — same as the tutorial.
        var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
        player.name = "Player";
        player.SetActive(true);          // the gameplay scene keeps it inactive until GameSetUp
        var cam = player.GetComponentInChildren<Camera>(true);
        if (cam != null) cam.clearFlags = CameraClearFlags.Skybox;
        player.transform.SetPositionAndRotation(new Vector3(-36f, 1.2f, 0f), Quaternion.LookRotation(Vector3.right));

        var uiRoot = new GameObject("--- UI ---");
        BuildCrosshair(uiRoot.transform);

        var es = new GameObject("EventSystem");
        es.AddComponent<UnityEngine.EventSystems.EventSystem>();
        es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
    }

    /// The pack's pitch is a soccer field: two grass materials in alternating
    /// 5.25 m mow stripes and a third for the soccer lines — and the lines are
    /// not painted ON the grass, they are strips CUT INTO it (an invisible
    /// material there showed the ground slab a metre below as dark lines). So
    /// every slot gets the one flat grass: the stripes vanish and the line
    /// strips turn to grass. FootballFieldMarkings paints its own stripes
    /// between the yard lines. Material overrides on the instance — the prefab
    /// is untouched.
    static void ConvertPitchToFootball(GameObject pitch)
    {
        var mr = pitch.GetComponentInChildren<MeshRenderer>(true);
        if (mr == null) { Debug.LogWarning("[FootballProto] pitch has no MeshRenderer — soccer lines left as-is."); return; }
        var grass = AssetDatabase.LoadAssetAtPath<Material>(GrassMatPath);
        if (grass == null) { Debug.LogWarning("[FootballProto] " + GrassMatPath + " missing — soccer stripes/lines left as-is."); return; }
        var mats = mr.sharedMaterials;
        for (int i = 0; i < mats.Length; i++) mats[i] = grass;
        mr.sharedMaterials = mats;
    }

    static Material LoadOrCreateMat(string path, System.Action<Material> configure)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard")) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(mat, path);
        }
        configure(mat);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
    }

    // Same crosshair the tutorial builds (TutorialSceneBuilder.BuildCrosshair) —
    // the reticle is what look-to-interact draws through; without it the player
    // has no aim point.
    static void BuildCrosshair(Transform parent)
    {
        var canvasGo = new GameObject("Overlay Canvas", typeof(RectTransform));
        canvasGo.layer = UILayer;
        canvasGo.transform.SetParent(parent, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var dot = new GameObject("Dot", typeof(RectTransform));
        dot.layer = UILayer;
        dot.transform.SetParent(canvasGo.transform, false);
        var rt = (RectTransform)dot.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(8f, 8f);
        var img = dot.AddComponent<Image>();
        img.raycastTarget = false;
        var reticle = dot.AddComponent<CrosshairReticle>();
        reticle.color = new Color(0.749f, 0.914f, 1f, 1f);
        reticle.scale = 1f;
        reticle.thickness = 2f;
        reticle.morphSeconds = 0.2f;
        reticle.showPip = true;
        reticle.requireLookToInteract = true;
        reticle.aimRadius = 0.1f;
        reticle.extraAimMargin = 0f;
        reticle.forgiveDepth = 0.5f;
    }
}
