# Radial Motion Blur Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a screen-space radial motion blur shader + driver component that smears the final composited image radially from the player's projected velocity vector, scaled by speed.

**Architecture:** A new `RadialMotionBlurEffect` MonoBehaviour with a non-`[ImageEffectOpaque]` `OnRenderImage`, automatically attached to the player camera at runtime by `CameraEffectsManager`. Since the existing atmosphere/planet/ocean post-processing chain runs as `[ImageEffectOpaque]` (it lives inside `CustomPostProcessing`), Unity schedules our new callback strictly *after* that chain finishes — so we read the already-composited final image and smear it without ever touching atmosphere code. The shader does N sample-and-average passes along `uv - center`, where `center` is the perspective projection of the velocity vector onto the camera's image plane.

**Tech Stack:** Unity 2022.3 built-in render pipeline, image-effect-style fragment shader, MonoBehaviour singletons (matches existing camera-effects modules).

**Reference:** `docs/superpowers/specs/2026-05-11-radial-motion-blur-design.md`

---

## File structure

**Create:**
- `Assets/3 - Scripts/Camera/RadialMotionBlur.shader` — fragment shader, N-sample radial blur
- `Assets/3 - Scripts/Camera/RadialMotionBlurEffect.cs` — MonoBehaviour on the camera; computes center + strength, invokes Graphics.Blit each frame

**Modify:**
- `Assets/3 - Scripts/Scripts/Game/Controllers/InputSettings.cs` — add `fxRadialMotionBlur` toggle + PlayerPrefs load/save
- `Assets/3 - Scripts/UI/TabbedPauseMenu.cs` — add ToggleDef in CAMERA tab's `LENS CHARACTER` section
- `Assets/3 - Scripts/Camera/CameraEffectsManager.cs` — auto-attach `RadialMotionBlurEffect` to the player camera in `AttachModules`

**Verification note:** As with the rest of the camera-effects work, Unity has no automated test framework here. "Test" = `mcp__coplay-mcp__check_compile_errors` returning `No compile errors` + the manual Play-mode check spelled out in the task. Commit only after both pass. Locked-zone protection (atmosphere/planet/ocean) means we never touch any file under `Planet Effects/`, `Celestial/`, `CustomPostProcessing.cs`, or any `.shader`/`.compute`/`.hlsl` under those folders.

---

## Task 1: Add `fxRadialMotionBlur` field to `InputSettings`

**Files:**
- Modify: `Assets/3 - Scripts/Scripts/Game/Controllers/InputSettings.cs`

- [ ] **Step 1: Add the toggle field**

Find the existing `[Header("Camera Effects — Lens Character")]` block. Add at the end of that block (after the existing `fxAnamorphicStreaks` field):

```csharp
public bool fxRadialMotionBlur = false; // default OFF — opt-in via pause menu
```

- [ ] **Step 2: Add load line**

Append inside `LoadSettings`, after the existing `fxAnamorphicStreaks` load line:

```csharp
fxRadialMotionBlur = PlayerPrefs.GetInt (nameof (fxRadialMotionBlur), 0) != 0;
```

- [ ] **Step 3: Add save line**

Append inside `SaveSettings`, after the existing `fxAnamorphicStreaks` save line:

```csharp
PlayerPrefs.SetInt (nameof (fxRadialMotionBlur), fxRadialMotionBlur ? 1 : 0);
```

