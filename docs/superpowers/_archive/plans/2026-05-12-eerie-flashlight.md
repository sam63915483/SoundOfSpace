# Eerie Flashlight Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the single Unity Spot light "flashlight" with a four-layer eerie/dusty-horror lighting effect: visible volumetric beam cone, dust particles drifting in the beam, bloomy halo at the source, and behavioral flicker + walking sway — while preserving the existing toggle (E / controller Y) and scroll-wheel brightness control.

**Architecture:** A new transparent-additive beam-cone shader (queue 2700, above the atmosphere cutoff) renders a procedurally-built cone mesh that's child of the flashlight Transform. A scene-authored `ParticleSystem` and a Unity `LensFlare` (using the existing `Halogen Bulb.flare` asset) live as siblings. `PlayerFlashlight.cs` grows from 75 to ~250 lines, embedding flicker + sway modules and ticking all four layers each frame.

**Tech Stack:** Unity 2022.3 built-in render pipeline, single CG/HLSL surface-style shader, `Mathf.PerlinNoise` flicker, `Mathf.Sin` walk-bob sway from `PlayerController.SurfaceVelocity`.

**Spec:** `docs/superpowers/specs/2026-05-12-eerie-flashlight-design.md`

**Verification model:** Unity project with no automated test framework (per `CLAUDE.md` — "all iteration happens in the Editor"). Each task verifies via **compile-clean Console + Editor Play-mode procedure** specific to that task.

---

## Task 1 · Beam cone shader

**Files:**
- Create: `Assets/2 - Materials/FlashlightBeamCone.shader`

This is the shader the beam mesh uses. Transparent additive blend, queue baked into `SubShader Tags` so Unity's StandardShaderGUI doesn't reset it (mirrors the working pattern in `Assets/sFuture Modules Pro/Materials/Glass_EarlyQueue.shader`).

- [ ] **Step 1: Create the shader file**

Path: `Assets/2 - Materials/FlashlightBeamCone.shader`

```hlsl
Shader "Custom/FlashlightBeamCone" {
    Properties {
        _Color ("Color", Color) = (1.0, 0.94, 0.78, 1.0)
        _InnerStrength ("Inner Strength", Range(0, 2)) = 1.2
        _OuterStrength ("Outer Strength", Range(0, 1)) = 0.0
        _FalloffPower ("Distance Falloff Power", Range(0.5, 5)) = 2.0
        _NoiseScale ("Noise Scale", Range(0, 20)) = 4.0
        _NoiseStrength ("Noise Strength", Range(0, 1)) = 0.35
    }

    SubShader {
        // Queue baked into Tags so StandardShaderGUI cannot reset it on us.
        // 2700 (Transparent-300) is above the 2500 atmosphere cutoff, so the
        // beam renders AFTER the opaque atmosphere pass and visibly cuts
        // through atmospheric haze. See CLAUDE.md "Atmosphere / ocean post-
        // process gotcha".
        Tags { "Queue"="Transparent-300" "RenderType"="Transparent" "IgnoreProjector"="True" }

        Cull Off
        ZWrite Off
        Blend One One   // additive

        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };
            struct v2f {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            fixed4 _Color;
            float _InnerStrength;
            float _OuterStrength;
            float _FalloffPower;
            float _NoiseScale;
            float _NoiseStrength;

            v2f vert (appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Cheap 2D value noise (no texture sample needed).
            float hash21(float2 p) {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }
            float vnoise(float2 p) {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            fixed4 frag (v2f i) : SV_Target {
                // UV.y = 0 at apex, 1 at far end of beam.
                float v = saturate(i.uv.y);

                // Strength interpolates inner -> outer along the length,
                // then drops via the falloff curve so the tail vanishes.
                float strength = lerp(_InnerStrength, _OuterStrength, v)
                               * pow(1.0 - v, _FalloffPower);

                // Noise modulation gives a faint dust/gobo texture in the beam.
                float n = vnoise(float2(i.uv.x * _NoiseScale * 6.2831, i.uv.y * _NoiseScale));
                strength *= 1.0 + (n - 0.5) * _NoiseStrength;

                return _Color * strength;
            }
            ENDCG
        }
    }
}
```

- [ ] **Step 2: Verify Unity compiles it cleanly**

In the Unity Editor: select the shader file in the Project window. The Inspector should show "Custom/FlashlightBeamCone" with no compile errors at the top.

Open the Console window (`Window → General → Console`). Confirm there are no red error entries mentioning `FlashlightBeamCone`.

- [ ] **Step 3: Commit**

