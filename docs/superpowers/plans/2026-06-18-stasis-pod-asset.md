# Stasis Pod Asset — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the removed placeholder pod with a real dark-industrial stasis pod prefab (octagonal faceted capsule, 4 side + floor + ceiling glass windows) that the player rides feet-first down to Humble Abode, with see-through windows the planet's atmosphere renders through.

**Architecture:** The pod is composed from Unity built-in primitives (thin boxes + quads) via a re-runnable **editor builder script** that saves a real `StasisPod.prefab`. Windows use a `Cull Off` copy of the project's existing early-queue glass shader (render queue 2450 ≤ 2500) so the `[ImageEffectOpaque]` atmosphere/ocean post-process shows *through* the glass. `PodArrivalSequence` instantiates the prefab, orients it from the **constant** descent direction `_dir` (bottom window → planet) — which is what eliminates the old per-frame `LookRotation` spin — and destroys it on crash.

**Tech Stack:** Unity 2022.3, Built-in Render Pipeline (NOT URP), C# (`Assembly-CSharp`, no asmdefs), ShaderLab surface shader. No CLI build/test — verification is **compile check** (Coplay MCP `check_compile_errors`) + **Play-mode observation** (New Game in `Assets/1.6.7.7.7.unity`), mirroring the existing `docs/superpowers/plans/2026-06-18-stasis-pod-arrival-intro.md` conventions.

**Spec:** `docs/superpowers/specs/2026-06-18-stasis-pod-asset-design.md`

---

## Conventions for every task

- **No CLI tests in this repo.** Where a generic plan says "run tests," this plan substitutes:
  - **Compile check:** save the file; run Coplay MCP `check_compile_errors` (or read the Unity Console). "Expected: no errors" means exactly that.
  - **Play test:** open `Assets/1.6.7.7.7.unity`, press Play, trigger a fresh **New Game** (so `PendingLoad.Data == null` and `EarlyGameProgress.IntroPlayed` resets) — the pod only appears on the New Game descent.