- [ ] **Step 4: Compile-check**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: `No compile errors`

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/Scripts/Game/Controllers/InputSettings.cs"
git commit -m "feat(settings): add fxRadialMotionBlur toggle field"
```

---

## Task 2: Write the `RadialMotionBlur.shader`

**Files:**
- Create: `Assets/3 - Scripts/Camera/RadialMotionBlur.shader`

- [ ] **Step 1: Create the shader file**

Create `Assets/3 - Scripts/Camera/RadialMotionBlur.shader`:

```shader
Shader "Hidden/RadialMotionBlur"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _Center  ("Center (UV)", Vector) = (0.5, 0.5, 0, 0)
        _Strength("Strength", Float) = 0
    }
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float2    _Center;
            float     _Strength;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };
            struct v2f
            {
                float2 uv     : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv     = v.uv;
                return o;
            }

            // N samples along the radial axis through this pixel, centered on
            // it. Distance-from-center scales the smear length so pixels near
            // the vanishing point stay sharp and pixels at the screen edge
            // smear hard. Strength is the global intensity multiplier (0-1
            // typical range).
            #define SAMPLES 10

            fixed4 frag(v2f i) : SV_Target
            {
                float2 dir  = i.uv - _Center;
                float  dist = length(dir);
                if (_Strength <= 0.001 || dist < 0.0001)
                    return tex2D(_MainTex, i.uv);

                float2 axis  = dir / dist;
                float  amount = _Strength * dist * 0.5; // scale tuned in C# side

                float4 sum = 0;
                [unroll]
                for (int s = 0; s < SAMPLES; s++)
                {
                    float t = (float)s / (SAMPLES - 1) - 0.5; // -0.5 .. +0.5
                    float2 sampleUV = i.uv + axis * t * amount;
                    sum += tex2D(_MainTex, sampleUV);
                }
                return sum / SAMPLES;
            }
            ENDCG
        }
    }
    Fallback Off
}
```

- [ ] **Step 2: Wait for Unity to import the shader**

After saving the file, focus the Unity Editor briefly so it imports + compiles the shader. (Asset import happens on focus.) The Editor Console should show no shader errors.

- [ ] **Step 3: Compile-check (C# side; shader compile errors appear in Console only)**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: `No compile errors`

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/Camera/RadialMotionBlur.shader"
git commit -m "feat(camera-fx): add radial motion blur shader (N-sample axis blur)"
```

---

## Task 3: Write the `RadialMotionBlurEffect` C# component

**Files:**
- Create: `Assets/3 - Scripts/Camera/RadialMotionBlurEffect.cs`

- [ ] **Step 1: Create the component**

Create `Assets/3 - Scripts/Camera/RadialMotionBlurEffect.cs`:

```csharp
using UnityEngine;

/// <summary>
/// Screen-space radial motion blur. Attached to the player camera at
/// runtime by <see cref="CameraEffectsManager"/>. Because this component's
/// <c>OnRenderImage</c> is NOT marked <c>[ImageEffectOpaque]</c>, Unity
/// schedules it after the <c>CustomPostProcessing</c> chain (which IS
/// opaque) — meaning the atmosphere / planet / ocean composite is already
/// in the source texture by the time we run, and we just smear the final
/// image. We never touch atmosphere code.
///
/// Strength and center are driven by the player's relative-to-planet
/// velocity (or the ship's velocity while piloting), perspective-projected
/// onto the camera's image plane. Same math as <see cref="SpeedLinesOverlay"/>.
/// </summary>
[RequireComponent(typeof(Camera))]
public class RadialMotionBlurEffect : MonoBehaviour
{
    // Speed thresholds (m/s). FullAt 100 saturates well below the player's
    // 200+ m/s top — the radial blur reads as "extreme speed" pretty fast,
    // so we cap intensity early to keep it tasteful.
    const float ShipThreshold = 12f;
    const float ShipFullAt = 100f;
    const float JetpackThreshold = 8f;
    const float JetpackFullAt = 100f;
    const float MaxStrength = 0.45f;

    Camera _cam;
    Material _material;
    Shader _shader;
    Ship _ship;
    PlayerController _player;
    float _strength;
    Vector2 _centerUv = new Vector2(0.5f, 0.5f);

    void Awake()
    {
        _cam = GetComponent<Camera>();
        _shader = Shader.Find("Hidden/RadialMotionBlur");
        if (_shader == null)
        {
            Debug.LogWarning("[RadialMotionBlurEffect] Shader 'Hidden/RadialMotionBlur' not found. Effect will pass-through.");
            return;
        }
        _material = new Material(_shader) { hideFlags = HideFlags.HideAndDontSave };
    }

    void OnDestroy()
    {
        if (_material != null) DestroyImmediate(_material);
    }

    // NOTE: deliberately NOT [ImageEffectOpaque]. We want to run AFTER the
    // CustomPostProcessing chain (which IS opaque) so atmosphere/planet/
    // ocean is already composited.
    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (_material == null) { Graphics.Blit(source, destination); return; }

        bool enabled = ShouldBeActive();
        if (!enabled) { Graphics.Blit(source, destination); return; }

        // Smooth strength + center over time so they don't snap.
        float targetStrength = ComputeTargetStrength();
        Vector2 targetCenter = ComputeTargetCenter();
        _strength = Mathf.MoveTowards(_strength, targetStrength, Time.unscaledDeltaTime * 1.5f);
        _centerUv = Vector2.Lerp(_centerUv, targetCenter, Time.unscaledDeltaTime * 4f);

        if (_strength <= 0.001f) { Graphics.Blit(source, destination); return; }

        _material.SetVector("_Center", new Vector4(_centerUv.x, _centerUv.y, 0f, 0f));
        _material.SetFloat("_Strength", _strength * MaxStrength);
        Graphics.Blit(source, destination, _material);
    }

    bool ShouldBeActive()
    {
        var mgr = CameraEffectsManager.Instance;
        if (mgr == null) return false;
        if (!mgr.MasterEnabled) return false;
        if (mgr.Input == null) return false;
        return mgr.Input.fxRadialMotionBlur;
    }

    float ComputeTargetStrength()
    {
        // Ship contribution.
        if (_ship == null) _ship = FindObjectOfType<Ship>(true);
        var shipRb = _ship != null ? _ship.GetComponent<Rigidbody>() : null;
        float shipSpeed = shipRb != null ? shipRb.velocity.magnitude : 0f;
        float shipT = Mathf.Clamp01((shipSpeed - ShipThreshold) / (ShipFullAt - ShipThreshold));

        // Player on-foot contribution — speed-driven, no input gate.
        if (_player == null) _player = FindObjectOfType<PlayerController>(true);
        float playerT = 0f;
        if (_player != null && _player.isActiveAndEnabled && !_player.IsOnGround)
        {
            float relSpeed = _player.RelativeVelocity.magnitude;
            playerT = Mathf.Clamp01((relSpeed - JetpackThreshold) / (JetpackFullAt - JetpackThreshold));
        }

        return Mathf.Max(shipT, playerT);
    }

    Vector2 ComputeTargetCenter()
    {
        // Find the velocity vector that's driving the blur.
        Vector3 worldVel = Vector3.zero;
        if (_player != null && _player.isActiveAndEnabled && !_player.IsOnGround)
            worldVel = _player.RelativeVelocity;
        else if (_ship != null)
        {
            var rb = _ship.GetComponent<Rigidbody>();
            if (rb != null) worldVel = rb.velocity;
        }
        if (worldVel.sqrMagnitude < 1f) return new Vector2(0.5f, 0.5f);

        // Project the camera-local velocity onto the image plane in
        // normalized [0, 1] UV space.
        Vector3 vCam = _cam.transform.InverseTransformDirection(worldVel);
        float fovRad = _cam.fieldOfView * Mathf.Deg2Rad;
        // halfHeight = 0.5 (UV). focalLen = 0.5 / tan(halfFOV) — distance from
        // pinhole to image plane in normalized image-plane units.
        float focalLen = 0.5f / Mathf.Tan(fovRad * 0.5f);

        Vector2 vp;
        if (vCam.z > 0.5f)
        {
            // Forward component dominates → perspective project.
            float aspect = (float)_cam.pixelWidth / Mathf.Max(1, _cam.pixelHeight);
            vp = new Vector2(
                (vCam.x / vCam.z) * focalLen / aspect,
                (vCam.y / vCam.z) * focalLen);
        }
        else
        {
            // Velocity is perpendicular or pointing behind — push to UV edge.
            Vector2 lateral = new Vector2(vCam.x, vCam.y);
            vp = lateral.sqrMagnitude < 0.001f
                ? Vector2.zero
                : lateral.normalized * 1.5f;
        }

        // UV space is centered on 0.5, 0.5 — offset and clamp slightly past
        // the rect (overshoot is fine; the shader still computes valid
        // distances from off-screen centers).
        Vector2 centerUv = new Vector2(0.5f, 0.5f) + vp * 0.5f;
        centerUv.x = Mathf.Clamp(centerUv.x, -0.5f, 1.5f);
        centerUv.y = Mathf.Clamp(centerUv.y, -0.5f, 1.5f);
        return centerUv;
    }
}
```

