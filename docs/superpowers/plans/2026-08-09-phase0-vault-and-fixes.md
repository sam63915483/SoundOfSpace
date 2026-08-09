# Phase 0 — Vault and Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement
> this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Strip the concert venue, ship school, Tev's cabin ambush and Tev's village presence
out of the running game with zero runtime cost, and fix the five defects found reviewing
commit `237e46a8` — so Phase 1's sync spine is built against a smaller, sound baseline.

**Architecture:** Vaulting is *asset preservation plus scene deletion*, not runtime gating —
an inactive GameObject still loads its meshes, textures and audio, and the concert is being
removed for performance. Each vaulted hierarchy is saved as a prefab under
`Assets/1 - samsPrefabs/_Vaulted/` and its scene instances deleted, so restoring is one drag.
`FeatureVault` gains a flag per system to gate any code path that could resurrect them.

**Tech Stack:** Unity 2022.3 Built-in RP, C#, Unity Netcode for GameObjects 1.12.
No CLI test runner — verification is `mcp__coplay-mcp__check_compile_errors` plus edit-mode
harnesses run through `mcp__coplay-mcp__execute_script`, written to the scratchpad so they
never enter the project (an editor script outside an `Editor/` folder breaks player builds).

---

## File Structure

**Modified**
- `Assets/3 - Scripts/Character/CharacterStore.cs` — atomic save, corrupt-file quarantine
- `Assets/3 - Scripts/Character/CharacterProfile.cs` — surrogate-safe `Sanitize`
- `Assets/3 - Scripts/Multiplayer/NetworkPlayerIdentity.cs` — surrogate-safe `Truncate`
- `Assets/3 - Scripts/Multiplayer/NetworkPlayerCombat.cs` — drop damage from the wire
- `Assets/3 - Scripts/Multiplayer/MultiplayerDeathRespawn.cs` — clear `isInDialogue` on scene load
- `Assets/3 - Scripts/Pickups/PistolController.cs` — `ShotInfo.MaxTracerLength`
- `Assets/3 - Scripts/Scripts/Game/FeatureVault.cs` — four new flags
- `Assets/1.6.7.7.7.unity` — vaulted hierarchies deleted

**Created**
- `Assets/1 - samsPrefabs/_Vaulted/` — one prefab per vaulted system
- `docs/VAULTED_SYSTEMS.md` — what was vaulted, why, and exactly how to restore it

Ordering is deliberate: the five code fixes come first because they are small, independent and
verifiable, so a mistake in the riskier scene surgery cannot obscure them.

---

## Task 1: CharacterStore — atomic save and corrupt-file quarantine

Highest priority of the five. `Save()` writes straight over the live file, and `EnsureLoaded()`
starts an empty book on a parse failure so the next mutation destroys the damaged original.
Harmless while a profile is a name and a colour; catastrophic once levels, money and hotbar
migrate in as planned.

**Files:**
- Modify: `Assets/3 - Scripts/Character/CharacterStore.cs`

- [ ] **Step 1: Replace `Save()` with an atomic write**

Find the existing body:

```csharp
    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonUtility.ToJson(_book, true));
        }
        catch (Exception e)
        {
            Debug.LogError($"[CharacterStore] Couldn't write {FilePath}: {e.Message}");
        }
        Changed?.Invoke();
    }
```

Replace with:

```csharp
    /// <summary>
    /// Writes via a temp file and swaps it into place, so an interrupted write
    /// cannot leave a truncated characters.json.
    ///
    /// A direct WriteAllText opens the real file and truncates it BEFORE the new
    /// bytes land — a crash or power cut in that window loses every character.
    /// The rename is the atomic step: readers see either the whole old file or
    /// the whole new one, never a half-written one.
    /// </summary>
    public void Save()
    {
        string path = FilePath;
        string tmp  = path + ".tmp";
        try
        {
            File.WriteAllText(tmp, JsonUtility.ToJson(_book, true));

            if (File.Exists(path))
            {
                // File.Replace is atomic where the platform supports it and
                // keeps no backup (null) — the temp file IS the backup until
                // the swap completes.
                File.Replace(tmp, path, null);
            }
            else
            {
                File.Move(tmp, path);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[CharacterStore] Couldn't write {path}: {e.Message}");
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
        }
        Changed?.Invoke();
    }
```

- [ ] **Step 2: Quarantine an unreadable file instead of overwriting it**