- **Serialized field convention (CLAUDE.md):** append new `[SerializeField]` fields at the END of the class, never mid-class.
- **New files need `git add` of BOTH the `.cs`/`.shader` AND its generated `.meta`.** `git commit -a` skips untracked files.
- **Forbidden zone (CLAUDE.md trap #2):** this plan touches none of the atmosphere/planet generation/shading code. The glass shader is a self-contained new file under `Assets/3 - Scripts/Tutorial/`, not under the celestial/post-processing trees.

---

## Prerequisite (Task 0): start from a clean tree

`PodArrivalSequence.cs` and the scene have uncommitted intro fixes (HAL autonomous-line suppression, fall-damage suppression, pod-visual removal, velocity-continuous flight, crash SFX + 3 s black hold) plus the new `Assets/Audio/Intro/pod_crash.wav`. Task 3 edits `PodArrivalSequence.cs`, so commit those first.

- [ ] **Step 1: Playtest-confirm the pending intro changes** (user) — descent feels right, crash sound plays, no fall damage on wake, HAL silent during descent.

- [ ] **Step 2: Commit the pending intro batch**

```bash
git add "Assets/3 - Scripts/AI/HALCommentator.cs" \
        "Assets/3 - Scripts/Survival/FallDamage.cs" \
        "Assets/3 - Scripts/Tutorial/PodArrivalSequence.cs" \
        "Assets/Audio/Intro/pod_crash.wav" "Assets/Audio/Intro/pod_crash.wav.meta" \
        "Assets/1.6.7.7.7.unity"
git commit -m "feat(intro): velocity-continuous descent, crash SFX + black hold, suppress HAL/fall-damage during pod"
```

- [ ] **Step 3: Confirm clean tree**

Run: `git status --short`
Expected: only `Assets/2 - Materials/Intro/Grogginess.mat` remains modified (a pre-existing unrelated change), nothing else.

---

## File Structure

- **Create:** `Assets/3 - Scripts/Tutorial/StasisPodGlass.shader` — a `Cull Off` copy of the early-queue glass shader so flat window panes show from inside the pod. One responsibility: double-sided early-queue glass.
- **Create:** `Assets/Editor/BuildStasisPod.cs` — editor builder: creates the 3 materials and composes + saves `StasisPod.prefab`. Re-runnable via a menu item. One responsibility: author the pod asset.
- **Create (generated):** `Assets/1 - samsPrefabs/StasisPod.prefab`, `Assets/2 - Materials/Intro/PodHull.mat`, `PodGlass.mat`, `PodTrim.mat` — produced by the builder.
- **Modify:** `Assets/3 - Scripts/Tutorial/PodArrivalSequence.cs` — add `podPrefab` field (END), instantiate in `Setup()`, drive transform in `LateUpdate()`, destroy in `Teardown()`.
- **Modify:** `Assets/1.6.7.7.7.unity` — assign the prefab to the component's `podPrefab` field.

---

## Task 1: Double-sided early-queue glass shader

A flat window quad with the stock `Cull Back` glass shader vanishes when viewed from inside the pod. This task adds a `Cull Off` variant at the same render queue.

**Files:**
- Create: `Assets/3 - Scripts/Tutorial/StasisPodGlass.shader`

- [ ] **Step 1: Create the shader**

Create `Assets/3 - Scripts/Tutorial/StasisPodGlass.shader`:

```shaderlab
// Double-sided variant of "Custom/sFuture Glass Early Queue"
// (Assets/sFuture Modules Pro/Materials/Glass_EarlyQueue.shader). Identical except
// `Cull Off`, so a flat window pane renders from BOTH sides (the player is inside
// the pod looking out). Queue "AlphaTest" (2450, <= 2500) keeps it BEHIND the
// planet's [ImageEffectOpaque] atmosphere/ocean post so the sky renders through
// the glass (the transparent-queue gotcha in CLAUDE.md).
Shader "Custom/StasisPodGlassDoubleSided" {
    Properties {
        _Color ("Color", Color) = (0.10, 0.12, 0.15, 0.35)
        _SpecColor ("Specular", Color) = (0.2, 0.2, 0.2, 1)
        _Glossiness ("Smoothness", Range(0,1)) = 0.6
    }

    SubShader {
        Tags { "RenderType"="Transparent" "Queue"="AlphaTest" "IgnoreProjector"="True" }
        LOD 200
        Cull Off

        CGPROGRAM
        #pragma surface surf StandardSpecular alpha:premul
        #pragma target 3.0

        struct Input {
            float2 uv_MainTex;
        };

        half _Glossiness;
        fixed4 _Color;

        void surf (Input IN, inout SurfaceOutputStandardSpecular o) {
            o.Albedo = _Color.rgb;
            o.Specular = _SpecColor.rgb;
            o.Smoothness = _Glossiness;
            o.Alpha = _Color.a;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
```

- [ ] **Step 2: Compile check**

Save. In Unity, confirm the shader imports with no errors (Console; run Coplay MCP `check_compile_errors` for C# — shaders log their own errors in the Console).
Expected: no shader compile errors; `Shader.Find("Custom/StasisPodGlassDoubleSided")` will resolve.

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/Tutorial/StasisPodGlass.shader" "Assets/3 - Scripts/Tutorial/StasisPodGlass.shader.meta"
git commit -m "feat(intro): double-sided early-queue glass shader for pod windows"
```

---

## Task 2: Editor builder script → StasisPod.prefab

Compose the pod from primitives and save the prefab. Re-runnable so the user can tweak constants and rebuild.

**Files:**
- Create: `Assets/Editor/BuildStasisPod.cs`
- Create (generated): `Assets/1 - samsPrefabs/StasisPod.prefab`, `Assets/2 - Materials/Intro/PodHull.mat`, `PodGlass.mat`, `PodTrim.mat`

- [ ] **Step 1: Create the builder script**

Create `Assets/Editor/BuildStasisPod.cs`:

```csharp
using UnityEngine;
using UnityEditor;

// Builds the StasisPod prefab from Unity primitives (no Blender, no asset-gen).
// Re-runnable: Tools ▸ Intro ▸ Build Stasis Pod (or Coplay execute_script -> Execute).
// Octagonal body = 4 gunmetal panels alternating with 4 glass windows; tapered
// collars top & bottom each capped by a glass window (ceiling = space, floor =
// planet). Dark-industrial: gunmetal hull, amber emissive trim, smoky glass.
public static class BuildStasisPod
{
    const string PrefabPath   = "Assets/1 - samsPrefabs/StasisPod.prefab";
    const string MatDir       = "Assets/2 - Materials/Intro";
    const string HullMatPath  = MatDir + "/PodHull.mat";
    const string GlassMatPath = MatDir + "/PodGlass.mat";
    const string TrimMatPath  = MatDir + "/PodTrim.mat";

    // Geometry (metres). Octagon circumradius R; the player camera sits near centre.
    const float R             = 1.35f;  // interior radius
    const float bodyHeight    = 1.6f;   // height of the side-window band
    const float wallThickness = 0.06f;
    const float capRise       = 0.7f;   // how far the cap window sits beyond the body band
    const float capRadius     = 0.55f;  // radius of the floor/ceiling glass cap
    const float trimThickness = 0.035f;

    [MenuItem("Tools/Intro/Build Stasis Pod")]
    public static void Execute()
    {
        EnsureFolder(MatDir);
        Material hull  = MakeHullMat();
        Material glass = MakeGlassMat();
        Material trim  = MakeTrimMat();

        var root = new GameObject("StasisPod");
        var hullRoot = new GameObject("Hull");
        hullRoot.transform.SetParent(root.transform, false);

        float edge = 2f * R * Mathf.Tan(Mathf.Deg2Rad * 22.5f); // octagon edge length

        // Body: 8 faces. Even index = glass window (0/90/180/270), odd = gunmetal strut.
        for (int i = 0; i < 8; i++)
        {
            float ang = i * 45f;
            Vector3 outDir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
            Quaternion rot = Quaternion.LookRotation(outDir, Vector3.up);
            if (i % 2 == 0)
            {
                var q = MakeQuad("Body_Window_" + i, hullRoot.transform, glass);
                q.transform.localPosition = outDir * R;
                q.transform.localRotation = rot;
                q.transform.localScale = new Vector3(edge * 0.92f, bodyHeight, 1f);
            }
            else
            {
                var b = MakeBox("Body_Panel_" + i, hullRoot.transform, hull);
                b.transform.localPosition = outDir * R;
                b.transform.localRotation = rot;
                b.transform.localScale = new Vector3(edge, bodyHeight, wallThickness);
            }
        }

        // Emissive trim ribs at the 8 octagon corners (frame each window).
        for (int c = 0; c < 8; c++)
        {
            float ang = c * 45f + 22.5f;
            Vector3 cornerDir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
            var rib = MakeBox("Trim_Rib_" + c, hullRoot.transform, trim);
            rib.transform.localPosition = cornerDir * (R + trimThickness * 0.5f);
            rib.transform.localRotation = Quaternion.LookRotation(cornerDir, Vector3.up);
            rib.transform.localScale = new Vector3(trimThickness, bodyHeight * 1.02f, trimThickness);
        }

        BuildCap(hullRoot.transform, hull, glass, +1);  // ceiling (space)
        BuildCap(hullRoot.transform, hull, glass, -1);  // floor (planet)

        var lightGO = new GameObject("InteriorLight");
        lightGO.transform.SetParent(root.transform, false);
        var lt = lightGO.AddComponent<Light>();
        lt.type = LightType.Point;
        lt.range = 3f;
        lt.intensity = 1.2f;
        lt.color = new Color(1f, 0.55f, 0.25f); // dim amber

        foreach (var col in root.GetComponentsInChildren<Collider>())
            Object.DestroyImmediate(col); // cosmetic only

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        if (prefab != null) { Selection.activeObject = prefab; EditorGUIUtility.PingObject(prefab); }
        Debug.Log("[StasisPod] Built prefab at " + PrefabPath);
    }

    // sign +1 = top (ceiling/space), -1 = bottom (floor/planet).
    static void BuildCap(Transform parent, Material hull, Material glass, int sign)
    {
        float yBodyEdge = sign * bodyHeight * 0.5f;
        float yCap      = sign * (bodyHeight * 0.5f + capRise);

        // Glass cap (horizontal pane).
        var cap = MakeQuad(sign > 0 ? "TopWindow" : "BottomWindow", parent, glass);
        cap.transform.localPosition = new Vector3(0f, yCap, 0f);
        cap.transform.localRotation = Quaternion.LookRotation(Vector3.up * sign, Vector3.forward);
        cap.transform.localScale = new Vector3(capRadius * 2f, capRadius * 2f, 1f);

        // 8 opaque collar panels bridging the body octagon edge up/down to the cap.
        float edge = 2f * R * Mathf.Tan(Mathf.Deg2Rad * 22.5f);
        for (int i = 0; i < 8; i++)
        {
            float ang = i * 45f;
            Vector3 outDir  = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
            Vector3 tangent = Quaternion.Euler(0f, ang, 0f) * Vector3.right;
            Vector3 bodyPt = outDir * R + Vector3.up * yBodyEdge;
            Vector3 capPt  = outDir * capRadius + Vector3.up * yCap;
            Vector3 slant  = capPt - bodyPt;
            float L = slant.magnitude;
            Vector3 slantDir = slant / L;
            Vector3 normal = Vector3.Cross(slantDir, tangent).normalized;

            var b = MakeBox("Collar_" + (sign > 0 ? "T_" : "B_") + i, parent, hull);
            b.transform.localPosition = (bodyPt + capPt) * 0.5f;
            b.transform.localRotation = Quaternion.LookRotation(normal, slantDir);
            b.transform.localScale = new Vector3(edge, L, wallThickness);
        }
    }

    // ── Primitive + material helpers ─────────────────────────────────────────
    static GameObject MakeBox(string name, Transform parent, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static GameObject MakeQuad(string name, Transform parent, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static Material MakeHullMat()
    {
        var m = LoadOrCreate(HullMatPath, () => new Material(Shader.Find("Standard")));
        m.color = new Color(0.18f, 0.19f, 0.21f);
        m.SetFloat("_Metallic", 0.7f);
        m.SetFloat("_Glossiness", 0.5f);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material MakeGlassMat()
    {
        var sh = Shader.Find("Custom/StasisPodGlassDoubleSided");
        var m = LoadOrCreate(GlassMatPath, () => new Material(sh));
        if (sh != null && m.shader != sh) m.shader = sh;
        m.SetColor("_Color", new Color(0.10f, 0.12f, 0.15f, 0.35f));
        m.SetFloat("_Glossiness", 0.6f);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material MakeTrimMat()
    {
        var m = LoadOrCreate(TrimMatPath, () => new Material(Shader.Find("Standard")));
        m.color = new Color(0.05f, 0.04f, 0.03f);
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        m.SetColor("_EmissionColor", new Color(1.0f, 0.4f, 0.1f) * 2.0f); // amber glow
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material LoadOrCreate(string path, System.Func<Material> create)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        var m = create();
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        var leaf = System.IO.Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
```

- [ ] **Step 2: Compile check**

Save. Run Coplay MCP `check_compile_errors`.
Expected: no errors. (Requires Task 1's shader to exist so `Shader.Find` resolves at runtime; the script still *compiles* regardless.)

- [ ] **Step 3: Build the prefab**

Run the builder — either menu `Tools ▸ Intro ▸ Build Stasis Pod`, or Coplay MCP `execute_script` on `Assets/Editor/BuildStasisPod.cs` with `methodName` `Execute`.
Expected: Console logs `[StasisPod] Built prefab at Assets/1 - samsPrefabs/StasisPod.prefab`.

- [ ] **Step 4: Verify the prefab + eyeball it**

Confirm the files exist (Coplay MCP `list_files` on `Assets/1 - samsPrefabs` for `StasisPod.prefab`, and `Assets/2 - Materials/Intro` for the 3 `.mat`s). Open the prefab in the scene or use Coplay MCP `capture_scene_object` on a temporary instance to eyeball it: a faceted dark capsule, 4 side windows + floor + ceiling windows, amber-edged.
Expected: recognizable pod; windows read as glass. (Tint/scale/emissive get tuned in Task 4 — don't over-polish here.)

- [ ] **Step 5: Commit**

```bash
git add "Assets/Editor/BuildStasisPod.cs" "Assets/Editor/BuildStasisPod.cs.meta" \
        "Assets/1 - samsPrefabs/StasisPod.prefab" "Assets/1 - samsPrefabs/StasisPod.prefab.meta" \
        "Assets/2 - Materials/Intro/PodHull.mat" "Assets/2 - Materials/Intro/PodHull.mat.meta" \
        "Assets/2 - Materials/Intro/PodGlass.mat" "Assets/2 - Materials/Intro/PodGlass.mat.meta" \
        "Assets/2 - Materials/Intro/PodTrim.mat" "Assets/2 - Materials/Intro/PodTrim.mat.meta"
git commit -m "feat(intro): stasis pod prefab + builder script + materials"
```

---

## Task 3: Spawn, orient, and destroy the pod in PodArrivalSequence

Wire the prefab into the cinematic. The orientation comes from the constant `_dir`, which is the fix for the old spin.

**Files:**
- Modify: `Assets/3 - Scripts/Tutorial/PodArrivalSequence.cs`
- Modify: `Assets/1.6.7.7.7.unity` (assign `podPrefab`)

- [ ] **Step 1: Add the serialized field (at END of the serialized block)**

In `Assets/3 - Scripts/Tutorial/PodArrivalSequence.cs`, the serialized block currently ends with:

```csharp
    [Header("Post-crash")]
    [SerializeField] float postCrashBlackHold = 3f;  // seconds the screen stays black after impact before the cabin teleport + wake-up
```

Append immediately after it:

```csharp
    [Header("Pod visual")]
    [SerializeField] GameObject podPrefab;   // dark-industrial stasis pod (StasisPod.prefab); null = no pod
```

- [ ] **Step 2: Add the runtime instance field**

Find the runtime field block (starts `// ── Runtime ──`). After the line `Camera _cam;` add:

```csharp
    GameObject _podInstance;      // spawned StasisPod, positioned/oriented each LateUpdate
```

- [ ] **Step 3: Instantiate in Setup()**

In `Setup()`, find:

```csharp
        BuildCanvas();
        StartAudio();
        _flying = true;
        _active = true;
        return true;
```

Replace with:

```csharp
        BuildCanvas();
        StartAudio();
        if (podPrefab != null) _podInstance = Instantiate(podPrefab);
        _flying = true;
        _active = true;
        return true;
```

- [ ] **Step 4: Drive the pod transform in LateUpdate() (oriented off the constant `_dir` — no spin)**

In `LateUpdate()`, find:

```csharp
        _rb.position = pos;
        _player.position = pos;
    }
```

Replace with:

```csharp
        _rb.position = pos;
        _player.position = pos;

        // Centre the pod on the camera (the eye) and orient it off the CONSTANT
        // descent direction _dir: +Y (ceiling/space window) points away from the
        // planet, so the floor window faces it. _dir never changes during flight,
        // so the pod is rock-stable — unlike the old per-frame LookRotation that
        // spun wildly once we were metres from the planet. Shake stays positional.
        if (_podInstance != null && _cam != null)
        {
            _podInstance.transform.position = _cam.transform.position;
            _podInstance.transform.rotation = Quaternion.FromToRotation(Vector3.up, _dir);
        }
    }
```

- [ ] **Step 5: Destroy in Teardown()**

In `Teardown()`, find the cleanup block:

```csharp
        if (_canvas != null) Destroy(_canvas.gameObject);
```

Insert immediately before it:

```csharp
        if (_podInstance != null) Destroy(_podInstance);
```

- [ ] **Step 6: Compile check**

Save. Run Coplay MCP `check_compile_errors`.
Expected: no errors.

- [ ] **Step 7: Assign the prefab to the component in the scene**

The `podPrefab` reference is an asset (prefab) reference — assign it via a `SerializedObject` (the same approach used to wire the pod audio clip; `set_property` may not resolve asset refs). Create `Assets/Editor/WirePodPrefab.cs`:

```csharp
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class WirePodPrefab
{
    public static void Execute()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/1 - samsPrefabs/StasisPod.prefab");
        if (prefab == null) { Debug.LogError("[WIREPOD] StasisPod.prefab not found"); return; }
        var comp = Object.FindObjectOfType<PodArrivalSequence>(true);
        if (comp == null) { Debug.LogError("[WIREPOD] PodArrivalSequence not found"); return; }
        var so = new SerializedObject(comp);
        var prop = so.FindProperty("podPrefab");
        if (prop == null) { Debug.LogError("[WIREPOD] podPrefab property not found"); return; }
        prop.objectReferenceValue = prefab;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(comp);
        EditorSceneManager.MarkSceneDirty(comp.gameObject.scene);
        EditorSceneManager.SaveScene(comp.gameObject.scene);
        Debug.Log("[WIREPOD] Wired StasisPod.prefab -> podPrefab and saved scene");
    }
}
```

Run it via Coplay MCP `execute_script` (`methodName` `Execute`).
Expected: Console logs `[WIREPOD] Wired StasisPod.prefab -> podPrefab and saved scene`.

- [ ] **Step 8: Delete the one-off wiring script**

```bash
rm -f "Assets/Editor/WirePodPrefab.cs" "Assets/Editor/WirePodPrefab.cs.meta"
```

Run Coplay MCP `check_compile_errors`.
Expected: no errors (a deleted editor-only script can't break compilation).

- [ ] **Step 9: Play test — pod appears, stable, frames the planet**

Enter Play mode → New Game.
Expected: during the descent the player is inside the dark pod; Humble Abode is visible **through the floor window** (looking down) growing as you fall; stars through the ceiling window; the 4 side windows show space/horizon as you free-look; the pod **does not spin or jitter** (only the impact shake near the end). At impact: cut to black, 3 s hold, then the cabin wake-up — the pod is gone, no leftover object, no errors.

- [ ] **Step 10: Commit**

```bash
git add "Assets/3 - Scripts/Tutorial/PodArrivalSequence.cs" "Assets/1.6.7.7.7.unity"
git commit -m "feat(intro): spawn the stasis pod and orient it off the descent direction"
```

---

## Task 4: Tuning + final verification

Dial in feel live, confirm the glass/atmosphere interaction, and confirm the load path is untouched.

**Files:**
- (Tuning) `Assets/Editor/BuildStasisPod.cs` constants → rebuild via the menu; and/or `Assets/2 - Materials/Intro/*.mat`, the `PodArrivalSequence` component in the Inspector.

- [ ] **Step 1: Atmosphere-through-glass check**

Play → New Game. While the planet is framed in the floor window, confirm its blue atmosphere / ocean post-process renders **through** the glass (not occluded, not painted on top of the pane).
Expected: the sky shows through the tinted glass. If the glass occludes it, the window material isn't on the `Custom/StasisPodGlassDoubleSided` shader (queue 2450) — re-check Task 1/2.

- [ ] **Step 2: Eye-placement + framing check**

Confirm the camera sits comfortably inside (windows ~arm's length, not clipping the player into a wall, floor window clearly "down"). If the eye is too high/low or the pod too tight, adjust `R` / `bodyHeight` / `capRise` in `BuildStasisPod.cs` and rerun `Tools ▸ Intro ▸ Build Stasis Pod` (the prefab updates in place; the scene reference is unchanged).

- [ ] **Step 3: Material feel**

Judge tint/emissive/light in motion. Adjust on the `.mat` assets directly (or the builder constants): glass `_Color` alpha (darker/clearer), trim `_EmissionColor` (amber↔red, intensity), `InteriorLight` intensity/color.
Expected: reads as a dark industrial pod; windows readable; not so dark you lose the interior, not so bright it flattens.

- [ ] **Step 4: No-spin + crash + handoff regression**

Play → New Game and watch the full descent once: stable pod through approach, shake only at impact, clean cut-to-black, 3 s hold, cabin wake-up with no leftover pod and no errors. Press **Esc** mid-descent once to confirm skip still tears the pod down cleanly.
Expected: all clean, no errors.

- [ ] **Step 5: Load-path regression**

Load an existing save (not New Game).
Expected: NO pod and NO wake-up — `IntroSequenceController` disables itself when `PendingLoad.Data != null`, so `PodArrivalSequence.Play()` never runs. Normal gameplay, no errors.

- [ ] **Step 6: Commit any tuning**

```bash
git add "Assets/Editor/BuildStasisPod.cs" "Assets/1 - samsPrefabs/StasisPod.prefab" "Assets/2 - Materials/Intro"
git commit -m "tune(intro): stasis pod size, glass tint, emissive, interior light"
```

- [ ] **Step 7: Update the feature audit/state if materially changed**

If this changes the documented intro behaviour, add a one-line note to `docs/CURRENT_STATE_AUDIT.md` (the pod is now a real prefab, not a placeholder). Commit.

---

## Self-Review notes (addressed)

- **Spec coverage:** faceted-capsule geometry (T2 body loop + `BuildCap`), 4 side + floor + ceiling glass windows (T2), dark-industrial materials + emissive trim + interior light (T2 material helpers), early-queue see-through glass with `Cull Off` (T1, used in T2), authored prefab via builder script (T2), spawn/destroy + orient-off-`_dir` no-spin (T3), claustrophobic size (T2 `R`/`bodyHeight`, tuned T4), atmosphere-through-glass verification (T4 Step 1), load-path untouched (T4 Step 5), forbidden zone untouched (no celestial/post-processing files). All covered.
- **Placeholder scan:** every code step contains complete code (shader, builder, edits, wiring script). No TBD/TODO.
- **Type/name consistency:** shader name `Custom/StasisPodGlassDoubleSided` matches between T1 and T2 `MakeGlassMat`. `podPrefab` / `_podInstance` / `_dir` / `_cam` used consistently across T3 steps. Prefab path `Assets/1 - samsPrefabs/StasisPod.prefab` matches between T2 (save), T3 Step 7 (load), and commits. Material paths under `Assets/2 - Materials/Intro/` consistent.
```