```
git add "Assets/2 - Materials/FlashlightBeamCone.shader" "Assets/2 - Materials/FlashlightBeamCone.shader.meta"
git commit -m "feat(flashlight): add additive beam-cone shader

Transparent additive shader with queue 2700 baked into SubShader Tags
(per CLAUDE.md atmosphere-queue gotcha and the Glass_EarlyQueue pattern).
Inner/outer strength + distance falloff + noise modulation for a dusty
volumetric beam.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 2 · Beam material asset

**Files:**
- Create: `Assets/2 - Materials/FlashlightBeam.mat` (manual via Unity Editor)

The shader needs a material instance assigned to the cone mesh's `MeshRenderer`.

- [ ] **Step 1: Create the material in Unity**

In the Unity Project window:
1. Navigate to `Assets/2 - Materials/`.
2. Right-click → `Create → Material`.
3. Name it `FlashlightBeam`.

- [ ] **Step 2: Assign the shader**

Select the new `FlashlightBeam.mat`. In the Inspector, click the shader dropdown (top right) → `Custom → FlashlightBeamCone`.

- [ ] **Step 3: Tune defaults**

In the Inspector, set:
- **Color**: RGB `(1.0, 0.94, 0.78)`, A `1.0` (warm white)
- **Inner Strength**: `1.2`
- **Outer Strength**: `0.0`
- **Distance Falloff Power**: `2.0`
- **Noise Scale**: `4.0`
- **Noise Strength**: `0.35`

(All values are tweakable in Play mode later; these are starting points from the spec.)

- [ ] **Step 4: Commit**

```
git add "Assets/2 - Materials/FlashlightBeam.mat" "Assets/2 - Materials/FlashlightBeam.mat.meta"
git commit -m "feat(flashlight): add FlashlightBeam material using the beam-cone shader

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 3 · `PlayerFlashlight.cs` — fields, refs, cone mesh builder, visibility helper

**Files:**
- Modify: `Assets/3 - Scripts/Player/PlayerFlashlight.cs` (replace entire contents)

Add all new public inspector fields, the procedural cone-mesh builder, and the visual-layer visibility helper. Behavior modules (flicker, sway) come in later tasks. After this task the script compiles and the existing toggle + scroll behavior still works; the beam mesh appears as a flat cone shape when the light is on.

- [ ] **Step 1: Replace the file**

Path: `Assets/3 - Scripts/Player/PlayerFlashlight.cs`