Find the `catch` in `EnsureLoaded()`:

```csharp
        catch (Exception e)
        {
            // A corrupt characters.json must not brick the main menu. Start
            // empty; the file is overwritten on the next mutation.
            Debug.LogError($"[CharacterStore] Couldn't read {FilePath}: {e.Message}");
            _book = new CharacterBook();
        }
```

Replace with:

```csharp
        catch (Exception e)
        {
            // A corrupt characters.json must not brick the main menu — but it
            // must not be silently DESTROYED either. Starting empty means the
            // next mutation writes over it, so the file is moved aside first
            // and the player keeps something a human could hand-repair.
            Debug.LogError($"[CharacterStore] Couldn't read {FilePath}: {e.Message}");
            Quarantine();
            _book = new CharacterBook();
        }
```

- [ ] **Step 3: Add the `Quarantine` helper**

Add directly below `EnsureLoaded()`:

```csharp
    /// Moves an unreadable characters.json aside so the next Save cannot
    /// overwrite it. Timestamped, so repeated failures never clobber each other.
    void Quarantine()
    {
        try
        {
            string path = FilePath;
            if (!File.Exists(path)) return;
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string dest  = path + ".corrupt-" + stamp;
            File.Move(path, dest);
            Debug.LogWarning($"[CharacterStore] Unreadable file moved to {dest}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[CharacterStore] Couldn't quarantine: {e.Message}");
        }
    }
```

- [ ] **Step 4: Verify it compiles**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: `No compile errors`

- [ ] **Step 5: Prove the round-trip and the quarantine with a harness**

Write to `<scratchpad>/TestCharacterStoreIO.cs` and run via `execute_script`. It uses a
temp directory, never the real save file:

```csharp
using System;
using System.IO;
using System.Text;
using UnityEngine;

public static class TestCharacterStoreIO
{
    public static string Execute()
    {
        var sb = new StringBuilder();
        int fail = 0;
        void Check(string label, bool ok, string detail)
        {
            if (!ok) fail++;
            sb.AppendLine((ok ? "PASS  " : "FAIL  ") + label + "  ->  " + detail);
        }

        string dir = Path.Combine(Path.GetTempPath(), "charstore_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "characters.json");

        // --- atomic write leaves no .tmp behind ---
        var book = new CharacterBook();
        book.characters.Add(CharacterProfile.Create("Zib", 2));
        book.lastSelectedId = book.characters[0].id;

        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonUtility.ToJson(book, true));
        if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);

        Check("file written", File.Exists(path), path);
        Check("no .tmp left behind", !File.Exists(tmp), tmp);

        var back = JsonUtility.FromJson<CharacterBook>(File.ReadAllText(path));
        Check("round-trips", back != null && back.characters.Count == 1
              && back.characters[0].name == "Zib", back?.characters[0].name);

        // --- overwrite an existing file (the File.Replace branch) ---
        book.characters.Add(CharacterProfile.Create("Bo", 5));
        File.WriteAllText(tmp, JsonUtility.ToJson(book, true));
        File.Replace(tmp, path, null);
        back = JsonUtility.FromJson<CharacterBook>(File.ReadAllText(path));
        Check("replace branch works", back != null && back.characters.Count == 2,
              back == null ? "null" : back.characters.Count.ToString());
        Check("no .tmp after replace", !File.Exists(tmp), tmp);

        // --- quarantine: a corrupt file is moved, not destroyed ---
        File.WriteAllText(path, "{ this is not json");
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string dest = path + ".corrupt-" + stamp;
        File.Move(path, dest);
        Check("corrupt file preserved", File.Exists(dest), dest);
        Check("original path free for a fresh save", !File.Exists(path), path);

        try { Directory.Delete(dir, true); } catch { }
        sb.Insert(0, fail == 0 ? "ALL PASS\n\n" : fail + " FAILURE(S)\n\n");
        return sb.ToString();
    }
}
```

Expected: `ALL PASS`

- [ ] **Step 6: Manual check against the real file**

Hand-edit `%AppData%\..\LocalLow\DefaultCompany\Solar System 2\characters.json` to
`{ broken`, enter Play, then create a character.
Expected: a `characters.json.corrupt-<timestamp>` exists alongside a fresh valid
`characters.json`; Console shows the quarantine warning.

- [ ] **Step 7: Commit**

