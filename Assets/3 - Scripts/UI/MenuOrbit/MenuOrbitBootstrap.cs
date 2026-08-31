using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MENU-ONLY: the single scene-authored component in MenuOrbit (the stripped
/// copy of the gameplay scene used as the main-menu 3D background). At Awake it
/// demilitarizes the copy — the player becomes a kinematic passenger inside the
/// shuttle, every gameplay behaviour on the shuttle is switched off, the
/// shuttle detaches from Humble Abode and takes over floating-origin
/// registration from the player — then starts MenuShuttleTour + MenuShotDirector.
/// The gameplay scene 1.6.7.7.7 never contains this component.
/// </summary>
public class MenuOrbitBootstrap : MonoBehaviour
{
    [Tooltip("The gameplay scene's skybox (ESO Milky Way). Additive scenes don't " +
             "contribute RenderSettings — only the ACTIVE scene's lighting applies, " +
             "and in the menu flow that's MainMenu's Default-Skybox — so the " +
             "bootstrap applies the real sky by hand.")]
    [SerializeField] Material skyboxMaterial;

    void Awake()
    {
        var playerPC = FindObjectOfType<PlayerController>(true);
        var shuttleT = FindShuttle();
        // NOT Camera.main: during the additive load MainMenu's camera is still
        // enabled and wins that lookup — v1 grabbed it, which both put the
        // director on a camera the menu controller then disables AND let the
        // mute sweep disable the real camera's atmosphere/ocean post stack.
        // The camera this scene owns lives under the player.
        var cam = playerPC != null ? playerPC.GetComponentInChildren<Camera>(true) : null;
        var endless = FindObjectOfType<EndlessManager>();
        if (playerPC == null || shuttleT == null || cam == null)
        {
            Debug.LogError($"[MenuOrbitBootstrap] missing pieces: player={playerPC != null} shuttle={shuttleT != null} cam={cam != null}");
            return;
        }
        Transform player = playerPC.transform;

        // Player: kinematic passenger, no controller, no colliders.
        playerPC.enabled = false;
        var prb = player.GetComponent<Rigidbody>();
        if (prb != null) { prb.isKinematic = true; prb.interpolation = RigidbodyInterpolation.None; }
        foreach (var col in player.GetComponentsInChildren<Collider>(true)) col.enabled = false;

        // Shuttle: detach from its planet, silence every gameplay behaviour on
        // it (autopilot, rider frame, monitors, doors, TRAX...). The player
        // subtree is excluded — its camera carries the atmosphere/ocean post
        // stack, which must stay alive.
        shuttleT.SetParent(null, true);
        foreach (var mb in shuttleT.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            if (mb.transform.IsChildOf(player)) continue;
            if (mb is MenuShuttleTour || mb is MenuShotDirector || mb is MenuOrbitBootstrap) continue;
            mb.enabled = false;
        }
        // Monitor/interior cameras on the shuttle render to textures nobody
        // will see — turn them off (the main camera is under the player).
        foreach (var c in shuttleT.GetComponentsInChildren<Camera>(true))
            if (c != cam) c.enabled = false;

        // Seat the player inside the shuttle (pod-ish) and hand origin-shift
        // duty to the shuttle: the player must NOT also be shifted directly or
        // it would move twice (it now rides the shuttle's hierarchy).
        player.SetParent(shuttleT, true);
        if ((player.position - shuttleT.position).sqrMagnitude > 40f * 40f)
            player.localPosition = Vector3.up * 2f;
        if (endless != null)
        {
            endless.UnregisterPhysicsObject(player);
            endless.RegisterPhysicsObject(shuttleT);
            // The tour never leaves a ~27k-unit sphere, where float precision is
            // still sub-centimeter — origin shifts buy nothing here and each one
            // costs a visible 2-frame interpolation hitch every ~20s of flight
            // (Sam's "occasional hitches"). Push the threshold beyond the tour.
            endless.distanceThreshold = 60000f;
        }

        // LODHandler lazily caches Camera.main on its first LOD pass — during
        // the additive load that can still be MAINMENU's (about-to-be-disabled)
        // camera, which then feeds it garbage screen heights forever. Hand it
        // the real camera up front.
        var lod = FindObjectOfType<LODHandler>();
        if (lod != null)
        {
            var f = typeof(LODHandler);
            const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            f.GetField("cam", F)?.SetValue(lod, cam);
            f.GetField("camT", F)?.SetValue(lod, cam.transform);
        }

        // No stray UI in the background scene. OWN SCENE ONLY — FindObjectsOfType
        // sees across loaded scenes, and the first version of this line
        // deactivated the MainMenu's canvas too, taking the menu buttons (and
        // the controller's running coroutines) down with it.
        foreach (var canvas in FindObjectsOfType<Canvas>(true))
            if (canvas.gameObject.scene == gameObject.scene) canvas.gameObject.SetActive(false);

        // ── Lighting parity with the gameplay scene ────────────────────────
        // Only the ACTIVE scene's RenderSettings apply (MainMenu's, in the
        // additive menu flow), so the real sky must be set by hand: Milky Way
        // skybox, flat black ambient, and the Sun Shadow Caster as the sun.
        if (skyboxMaterial != null) RenderSettings.skybox = skyboxMaterial;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientSkyColor = Color.black;
        RenderSettings.ambientIntensity = 0f;
        RenderSettings.fog = false;
        foreach (var l in FindObjectsOfType<Light>())
            if (l.name == "Sun Shadow Caster") { RenderSettings.sun = l; break; }

        // The lens flare and the rest of the camera FX belong to
        // CameraEffectsManager — a gameplay singleton seeded on PLAY, which the
        // menu flow never creates. Seeding it here is idempotent:
        // EnsureGameplaySingletons null-checks before creating its own.
        if (CameraEffectsManager.Instance == null)
        {
            var fx = new GameObject("CameraEffectsManager");
            DontDestroyOnLoad(fx);
            fx.AddComponent<CameraEffectsManager>();
        }

        // Silence the sleeping passenger: no breathing/suit audio in the menu.
        // Everything on the player except the camera object itself goes quiet
        // (the camera keeps its post stack + AudioListener).
        foreach (var mb in player.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null && mb.gameObject != cam.gameObject) mb.enabled = false;
        foreach (var src in player.GetComponentsInChildren<AudioSource>(true)) { src.Stop(); src.enabled = false; }
        foreach (var src in shuttleT.GetComponentsInChildren<AudioSource>(true)) { src.Stop(); src.enabled = false; }

        // The astronaut avatar must never appear on screen — the pod is shut.
        // Renderers and Animators are NOT MonoBehaviours, so the sweep above
        // misses them (the avatar visibly trailed the camera in v2).
        foreach (var r in player.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        foreach (var a in player.GetComponentsInChildren<Animator>(true)) a.enabled = false;

        // The lens flare (and every fx toggle) is gated on InputSettings.Active,
        // which only exists after Begin() — normally called by the (disabled)
        // player controller path. Call Begin() on the still-disabled component:
        // Active + Sam's saved fx prefs go live, its per-frame hotkey Update
        // never runs.
        var input = FindObjectOfType<InputSettings>(true);
        if (input != null) input.Begin();

        // AstronautReflectLight is a directional FILL light for the astronaut —
        // in the menu it floodlights the NIGHT side of every planet ("dark
        // sides appear brighter than they should", Sam's review). The avatar is
        // hidden here anyway; kill the light, keep the managers underneath it.
        var reflect = GameObject.Find("AstronautReflectLight");
        if (reflect != null)
        {
            var l = reflect.GetComponent<Light>();
            if (l != null) l.enabled = false;
        }

        var tour = shuttleT.gameObject.AddComponent<MenuShuttleTour>();
        var director = cam.gameObject.AddComponent<MenuShotDirector>();
        director.tour = tour;
        director.cam = cam;
        var tracker = shuttleT.gameObject.AddComponent<MenuTourTracker>();
        tracker.tour = tour;
        tracker.director = director;
        tracker.cam = cam;
        StartCoroutine(FixSunAim(cam));
        Debug.Log("[MenuOrbitBootstrap] menu background armed: tour + director + tracker running");
    }

    // SunShadowCaster caches Camera.main in ITS Start and forever aims the
    // sun's directional light at that camera. During the additive menu load,
    // Camera.main is still MAINMENU's (soon-disabled) camera near the world
    // origin — inside the sun — so sunlight beamed off in a degenerate
    // direction and washed every planet's NIGHT SIDE gray (Sam: "gray, I can
    // make out all the features"; the atmosphere post uses the true sun
    // position, so its terminator ring disagreed too = "atmospheres look
    // weird"). Third instance of the additive-load Camera.main trap. Re-point
    // it AFTER its Start has run.
    System.Collections.IEnumerator FixSunAim(Camera cam)
    {
        yield return null;
        yield return null;
        yield return null;
        var field = typeof(SunShadowCaster).GetField("track",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        foreach (var caster in FindObjectsOfType<SunShadowCaster>())
        {
            field?.SetValue(caster, cam.transform);
            Debug.Log($"[MenuOrbitBootstrap] SunShadowCaster '{caster.name}' re-aimed at the menu camera");
        }
    }

    Transform FindShuttle()
    {
        foreach (var t in FindObjectsOfType<Transform>(true))
            if (t.name == "Shuttle_Lander") return t;
        return null;
    }
}