- [ ] **Step 2: Compile-check**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: `No compile errors`

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/Camera/RadialMotionBlurEffect.cs"
git commit -m "feat(camera-fx): add radial motion blur effect driver (perspective-projected center, speed-driven strength)"
```

---

## Task 4: Auto-attach the effect to the camera in `CameraEffectsManager`

**Files:**
- Modify: `Assets/3 - Scripts/Camera/CameraEffectsManager.cs`

- [ ] **Step 1: Add a property + attach block**

In `CameraEffectsManager.cs`, near the other public module properties, add:

```csharp
public RadialMotionBlurEffect RadialBlur { get; private set; }
```

Then in `AttachModules`, append a new attach block (BUT — this one must be on the CAMERA's GameObject, not the manager's, because `OnRenderImage` only fires on components attached to a camera):

```csharp
if (RadialBlur == null && PlayerCamera != null)
{
    RadialBlur = PlayerCamera.GetComponent<RadialMotionBlurEffect>();
    if (RadialBlur == null) RadialBlur = PlayerCamera.gameObject.AddComponent<RadialMotionBlurEffect>();
}
```

(Putting it inside an `if (PlayerCamera != null)` guard so we skip when the camera ref isn't acquired yet. The manager's `Update` keeps retrying `TryAcquireRefs + AttachModules` until success.)

- [ ] **Step 2: Compile-check**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: `No compile errors`

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/Camera/CameraEffectsManager.cs"
git commit -m "feat(camera-fx): auto-attach RadialMotionBlurEffect to player camera"
```

---

## Task 5: Add the `RADIAL MOTION BLUR` toggle to the CAMERA tab

**Files:**
- Modify: `Assets/3 - Scripts/UI/TabbedPauseMenu.cs`

- [ ] **Step 1: Add the toggle row**

In `BuildSettingsList`, find the `LENS CHARACTER` section of the CAMERA tab (the `new HeaderDef { label = "LENS CHARACTER" }` line and the rows following it). Append a new `ToggleDef` AFTER the existing `ANAMORPHIC STREAKS` row:

```csharp
new ToggleDef {
    label = "RADIAL MOTION BLUR",
    get = () => _input != null && _input.fxRadialMotionBlur,
    set = v => { if (_input != null) _input.fxRadialMotionBlur = v; }
},
```

- [ ] **Step 2: Compile-check**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: `No compile errors`

- [ ] **Step 3: Manual verify**