```bash
git add "Assets/3 - Scripts/Character/CharacterStore.cs"
git commit -m "fix(character): atomic save and quarantine an unreadable characters.json

A direct WriteAllText truncates the live file before the new bytes land, so an
interrupted write loses every character. Writes now go to a .tmp and swap in.

The parse-failure path started an empty book, which meant the next mutation
OVERWROTE the damaged file. It is now moved to .corrupt-<timestamp> first, so
nothing is ever destroyed. Cheap today; the profile is about to carry levels,
money and hotbar."
```

---

## Task 2: Stop sending damage over the wire

`ReportHitServerRpc` relays an attacker-supplied float. Trusting the shooter for *hits* is a
deliberate design choice and stays; the *amount* needs no trust at all, because `DamagePerHit`
is a const both builds already have.

**Files:**
- Modify: `Assets/3 - Scripts/Multiplayer/NetworkPlayerCombat.cs`

- [ ] **Step 1: Drop the parameter from the call site**

Find:

```csharp
        if (best == null) return;
        ReportHitServerRpc(best.OwnerClientId, DamagePerHit);
```

Replace with:

```csharp
        if (best == null) return;
        ReportHitServerRpc(best.OwnerClientId);
```

- [ ] **Step 2: Drop it from both RPCs**

Find:

```csharp
    [ServerRpc]
    void ReportHitServerRpc(ulong victimClientId, float amount)
    {
        // Forward to the victim alone; nobody else needs to know.
        var p = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { victimClientId } }
        };
        ApplyDamageClientRpc(amount, p);
    }

    [ClientRpc]
    void ApplyDamageClientRpc(float amount, ClientRpcParams clientRpcParams = default)
    {
```

Replace with:

```csharp
    [ServerRpc]
    void ReportHitServerRpc(ulong victimClientId)
    {
        // The AMOUNT is deliberately not a parameter. Trusting the shooter for
        // whether a hit landed is a design choice (see the class comment);
        // trusting them for how much it hurt is just an unnecessary value on
        // the wire, and DamagePerHit is a const both builds already share.
        var p = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { victimClientId } }
        };
        ApplyDamageClientRpc(p);
    }

    [ClientRpc]
    void ApplyDamageClientRpc(ClientRpcParams clientRpcParams = default)
    {
```

- [ ] **Step 3: Apply the const in the body**

Find, inside `ApplyDamageClientRpc`:

```csharp
        var rm = ResourceManager.Instance;
        if (rm != null) rm.TakeDamage(amount);
```

Replace with:

```csharp
        var rm = ResourceManager.Instance;
        if (rm != null) rm.TakeDamage(DamagePerHit);
```

- [ ] **Step 4: Verify it compiles**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: `No compile errors`

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/Multiplayer/NetworkPlayerCombat.cs"
git commit -m "refactor(mp): don't send the damage amount over the wire

DamagePerHit is a const both builds share. Trusting the shooter for whether a
hit landed is deliberate; trusting them for the amount is a value on the wire
that buys nothing."
```

---

## Task 3: Remove the per-shot scene scan

`LocalMaxTracerLength()` runs `FindObjectOfType<PistolController>()` on every shot — exactly
the scan CLAUDE.md bans from hot paths. The controller that fired already knows the value.

**Files:**
- Modify: `Assets/3 - Scripts/Pickups/PistolController.cs`
- Modify: `Assets/3 - Scripts/Multiplayer/NetworkPlayerCombat.cs`

- [ ] **Step 1: Add the field to `ShotInfo`**

In `PistolController.cs`, find:

```csharp
        /// Distance from RayOrigin to whatever the world raycast hit, or `range`
        /// if it hit nothing. Used to reject shots that struck a wall first.
        public float WorldHitDistance;
    }
```

Replace with:

```csharp
        /// Distance from RayOrigin to whatever the world raycast hit, or `range`
        /// if it hit nothing. Used to reject shots that struck a wall first.
        public float WorldHitDistance;
        /// The firing weapon's tracer cap, carried on the shot so listeners
        /// never have to go looking for the controller that fired.
        public float MaxTracerLength;
    }
```

- [ ] **Step 2: Populate it where the event is raised**

Find:

```csharp
        OnLocalShotFired?.Invoke(new ShotInfo
        {
            RayOrigin        = origin,
            RayDirection     = forward,
            MuzzleStart      = tracerStart,
            WorldHitDistance = Vector3.Distance(origin, endPoint),
        });
