using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Press-to-toggle runtime debugger for the "ground dimming when I rotate the
// camera" bug. Lets the user disable parts of the lighting pipeline ONE AT A
// TIME while playing. Whichever toggle makes the wedge artifact disappear is
// the cause. On-screen status text shows what's currently enabled/disabled.
//
// Hotkeys (all activate during Play mode):
//   F6 — toggle the CustomPostProcessing component on the main camera
//        (disables atmosphere + ocean + bloom in one go)
//   F7 — toggle the Sun's directional shadow caster light (SunShadowCaster)
//   F8 — toggle the Sun's Point Light component
//   F9 — toggle ALL torch lights in the scene
//   F10 — toggle player flashlight component
//   F11 — toggle ALL post-process effects (same as F6, redundant convenience)
//   F12 — print a one-line summary of what's currently disabled
//
// To REVERT: delete this file, or delete the [PixelLightLimitFix]-style auto
// singleton block. Disabling a toggle and quitting Play mode does NOT persist
// the change — every toggle resets on scene reload.
public class LightingDebugToolbox : MonoBehaviour
{
    public static LightingDebugToolbox Instance { get; private set; }

    bool _postProcessOff;
    bool _sunShadowsOff;
    bool _sunPointOff;
    bool _torchesOff;
    bool _flashlightOff;

    GUIStyle _style;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("[LightingDebugToolbox]");
        DontDestroyOnLoad(go);
        go.AddComponent<LightingDebugToolbox>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Debug.Log("[LightingDebugToolbox] Loaded. Click the on-screen buttons (top-left) to toggle lighting elements.");
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F6))  TogglePostProcess();
        if (Input.GetKeyDown(KeyCode.F7))  ToggleSunShadowCaster();
        if (Input.GetKeyDown(KeyCode.F8))  ToggleSunPointLight();
        if (Input.GetKeyDown(KeyCode.F9))  ToggleAllTorches();
        if (Input.GetKeyDown(KeyCode.F10)) ToggleFlashlight();
        if (Input.GetKeyDown(KeyCode.F11)) TogglePostProcess();
        if (Input.GetKeyDown(KeyCode.F12)) PrintSummary();
    }

    void TogglePostProcess()
    {
        _postProcessOff = !_postProcessOff;
        var cam = Camera.main;
        if (cam != null)
        {
            var cpp = cam.GetComponent<CustomPostProcessing>();
            if (cpp != null) cpp.enabled = !_postProcessOff;
        }
        Debug.Log($"[LightingDebug] PostProcessing: {(_postProcessOff ? "OFF" : "ON")}");
    }

    void ToggleSunShadowCaster()
    {
        _sunShadowsOff = !_sunShadowsOff;
        var sc = Object.FindObjectOfType<SunShadowCaster>();
        if (sc != null)
        {
            var l = sc.GetComponent<Light>();
            if (l != null) l.enabled = !_sunShadowsOff;
        }
        Debug.Log($"[LightingDebug] SunShadowCaster Light: {(_sunShadowsOff ? "OFF" : "ON")}");
    }

    void ToggleSunPointLight()
    {
        _sunPointOff = !_sunPointOff;
        // The Sun's point light lives at Sun/Point Light (Sun) in the hierarchy.
        // Find every Light in the scene whose parent is named "Sun" and is NOT
        // the SunShadowCaster — that's it.
        var allLights = Object.FindObjectsOfType<Light>(true);
        int touched = 0;
        for (int i = 0; i < allLights.Length; i++)
        {
            var l = allLights[i];
            if (l == null) continue;
            if (l.GetComponent<SunShadowCaster>() != null) continue;
            var parent = l.transform.parent;
            if (parent != null && parent.name == "Sun")
            {
                l.enabled = !_sunPointOff;
                touched++;
            }
        }
        Debug.Log($"[LightingDebug] Sun Point Light (touched {touched}): {(_sunPointOff ? "OFF" : "ON")}");
    }

    void ToggleAllTorches()
    {
        _torchesOff = !_torchesOff;
        var allLights = Object.FindObjectsOfType<Light>(true);
        int touched = 0;
        for (int i = 0; i < allLights.Length; i++)
        {
            var l = allLights[i];
            if (l == null) continue;
            // A torch light has a parent named "Torch" or similar — match by
            // GameObject name being "TorchLight" which is the convention in the
            // scene hierarchy.
            if (l.gameObject.name == "TorchLight")
            {
                l.enabled = !_torchesOff;
                touched++;
            }
        }
        Debug.Log($"[LightingDebug] Torches (touched {touched}): {(_torchesOff ? "OFF" : "ON")}");
    }

    void ToggleFlashlight()
    {
        _flashlightOff = !_flashlightOff;
        var allLights = Object.FindObjectsOfType<Light>(true);
        int touched = 0;
        for (int i = 0; i < allLights.Length; i++)
        {
            var l = allLights[i];
            if (l == null) continue;
            if (l.gameObject.name.ToLower().Contains("flashlight"))
            {
                l.enabled = !_flashlightOff;
                touched++;
            }
        }
        Debug.Log($"[LightingDebug] Flashlight (touched {touched}): {(_flashlightOff ? "OFF" : "ON")}");
    }

    void PrintSummary()
    {
        Debug.Log($"[LightingDebug] PostProcess={(!_postProcessOff)} | SunShadows={(!_sunShadowsOff)} | SunPoint={(!_sunPointOff)} | Torches={(!_torchesOff)} | Flashlight={(!_flashlightOff)}");
    }

    void OnGUI()
    {
        // Header.
        GUI.Box(new Rect(8, 8, 320, 28), "LIGHTING DEBUG — click toggles");

        // Click-buttons. Each shows current state and toggles on click.
        if (DrawToggleButton(new Rect(8, 40, 320, 26),  "PostProcessing",   _postProcessOff)) TogglePostProcess();
        if (DrawToggleButton(new Rect(8, 70, 320, 26),  "Sun Shadow Caster", _sunShadowsOff)) ToggleSunShadowCaster();
        if (DrawToggleButton(new Rect(8, 100, 320, 26), "Sun Point Light",  _sunPointOff))   ToggleSunPointLight();
        if (DrawToggleButton(new Rect(8, 130, 320, 26), "All Torches",      _torchesOff))    ToggleAllTorches();
        if (DrawToggleButton(new Rect(8, 160, 320, 26), "Flashlight",       _flashlightOff)) ToggleFlashlight();
        if (GUI.Button(new Rect(8, 190, 320, 26), "Log summary"))                            PrintSummary();
    }

    static bool DrawToggleButton(Rect rect, string label, bool isOff)
    {
        var state = isOff ? "OFF" : "ON ";
        return GUI.Button(rect, $"[{state}]  {label}");
    }
}