```csharp
using UnityEngine;

public class PlayerFlashlight : MonoBehaviour
{
    [Header("Light")]
    [Tooltip("The Light component on the player that acts as the flashlight. If left empty, an existing child Spot light is auto-found.")]
    public Light flashlight;
    [Tooltip("Toggle key.")]
    public KeyCode toggleKey = KeyCode.E;
    [Tooltip("Optional light cookie texture applied to the spot light at Start. Leave null for no cookie.")]
    public Texture lightCookie;

    [Header("Brightness")]
    public float minBrightness = 0.5f;
    public float maxBrightness = 8f;
    public float scrollSensitivity = 20f;

    [Header("Beam Cone")]
    [Tooltip("Child MeshFilter the procedural cone mesh is written into.")]
    public MeshFilter beamMeshFilter;
    [Tooltip("Renderer toggled with the light's enabled state.")]
    public MeshRenderer beamRenderer;
    public float coneLength = 30f;
    [Tooltip("Full cone angle in degrees (matches the Light's Spot Angle for visual coherence).")]
    public float coneAngleDegrees = 50f;
    [Range(3, 64)] public int coneSegments = 16;

    [Header("Dust Particles")]
    public ParticleSystem dustParticles;
    public float dustRateScale = 1f;

    [Header("Halo")]
    public LensFlare halo;
    public float haloIntensityMultiplier = 1f;

    // --- runtime state ---
    Ship _ship;

    float _lastConeLength = -1f;
    float _lastConeAngle = -1f;

    void Start()
    {
        if (flashlight == null)
        {
            var lights = GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null && lights[i].type == LightType.Spot)
                {
                    flashlight = lights[i];
                    break;
                }
            }
        }
        if (flashlight != null)
        {
            flashlight.enabled = false;
            // Force per-pixel rendering so the spotlight's cone stays tight.
            // Without this, Unity's built-in pipeline can demote the flashlight
            // to vertex shading at certain camera angles when other scene lights
            // (sun, ambient caster, bonfires) push it out of the limited
            // pixel-light budget — the demotion makes the focused cone look
            // like a flat wash spread over a wide area.
            flashlight.renderMode = LightRenderMode.ForcePixel;
            if (lightCookie != null) flashlight.cookie = lightCookie;
            // Keep the visible beam cone aligned with the real lit area.
            flashlight.spotAngle = coneAngleDegrees;
            flashlight.range = Mathf.Max(flashlight.range, coneLength);
        }
        _ship = FindObjectOfType<Ship>();

        BuildBeamConeMesh();
        SetVisualLayersVisible(false);
    }

    void Update()
    {
        if (flashlight == null) return;

        bool piloting = _ship != null && _ship.IsPiloted;
        if (piloting)
        {
            if (flashlight.enabled)
            {
                flashlight.enabled = false;
                SetVisualLayersVisible(false);
            }
            return;
        }

        // Configurable keyboard key OR controller Y button.
        if (TutorialGate.GetKeyDown(toggleKey, TutorialAbility.Flashlight) ||
            TutorialGate.FlashlightPressed(TutorialAbility.Flashlight))
        {
            flashlight.enabled = !flashlight.enabled;
            SetVisualLayersVisible(flashlight.enabled);
        }

        if (!flashlight.enabled) return;

        // Scroll-wheel brightness (unchanged behavior).
        float scroll = TutorialGate.GetAxis("Mouse ScrollWheel", TutorialAbility.Flashlight);
        if (Mathf.Abs(scroll) > 0.0001f)
            flashlight.intensity = Mathf.Clamp(flashlight.intensity + scroll * scrollSensitivity, minBrightness, maxBrightness);
        else
            flashlight.intensity = Mathf.Clamp(flashlight.intensity, minBrightness, maxBrightness);

        // Rebuild cone mesh on inspector change (no-op when values are stable).
        if (!Mathf.Approximately(coneLength, _lastConeLength) ||
            !Mathf.Approximately(coneAngleDegrees, _lastConeAngle))
        {
            BuildBeamConeMesh();
            flashlight.spotAngle = coneAngleDegrees;
            if (flashlight.range < coneLength) flashlight.range = coneLength;
        }
    }

    void SetVisualLayersVisible(bool visible)
    {
        if (beamRenderer != null) beamRenderer.enabled = visible;
        if (dustParticles != null)
        {
            if (visible) dustParticles.Play(true);
            else dustParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        if (halo != null) halo.enabled = visible;
    }

    void BuildBeamConeMesh()
    {
        if (beamMeshFilter == null) return;
        _lastConeLength = coneLength;
        _lastConeAngle = coneAngleDegrees;

        int seg = Mathf.Max(3, coneSegments);
        float halfAngleRad = coneAngleDegrees * 0.5f * Mathf.Deg2Rad;
        float baseRadius = Mathf.Tan(halfAngleRad) * coneLength;

        // Apex at origin (light position), open base ring at +Z = coneLength.
        // (seg + 1) base verts so the UV.x seam doesn't share a vertex —
        // gives a continuous 0..1 sweep of the noise sample around the cone.
        var verts = new Vector3[1 + (seg + 1)];
        var uvs = new Vector2[verts.Length];
        var tris = new int[seg * 3];

        verts[0] = Vector3.zero;
        uvs[0] = new Vector2(0.5f, 0f);

        for (int i = 0; i <= seg; i++)
        {
            float t = (float)i / seg;
            float angle = t * Mathf.PI * 2f;
            verts[1 + i] = new Vector3(Mathf.Cos(angle) * baseRadius, Mathf.Sin(angle) * baseRadius, coneLength);
            uvs[1 + i] = new Vector2(t, 1f);
        }
        for (int i = 0; i < seg; i++)
        {
            tris[i * 3 + 0] = 0;
            tris[i * 3 + 1] = 1 + i;
            tris[i * 3 + 2] = 1 + i + 1;
        }

        Mesh mesh = beamMeshFilter.sharedMesh;
        if (mesh == null || mesh.name != "FlashlightBeamCone")
        {
            mesh = new Mesh { name = "FlashlightBeamCone" };
            beamMeshFilter.sharedMesh = mesh;
        }
        mesh.Clear();
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
    }
}
```

- [ ] **Step 2: Verify the script compiles**

Save the file. In the Unity Editor, watch for the compile spinner in the bottom-right. When it finishes, open the Console (`Window → General → Console`) and confirm there are no red errors.

- [ ] **Step 3: Commit**