```

Replace with:

```csharp
        OnLocalShotFired?.Invoke(new ShotInfo
        {
            RayOrigin        = origin,
            RayDirection     = forward,
            MuzzleStart      = tracerStart,
            WorldHitDistance = Vector3.Distance(origin, endPoint),
            MaxTracerLength  = maxTracerLength,
        });
```

- [ ] **Step 3: Read it from the shot and delete the scan**

In `NetworkPlayerCombat.cs`, find:

```csharp
        float tracerLen = Mathf.Min(shot.WorldHitDistance, LocalMaxTracerLength());
```

Replace with:

```csharp
        float tracerLen = Mathf.Min(shot.WorldHitDistance, shot.MaxTracerLength);
```

Then delete this method entirely:

```csharp
    static float LocalMaxTracerLength()
    {
        var p = Object.FindObjectOfType<PistolController>();
        return p != null ? p.maxTracerLength : 15f;
    }
```

- [ ] **Step 4: Verify it compiles and the scan is gone**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: `No compile errors`

Run: `grep -n "FindObjectOfType" "Assets/3 - Scripts/Multiplayer/NetworkPlayerCombat.cs"`
Expected: no output.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/Pickups/PistolController.cs" "Assets/3 - Scripts/Multiplayer/NetworkPlayerCombat.cs"
git commit -m "perf(mp): carry the tracer cap on ShotInfo instead of scanning per shot

FindObjectOfType ran on every trigger pull. The controller that fired already
knows the value, so it ships it with the shot."
```

---

## Task 4: Surrogate-safe name trimming

`Substring(0, MaxNameLength)` and the byte-trim loop can cut an emoji in half, leaving a
replacement glyph on the end of a name.

**Files:**
- Modify: `Assets/3 - Scripts/Character/CharacterProfile.cs`
- Modify: `Assets/3 - Scripts/Multiplayer/NetworkPlayerIdentity.cs`

- [ ] **Step 1: Add the shared helper to `CharacterProfile`**

Add directly above `Sanitize`:

```csharp
    /// Drops a trailing lone high surrogate.
    ///
    /// A char is 16 bits, but an emoji is a SURROGATE PAIR of two chars. Cutting
    /// a string to a char count can land between the halves and leave an
    /// orphaned high surrogate, which renders as a replacement box. Trimming one
    /// more char removes the whole character instead of half of it.
    public static string TrimDanglingSurrogate(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.IsHighSurrogate(s[s.Length - 1]) ? s.Substring(0, s.Length - 1) : s;
    }
```

- [ ] **Step 2: Use it in `Sanitize`**

Find:

```csharp
        var s = raw.Trim();
        if (s.Length > MaxNameLength) s = s.Substring(0, MaxNameLength);
        return s;
```

Replace with:

```csharp
        var s = raw.Trim();
        if (s.Length > MaxNameLength) s = TrimDanglingSurrogate(s.Substring(0, MaxNameLength));
        return s;
```

- [ ] **Step 3: Use it in `NetworkPlayerIdentity.Truncate`**

Find:

```csharp
        if (s.Length > CharacterProfile.MaxNameLength)
            s = s.Substring(0, CharacterProfile.MaxNameLength);
        while (System.Text.Encoding.UTF8.GetByteCount(s) > 29 && s.Length > 0)
            s = s.Substring(0, s.Length - 1);
        return s;
```

Replace with:

```csharp
        if (s.Length > CharacterProfile.MaxNameLength)
            s = CharacterProfile.TrimDanglingSurrogate(s.Substring(0, CharacterProfile.MaxNameLength));
        while (System.Text.Encoding.UTF8.GetByteCount(s) > 29 && s.Length > 0)
            s = CharacterProfile.TrimDanglingSurrogate(s.Substring(0, s.Length - 1));
        return s;
```

- [ ] **Step 4: Verify it compiles**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: `No compile errors`

- [ ] **Step 5: Prove it with a harness**

Write to `<scratchpad>/TestSurrogateTrim.cs` and run via `execute_script`:

```csharp
using System.Text;
using UnityEngine;

public static class TestSurrogateTrim
{
    public static string Execute()
    {
        var sb = new StringBuilder();
        int fail = 0;
        void Check(string label, bool ok, string detail)
        {
            if (!ok) fail++;
            sb.AppendLine((ok ? "PASS  " : "FAIL  ") + label + "  ->  " + detail);
        }

        // 16 rockets = 32 chars; the 16-char cut lands mid-pair.
        string rockets = string.Concat(System.Linq.Enumerable.Repeat("\U0001F680", 16));
        string cut = CharacterProfile.Sanitize(rockets);
        bool danglingRockets = cut.Length > 0 && char.IsHighSurrogate(cut[cut.Length - 1]);
        Check("emoji name has no dangling surrogate", !danglingRockets,
              $"len={cut.Length}");
        Check("emoji name stays within cap", cut.Length <= CharacterProfile.MaxNameLength,
              cut.Length.ToString());

        // Plain ASCII must be unaffected.
        string ascii = CharacterProfile.Sanitize("AbcdefghijklmnopqrstuvwxyZ");
        Check("ascii still cut to exactly the cap", ascii.Length == CharacterProfile.MaxNameLength,
              $"'{ascii}' ({ascii.Length})");

        // A name ending in one emoji, just over the cap.
        string mixed = CharacterProfile.Sanitize("ColonistNumber1\U0001F680");
        bool danglingMixed = mixed.Length > 0 && char.IsHighSurrogate(mixed[mixed.Length - 1]);
        Check("mixed name has no dangling surrogate", !danglingMixed, $"'{mixed}'");

        Check("helper leaves a clean string alone",
              CharacterProfile.TrimDanglingSurrogate("Zib") == "Zib", "Zib");
        Check("helper tolerates empty",
              CharacterProfile.TrimDanglingSurrogate("") == "", "(empty)");

        sb.Insert(0, fail == 0 ? "ALL PASS\n\n" : fail + " FAILURE(S)\n\n");
        return sb.ToString();
    }
}
```

Expected: `ALL PASS`

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/Character/CharacterProfile.cs" "Assets/3 - Scripts/Multiplayer/NetworkPlayerIdentity.cs"
git commit -m "fix(character): don't cut names through a surrogate pair

Trimming to a char count can land between the halves of an emoji and leave an
orphaned high surrogate, which renders as a replacement box."
```

---

## Task 5: Clear `isInDialogue` if a respawn is interrupted

`RespawnInPod()` sets `PlayerController.isInDialogue = true` and clears it after two waits.
Quit to menu inside those waits and the coroutine dies with the flag still set. It is a
`static`, so it survives the scene load and the **next** session starts unable to move.
Verified: nothing else clears it on scene load — only `DeathCutsceneController`'s own paths.

**Files:**
- Modify: `Assets/3 - Scripts/Multiplayer/MultiplayerDeathRespawn.cs`

- [ ] **Step 1: Clear the flag in `OnSceneLoaded`**

Find:

```csharp
    void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        _handling = false;
        // DeathCutsceneController nulls LegacyRespawnSuppressed in its OnDestroy,
        // so a scene change can wipe our chained hook - re-install on next death.
        _hookInstalled = false;
        Unsubscribe();
    }
```

Replace with:

```csharp
    void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        // RespawnInPod sets isInDialogue and clears it after two waits. A scene
        // change inside those waits kills the coroutine with the flag still set
        // — and it is a STATIC, so it survives the load and the next session
        // starts frozen. Nothing else clears it (DeathCutsceneController only
        // clears it on its own paths), so this is the backstop.
        if (_handling) PlayerController.isInDialogue = false;

        _handling = false;
        // DeathCutsceneController nulls LegacyRespawnSuppressed in its OnDestroy,
        // so a scene change can wipe our chained hook - re-install on next death.
        _hookInstalled = false;
        Unsubscribe();
    }
```

- [ ] **Step 2: Verify it compiles**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: `No compile errors`

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/Multiplayer/MultiplayerDeathRespawn.cs"
git commit -m "fix(mp): clear isInDialogue if a pod respawn is interrupted

Quitting to menu during the respawn waits killed the coroutine with the flag
still set. It is static, so the next session started unable to move."
```

---

## Task 6: FeatureVault flags

**Files:**
- Modify: `Assets/3 - Scripts/Scripts/Game/FeatureVault.cs`

- [ ] **Step 1: Append four flags**

Add at the end of the class, following the existing comment style (each flag says why it is
held, not that it failed):

