# Space Dust Field Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a purely-visual field of glowing amber "space dust" specks that drift toward the black hole, form galaxy spiral arms, thicken near the BH and away from planets, vanish inside atmospheres, and stay rock-solid through floating-origin rebases.

**Architecture:** One auto-singleton `SpaceDustField` keeps a fixed pool of specks in a camera-following cube. Each speck is world-fixed (you fly *through* them = parallax) and wraps to the far side when it exits the cube. All motion is computed from the camera's position **relative to the black hole** — which is invariant under `EndlessManager` rebases, so origin shifts are automatically invisible. Per-speck visibility/brightness comes from a density field (BH proximity × distance-from-planets × spiral arms + light noise). Rendered with one additive instanced billboard material via `Graphics.DrawMeshInstanced`.

**Tech Stack:** Unity 2022.3 Built-in Render Pipeline, C# (`Assembly-CSharp`), a small unlit additive instanced billboard shader, `Graphics.DrawMeshInstanced`. No CLI tests — verification is MCP compile-checks (`mcp__coplay-mcp__check_compile_errors`) + in-Editor play verification (`mcp__coplay-mcp__play_game` / `mcp__coplay-mcp__scene_view_functions`).

**Spec:** `docs/superpowers/specs/2026-06-17-space-dust-field-design.md`

---

## Reference facts (verified against the codebase)