```
git add "Assets/3 - Scripts/Player/PlayerFlashlight.cs"
git commit -m "feat(flashlight): add inspector fields, cone mesh builder, visibility helper

Adds public refs for beam mesh, dust particles, and halo lens flare;
procedurally generates the cone mesh in Start (and on inspector change);
hides the new layers when the light is off or while piloting. Existing
toggle + scroll brightness behavior preserved verbatim. Behavior modules
(flicker, sway) come in later commits.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 4 · `PlayerFlashlight.cs` — flicker module

**Files:**
- Modify: `Assets/3 - Scripts/Player/PlayerFlashlight.cs`

Add the three-layer flicker: continuous Perlin micro-drift + periodic panic stutter (6–10 s) + occasional dying-battery dip (35–55 s). Output multipliers compose multiplicatively onto a `_baseIntensity` value (the scroll-wheel-controlled level). Halo brightness syncs to the final value so the bloom dims with every flicker.

- [ ] **Step 1: Add `using` and private state**

At the top of the file, change:

```csharp
using UnityEngine;
```

to:

```csharp
using System.Collections;
using UnityEngine;
```

Just below the `Ship _ship;` line, add:

```csharp
    float _baseIntensity;            // current scroll-wheel-controlled base
    float _stutterMultiplier = 1f;   // 0..1 set by panic coroutine
    float _dyingMultiplier = 1f;     // 0..1 set by dying-battery coroutine
    Coroutine _panicRoutine;
    Coroutine _dyingRoutine;
```

- [ ] **Step 2: Add the Flicker `[Header]` block**

Just above the `// --- runtime state ---` comment, add:

```csharp
    [Header("Flicker")]
    public bool enableFlicker = true;
    [Range(0f, 0.5f)] public float microDriftAmount = 0.04f;
    public Vector2 panicStutterInterval = new Vector2(6f, 10f);
    public Vector2 dyingDipInterval = new Vector2(35f, 55f);

```

- [ ] **Step 3: Capture base intensity in `Start()`**

Inside the existing `if (flashlight != null) { ... }` block in `Start()`, add at the end (after the cookie assignment line):

```csharp
            _baseIntensity = flashlight.intensity;
```

- [ ] **Step 4: Replace the scroll-wheel block in `Update()` with the flicker-aware version**

Find this block in `Update()`:

```csharp
        // Scroll-wheel brightness (unchanged behavior).
        float scroll = TutorialGate.GetAxis("Mouse ScrollWheel", TutorialAbility.Flashlight);
        if (Mathf.Abs(scroll) > 0.0001f)
            flashlight.intensity = Mathf.Clamp(flashlight.intensity + scroll * scrollSensitivity, minBrightness, maxBrightness);
        else
            flashlight.intensity = Mathf.Clamp(flashlight.intensity, minBrightness, maxBrightness);
```

Replace with:

```csharp
        // Scroll-wheel adjusts the BASE intensity. Flicker multipliers
        // ride on top so the bulb can dim even at max scroll.
        float scroll = TutorialGate.GetAxis("Mouse ScrollWheel", TutorialAbility.Flashlight);
        if (Mathf.Abs(scroll) > 0.0001f)
            _baseIntensity = Mathf.Clamp(_baseIntensity + scroll * scrollSensitivity, minBrightness, maxBrightness);
        else
            _baseIntensity = Mathf.Clamp(_baseIntensity, minBrightness, maxBrightness);

        // Continuous tiny shimmer — "the bulb is alive."
        float micro = enableFlicker
            ? 1f - microDriftAmount * (Mathf.PerlinNoise(Time.time * 3f, 0f) - 0.5f)
            : 1f;

        float finalIntensity = _baseIntensity * micro * _stutterMultiplier * _dyingMultiplier;
        flashlight.intensity = finalIntensity;
        if (halo != null) halo.brightness = finalIntensity * haloIntensityMultiplier;
```

- [ ] **Step 5: Add `OnEnable` / `OnDisable` and the two coroutines**

At the end of the class (after `BuildBeamConeMesh()`), add:

```csharp
    void OnEnable()
    {
        _panicRoutine = StartCoroutine(PanicStutterLoop());
        _dyingRoutine = StartCoroutine(DyingBatteryLoop());
    }

    void OnDisable()
    {
        if (_panicRoutine != null) StopCoroutine(_panicRoutine);
        if (_dyingRoutine != null) StopCoroutine(_dyingRoutine);
        _panicRoutine = null;
        _dyingRoutine = null;
        _stutterMultiplier = 1f;
        _dyingMultiplier = 1f;
    }

    // Rare hard glitch — 6–10s gaps between events. Only fires while the
    // flashlight is enabled (skipped otherwise so the player doesn't see
    // flicker on a powered-off torch). Total event duration ~120ms.
    IEnumerator PanicStutterLoop()
    {
        while (true)
        {
            float wait = Random.Range(panicStutterInterval.x, panicStutterInterval.y);
            yield return new WaitForSeconds(wait);
            if (!enableFlicker || flashlight == null || !flashlight.enabled) continue;

            // 1f @ 0.20, 1f @ 1.00, 1f @ 0.40, 1f @ 1.00, 2f @ 0.15, then restore.
            _stutterMultiplier = 0.20f; yield return null;
            _stutterMultiplier = 1.00f; yield return null;
            _stutterMultiplier = 0.40f; yield return null;
            _stutterMultiplier = 1.00f; yield return null;
            _stutterMultiplier = 0.15f; yield return null; yield return null;
            _stutterMultiplier = 1.00f;
        }
    }

    // Occasional slow sag toward darkness then recovery — 35–55s gaps.
    // 1.8s sweep from 1.0 -> 0.45 -> 1.0 with tiny Perlin jitter at the trough.
    IEnumerator DyingBatteryLoop()
    {
        while (true)
        {
            float wait = Random.Range(dyingDipInterval.x, dyingDipInterval.y);
            yield return new WaitForSeconds(wait);
            if (!enableFlicker || flashlight == null || !flashlight.enabled) continue;

            const float duration = 1.8f;
            float t0 = Time.time;
            while (Time.time - t0 < duration)
            {
                float u = (Time.time - t0) / duration;     // 0..1
                // Symmetric V-curve: 1.0 at edges, 0.45 at center, with jitter near trough.
                float v = 1f - Mathf.Sin(u * Mathf.PI) * 0.55f;
                float jitter = (Mathf.PerlinNoise(Time.time * 25f, 0f) - 0.5f) * 0.10f * Mathf.Sin(u * Mathf.PI);
                _dyingMultiplier = Mathf.Clamp01(v + jitter);
                yield return null;
            }
            _dyingMultiplier = 1f;
        }
    }
```

- [ ] **Step 6: Verify the script compiles**

Save. Watch the Unity compile spinner. Open Console; confirm no red errors.

- [ ] **Step 7: Play-mode smoke test**

Enter Play mode. Press `E` to turn on the flashlight. Watch the intensity value on the Light component in the Inspector during play — you should see it micro-drifting around the base value. Wait ~10s; you should observe at least one panic stutter (intensity briefly drops to ~20% of base and recovers in a flash). Wait ~45s for a dying-battery dip (slow ~2s sag and recovery). Exit Play.

- [ ] **Step 8: Commit**

```
git add "Assets/3 - Scripts/Player/PlayerFlashlight.cs"
git commit -m "feat(flashlight): add three-layer flicker (micro-drift + panic + dying)

Perlin micro-drift breathes continuously; a panic-stutter coroutine fires
every 6-10s for a 120ms hard glitch; a dying-battery coroutine fires every
35-55s for a 1.8s slow dim-and-recover. All three compose multiplicatively
onto a base intensity captured at Start and updated by the scroll wheel.
Halo brightness tracks the final intensity so the bloom dims with the bulb.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 5 · `PlayerFlashlight.cs` — walking sway module

**Files:**
- Modify: `Assets/3 - Scripts/Player/PlayerFlashlight.cs`

Read `PlayerController.SurfaceVelocity` (the property at `PlayerController.cs:794` — it's the correct one because `MovePosition`-driven walking is invisible to `rb.velocity`). Smooth-damp the speed into a 0..1 walk factor. Apply a sin-wave pitch/yaw rotation scaled by that factor. Yaw runs at half the pitch frequency so the beam traces a figure-8 on the ground.

- [ ] **Step 1: Add private cached refs**

Below the existing flicker private state (the coroutines block), add:

```csharp
    PlayerController _player;
    Quaternion _baseLocalRot;
    float _walkFactor;
    float _walkVel;
```

- [ ] **Step 2: Add the Sway `[Header]` block**

Just above the `// --- runtime state ---` comment, after the Flicker block, add:

```csharp
    [Header("Sway")]
    public bool enableSway = true;
    [Tooltip("Peak rotation in degrees applied at full walking speed.")]
    public float walkBobAmplitude = 2f;
    public float walkBobFrequency = 7f;
    [Tooltip("Speed at which walk factor reaches 1.0 (m/s).")]
    public float walkingTopSpeed = 4f;

```

- [ ] **Step 3: Capture base rotation and PlayerController in `Start()`**

Inside `Start()`, after the existing `_ship = FindObjectOfType<Ship>();` line, add:

```csharp
        _player = FindObjectOfType<PlayerController>();
        if (flashlight != null) _baseLocalRot = flashlight.transform.localRotation;
```

- [ ] **Step 4: Add sway application in `Update()`**

In `Update()`, after the halo-brightness sync line and before the `// Rebuild cone mesh on inspector change` block, add:

```csharp
        // Walk-driven sway. SurfaceVelocity is the correct source (rb.velocity
        // misses MovePosition-driven walking; see PlayerController.cs:794).
        if (enableSway && _player != null)
        {
            float speed = _player.SurfaceVelocity.magnitude;
            float target = Mathf.Clamp01(speed / Mathf.Max(0.01f, walkingTopSpeed));
            _walkFactor = Mathf.SmoothDamp(_walkFactor, target, ref _walkVel, 0.15f);
            float pitch = Mathf.Sin(Time.time * walkBobFrequency) * walkBobAmplitude * _walkFactor;
            float yaw = Mathf.Sin(Time.time * walkBobFrequency * 0.5f) * walkBobAmplitude * 0.6f * _walkFactor;
            flashlight.transform.localRotation = _baseLocalRot * Quaternion.Euler(pitch, yaw, 0f);
        }
        else
        {
            flashlight.transform.localRotation = _baseLocalRot;
        }

```

- [ ] **Step 5: Verify the script compiles**

Save. Watch the Unity compile spinner. Console: no red errors.

- [ ] **Step 6: Play-mode smoke test**

Enter Play mode. Press `E` to turn on the flashlight. Walk forward — the beam should visibly bob in a figure-8 motion. Stop walking — the bob smoothly decays to zero over ~0.3s. The light is steady when standing still (no idle breath — that was explicitly excluded). Exit Play.

- [ ] **Step 7: Commit**

```
git add "Assets/3 - Scripts/Player/PlayerFlashlight.cs"
git commit -m "feat(flashlight): add walking sway driven by SurfaceVelocity

±2° pitch + ±1.2° yaw sin-wave applied to flashlight.transform.localRotation,
scaled by a 0..1 walk factor that smooth-damps from SurfaceVelocity / topSpeed.
Yaw runs at half the pitch frequency for a figure-8 ground trace.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 6 · `PlayerFlashlight.cs` — dust particle emission scaling

**Files:**
- Modify: `Assets/3 - Scripts/Player/PlayerFlashlight.cs`

The visibility helper from Task 3 already starts/stops the particle system with the light's enabled state. Add one more touch: scale the emission rate with `finalIntensity` so dust thins out when the player dims the bulb.

- [ ] **Step 1: Add emission scaling at the end of `Update()`**

In `Update()`, AFTER the cone-mesh rebuild check (after the `BuildBeamConeMesh();` call inside the `if` block), add:

```csharp
        // Dust emission tracks intensity so the cone "thins out" when dimmed.
        if (dustParticles != null)
        {
            var emission = dustParticles.emission;
            emission.rateOverTimeMultiplier = dustRateScale * Mathf.Clamp01(finalIntensity / Mathf.Max(0.01f, maxBrightness));
        }
```

- [ ] **Step 2: Verify the script compiles**

Save. Watch the Unity compile spinner. Console: no red errors.

- [ ] **Step 3: Commit**

```
git add "Assets/3 - Scripts/Player/PlayerFlashlight.cs"
git commit -m "feat(flashlight): scale dust emission rate with current intensity

Dust thins out when the player dims the flashlight via scroll; matches the
visual coherence pattern of the halo brightness sync.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 7 · Scene setup — author the four visual layers

**Files:**
- Modify: `Assets/1.6.7.7.7.unity` (manual edits via Unity Editor)

The script is fully ready. This task wires it up in the active gameplay scene. The four new layers all live as children of the existing Light GameObject so they inherit its rotation (the sway moves all of them together).

**Reference scene structure to end up with:**

```
Player
└─ [...] FlashlightLight (existing GameObject with the Light component)
    ├─ BeamMesh    (new — MeshFilter + MeshRenderer, FlashlightBeam material)
    ├─ DustParticles (new — ParticleSystem)
    └─ (LensFlare component is added directly onto FlashlightLight; no child needed)
```

- [ ] **Step 1: Find the flashlight GameObject in the scene**

In Unity, open `Assets/1.6.7.7.7.unity`. In the Hierarchy, expand the Player GameObject and locate the existing child GameObject that has the `Light` component (type = Spot). This is the GameObject that `PlayerFlashlight.cs` auto-finds. Call this the "FlashlightLight" GameObject. (The exact name varies — confirm by clicking and checking the Inspector for the `Light` component.)

- [ ] **Step 2: Add the BeamMesh child**

Right-click the FlashlightLight GameObject → `Create Empty`. Rename the new child to `BeamMesh`. Reset its Transform (gear icon → Reset) so `Position = (0,0,0)`, `Rotation = (0,0,0)`, `Scale = (1,1,1)`.