```csharp
    /// The CONCERT VENUE — the stage, both AudienceZones, Max Audience, the
    /// strobe rig, cone beams and the whole audience spawner.
    ///
    /// Vaulted 2026-08-09 at Sam's request: "the concert is pretty heavy on a
    /// machine", and multiplayer is being built on a deliberately small
    /// baseline. Nothing failed and nothing is deleted — the hierarchy lives in
    /// Assets/1 - samsPrefabs/_Vaulted/ConcertVenue.prefab.
    ///
    /// Note the objects are REMOVED from the scene rather than merely disabled:
    /// an inactive GameObject still loads its meshes, textures and audio, which
    /// defeats the point of vaulting it for performance. See docs/VAULTED_SYSTEMS.md.
    public const bool ConcertVenue = false;

    /// Tev's ship parked outside his cabin and the ambush it triggers on entry.
    /// Vaulted 2026-08-09: a scripted jumpscare keyed to one player entering is
    /// ill-defined in co-op, and it is not worth designing around yet.
    public const bool TevCabinAmbush = false;

    /// The SHIP SCHOOL in the village (Combined_SHIPSCHOOL_0/1/2) and its
    /// instructor flow. Vaulted 2026-08-09 while the core co-op loop is built.
    public const bool ShipSchool = false;

    /// Tev's presence IN THE VILLAGE. Vaulted 2026-08-09.
    ///
    /// ⚠️ Tev HIMSELF is not vaulted — he still lives at his cabin and still owns
    /// rent collection and the mushroom onboarding, both of which are core loop.
    /// This flag covers only his village appearance.
    public const bool VillageTev = false;
```

- [ ] **Step 2: Verify it compiles**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: `No compile errors`

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/Scripts/Game/FeatureVault.cs"
git commit -m "chore: add vault flags for concert, ship school, village Tev, cabin ambush"
```

---

## Task 7: Discover the exact vault targets

Scene surgery is destructive, so the objects are identified and recorded before anything is
touched. Known already: `AudienceZone`, `AudienceZone 2`, `Max Audience`, `_StrobeRig`,
`_StrobeVisual`, `ConcertConeBeam`, `Combined_SHIPSCHOOL_0/1/2`.

**Files:**
- Create: `<scratchpad>/DumpVaultTargets.cs` (scratchpad only — never in the project)

- [ ] **Step 1: List every candidate with its full path and root**

Write to `<scratchpad>/DumpVaultTargets.cs` and run via `execute_script`:

```csharp
using System.Text;
using UnityEngine;

public static class DumpVaultTargets
{
    public static string Execute()
    {
        var sb = new StringBuilder();
        string[] needles =
        {
            "concert", "audience", "stage", "strobe", "speaker", "laser", "blinder", "haze",
            "shipschool", "ship school", "tev",
        };

        foreach (var t in Object.FindObjectsOfType<Transform>(true))
        {
            string lower = t.name.ToLowerInvariant();
            foreach (var n in needles)
            {
                if (!lower.Contains(n)) continue;
                sb.AppendLine($"[{n,-11}] {Path(t)}   (active={t.gameObject.activeInHierarchy}, children={t.childCount})");
                break;
            }
        }

        sb.AppendLine();
        sb.AppendLine("--- objects carrying Concert/Audience scripts ---");
        foreach (var mb in Object.FindObjectsOfType<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            string tn = mb.GetType().Name;
            if (tn.StartsWith("Concert") || tn.StartsWith("Audience") || tn == "SpeakerSource")
                sb.AppendLine($"{tn,-26} on {Path(mb.transform)}");
        }
        return sb.ToString();
    }