Play mode → Esc → SETTINGS → CAMERA tab → scroll to LENS CHARACTER section → confirm "RADIAL MOTION BLUR" toggle is present, defaults to OFF. Toggling ON should make the radial blur immediately activate when the player is airborne + moving fast (or in the ship moving fast).

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/UI/TabbedPauseMenu.cs"
git commit -m "feat(pause-menu): expose RADIAL MOTION BLUR toggle in CAMERA tab"
```

---

## Task 6: Manual atmosphere-preservation validation

**Files:** (none — playtest only)

- [ ] **Step 1: Toggle OFF → camera output unchanged**

Play mode. Pause menu → CAMERA → ensure RADIAL MOTION BLUR is OFF. Sprint, jetpack, fly the ship at high speed — the rendered image must be visually identical to a fresh checkout of the branch *before* this feature landed. Sky, planet surface, ocean, and atmospheric haze should look pixel-correct. Verifies the pass-through `Graphics.Blit(source, destination)` path doesn't corrupt anything.

- [ ] **Step 2: Toggle ON at zero speed → no blur**

Stand still on the planet. Toggle ON. Image should still be sharp; strength is 0 at this speed and the shader's early-out (`if (_Strength <= 0.001) return source`) keeps the output identical.

- [ ] **Step 3: Toggle ON + sprint/jetpack → blur ramps in**

Jump + jetpack + sprint forward. As speed crosses ~8 m/s the radial smear should start, faint at first, ramping up around 30 m/s, saturating near 100 m/s. The smear should be centered on the direction of motion (forward = center, up-thrust = top, etc.).

- [ ] **Step 4: Test directions**

Up-thrust with camera level → blur centered above (smear streaks downward visually). Strafe-sprint while looking 90° to the side → blur centered off-screen at the edge in the direction you're heading. Forward sprint while looking forward → blur from center.

- [ ] **Step 5: Visual regression sweep on atmosphere**

Fly close to a planet's surface. Watch the atmospheric haze, the ocean shimmer, and the gradient between space and sky. With the blur OFF, these should look exactly as before. With it ON at sufficient speed, they should be smeared radially but the *underlying* color/gradient/haze pattern must look correct (just blurred). Any other anomaly (banding, missing layers, wrong color) is a sign we accidentally interacted with the atmosphere chain — STOP and rollback by deleting `Assets/3 - Scripts/Camera/RadialMotionBlur.shader` + `Assets/3 - Scripts/Camera/RadialMotionBlurEffect.cs` + reverting the four other files via `git restore`.

- [ ] **Step 6: Commit a validation note (if pass)**

If all visual checks pass, no commit needed — the implementation tasks already landed. If any failed, file a follow-up task with the specific failure mode before continuing.

---

## Self-review checklist

1. **Spec coverage:** Every section of the spec maps to a task:
   - Spec "Components — RadialMotionBlurEffect" → Task 3.
   - Spec "Components — RadialMotionBlur.shader" → Task 2.
   - Spec "Settings integration — `InputSettings`" → Task 1.
   - Spec "Settings integration — `TabbedPauseMenu`" → Task 5.
   - Spec "Risk mitigation — atmosphere" → Task 6 (validation).
   - Spec "Implementation phases" 1–5 → Tasks 2, 3 (effect+attach split into Tasks 3+4), 1, 5, 6.
2. **Placeholder scan:** None. All shader code, all C# code, all commit messages spelled out.
3. **Type consistency:** Shader properties (`_Center`, `_Strength`), C# accessors (`fxRadialMotionBlur`, `RelativeVelocity`, `IsOnGround`, `JetpackUnlocked`, `MasterEnabled`), and method names (`AttachModules`, `BuildSettingsList`) all match across tasks and existing codebase.
4. **Locked-zone protection:** No task modifies any file under `Planet Effects/`, `Celestial/`, `Atmosphere.cs`, `CustomImageEffect.cs`, `CustomPostProcessing.cs`, or any `.shader`/`.compute`/`.hlsl` under those folders. ✓

---

**Plan complete and saved to `docs/superpowers/plans/2026-05-11-radial-motion-blur.md`.** Two execution options:

1. **Subagent-Driven (recommended)** — fresh subagent per task with two-stage review.
2. **Inline Execution** — execute tasks in this session with batched checkpoints.

Which approach? Or just say "go" and I'll do it inline since it's only 6 tasks and they're short.
