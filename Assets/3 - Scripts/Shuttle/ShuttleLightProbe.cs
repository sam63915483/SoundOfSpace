using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Light-leak TEST OBJECT for the shuttle cabin (Sam, 2026-09-06: "outside
/// lights just seem to easily go through the walls"). Sits at the centre of
/// the ShuttleInteriorVolume (installed by Tools ▸ Shuttle Travel ▸ Add Light
/// Probe + Interior Light Guard). Twice a second it asks, for every enabled
/// Light in the scene: does this light reach the cabin centre, and how?
///
///   • reach  = the light's strength at this point (intensity × Unity's
///              distance falloff, × the cone for spots). Below minReach it is
///              ignored.
///   • a ray from the probe TOWARD the light: if it hits hull geometry first
///              the light is "behind the hull".
///   • behind the hull + the light casts shadows      → blocked, fine.
///     behind the hull + NO shadows                   → !! LEAK: nothing stops
///              an unshadowed light — it lights the walls straight through.
///     clear line                                     → coming through a window
///              or the open door, which is the intended look.
///
/// F1 toggles the on-screen list (cheats build only). Every time the set of
/// lights reaching the cabin changes, the same list goes to the log
/// ([ShuttleLightProbe]), so a build's Player.log shows what lit the cabin
/// and when. Pure diagnostics: it never changes a light.
/// </summary>
public class ShuttleLightProbe : MonoBehaviour
{
    public KeyCode toggleKey = KeyCode.F1;
    [Tooltip("Bisect: each press MUTES the next light group from the list (the previous one comes back). When the orange vanishes, the muted group is the culprit. Delete clears.")]
    public KeyCode muteNextKey = KeyCode.Insert;
    public KeyCode muteClearKey = KeyCode.Delete;
    [Tooltip("Seconds between scans.")]
    public float interval = 0.5f;
    [Tooltip("Lights weaker than this at the probe are ignored.")]
    public float minReach = 0.02f;
    [Tooltip("How far the occlusion ray looks for hull geometry (directional lights use the whole distance).")]
    public float occlusionMaxDistance = 80f;
    [Tooltip("Layers that count as hull for the occlusion ray (the shuttle is on Body; Default/WorldProp/Ship for props).")]
    public LayerMask occluders = (1 << 0) | (1 << 3) | (1 << 9) | (1 << 10);

    class Entry { public string line; public bool leak; public float reach; }

    readonly List<Entry> _entries = new List<Entry>();
    Light[] _lights;
    float _nextScan, _nextRefresh;
    bool _show;
    string _lastSummary = "";
    Transform _shuttleRoot;
    GUIStyle _style;

    // Bisect state: the muted group's name, and the lights we switched off for it.
    string _mutedGroup = "";
    int _muteIndex = -1;
    readonly List<Light> _mutedLights = new List<Light>();
    readonly List<string> _groups = new List<string>();

    void Awake()
    {
        // The shuttle is parented under a planet when landed, so transform.root
        // would be the whole celestial tree (the first playtest log listed the
        // SUN as "shuttle's own light"). The guard sits on the shuttle's root.
        var guard = GetComponentInParent<ShuttleInteriorLightGuard>();
        _shuttleRoot = guard != null ? guard.transform : transform.root;
    }

    void Update()
    {
        if (Universe.cheatsEnabled && Input.GetKeyDown(toggleKey)) _show = !_show;
        if (Universe.cheatsEnabled && Input.GetKeyDown(muteNextKey)) MuteNext();
        if (Universe.cheatsEnabled && Input.GetKeyDown(muteClearKey)) Unmute();
        if (_mutedLights.Count > 0) foreach (var ml in _mutedLights) if (ml != null && ml.enabled) ml.enabled = false;   // re-assert: the FX script re-enables its lights
        if (Time.unscaledTime < _nextScan) return;
        _nextScan = Time.unscaledTime + Mathf.Max(0.1f, interval);
        Scan();
    }

    void Scan()
    {
        if (_lights == null || Time.unscaledTime > _nextRefresh)
        {
            _lights = FindObjectsOfType<Light>();
            _nextRefresh = Time.unscaledTime + 2f;
        }
        _entries.Clear();
        Vector3 p = transform.position;
        int wallsLayerBit = 1 << gameObject.layer;   // the probe shares the hull's layer
        int leaks = 0;

        for (int i = 0; i < _lights.Length; i++)
        {
            var l = _lights[i];
            if (l == null || !l.enabled || !l.gameObject.activeInHierarchy || l.intensity <= 0f) continue;

            float reach, dist;
            Vector3 toLight;
            if (l.type == LightType.Directional)
            {
                reach = l.intensity;
                toLight = -l.transform.forward;
                dist = occlusionMaxDistance;
            }
            else
            {
                Vector3 d = l.transform.position - p;
                dist = d.magnitude;
                if (dist > l.range || dist < 1e-3f) continue;
                toLight = d / dist;
                float x = dist / Mathf.Max(l.range, 0.001f);
                float atten = 1f / (1f + 25f * x * x);
                reach = l.intensity * atten;
                if (l.type == LightType.Spot)
                {
                    float cosA = Vector3.Dot(-toLight, l.transform.forward);
                    float cosOuter = Mathf.Cos(l.spotAngle * 0.5f * Mathf.Deg2Rad);
                    if (cosA < cosOuter) continue;
                }
            }
            if (reach < minReach) continue;

            bool own = _shuttleRoot != null && l.transform.IsChildOf(_shuttleRoot);
            bool hitsWalls = (l.cullingMask & wallsLayerBit) != 0;
            float rayLen = Mathf.Min(dist, occlusionMaxDistance);
            bool behindHull = Physics.Raycast(p, toLight, out RaycastHit hit, rayLen, occluders, QueryTriggerInteraction.Ignore)
                              && (l.type == LightType.Directional || hit.distance < dist - 0.05f);
            bool shadowed = l.shadows != LightShadows.None;
            string blocker = behindHull ? hit.collider.name : "";
            // Does the thing in the way even CAST shadows? A wall with shadow
            // casting off blocks nothing, however good the light's shadows are.
            bool blockerCasts = true;
            if (behindHull)
            {
                var rend = hit.collider.GetComponent<Renderer>();
                if (rend == null) rend = hit.collider.GetComponentInParent<Renderer>();
                blockerCasts = rend != null && rend.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // How far the blocking surface is from the LIGHT: a caster nearer than
            // the light's shadow near plane is simply not in its shadow map.
            float occFromLight = (behindHull && l.type != LightType.Directional) ? dist - hit.distance : float.PositiveInfinity;

            string status;
            bool leak = false;
            if (!hitsWalls) status = "masked off the hull layer";
            else if (!behindHull) status = own ? "clear line inside the cabin" : "clear line (window / open door)";
            else if (!shadowed) { status = $"LEAK: behind {blocker}, NO shadows -> lights the walls through it"; leak = true; }
            else if (!blockerCasts) { status = $"LEAK: behind {blocker}, but {blocker} has shadow casting OFF -> cannot block anything"; leak = true; }
            else if (occFromLight < l.shadowNearPlane) { status = $"LEAK: {blocker} is {occFromLight:0.00} m from the light, inside its shadow near plane ({l.shadowNearPlane:0.00} m) -> cannot cast"; leak = true; }
            else if (l.renderMode == LightRenderMode.ForceVertex) { status = $"LEAK: behind {blocker} but rendered per-vertex (no shadows)"; leak = true; }
            else status = $"behind {blocker} ({occFromLight:0.0} m from the light), {l.shadows} shadows, bias {l.shadowBias:0.00}/{l.shadowNormalBias:0.00}, near {l.shadowNearPlane:0.00}, {l.renderMode} -> should be blocked";
            if (leak) leaks++;

            string where = l.type == LightType.Directional ? "" : $" at {dist:0} m";
            _entries.Add(new Entry
            {
                line = $"{(leak ? "!! " : "   ")}{(own ? "[shuttle] " : "")}{l.name} [{l.type}, I {l.intensity:0.##}{where}]  reach {reach:0.00}  — {status}",
                leak = leak, reach = reach,
            });
        }
        _entries.Sort((a, b) => a.leak != b.leak ? (a.leak ? -1 : 1) : b.reach.CompareTo(a.reach));

        // Groups for the bisect: light names with a trailing _N stripped, in list order.
        _groups.Clear();
        foreach (var l in _lights)
        {
            if (l == null) continue;
            string g = GroupOf(l.name);
            if (!_groups.Contains(g)) _groups.Add(g);
        }
        _groups.Sort();

        var sb = new StringBuilder();
        foreach (var e in _entries) sb.Append(e.line).Append('\n');
        string summary = $"{leaks} leaking / {_entries.Count} reaching the cabin centre  (quality: shadows {QualitySettings.shadows}, distance {QualitySettings.shadowDistance:0}, pixel lights {QualitySettings.pixelLightCount})";
        string full = summary + "\n" + sb;
        if (full != _lastSummary)
        {
            _lastSummary = full;
            Debug.Log("[ShuttleLightProbe] " + full);
        }
    }

    static string GroupOf(string name)
    {
        int us = name.LastIndexOf('_');
        if (us > 0 && us < name.Length - 1 && int.TryParse(name.Substring(us + 1), out _)) return name.Substring(0, us);
        return name;
    }

    void MuteNext()
    {
        if (_groups.Count == 0) return;
        Unmute(false);
        _muteIndex = (_muteIndex + 1) % _groups.Count;
        _mutedGroup = _groups[_muteIndex];
        foreach (var l in FindObjectsOfType<Light>())
            if (l != null && l.enabled && GroupOf(l.name) == _mutedGroup) { l.enabled = false; _mutedLights.Add(l); }
        Debug.Log($"[ShuttleLightProbe] MUTED '{_mutedGroup}' ({_mutedLights.Count} light(s)). If the cabin went normal, this is the culprit.");
    }

    void Unmute(bool log = true)
    {
        foreach (var l in _mutedLights) if (l != null) l.enabled = true;
        _mutedLights.Clear();
        if (log && !string.IsNullOrEmpty(_mutedGroup)) Debug.Log($"[ShuttleLightProbe] unmuted '{_mutedGroup}'.");
        if (log) { _mutedGroup = ""; _muteIndex = -1; }
    }

    void OnDisable() { Unmute(false); _mutedGroup = ""; _muteIndex = -1; }

    void OnGUI()
    {
        if (!_show) return;
        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = false, wordWrap = false };
            _style.normal.textColor = Color.white;
        }
        float w = 900f, h = 24f + 18f * Mathf.Max(1, _entries.Count + 2);
        var rect = new Rect(16f, Screen.height * 0.5f - h * 0.5f, w, h);
        GUI.Box(rect, GUIContent.none);
        GUILayout.BeginArea(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, rect.height - 12f));
        GUILayout.Label("SHUTTLE LIGHT PROBE (F1 hides · Insert = mute next light group · Delete = unmute)  —  " + _lastSummary.Split('\n')[0], _style);
        if (!string.IsNullOrEmpty(_mutedGroup))
        {
            _style.normal.textColor = new Color(1f, 0.85f, 0.3f);
            GUILayout.Label($"MUTED: {_mutedGroup}   ({_muteIndex + 1}/{_groups.Count})  — if the cabin just went normal, THIS is the leak", _style);
            _style.normal.textColor = Color.white;
        }
        foreach (var e in _entries)
        {
            _style.normal.textColor = e.leak ? new Color(1f, 0.45f, 0.4f) : (e.line.Contains("(ok)") || e.line.Contains("own light") ? new Color(0.7f, 1f, 0.7f) : Color.white);
            GUILayout.Label(e.line, _style);
        }
        _style.normal.textColor = Color.white;
        GUILayout.EndArea();
    }
}