    static string Path(Transform t)
    {
        var s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }
}
```

- [ ] **Step 2: Record the findings in the plan**

Paste the output into a scratch note. Group into four vault sets: concert, ship school,
Tev cabin ambush, village Tev. Identify the **topmost** object of each hierarchy — that is
what becomes the prefab, so children come along automatically.

- [ ] **Step 3: Flag anything ambiguous before proceeding**

If an object could belong to a kept system (for example a "stage" that is the fish-preview
stage, or a Tev prop inside his cabin that should stay), stop and ask rather than guessing.
`FishPreviewStage` and `TevFamilyPhoto_Prop` are known keepers.

---

## Task 8: Vault the concert venue

**Files:**
- Create: `Assets/1 - samsPrefabs/_Vaulted/ConcertVenue.prefab`
- Modify: `Assets/1.6.7.7.7.unity`

- [ ] **Step 1: Save each concert root as a prefab, then delete it from the scene**

Write to `<scratchpad>/VaultConcert.cs` and run via `execute_script`. Fill `roots` from the
Task 7 output before running:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class VaultConcert
{
    // Filled in from Task 7. Full hierarchy paths of the TOPMOST object of each
    // concert hierarchy.
    static readonly string[] Roots =
    {
        // e.g. "--- World ---/ConcertVenue",
    };

    public static string Execute()
    {
        const string Dir = "Assets/1 - samsPrefabs/_Vaulted";
        if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);

        var sb = new StringBuilder();
        var toDelete = new List<GameObject>();

        foreach (var path in Roots)
        {
            var go = GameObject.Find(path);
            if (go == null) { sb.AppendLine("MISS  " + path); continue; }

            string dest = $"{Dir}/{go.name}.prefab";
            dest = AssetDatabase.GenerateUniqueAssetPath(dest);
            var saved = PrefabUtility.SaveAsPrefabAsset(go, dest, out bool ok);
            if (!ok || saved == null) { sb.AppendLine("FAIL  could not save " + path); continue; }

            sb.AppendLine($"SAVED {dest}   <- {path}");
            toDelete.Add(go);
        }

        foreach (var go in toDelete)
        {
            sb.AppendLine("DELETED from scene: " + go.name);
            Object.DestroyImmediate(go);
        }

        if (toDelete.Count > 0)
        {
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            sb.AppendLine("Scene saved.");
        }
        AssetDatabase.Refresh();
        return sb.ToString();
    }
}
```

Expected: one `SAVED` line per root, matching `DELETED` lines, then `Scene saved.`

- [ ] **Step 2: Confirm nothing concert-related survives in the scene**

Re-run `DumpVaultTargets` from Task 7.
Expected: no concert/audience/strobe entries remain (aside from the known keeper
`FishPreviewStage`).

- [ ] **Step 3: Verify it compiles and the scripts are intact**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: `No compile errors` — the `Concert/` scripts still compile; only the instances left.

- [ ] **Step 4: Delete the one-shot editor script**

The vault script uses `UnityEditor` and lives in the scratchpad, so nothing to remove from
the project. Confirm with:

Run: `git status --short | grep -i vault`
Expected: only the new prefab and the scene, never a `.cs` in `Assets/`.

- [ ] **Step 5: Commit**

```bash
git add "Assets/1 - samsPrefabs/_Vaulted" Assets/1.6.7.7.7.unity
git commit -m "chore: vault the concert venue out of the scene

Saved as a prefab and REMOVED from the scene rather than disabled — an
inactive GameObject still loads its meshes, textures and audio, which defeats
vaulting it for performance. Restore by dragging the prefab back in."
```

---

## Task 9: Vault the ship school, cabin ambush and village Tev

Same mechanism, three more sets. Kept separate from Task 8 so a mistake in one is one
`git revert`, and because the concert is the only one being removed for performance.

**Files:**
- Create: `Assets/1 - samsPrefabs/_Vaulted/ShipSchool.prefab`, `TevCabinAmbush.prefab`, `VillageTev.prefab`
- Modify: `Assets/1.6.7.7.7.unity`

- [ ] **Step 1: Vault each set**

Reuse the `VaultConcert` script from Task 8, changing only the `Roots` array, and run it once
per set. Known ship-school roots: `Combined_SHIPSCHOOL_0`, `Combined_SHIPSCHOOL_1`,
`Combined_SHIPSCHOOL_2`. The ambush ship and village Tev come from Task 7's output.

**Do not vault:** `TevFamilyPhoto_Prop` or anything inside Tev's cabin — Tev himself stays.

- [ ] **Step 2: Confirm Tev still works at his cabin**

Run: `grep -rn "TevDialogue\|TevMushroomOnboarding" --include="*.cs" "Assets/3 - Scripts" | head`
Then confirm in the Editor that the cabin's Tev object still exists and still has those
components.
Expected: Tev present at the cabin; rent and mushroom onboarding untouched.

- [ ] **Step 3: Verify it compiles**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: `No compile errors`

- [ ] **Step 4: Commit**