- Black hole: a `CelestialBody` with `isStaticAttractor = true`, `radius = 4000`, `bodyName = "Black Hole"`. World position via `body.Position` (returns `rb.position`).
- Enumerate bodies: `NBodySimulation.Bodies` → `CelestialBody[]`, null-safe (returns `Array.Empty` off the solar scene).
- `CelestialBody` fields: `public float radius;`, `public string bodyName;`, `public bool isStaticAttractor;`, `public Vector3 Position { get; }`.
- Atmosphere radius = `(1 + atmosphereScale) * radius`. `atmosphereScale` lives at `CelestialBodyGenerator.body.shading.atmosphereSettings.atmosphereScale` — read **read-only**, accessed via reflection (so a renamed member can never break the build, and we never reference forbidden-zone types). Fallback: `radius * atmosphereFallbackMultiplier`.
- Singleton pattern to mirror: `Assets/3 - Scripts/Player/SpaceDustInventory.cs`.
- Seeding (CLAUDE.md trap #1): `MainMenuController.EnsureGameplaySingletonsAsync` (Assets/3 - Scripts/UI/MainMenuController.cs ~lines 523–619), pattern: `if (X.Instance == null) { var go = new GameObject("name"); DontDestroyOnLoad(go); go.AddComponent<X>(); } tick("label"); yield return null;`
- Camera-effect flag pattern: `InputSettings.cs` (Assets/3 - Scripts/Scripts/Game/Controllers/InputSettings.cs): declare `public bool fxX = true;`, load `fxX = PlayerPrefs.GetInt(nameof(fxX), 1) != 0;`, save `PlayerPrefs.SetInt(nameof(fxX), fxX ? 1 : 0);`.
- Pause-menu toggle row: `TabbedPauseMenu.cs` CAMERA tab (~lines 535–581): `new ToggleDef { label = "X", get = () => _input != null && _input.fxX, set = v => { if (_input != null) _input.fxX = v; } },`.
- Instanced draw pattern: `InstancedGrassRenderer.cs` uses `Graphics.DrawMeshInstanced(mesh, 0, mat, Matrix4x4[1023], count, mpb, ShadowCastingMode.Off, receiveShadows)` in ≤1023 batches.

**Hard constraints:** Do NOT edit any forbidden-zone file (Atmosphere/Celestial generation/shading, planet shaders/materials). Only *read* `radius`/`Position`/`atmosphereScale`. No `FindObjectOfType`/`Camera.main` in hot loops without caching. Append serialized fields at the END of the class.

---

### Task 1: Additive instanced billboard shader

**Files:**
- Create: `Assets/3 - Scripts/World/SpaceDust.shader`

- [ ] **Step 1: Write the shader**

```shaderlab
Shader "Custom/SpaceDust"
{
    Properties
    {
        _MainTex ("Glow", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One One        // additive
        ZWrite Off
        Cull Off
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 col : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                // Instance center + uniform scale from the per-instance matrix
                float3 center = mul(unity_ObjectToWorld, float4(0,0,0,1)).xyz;
                float size = length(float3(unity_ObjectToWorld[0][0],
                                           unity_ObjectToWorld[1][0],
                                           unity_ObjectToWorld[2][0]));
                // Camera-facing billboard: V rows are the camera basis in world space
                float3 camR = UNITY_MATRIX_V[0].xyz;
                float3 camU = UNITY_MATRIX_V[1].xyz;
                float3 wpos = center + (camR * v.vertex.x + camU * v.vertex.y) * size;

                o.pos = mul(UNITY_MATRIX_VP, float4(wpos, 1.0));
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.col = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                fixed g = tex2D(_MainTex, i.uv).a;      // radial glow mask
                fixed3 c = i.col.rgb * g * i.col.a;     // rgb = amber tint, a = brightness
                return fixed4(c, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
```

- [ ] **Step 2: Let Unity import + verify the shader compiles with no errors**

Run: `mcp__coplay-mcp__check_compile_errors`, then `mcp__coplay-mcp__get_unity_logs`.
Expected: no C# errors; no `Shader error in 'Custom/SpaceDust'` lines in the logs.

- [ ] **Step 3: Add the shader to Always Included Shaders** (so `Shader.Find("Custom/SpaceDust")` resolves in builds, since the material is created at runtime)

Run via `mcp__coplay-mcp__execute_script` (editor script):

```csharp
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

var gs = AssetDatabase.LoadAssetAtPath<GraphicsSettings>("ProjectSettings/GraphicsSettings.asset");
var so = new SerializedObject(gs);
var arr = so.FindProperty("m_AlwaysIncludedShaders");
var sh = Shader.Find("Custom/SpaceDust");
bool present = false;
for (int i = 0; i < arr.arraySize; i++)
    if (arr.GetArrayElementAtIndex(i).objectReferenceValue == sh) { present = true; break; }
if (!present)
{
    arr.InsertArrayElementAtIndex(arr.arraySize);
    arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
    so.ApplyModifiedProperties();
    AssetDatabase.SaveAssets();
    Debug.Log("[SpaceDust] Added Custom/SpaceDust to Always Included Shaders");
}
else Debug.Log("[SpaceDust] Shader already in Always Included Shaders");
```

Expected log: one of the two `[SpaceDust]` messages. (If `execute_script` cannot edit ProjectSettings, do it manually: Project Settings → Graphics → Always Included Shaders → add `Custom/SpaceDust`.)

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/World/SpaceDust.shader" "Assets/3 - Scripts/World/SpaceDust.shader.meta" ProjectSettings/GraphicsSettings.asset
git commit -m "feat(spacedust): additive instanced billboard shader + always-included"
```

---

### Task 2: `SpaceDustField` singleton skeleton

**Files:**
- Create: `Assets/3 - Scripts/World/SpaceDustField.cs`

- [ ] **Step 1: Write the skeleton (singleton lifecycle only)**

```csharp
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Purely-visual glowing amber "space dust" field. Auto-singleton (mirrors SpaceDustInventory).
/// Renders specks that drift toward the black hole, form spiral arms, thicken near the BH and
/// away from planets, vanish inside atmospheres, and are immune to floating-origin rebases.
/// </summary>
public class SpaceDustField : MonoBehaviour
{
    public static SpaceDustField Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("SpaceDustField");
        DontDestroyOnLoad(go);
        go.AddComponent<SpaceDustField>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Debug.Log("[SpaceDustField] created");
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
```

- [ ] **Step 2: Verify compile**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: no errors.

- [ ] **Step 3: Verify it auto-creates in play mode**

Run: `mcp__coplay-mcp__play_game`, then `mcp__coplay-mcp__get_unity_logs`, then `mcp__coplay-mcp__stop_game`.
Expected: `[SpaceDustField] created` appears once.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/World/SpaceDustField.cs" "Assets/3 - Scripts/World/SpaceDustField.cs.meta"
git commit -m "feat(spacedust): SpaceDustField auto-singleton skeleton"
```

---

### Task 3: Seed the singleton in `MainMenuController` (trap #1)

**Files:**
- Modify: `Assets/3 - Scripts/UI/MainMenuController.cs` (inside `EnsureGameplaySingletonsAsync`, near the other `if (X.Instance == null)` blocks ~lines 533–580)

- [ ] **Step 1: Add the seeding block**

Find a representative block such as:

```csharp
if (Hotbar.Instance == null) { var go = new GameObject("Hotbar"); DontDestroyOnLoad(go); go.AddComponent<Hotbar>(); }
tick("hotbar");           yield return null;
```

Add immediately after it:

```csharp
if (SpaceDustField.Instance == null) { var go = new GameObject("SpaceDustField"); DontDestroyOnLoad(go); go.AddComponent<SpaceDustField>(); }
tick("space dust");       yield return null;
```

- [ ] **Step 2: Verify compile**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/UI/MainMenuController.cs"
git commit -m "feat(spacedust): seed SpaceDustField in EnsureGameplaySingletons (build trap #1)"
```

---

### Task 4: Camera-effect toggle flag in `InputSettings`

**Files:**
- Modify: `Assets/3 - Scripts/Scripts/Game/Controllers/InputSettings.cs` (declaration near other `fx*` flags ~line 177; load near ~line 310; save near ~line 410)

- [ ] **Step 1: Add the declaration** (next to `public bool fxBloom = true;`)

```csharp
public bool fxSpaceDust = true;
```

- [ ] **Step 2: Add the PlayerPrefs load line** (next to the `fxBloom` load line)

```csharp
fxSpaceDust                 = PlayerPrefs.GetInt   (nameof (fxSpaceDust),                 1) != 0;
```

- [ ] **Step 3: Add the PlayerPrefs save line** (next to the `fxBloom` save line)

```csharp
PlayerPrefs.SetInt   (nameof (fxSpaceDust),                 fxSpaceDust                 ? 1 : 0);
```

- [ ] **Step 4: Verify compile**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/Scripts/Game/Controllers/InputSettings.cs"
git commit -m "feat(spacedust): add fxSpaceDust camera-effect flag"
```

---

### Task 5: Full `SpaceDustField` implementation (field + rendering + density + origin-proofing + toggle)

**Files:**
- Modify: `Assets/3 - Scripts/World/SpaceDustField.cs` (replace the whole file)

- [ ] **Step 1: Replace the file with the full implementation**

```csharp
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Purely-visual glowing amber "space dust" field. Auto-singleton (mirrors SpaceDustInventory).
/// A fixed pool of specks lives in a cube that follows the camera. Each speck is world-fixed
/// (parallax) and wraps to the far side of the cube when it exits, giving an endless local field
/// at constant cost. All motion is derived from camera-position-relative-to-the-black-hole, which
/// is invariant under EndlessManager floating-origin rebases, so origin shifts never cause a jump.
/// Per-speck visibility/brightness comes from a density field (BH proximity, planet avoidance,
/// spiral arms + light noise). One additive instanced billboard material, drawn in <=1023 batches.
/// </summary>
public class SpaceDustField : MonoBehaviour
{
    public static SpaceDustField Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("SpaceDustField");
        DontDestroyOnLoad(go);
        go.AddComponent<SpaceDustField>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_glowTex != null) Destroy(_glowTex);
        if (_material != null) Destroy(_material);
        if (_mesh != null) Destroy(_mesh);
    }

    // ---- runtime refs ----
    Camera _cam;
    CelestialBody _blackHole;
    readonly List<CelestialBody> _planets = new List<CelestialBody>();
    readonly Dictionary<CelestialBody, float> _atmoRadius = new Dictionary<CelestialBody, float>();
    InputSettings _input;
    int _inputRefindCooldown;

    // ---- origin-invariant parallax state ----
    bool _hasPrevRel;
    Vector3 _prevCamRelBH;

    // ---- particle pool (local offset from camera) ----
    Vector3[] _local;
    float[] _threshold;
    float[] _sizeRand;
    float[] _phase;

    // ---- render buffers ----
    Matrix4x4[] _matrices;
    Vector4[] _colors;
    MaterialPropertyBlock _mpb;
    Mesh _mesh;
    Material _material;
    Texture2D _glowTex;
    static readonly int _ColorID = Shader.PropertyToID("_Color");
    bool _initialized;

    void LateUpdate()
    {
        // --- toggle (throttled re-find; default ON if InputSettings not found) ---
        if (_input == null && --_inputRefindCooldown <= 0)
        {
            _input = FindObjectOfType<InputSettings>();
            _inputRefindCooldown = 60;
        }
        if (_input != null && !_input.fxSpaceDust) return;

        // --- camera ---
        if (_cam == null) _cam = Camera.main;
        if (_cam == null && Camera.allCamerasCount > 0) _cam = Camera.allCameras[0];
        if (_cam == null) return;

        // --- bodies / black hole ---
        var bodies = NBodySimulation.Bodies;
        if (bodies == null || bodies.Length == 0) return;
        if (_blackHole == null || _planets.Count == 0) RefreshBodies(bodies);
        if (_blackHole == null) return;

        EnsureInit();
        if (_material == null) return;

        Vector3 camPos = _cam.transform.position;
        Vector3 bhPos = _blackHole.Position;

        // On a planet surface? nothing would be visible — skip the whole draw.
        if (IsInsideAnyAtmosphere(camPos)) return;

        // --- genuine, origin-shift-invariant camera delta (relative to the BH) ---
        Vector3 camRelBH = camPos - bhPos;          // invariant under EndlessManager rebase
        Vector3 genuineDelta = Vector3.zero;
        if (_hasPrevRel)
        {
            genuineDelta = camRelBH - _prevCamRelBH;
            if (genuineDelta.magnitude > sanityThreshold) genuineDelta = Vector3.zero; // glitch guard
        }
        _prevCamRelBH = camRelBH;
        _hasPrevRel = true;

        float dt = Time.deltaTime;
        float L = boxSize;
        float half = L * 0.5f;
        float fadeStart = half * (1f - edgeFadeFrac);
        float t = Time.time;

        int m = 0;
        for (int i = 0; i < _local.Length; i++)
        {
            Vector3 lp = _local[i];
            Vector3 wp = camPos + lp;

            // slow drift toward BH + world-fixed parallax
            Vector3 toBH = bhPos - wp;
            float toBHmag = toBH.magnitude;
            if (toBHmag > 0.001f) lp += (toBH / toBHmag) * driftSpeed * dt;
            lp -= genuineDelta;

            // toroidal wrap into [-half, half]
            lp.x -= L * Mathf.Round(lp.x / L);
            lp.y -= L * Mathf.Round(lp.y / L);
            lp.z -= L * Mathf.Round(lp.z / L);
            _local[i] = lp;

            wp = camPos + lp;

            // density at this world position
            float d = DensityAt(wp, bhPos);
            if (d <= _threshold[i]) continue;

            // spherical edge fade (hides cube corners + wrap pops)
            float lpMag = lp.magnitude;
            if (lpMag >= half) continue;
            float edge = 1f - Mathf.SmoothStep(fadeStart, half, lpMag);
            if (edge <= 0.001f) continue;

            float vis = Mathf.SmoothStep(_threshold[i], Mathf.Min(1f, _threshold[i] + 0.15f), d);
            float tw = 1f - twinkleAmount + twinkleAmount * (0.5f + 0.5f * Mathf.Sin(t * twinkleSpeed + _phase[i]));
            float b = brightness * vis * edge * tw;
            if (b <= 0.004f) continue;

            float size = glowSize * _sizeRand[i];
            _matrices[m] = Matrix4x4.TRS(wp, Quaternion.identity, new Vector3(size, size, size));
            Color col = Color.Lerp(amberWarm, amberBright, Mathf.Clamp01(d));
            _colors[m] = new Vector4(col.r, col.g, col.b, b);

            m++;
            if (m == 1023) { Flush(m); m = 0; }
        }
        if (m > 0) Flush(m);
    }

    void Flush(int count)
    {
        _mpb.Clear();
        _mpb.SetVectorArray(_ColorID, _colors);
        Graphics.DrawMeshInstanced(_mesh, 0, _material, _matrices, count, _mpb,
            ShadowCastingMode.Off, false);
    }

    // ---- density field ∈ [0,1] ----
    float DensityAt(Vector3 wp, Vector3 bhPos)
    {
        // planet avoidance: 0 inside any atmosphere, rising to 1 away from the nearest planet
        float fPlanet = 1f;
        for (int i = 0; i < _planets.Count; i++)
        {
            var p = _planets[i];
            if (p == null) continue;
            float ar = AtmosphereRadius(p);
            float dist = Vector3.Distance(wp, p.Position);
            if (dist <= ar) return 0f;
            float f = Mathf.Clamp01((dist - ar) / Mathf.Max(1f, planetFalloff));
            if (f < fPlanet) fPlanet = f;
        }

        // black-hole proximity boost (1 near BH, 0 far)
        float R = Vector3.Distance(wp, bhPos);
        float fbh = 1f - Mathf.Clamp01((R - bhInnerRadius) / Mathf.Max(1f, bhOuterRadius - bhInnerRadius));

        // logarithmic spiral arms in the BH equatorial (XZ) plane
        Vector3 rel = wp - bhPos;
        float rho = Mathf.Sqrt(rel.x * rel.x + rel.z * rel.z);
        float theta = Mathf.Atan2(rel.z, rel.x);
        float armPhase = theta - armTwist * Mathf.Log(rho + 1f) * 0.01f;
        float arm = 0.5f + 0.5f * Mathf.Cos(armPhase * armCount);
        arm = Mathf.Pow(Mathf.Clamp01(arm), armSharpness);
        float n = Mathf.PerlinNoise(rho * 0.0008f, theta * 1.5f + armPhase) - 0.5f; // light filaments
        arm = Mathf.Clamp01(arm + n * filamentStrength);
        float vert = Mathf.Exp(-(rel.y * rel.y) / Mathf.Max(1f, diskThickness * diskThickness));
        float armFactor = Mathf.Lerp(armFloor, 1f, arm * vert);

        float d = fPlanet * Mathf.Clamp01(baseDensity + bhBoost * fbh) * armFactor;
        return Mathf.Clamp01(d);
    }

    bool IsInsideAnyAtmosphere(Vector3 wp)
    {
        for (int i = 0; i < _planets.Count; i++)
        {
            var p = _planets[i];
            if (p == null) continue;
            if (Vector3.Distance(wp, p.Position) <= AtmosphereRadius(p)) return true;
        }
        return false;
    }

    void RefreshBodies(CelestialBody[] bodies)
    {
        _planets.Clear();
        _blackHole = null;
        foreach (var b in bodies)
        {
            if (b == null) continue;
            if (b.isStaticAttractor) { if (_blackHole == null) _blackHole = b; }
            else _planets.Add(b);
        }
    }

    // ---- atmosphere radius (cached; reflection so we never touch/break the forbidden zone) ----
    float AtmosphereRadius(CelestialBody b)
    {
        if (_atmoRadius.TryGetValue(b, out float r)) return r;
        r = ComputeAtmosphereRadius(b);
        _atmoRadius[b] = r;
        return r;
    }

    float ComputeAtmosphereRadius(CelestialBody b)
    {
        float fallback = b.radius * atmosphereFallbackMultiplier;
        try
        {
            MonoBehaviour gen = null;
            var comps = b.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (var c in comps)
                if (c != null && c.GetType().Name == "CelestialBodyGenerator") { gen = c; break; }
            if (gen == null) return fallback;

            object settings = GetMember(gen, "body");
            object shading = settings != null ? GetMember(settings, "shading") : null;
            object atmo = shading != null ? GetMember(shading, "atmosphereSettings") : null;
            if (atmo == null) return fallback; // body has no atmosphere settings
            object scaleObj = GetMember(atmo, "atmosphereScale");
            if (scaleObj is float scale) return (1f + scale) * b.radius;
        }
        catch { /* fall through */ }
        return fallback;
    }

    static object GetMember(object obj, string name)
    {
        if (obj == null) return null;
        var tp = obj.GetType();
        var f = tp.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (f != null) return f.GetValue(obj);
        var p = tp.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (p != null) return p.GetValue(obj);
        return null;
    }

    // ---- init: pool + mesh + material + glow texture ----
    void EnsureInit()
    {
        if (_initialized) return;
        _initialized = true;

        _local = new Vector3[particleCount];
        _threshold = new float[particleCount];
        _sizeRand = new float[particleCount];
        _phase = new float[particleCount];
        float half = boxSize * 0.5f;
        for (int i = 0; i < particleCount; i++)
        {
            _local[i] = new Vector3(Random.Range(-half, half), Random.Range(-half, half), Random.Range(-half, half));
            _threshold[i] = Random.value;
            _sizeRand[i] = Random.Range(1f - sizeJitter, 1f + sizeJitter);
            _phase[i] = Random.Range(0f, 6.2831853f);
        }

        _matrices = new Matrix4x4[1023];
        _colors = new Vector4[1023];
        _mpb = new MaterialPropertyBlock();
        BuildMeshAndMaterial();
    }

    void BuildMeshAndMaterial()
    {
        _mesh = new Mesh { name = "SpaceDustQuad" };
        _mesh.vertices = new[]
        {
            new Vector3(-0.5f,-0.5f,0f), new Vector3(0.5f,-0.5f,0f),
            new Vector3(0.5f, 0.5f,0f), new Vector3(-0.5f, 0.5f,0f)
        };
        _mesh.uv = new[] { new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1) };
        _mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f); // never frustum-cull the batch

        const int S = 64;
        _glowTex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        Vector2 c = new Vector2((S - 1) * 0.5f, (S - 1) * 0.5f);
        float maxd = S * 0.5f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dd = Vector2.Distance(new Vector2(x, y), c) / maxd;
                float a = Mathf.Clamp01(1f - dd);
                a = a * a * a; // soft radial falloff
                _glowTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        _glowTex.Apply();

        Shader sh = Shader.Find("Custom/SpaceDust");
        if (sh == null) { Debug.LogError("[SpaceDustField] Custom/SpaceDust shader not found (add it to Always Included Shaders)"); return; }
        _material = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        _material.enableInstancing = true;
        _material.mainTexture = _glowTex;
    }

    // ================= tuning (appended at END per conventions) =================
    [Header("Budget")]
    [SerializeField] int particleCount = 5000;
    [SerializeField] float boxSize = 1200f;            // L: cube side around the camera

    [Header("Motion")]
    [SerializeField] float driftSpeed = 4f;            // m/s toward the BH
    [SerializeField] float sanityThreshold = 900f;     // single-frame delta guard (< EndlessManager's 1000)

    [Header("Density")]
    [SerializeField] float baseDensity = 0.35f;        // deep-space baseline ("noticeable field")
    [SerializeField] float bhBoost = 0.65f;            // extra density near the BH
    [SerializeField] float bhOuterRadius = 30000f;     // distance where BH influence begins
    [SerializeField] float bhInnerRadius = 5000f;      // ~just outside the event horizon (BH radius 4000)
    [SerializeField] float atmosphereFallbackMultiplier = 1.35f;
    [SerializeField] float planetFalloff = 600f;       // fade band beyond a planet's atmosphere

    [Header("Spiral arms")]
    [SerializeField] int armCount = 2;
    [SerializeField] float armTwist = 8f;
    [SerializeField] float armSharpness = 1.4f;
    [SerializeField] float armFloor = 0.45f;           // inter-arm density floor
    [SerializeField] float diskThickness = 8000f;      // vertical falloff of the arm disk
    [SerializeField] float filamentStrength = 0.25f;   // light turbulence

    [Header("Look")]
    [SerializeField] Color amberWarm = new Color(1f, 0.55f, 0.18f);
    [SerializeField] Color amberBright = new Color(1f, 0.82f, 0.45f);
    [SerializeField] float glowSize = 6f;
    [SerializeField] float sizeJitter = 0.6f;
    [SerializeField] float brightness = 1.4f;
    [SerializeField] float twinkleAmount = 0.3f;
    [SerializeField] float twinkleSpeed = 1.5f;
    [SerializeField] float edgeFadeFrac = 0.18f;       // fraction of half-box where specks fade out
}
```

- [ ] **Step 2: Verify compile**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: no errors.

- [ ] **Step 3: Visual verification — specks render in space**

Run: `mcp__coplay-mcp__play_game`. Move the player/ship up out of the atmosphere into open space. Use `mcp__coplay-mcp__scene_view_functions` (or a Game-view capture) to confirm: amber glowing specks are visible in open space.
Expected: a "noticeable" field of warm amber glow specks around the camera; none visible while inside the atmosphere / on the surface.
(If nothing shows: check `get_unity_logs` for the shader-not-found error, and confirm Task 1 Step 3 ran.)

- [ ] **Step 4: Visual verification — parallax + origin-shift stability**

Still in play mode: fly forward at speed. Specks should stream past (world-fixed parallax), not move with you. Then fly continuously in one direction past the floating-origin threshold (>1000 units) so `EndlessManager` rebases.
Expected: at the moment of the origin shift, the dust does NOT visibly jump/shift — it stays put relative to the world. (Watch `get_unity_logs` for any `EndlessManager` rebase log to time it.)

- [ ] **Step 5: Visual verification — escalation toward the BH & spiral arms**

Fly toward the black hole. Expected: dust gradually thickens; spiral-arm banding becomes visible; densest near the BH. Fly back out near a planet: dust thins and disappears as you enter the atmosphere.

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/World/SpaceDustField.cs"
git commit -m "feat(spacedust): full field — parallax, spiral-arm density, atmosphere cull, origin-proof"
```

---

### Task 6: CAMERA-tab on/off toggle

**Files:**
- Modify: `Assets/3 - Scripts/UI/TabbedPauseMenu.cs` (CAMERA TabDef, ~lines 535–581)

- [ ] **Step 1: Add the toggle row**

Find the CAMERA tab's `LENS CHARACTER` group with the `BLOOM` row:

```csharp
new HeaderDef { label = "LENS CHARACTER" },
new ToggleDef { label = "BLOOM",              get = () => _input != null && _input.fxBloom,             set = v => { if (_input != null) _input.fxBloom = v; } },
```

Add a new row (a sensible spot is near the top of the CAMERA tab, e.g. just after the `LENS CHARACTER` header or under a `WORLD` header if one exists):

```csharp
new ToggleDef { label = "SPACE DUST",         get = () => _input != null && _input.fxSpaceDust,         set = v => { if (_input != null) _input.fxSpaceDust = v; } },
```

- [ ] **Step 2: Verify compile**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: no errors.

- [ ] **Step 3: Visual verification — toggle works**

Run: `mcp__coplay-mcp__play_game`, fly into open space (dust visible), open the pause menu → CAMERA tab → toggle `SPACE DUST` off.
Expected: dust disappears immediately; toggling back on restores it. Confirm the setting persists across a stop/replay (PlayerPrefs).

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/UI/TabbedPauseMenu.cs"
git commit -m "feat(spacedust): CAMERA-tab SPACE DUST on/off toggle"
```

---

### Task 7: Build verification (traps #1 & shader-variant)

**Files:** none (verification only)

- [ ] **Step 1: Make a build and launch it**

Build the project (Editor: File → Build, or the project's existing build path producing `Solar System 2.exe`). Launch the build, start a game (boots in `MainMenu.unity` → loads `1.6.7.7.7.unity`).

- [ ] **Step 2: Verify in the build**

Fly out into open space.
Expected (all three confirm the known traps are handled):
1. The amber dust renders (shader variant shipped — Task 1 Step 3 worked).
2. It renders without needing to press Play in the gameplay scene first (singleton seeded via `EnsureGameplaySingletons` — trap #1).
3. Origin shifts cause no visible jump.

If the dust is missing in the build but present in the Editor: re-check Always Included Shaders (Task 1 Step 3) and the `EnsureGameplaySingletons` seed (Task 3).

- [ ] **Step 3: Final tuning pass (optional)**

In the Editor, select the runtime `SpaceDustField` object (or set defaults in the Inspector before play) and tune `particleCount`, `baseDensity`, `bhBoost`, `armCount`, `glowSize`, `brightness` to taste ("crank it later" is expected here). No code change needed — all fields are serialized.

- [ ] **Step 4: Final commit (if tuning defaults changed)**

```bash
git add "Assets/3 - Scripts/World/SpaceDustField.cs"
git commit -m "tune(spacedust): default density/look pass"
```

---

## Self-review notes (addressed)

- **Spec coverage:** amber look (Task 5 `amberWarm/Bright`), galaxy arms + light filaments (`DensityAt` arm/filament), noticeable baseline (`baseDensity`), escalation toward BH (`bhBoost`/`bhInnerRadius`), atmosphere cull (`IsInsideAnyAtmosphere`/`DensityAt` early `return 0`), pure parallax (`genuineDelta`, no streaking), floating-origin invariance (BH-relative delta + glitch guard, Tasks 5 Steps 4 & Task 7), toggle (Tasks 4 & 6), singleton + trap #1 (Tasks 2–3 & Task 7), shader-variant build risk (Task 1 Step 3 + Task 7), no save/reset coupling (none added), forbidden zone untouched (reflection-only atmosphere read).
- **Type consistency:** `fxSpaceDust` used identically in Tasks 4/5/6; `Custom/SpaceDust` shader name identical in Tasks 1/5; `SpaceDustField` GameObject name identical in Tasks 2/3; `_ColorID`/`_Color` match the shader's instanced prop.
- **No placeholders:** every code step is complete and compilable.