With BeamMesh selected, in the Inspector:
1. Click `Add Component → Mesh Filter`.
2. Click `Add Component → Mesh Renderer`.
3. On the MeshRenderer:
   - **Materials → Element 0**: drag `Assets/2 - Materials/FlashlightBeam.mat` into the slot.
   - **Cast Shadows**: `Off`
   - **Receive Shadows**: unchecked
   - **Light Probes**: `Off`
   - **Reflection Probes**: `Off`

(The MeshFilter's Mesh slot stays empty — `PlayerFlashlight.BuildBeamConeMesh()` writes into `sharedMesh` at runtime.)

- [ ] **Step 3: Add the DustParticles child**

Right-click FlashlightLight → `Effects → Particle System`. Rename to `DustParticles`. Reset its Transform.

In the Inspector, configure the ParticleSystem modules:

**Main module:**
- Duration: `5`
- Looping: ✓
- Start Lifetime: `4`
- Start Speed: `0.3`
- Start Size: random between two constants, `0.04` and `0.10`
- Start Color: `(255, 240, 200, 200)` (warm white, ~78% alpha)
- Gravity Modifier: `0`
- Simulation Space: `Local`
- Max Particles: `40`
- Play On Awake: unchecked (the script will `.Play()` it)

**Emission module:**
- Rate over Time: `10`

**Shape module:**
- Shape: `Cone`
- Angle: `25` (matches the half-angle of the default 50° cone)
- Radius: `0.05`
- Length: `coneLength` is set on the script; for the particle shape use Radius Thickness `1`, Arc `360`.
- Position Z offset: `0` (apex at the light position)

**Color over Lifetime:**
- ✓ Enable
- Alpha curve: starts at `0`, ramps to `1` at 20%, holds, ramps back to `0` at 100%. (Click the gradient → set the alpha keys.)

**Size over Lifetime:**
- ✓ Enable
- Size curve: a gentle bell, `0.7` at 0%, `1.0` at 50%, `0.7` at 100%.

**Renderer module:**
- Render Mode: `Billboard`
- Material: leave the default `Default-Particle` material that Unity auto-assigns when the ParticleSystem is created. (It's the small soft-white circle that ships built-in via `Resources/unity_builtin_extra` — visible in the Renderer module's Material slot immediately after creation.) If for some reason the slot is empty, click it → search for `Default-Particle` and select.
- Sort Mode: `By Distance`
- Min Particle Size: `0`
- Max Particle Size: `0.5`

- [ ] **Step 4: Add the LensFlare component to FlashlightLight**

Select the FlashlightLight GameObject (NOT the BeamMesh child). In the Inspector → `Add Component → Effects → Lens Flare`.

Configure:
- **Flare**: drag `Assets/Lens Flares/Halogen Bulb.flare` into the slot.
- **Color**: `(255, 240, 210)` (warm white)
- **Brightness**: `1`  (script will overwrite this per frame at runtime)
- **Fade Speed**: `3`
- **Ignore Layers**: leave default
- **Directional**: unchecked

- [ ] **Step 5: Wire references onto the `PlayerFlashlight` component**

Select the Player GameObject (the one with the `PlayerFlashlight` script). In the Inspector, find the PlayerFlashlight component and fill in:

- **Light** group:
  - `Flashlight`: leave empty — auto-found at Start (the existing Spot light child).
  - `Light Cookie`: leave empty for v1. (Optional: drag in a soft-radial-gradient texture if you want a gobo on the surfaces.)

- **Beam Cone** group:
  - `Beam Mesh Filter`: drag the `BeamMesh` GameObject's `MeshFilter` into the slot.
  - `Beam Renderer`: drag the `BeamMesh` GameObject's `MeshRenderer` into the slot.
  - Leave `coneLength`, `coneAngleDegrees`, `coneSegments` at defaults (30, 50, 16).

- **Dust Particles** group:
  - `Dust Particles`: drag the `DustParticles` GameObject (the ParticleSystem) into the slot.
  - Leave `dustRateScale` at `1`.

- **Halo** group:
  - `Halo`: drag the FlashlightLight GameObject into the slot — Unity will resolve to its `LensFlare` component automatically.
  - Leave `haloIntensityMultiplier` at `1`.

(Flicker and Sway groups have no inspector refs — defaults are correct.)

- [ ] **Step 6: Save the scene**

In Unity: `File → Save` (or `Ctrl+S`). Watch the title-bar `*` indicator clear.

- [ ] **Step 7: Commit**

```
git add "Assets/1.6.7.7.7.unity"
git commit -m "scene(flashlight): wire beam mesh, dust particles, and lens flare halo

Added BeamMesh + DustParticles children under the player's spot light, and
a LensFlare component using Halogen Bulb.flare. All references plumbed
into the PlayerFlashlight script on the Player.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 8 · Final Play-mode verification

**Files:** none modified

Run the full feature exercise to confirm every spec requirement holds.

- [ ] **Step 1: Enter Play mode**

Open `Assets/1.6.7.7.7.unity`. Click Play. Wait for the scene to settle.

- [ ] **Step 2: Toggle the flashlight on**

Press `E`. Confirm:
- The beam mesh appears as a warm-white visible cone in front of the player.
- Dust particles drift through the cone (slow, sparse, ~10–25 visible at any time).
- A bright halo blob appears at the lamp head (Halogen Bulb.flare).
- Surfaces in the cone are illuminated by the actual Light.

- [ ] **Step 3: Verify scroll-wheel brightness**

Scroll the mouse wheel down. The light dims; dust thins out; halo bloom shrinks. Scroll up; everything restores. Both inputs respect the `minBrightness` (0.5) and `maxBrightness` (8) clamps.

- [ ] **Step 4: Verify walking sway**

Walk forward. The beam wobbles in a clear figure-8 motion. Stop. The sway decays smoothly to zero over ~0.3s.

- [ ] **Step 5: Verify flicker**

Stand still with the flashlight on for ~90 seconds while watching the cone:
- Continuously: the cone has a faint shimmer (Perlin micro-drift).
- Several times: a sharp ~120ms glitch (panic stutter) — beam drops to ~20% and snaps back.
- Once or twice: a slow ~2s sag-and-recover (dying battery dip).

- [ ] **Step 6: Verify toggle-off hides all layers**

Press `E` to turn the light off. Confirm:
- Beam mesh disappears.
- Dust particles stop emitting and clear (no leftover particles drift through).
- Halo bloom disappears.

- [ ] **Step 7: Verify piloting hides all layers**

Press `E` to turn the light back on. Walk to the ship and pilot it. While piloting, confirm:
- All four flashlight layers are hidden, regardless of what `flashlight.enabled` was before entering the ship.

Exit the ship. Confirm the flashlight returns to its prior state (or off, if it was off before piloting).

- [ ] **Step 8: Exit Play mode**

Click Play to stop. Confirm the Console has no red errors or warnings introduced by the flashlight system across the entire session.

- [ ] **Step 9: Commit final-pass marker (optional)**

This task changes no files. If anything was tweaked during verification (cone angle, flicker intervals, dust rate, etc.), commit those individually with a `polish(flashlight): ...` message. Otherwise this task ends without a commit.

---

## Self-review against the spec

- **Layer 1 (real light):** Existing Light, ForcePixel, lightCookie field. ✓ (Tasks 3, 7)
- **Layer 2 (beam cone):** Shader + material + procedural mesh + renderer hookup. ✓ (Tasks 1, 2, 3, 7)
- **Layer 3 (dust):** ParticleSystem authored in scene, started/stopped by visibility helper, emission rate scaled by intensity. ✓ (Tasks 3, 6, 7)
- **Layer 4 (halo):** LensFlare using Halogen Bulb.flare, brightness synced to finalIntensity. ✓ (Tasks 4, 7)
- **Flicker — micro-drift:** Perlin in Update. ✓ (Task 4)
- **Flicker — panic stutter:** Coroutine, 6–10s interval, hardcoded 120ms sequence. ✓ (Task 4)
- **Flicker — dying-battery dip:** Coroutine, 35–55s interval, 1.8s V-curve with jitter. ✓ (Task 4)
- **Sway — walking only, ±2°:** SurfaceVelocity-driven, smooth-damped walk factor, figure-8 yaw. ✓ (Task 5)
- **Sway — no idle breath:** When walk factor is 0, rotation = baseLocalRot exactly. ✓ (Task 5)
- **Inspector groups:** Light, Brightness, Beam Cone, Dust Particles, Halo, Flicker, Sway. ✓ (Tasks 3, 4, 5)
- **Piloting guard:** Existing `_ship.IsPiloted` check extended to hide all four layers. ✓ (Task 3)
- **Toggle-off hides all layers:** SetVisualLayersVisible(false) on toggle and in Start. ✓ (Task 3)
- **Save-system:** No changes — existing `ApplyFlashlight` round-trips intensity + enabled. ✓ (no task needed)
- **Atmosphere queue gotcha:** Shader bakes Queue=Transparent-300 (2700) into Tags. ✓ (Task 1)
- **Live-tunable inspector fields:** All values read every frame; cone mesh rebuilds on change. ✓ (Tasks 3, 4, 5, 6)