```bash
git add "Assets/1 - samsPrefabs/_Vaulted" Assets/1.6.7.7.7.unity
git commit -m "chore: vault the ship school, Tev's cabin ambush and his village presence

Tev himself stays at his cabin — he owns rent collection and the mushroom
onboarding, both core loop. Only his village appearance is vaulted."
```

---

## Task 10: Document the vault

**Files:**
- Create: `docs/VAULTED_SYSTEMS.md`

- [ ] **Step 1: Write the restore instructions**

```markdown
# Vaulted systems

Nothing here failed or was deleted. Each was switched off to keep the multiplayer
baseline small; each comes back the same way.

## How to restore any of them

1. Flip its flag in `Assets/3 - Scripts/Scripts/Game/FeatureVault.cs` to `true`.
2. Drag the matching prefab from `Assets/1 - samsPrefabs/_Vaulted/` back into
   `1.6.7.7.7.unity`, at the same parent it came from (recorded below).
3. Save the scene.

## Why prefab-and-delete rather than "set inactive"

An inactive GameObject still loads its meshes, textures and audio with the scene.
The concert was vaulted specifically because it is heavy, so leaving it in the
scene disabled would have kept most of the cost. Removing the instances gives back
the CPU, GPU and scene-load memory; the prefab keeps the work.

## What is vaulted (2026-08-09)

| System | Flag | Prefab | Original parent |
|---|---|---|---|
| Concert venue | `FeatureVault.ConcertVenue` | `ConcertVenue.prefab` | _(record during Task 8)_ |
| Ship school | `FeatureVault.ShipSchool` | `ShipSchool.prefab` | _(record during Task 9)_ |
| Tev's cabin ambush | `FeatureVault.TevCabinAmbush` | `TevCabinAmbush.prefab` | _(record during Task 9)_ |
| Tev in the village | `FeatureVault.VillageTev` | `VillageTev.prefab` | _(record during Task 9)_ |

## Explicitly NOT vaulted

- **Tev himself**, at his cabin — he owns weekly rent collection and the mushroom
  onboarding. Only his village appearance went.
- **The village** — kept as scenery; Sam intends to give it a purpose later.
- The entire `Assets/3 - Scripts/Concert/` script folder still compiles. Only the
  scene instances were removed.
```

Replace each `_(record during Task N)_` with the real parent path from the vault script's
output — the plan is not done until those are filled in.

- [ ] **Step 2: Commit**

```bash
git add docs/VAULTED_SYSTEMS.md
git commit -m "docs: how to restore the vaulted systems"
```

---

## Task 11: Whole-phase verification

- [ ] **Step 1: Compile**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: `No compile errors`

- [ ] **Step 2: Confirm no editor scripts leaked into the project**

Run: `grep -rln "using UnityEditor" --include="*.cs" "Assets/3 - Scripts" | grep -v "/Editor/"`
Expected: no output. A `UnityEditor` reference outside an `Editor/` folder breaks player builds.

- [ ] **Step 3: Confirm every new script has its `.meta`**

Run: `git status --short`
Expected: every `.cs` addition paired with a `.cs.meta`. This repo has lost untracked files to
`commit -a` before.

- [ ] **Step 4: Playtest checklist for Sam**

- Boot to main menu, create/pick a character — no errors.
- Enter the world: no concert stage, no audience, no ship school, no ship outside Tev's cabin,
  no Tev in the village.
- Tev at his cabin still talks and still collects rent.
- The village is still standing.
- Frame rate at the old concert site is no worse than elsewhere.
- Shoot another player: damage still lands, tracer still leaves the muzzle.
- Die in co-op: wake in the pod, door opens then closes.
- Quit to menu mid-respawn, start a new session: you can still move.

---

## Self-review notes

- **Spec coverage:** Phase 0 of the design lists four vault flags (Tasks 6–9) and five fixes
  (Tasks 1–5); all are covered. Design sections 4–8 are later phases and out of scope here.
- **Placeholders:** the only intentional blanks are the `Roots` arrays and the parent-path
  column, both of which are *outputs of Task 7* and explicitly gated on it. Every code step
  shows complete code.
- **Type consistency:** `TrimDanglingSurrogate` is defined once in `CharacterProfile` (Task 4
  Step 1) and referenced with that exact name in Steps 2 and 3. `ShotInfo.MaxTracerLength` is
  defined in Task 3 Step 1 and read in Step 3. `DamagePerHit` already exists.
- **Ordering:** code fixes precede scene surgery so a destructive mistake cannot mask them.
