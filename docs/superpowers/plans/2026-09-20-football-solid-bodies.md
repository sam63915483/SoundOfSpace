# Football Solid Bodies Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give every football alien a solid body inside the existing field-space sim, and replace the distance-plus-dice blocking, pocket timer and tackle radius with contact-decided outcomes.

**Architecture:** A new `FootballBodies` pass runs every sim step after the brains move and before tackles are judged: it separates overlapping discs, trades momentum and records a contact list on `PlayView`. `PlayInstance` reads that list to form engagements (a shoving match resolved from the two teams' stats) and to start tackles (hit quality from closing speed and angle). The brains, routes, rig, replay and analytic ball are untouched except for the pass-set in `OLBrain`/`CenterBrain`, a rush move in `DLBrain`, and a sidestep in `MoveToBrain`. The human QB slot takes part as a disc; shoves reach the real rigidbody, and kinematic capsules stop the player walking through aliens.

**Tech Stack:** Unity 2022.3 C# (Built-in RP), no test framework. Verification = `py -3 prototypes/shuttle-computer/test/compile-unity.py` (compile), the edit-mode scripts in `tools/football/*.cs.txt` run through Coplay `execute_script` (soak / drill), and Sam's playtest. Spec: `docs/superpowers/specs/2026-09-20-football-solid-bodies-design.md`.

---

## Ground rules for every task

- **Never enter play mode.** Sam runs playtests. Edit-mode scripts only.
- **Compile check after every code step:** `py -3 prototypes/shuttle-computer/test/compile-unity.py` from the repo root. Expected last line: `OK` (or zero `error CS` lines). Any `warning CS` is a real signal (the baseline is zero).
- **Soak = the test.** `tools/football/FootballSoak.cs.txt` → copy to the scratchpad, replace `<scratchpad>/` with the scratchpad folder, run with Coplay `mcp__coplay-mcp__execute_script`. Before trusting it, check `Library/ScriptAssemblies/Assembly-CSharp.dll` is newer than the edited `.cs` (an unfocused editor compiles but does not reload the domain; run `AssetDatabase.Refresh(); UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();` in a script if not).
- **Append new serialized fields at the END of a MonoBehaviour.** `FootballPlayer` and `FootballMatch` are MonoBehaviours in the saved scene; `[NonSerialized]` fields can go anywhere.
- **Commit after each task** with `git add` on every touched file (new `.cs` needs its `.meta` too: let the Editor generate it, or write one by copying `FootballShader.cs.meta` and changing the guid to a fresh 32-hex string).
- Branch: `feat/helmet-hud` (current). Do not push.

## File map

| File | Change |
|---|---|
| `Assets/3 - Scripts/Football/FootballBodies.cs` (**new**) | `BodyContact` struct; `FootballBodies.Resolve` (separate, trade momentum, record contacts); role radius/mass tables; the two public formulas `BlockDrive` and `TackleHit` |
| `Assets/3 - Scripts/Football/FootballPlayer.cs` | `BodyRadius`, `BodyMass`, `BodyLifted`, `contactSwing`, `leanField`, `inContact`, `ShiftBody`, `SetVelAlong`, `TakePendingShove`, `wantTangentOf`; capsule collider + kinematic rigidbody for aliens; contact ring |
| `Assets/3 - Scripts/Football/IPlayerBrain.cs` | `PlayView.contacts`, `PlayView.EngagedWith`, `PlayView.SetEngaged` |
| `Assets/3 - Scripts/Football/PlayInstance.cs` | run the pass in Setup and Live; `ResolveEngagements` replaces `BlockSlowdown`; geometric `pocketCollapsed`; contact `TickTackles`; wrap break by momentum; pile-on by contact; stats |
| `Assets/3 - Scripts/Football/FootballBrains.cs` | `OLBrain`/`CenterBrain` pass-set with lateral cap; `DLBrain` bull/swim; `MoveToBrain` sidestep; four `speedScale < 0.5f` reads → `view.EngagedWith` |
| `Assets/3 - Scripts/Football/FootballAlienRig.cs` | `lean` into the spine |
| `Assets/3 - Scripts/Football/FootballHumanQB.cs` | apply the slot's pending shove to the player's rigidbody |
| `Assets/3 - Scripts/Football/FootballBroadcast.cs` | strip `Rigidbody` from replay ghosts |
| `Assets/3 - Scripts/Football/FootballMatch.cs` | `GameStats` counters + `Summary`; F8 "contact discs" toggle |
| `tools/football/ContactDrill.cs.txt` (**new**) | tables the two formulas and checks the separation pass |
| `tools/football/FootballSoak.cs.txt`, `FootballAnalyzer.cs.txt`, `README.md` | overlap assertion; README entry |
| `docs/FOOTBALL.md` | Pass 5 section; rewrite the Blocking and Tackles rule paragraphs |

---

### Task 1: The disc layer — `FootballBodies.cs` and the player's body fields

**Files:**
- Create: `Assets/3 - Scripts/Football/FootballBodies.cs`
- Modify: `Assets/3 - Scripts/Football/FootballPlayer.cs` (fields after `humanDriven` at line 63; methods after `Nudge` at line 333)

- [ ] **Step 1: Write the drill that exercises the pass (the test)**

Create `tools/football/ContactDrill.cs.txt`:

```csharp
using System.Text;
using UnityEditor;
using UnityEngine;

// Contact drill (2026-09-20): no play mode, no whole game. (A) two discs
// pushed into each other come out separated with a contact recorded and
// momentum traded; (B) a table of BlockDrive across the stat range; (C) a
// table of TackleHit across closing speed × angle against WrapThreshold.
// Copy to the scratchpad, replace <scratchpad>/, run with execute_script.
public static class ContactDrill
{
    const string Out = "<scratchpad>/drill.txt";

    public static void Execute()
    {
        var sb = new StringBuilder();
        GameObject root = null;
        try
        {
            root = new GameObject("__ContactDrill") { hideFlags = HideFlags.DontSave };
            root.AddComponent<FootballField>();
            var home = new FootballTeam { name = "A", shortName = "A", attackDir = 1 };
            var away = new FootballTeam { name = "B", shortName = "B", attackDir = -1 };
            var mat = new Material(FootballShader.Standard);
            FootballPlayer Make(FootballTeam t, FootballRole off, FootballRole def)
            {
                var go = new GameObject("p"); go.transform.SetParent(root.transform, false);
                var p = go.AddComponent<FootballPlayer>();
                p.Init(root.transform, t, off, 0, def, 0, mat);
                return p;
            }
            // (A) an OL and a DL 0.6 m apart (radii 0.5 + 0.5 = 1.0 → 0.4 overlap), DL running at him.
            var ol = Make(home, FootballRole.OL, FootballRole.DL);
            var dl = Make(away, FootballRole.DL, FootballRole.OL);
            ol.Teleport(new Vector3(0f, 0f, 0f), Vector3.forward);
            dl.Teleport(new Vector3(0f, 0f, 0.6f), Vector3.back);
            dl.SetVelForDrill(new Vector3(0f, 0f, -4f));
            var players = new System.Collections.Generic.List<FootballPlayer> { ol, dl };
            var contacts = new System.Collections.Generic.List<BodyContact>();
            FootballBodies.Resolve(players, contacts);
            float gap = Vector3.Distance(ol.Pos, dl.Pos);
            sb.AppendLine("A: gap after resolve " + gap.ToString("0.000") + " (want 1.000) contacts " + contacts.Count + " (want 1)");
            sb.AppendLine("A: ol.z " + ol.Pos.z.ToString("0.000") + " dl.z " + dl.Pos.z.ToString("0.000") + " (equal masses: each moved 0.2)");
            sb.AppendLine("A: dl.vel.z " + dl.Vel.z.ToString("0.00") + " ol.vel.z " + ol.Vel.z.ToString("0.00") + " (inelastic: both -2.00)");
            sb.AppendLine("A: closing " + (contacts.Count > 0 ? contacts[0].closing.ToString("0.00") : "-") + " (want 4.00)");
            bool okA = Mathf.Abs(gap - 1f) < 0.01f && contacts.Count == 1 && Mathf.Abs(dl.Vel.z + 2f) < 0.01f && Mathf.Abs(ol.Vel.z + 2f) < 0.01f;
            sb.AppendLine(okA ? "A: PASS" : "A: FAIL");
            // A downed man is immovable: only the standing man moves.
            ol.Teleport(Vector3.zero, Vector3.forward); dl.Teleport(new Vector3(0f, 0f, 0.6f), Vector3.back);
            ol.FallDown(2f);
            FootballBodies.Resolve(players, contacts);
            sb.AppendLine("A2: downed ol.z " + ol.Pos.z.ToString("0.000") + " (want 0) dl.z " + dl.Pos.z.ToString("0.000") + " (want 1.05 = 0.55 + 0.5)");
            sb.AppendLine(Mathf.Abs(ol.Pos.z) < 0.001f && Mathf.Abs(dl.Pos.z - 1.05f) < 0.01f ? "A2: PASS" : "A2: FAIL");

            // (B) BlockDrive: + = blocker driven back (m/s).
            sb.AppendLine("\nB: BlockDrive(passRush, blocking) m/s, lineman on lineman, swing 1");
            sb.Append("      blk=0.2  0.5  0.8\n");
            foreach (float pr in new[] { 0.2f, 0.5f, 0.8f })
            {
                sb.Append("pr=" + pr.ToString("0.0"));
                foreach (float bl in new[] { 0.2f, 0.5f, 0.8f })
                    sb.Append("  " + FootballBodies.BlockDrive(FootballBodies.Push(pr, 1.25f, 1f), FootballBodies.Hold(bl, 1.25f, 1f, true)).ToString("+0.00;-0.00"));
                sb.AppendLine();
            }
            sb.AppendLine("even stats should be ~0; pr 0.8 vs bl 0.2 should be near +" + FootballBodies.MaxDrive);

            // (C) TackleHit vs WrapThreshold (" + FootballBodies.WrapThreshold + ").
            sb.AppendLine("\nC: TackleHit(closing, square) mass 1 — W = wrap, a = arm tackle");
            sb.Append("           sq=1.0  0.8  0.5  0.3  0.0\n");
            foreach (float closing in new[] { 0f, 0.5f, 1.5f, 2.5f, 4f, 7f })
            {
                sb.Append("close=" + closing.ToString("0.0").PadLeft(4));
                foreach (float sq in new[] { 1f, 0.8f, 0.5f, 0.3f, 0f })
                {
                    float h = FootballBodies.TackleHit(closing, sq, 1f);
                    sb.Append("  " + h.ToString("0.00") + (h >= FootballBodies.WrapThreshold ? "W" : "a"));
                }
                sb.AppendLine();
            }
            sb.AppendLine("targets: close 0.5 sq 1.0 = W (a chase-down from behind wraps); close 2.5 sq 0.3 = a (a juke makes it glancing); close 4 sq 0.8 = W");
        }
        catch (System.Exception e) { sb.AppendLine("!! EXCEPTION " + e); }
        finally { if (root != null) Object.DestroyImmediate(root); }
        System.IO.File.WriteAllText(Out, sb.ToString());
        Debug.Log("[ContactDrill] wrote " + Out);
    }
}
```

- [ ] **Step 2: Run the drill to verify it fails**

Copy to the scratchpad, replace `<scratchpad>/`, run with `execute_script`.
Expected: compile errors naming `BodyContact`, `FootballBodies`, `SetVelForDrill` (the script cannot run yet). That is the failing state.

- [ ] **Step 3: Create `FootballBodies.cs`**

```csharp
using System.Collections.Generic;
using UnityEngine;

/// One contact between two discs this tick. `normal` points from `a` to `b`
/// (field space, y = 0). `closing` is how fast they were approaching along
/// it before the momentum trade (+ = toward each other).
public struct BodyContact
{
    public FootballPlayer a, b;
    public Vector3 normal;
    public float closing;
    public float overlap;
    public FootballPlayer Other(FootballPlayer p) => p == a ? b : a;
    /// The normal as seen from `p` (pointing at the other man).
    public Vector3 NormalFrom(FootballPlayer p) => p == a ? normal : -normal;
}

/// The solid-body layer (design 2026-09-20): every man is a disc on the field
/// plane with a radius and a mass. Once per sim step, AFTER the brains have
/// moved everyone and BEFORE tackles are judged, overlapping pairs are pushed
/// apart (the heavier man moves less), closing momentum is traded (no bounce),
/// and every contact is recorded for the blocking and tackling rules. Nothing
/// else in the sim tests "touching" by distance any more.
public static class FootballBodies
{
    public const float DownRadius = 0.55f;        // a man on the ground: a wide, immovable obstacle

    // Blocking: two forces in an engagement (spec §2).
    public const float PushBase = 1.0f;
    public const float DriveGain = 2.4f;          // m/s per unit of (push − hold)
    public const float MaxDrive = 1.2f;           // m/s, either way
    public const float NonLinemanHold = 0.6f;     // a receiver's hold is weak
    public const float SwimDriveScale = 0.5f;     // swimming, he pushes at half strength

    // Tackling: hit quality (spec §3).
    public const float TackleBase = 1.5f;         // a tackler on him at no closing speed still has this much
    public const float WrapThreshold = 2.0f;      // hit ≥ this is a wrap; below is an arm tackle

    public static float RadiusFor(FootballRole role)
        => role == FootballRole.OL || role == FootballRole.DL || role == FootballRole.C ? 0.50f
         : role == FootballRole.LB || role == FootballRole.S ? 0.45f : 0.42f;

    public static float MassFor(FootballRole role)
        => role == FootballRole.OL || role == FootballRole.DL || role == FootballRole.C ? 1.25f
         : role == FootballRole.LB || role == FootballRole.S ? 1.10f : 1.00f;

    public static bool IsLineman(FootballPlayer p)
        => p.role == FootballRole.OL || p.role == FootballRole.DL || p.role == FootballRole.C;

    public static float Push(float stat, float mass, float swing) => PushBase * (0.6f + 0.8f * stat) * mass * swing;
    public static float Hold(float stat, float mass, float swing, bool lineman) => PushBase * (0.6f + 0.8f * stat) * mass * swing * (lineman ? 1f : NonLinemanHold);
    /// m/s the engaged pair moves along the rusher's line; + = the blocker is driven back.
    public static float BlockDrive(float push, float hold) => Mathf.Clamp((push - hold) * DriveGain, -MaxDrive, MaxDrive);

    /// `closing` m/s along the contact normal, `square` 0..1 (1 = met head-on
    /// or from straight behind, 0 = glancing), `mass` the tackler's.
    public static float TackleHit(float closing, float square, float mass)
        => (TackleBase + Mathf.Max(0f, closing)) * (0.35f + 0.65f * square) * mass;

    /// The pass. `contacts` is cleared and refilled.
    public static void Resolve(IReadOnlyList<FootballPlayer> players, List<BodyContact> contacts)
    {
        contacts.Clear();
        int n = players.Count;
        for (int i = 0; i < n; i++)
        {
            var a = players[i];
            if (a == null || a.Benched) continue;
            for (int j = i + 1; j < n; j++)
            {
                var b = players[j];
                if (b == null || b.Benched) continue;
                if (a.IsDown && b.IsDown) continue;                              // a pile doesn't shuffle
                if ((a.BodyLifted && b.IsDiving) || (b.BodyLifted && a.IsDiving)) continue;   // a hurdle clears a diver
                float ra = a.IsDown ? DownRadius : a.BodyRadius, rb = b.IsDown ? DownRadius : b.BodyRadius;
                Vector3 d = b.Pos - a.Pos; d.y = 0f;
                float dist = d.magnitude, minD = ra + rb;
                if (dist >= minD) continue;
                Vector3 normal = dist > 1e-4f ? d / dist : Vector3.right;
                float overlap = minD - dist;
                float ia = a.IsDown ? 0f : 1f / a.BodyMass, ib = b.IsDown ? 0f : 1f / b.BodyMass;
                float sum = ia + ib;
                if (sum > 0f)
                {
                    a.ShiftBody(-normal * (overlap * ia / sum));
                    b.ShiftBody(normal * (overlap * ib / sum));
                }
                float va = Vector3.Dot(a.Vel, normal), vb = Vector3.Dot(b.Vel, normal);
                float closing = va - vb;
                if (closing > 0f && sum > 0f)
                {
                    // Perfectly inelastic along the normal: both take the common speed.
                    float common = ia > 0f && ib > 0f ? (va * a.BodyMass + vb * b.BodyMass) / (a.BodyMass + b.BodyMass) : (ia > 0f ? vb : va);
                    if (ia > 0f) a.SetVelAlong(normal, common);
                    if (ib > 0f) b.SetVelAlong(normal, common);
                }
                a.inContact = b.inContact = true;
                contacts.Add(new BodyContact { a = a, b = b, normal = normal, closing = closing, overlap = overlap });
            }
        }
    }
}
```

- [ ] **Step 4: Add the body fields and methods to `FootballPlayer`**

After the `humanDriven` field (line 63) add:

```csharp
    // ── the solid body (FootballBodies) ────────────────────────────────────
    /// Disc radius / mass for the contact pass; set from the role in Init.
    [System.NonSerialized] public float BodyRadius = 0.42f, BodyMass = 1f;
    /// Rolled per snap by PlayInstance (0.8–1.2): how strong he is today.
    [System.NonSerialized] public float contactSwing = 1f;
    /// Field-space lean from a shove (the rig tips the spine into it). Reset every tick by the engagement pass.
    [System.NonSerialized] public Vector3 leanField;
    /// Touched somebody this tick (for the debug ring). Reset by FootballBodies.Resolve's caller.
    [System.NonSerialized] public bool inContact;
    /// While engaged in a swim, his wanted move is projected onto the tangent of this blocker (null = free).
    [System.NonSerialized] public FootballPlayer wantTangentOf;
    /// A human slot: displacement the contact pass asked for this step, for FootballHumanQB to apply to the real body.
    Vector3 _pendingShove;
    /// Off the field (the alien QB at the bench while the human plays).
    public bool Benched => humanDriven ? false : _benched;
    bool _benched;
    /// A hurdling carrier's disc is lifted through the middle of the leap so a diver passes under it.
    public bool BodyLifted => IsHurdling && HurdlePhase > 0.2f && HurdlePhase < 0.8f;
```

Check whether a "benched" notion already exists: `grep -n "Bench\b\|Bench(" FootballPlayer.cs`. `Bench()` exists (used at boot). Wire `_benched = true` inside `Bench()` and `_benched = false` inside `Teleport(...)` (a man teleported onto the field is on it). If `Bench()` sets a position off-field only, the flag is still what the pass needs.

After `Nudge` (line 333) add:

```csharp
    /// The contact pass moves the disc: a few centimetres, never a teleport.
    /// A human slot doesn't move here — the real body does (FootballHumanQB).
    public void ShiftBody(Vector3 deltaField)
    {
        deltaField.y = 0f;
        if (humanDriven) { _pendingShove += deltaField; return; }
        _pos += deltaField;
        transform.localPosition = _pos;
    }

    /// Replace the velocity component along `normal` (a momentum trade).
    public void SetVelAlong(Vector3 normal, float value)
    {
        if (humanDriven) return;
        float cur = Vector3.Dot(_vel, normal);
        _vel += normal * (value - cur);
    }

    /// FootballHumanQB drains this every step and applies it to the player's rigidbody.
    public Vector3 TakePendingShove() { var s = _pendingShove; _pendingShove = Vector3.zero; return s; }

    /// Drill / probe only: set the velocity directly.
    public void SetVelForDrill(Vector3 velField) { velField.y = 0f; _vel = velField; }
```

In `Init` (after `_baseMaxSpeed = _maxSpeed = ...`):

```csharp
        BodyRadius = FootballBodies.RadiusFor(off);
        BodyMass = FootballBodies.MassFor(off);
```

and in `SetSide(bool offense)` (line 309), after `role` is assigned, the same two lines so a man's disc follows his current role (an OL is a DL both ways, so the values do not change, but keep them in one place).

In the movement tick (line ~665, just before `Vector3 targetVel = want * ...`) add the swim projection:

```csharp
        if (wantTangentOf != null)
        {
            Vector3 nrm = wantTangentOf.Pos - _pos; nrm.y = 0f;
            if (nrm.sqrMagnitude > 1e-4f) { nrm.Normalize(); want -= nrm * Mathf.Max(0f, Vector3.Dot(want, nrm)); }
        }
```

- [ ] **Step 5: Compile check**

Run: `py -3 prototypes/shuttle-computer/test/compile-unity.py`
Expected: no `error CS`, no new `warning CS`.

- [ ] **Step 6: Run the drill**

Run `ContactDrill` through `execute_script` (after a domain reload: check the DLL timestamp).
Expected in `drill.txt`: `A: PASS`, `A2: PASS`, a B table with `~0.00` on the diagonal and `+1.20` at pr 0.8 / bl 0.2, a C table where `close=0.5 sq=1.0` is `W`, `close=2.5 sq=0.3` is `a`, `close=4.0 sq=0.8` is `W`. If B's diagonal is not near zero, `Push`/`Hold` differ — they must be the same formula for a lineman.

- [ ] **Step 7: Commit**

```bash
git add "Assets/3 - Scripts/Football/FootballBodies.cs" "Assets/3 - Scripts/Football/FootballBodies.cs.meta" "Assets/3 - Scripts/Football/FootballPlayer.cs" tools/football/ContactDrill.cs.txt
git commit -m "feat(football): solid-body layer — discs with radius and mass, separation + inelastic momentum trade, contact list; ContactDrill

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: Run the pass every step; contacts on `PlayView`; the setup sidestep

**Files:**
- Modify: `Assets/3 - Scripts/Football/IPlayerBrain.cs` (`PlayView`, line 56+)
- Modify: `Assets/3 - Scripts/Football/PlayInstance.cs` (`Tick` Setup case ~line 466; `TickLive` line 684)
- Modify: `Assets/3 - Scripts/Football/FootballBrains.cs` (`MoveToBrain`, line 102)
- Modify: `tools/football/FootballAnalyzer.cs.txt` (line 114)

- [ ] **Step 1: Turn the analyzer's overlap flag into an assertion (the test)**

In `FootballAnalyzer.cs.txt` line 114 replace the `d < 0.35f` check with:

```csharp
                    float minD = (ps[i].IsDown ? FootballBodies.DownRadius : ps[i].BodyRadius) + (ps[j].IsDown ? FootballBodies.DownRadius : ps[j].BodyRadius);
                    if (!(ps[i].IsDown && ps[j].IsDown) && d < minD - 0.05f) Flag("!! OVERLAP after the contact pass", ps[i].name + " / " + ps[j].name + " " + d.ToString("0.00") + " m (min " + minD.ToString("0.00") + ") at " + t.ToString("0.0") + " s");
```

Run the analyzer now (before Task 2's code): expected many `!! OVERLAP` flags (the pass is not wired yet). That is the failing state.

- [ ] **Step 2: Contacts and engagements on `PlayView`**

In `IPlayerBrain.cs`, inside `PlayView` after `public float qbExtendSide;` add:

```csharp
    /// Every disc contact this tick (FootballBodies.Resolve). Rebuilt each step.
    public readonly List<BodyContact> contacts = new List<BodyContact>();
    readonly Dictionary<FootballPlayer, FootballPlayer> _engaged = new Dictionary<FootballPlayer, FootballPlayer>();
    /// The blocker holding this man this tick, or null. (Replaces every `speedScale < 0.5f` read.)
    public FootballPlayer EngagedWith(FootballPlayer p) => p != null && _engaged.TryGetValue(p, out var b) ? b : null;
    public bool IsEngaged(FootballPlayer p) => p != null && _engaged.ContainsKey(p);
    public void ClearEngaged() => _engaged.Clear();
    public void SetEngaged(FootballPlayer defender, FootballPlayer blocker) => _engaged[defender] = blocker;
    /// The contact between `p` and `q` this tick, if any.
    public bool ContactBetween(FootballPlayer p, FootballPlayer q, out BodyContact c)
    {
        for (int i = 0; i < contacts.Count; i++)
            if ((contacts[i].a == p && contacts[i].b == q) || (contacts[i].a == q && contacts[i].b == p)) { c = contacts[i]; return true; }
        c = default; return false;
    }
```

Add `using System.Collections.Generic;` at the top if missing.

- [ ] **Step 3: Run the pass in `PlayInstance`**

Add a helper in `PlayInstance` (near `BlockSlowdown`):

```csharp
    /// The contact pass: after everyone has moved, before anything is judged.
    void ResolveBodies()
    {
        for (int i = 0; i < _players.Count; i++) _players[i].inContact = false;
        FootballBodies.Resolve(_players, view.contacts);
        for (int i = 0; i < view.contacts.Count; i++) { _stats.contacts++; _stats.maxOverlap = Mathf.Max(_stats.maxOverlap, view.contacts[i].overlap); }
    }
```

Call it in the Setup case right after the `foreach (var kv in _setup) { ... p.Tick(view, dt); ... }` loop finishes (before `_othersSet = all;`), and in `TickLive` immediately after the brains loop (`for ... p.Tick(view, dt) ...`), i.e. before the `foreach (var kv in _engaged)` contact loop that Task 3 deletes.

Add to `PlayStats`: `public int contacts, sidesteps; public float maxOverlap;` and in the Setup timeout branch (`_phaseTime > SetupTimeout` when it fires) `_stats.setupTimeouts++` (add `public int setupTimeouts;`).

- [ ] **Step 4: The setup sidestep in `MoveToBrain`**

Replace `MoveToBrain.Tick`:

```csharp
    float _blockedFor;
    public void Tick(FootballPlayer self, PlayView view, float dt, ref BrainOutput o)
    {
        float dist = Vector3.Distance(self.Pos, target);
        if (_hasFace && dist < 1.0f + stopShort) o.face = _face - self.Pos;
        if (dist < stopShort) { o.move = Vector3.zero; return; }
        float far = Mathf.Clamp01((dist - 15f) / 30f);
        float pace = hurry ? 0.92f : Mathf.Lerp(0.78f, 0.92f, far);
        Vector3 move = Steer.To(self.Pos, target, 2f + stopShort) * pace;
        // A body in the way for more than a beat: step round it (bodies are
        // solid now; two men walking to spots through each other used to pass).
        BodyContact block = default; bool blocked = false;
        for (int i = 0; i < view.contacts.Count; i++)
        {
            var c = view.contacts[i];
            if (c.a != self && c.b != self) continue;
            if (Vector3.Dot(c.NormalFrom(self), move.normalized) > 0.5f) { block = c; blocked = true; break; }
        }
        _blockedFor = blocked ? _blockedFor + dt : 0f;
        if (blocked && _blockedFor > 0.3f)
        {
            Vector3 n = block.NormalFrom(self);
            Vector3 tangent = Vector3.Cross(Vector3.up, n);
            if (Vector3.Dot(tangent, target - self.Pos) < 0f) tangent = -tangent;
            move = (tangent * 0.8f + move * 0.3f).normalized * pace;
            if (_blockedFor < 0.3f + dt) self.sidesteps++;
        }
        o.move = move;
    }
```

Add `[System.NonSerialized] public int sidesteps;` to `FootballPlayer` (beside `inContact`), and in `PlayInstance`'s Setup → PreSnap transition sum them: `foreach (var p in _players) { _stats.sidesteps += p.sidesteps; p.sidesteps = 0; }`.

- [ ] **Step 5: Compile check, then the analyzer**

Run: `py -3 prototypes/shuttle-computer/test/compile-unity.py` → no errors.
Run `FootballAnalyzer` (≈90 s; poll for `analysis.txt`). Expected: zero `!! OVERLAP` flags. Some `bodies …` lines from other checks are fine. If a huddle never breaks (`setup that runs too long`), the sidestep is not firing — check `contacts` is filled during Setup (the pass must run in the Setup case too).

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/Football/IPlayerBrain.cs" "Assets/3 - Scripts/Football/PlayInstance.cs" "Assets/3 - Scripts/Football/FootballBrains.cs" "Assets/3 - Scripts/Football/FootballPlayer.cs" tools/football/FootballAnalyzer.cs.txt
git commit -m "feat(football): the contact pass runs every step (setup and live); contacts on PlayView; men step round a body in the way; analyzer asserts no overlap

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: Blocking is a shoving match

**Files:**
- Modify: `Assets/3 - Scripts/Football/PlayInstance.cs` (`BlockSlowdown` 1054–1110, its fields 1112–1118, the `foreach (var kv in _engaged)` loop ~747–755, the pocket block 689–697, `_pocketLife`/`_pocketDone`/`_engagedScale` fields 175–180 and their assignment 333–335, `TickLive`)
- Modify: `Assets/3 - Scripts/Football/FootballBrains.cs` (`OLBrain` 315, `CenterBrain` 365, `DLBrain` 563, reads at 188, 347, 353, 399)
- Modify: `Assets/3 - Scripts/Football/FootballAlienRig.cs` (line ~211)
- Modify: `Assets/3 - Scripts/Football/FootballPlayer.cs` (`Apply`, the `_rig.` block)

- [ ] **Step 1: Add the counters the soak will read (the test)**

`PlayStats` gains: `public int engagements, blocksWonRush, blocksWonHold; public float pushbackMetres, pocketLife;` (`pocketLife` = −1 when the pocket never collapsed; set −1 in both constructors: `_stats.pocketLife = -1f;`).
`GameStats` (FootballMatch line 102) gains the same plus `public int pocketSamples; public float pocketLifeSum;`. In `Absorb`: `_stats.engagements += s.engagements; _stats.blocksWonRush += s.blocksWonRush; _stats.blocksWonHold += s.blocksWonHold; _stats.pushbackMetres += s.pushbackMetres; _stats.contacts += s.contacts; _stats.sidesteps += s.sidesteps; _stats.setupTimeouts += s.setupTimeouts; _stats.maxOverlap = Mathf.Max(_stats.maxOverlap, s.maxOverlap); if (s.pocketLife >= 0f) { _stats.pocketSamples++; _stats.pocketLifeSum += s.pocketLife; }` (add `contacts, sidesteps, setupTimeouts` ints and `maxOverlap` float to `GameStats`). Append to `Summary`:

```csharp
             + " | contacts " + contacts + " engagements " + engagements + " (rush won " + blocksWonRush + ", hold won " + blocksWonHold + ") pushback " + pushbackMetres.ToString("0") + " m"
             + " | pocket avg " + (pocketSamples > 0 ? (pocketLifeSum / pocketSamples).ToString("0.0") : "-") + " s (" + pocketSamples + " collapsed)"
             + " | sidesteps " + sidesteps + " setup timeouts " + setupTimeouts + " max overlap " + maxOverlap.ToString("0.00")
```

Run the soak now: the new fields print zeros. That is the failing state.

- [ ] **Step 2: Delete the old blocking and the pocket timer**

In `PlayInstance.cs`:
- Delete fields `_pocketLife`, `_pocketDone`, `_engagedScale` (175–180) and their assignments in `BuildScrimmage` (333–335, keep `float edge` only if still used — it is not; delete it).
- Delete the pocket-clock block in `TickLive` (689–697).
- Delete the `foreach (var kv in _engaged)` nudge loop and its comment (≈745–755).
- Delete `BlockSlowdown` entirely, the `Engagement` struct, `EngageStart`, `EngageKeep`, `BlockContact`, `_engaged`, `_shed`.
- Replace the call `BlockSlowdown(carrier ?? (view.SnapInFlight ? _qb : null));` with `ResolveEngagements(carrier ?? (view.SnapInFlight ? _qb : null), dt);` and move that call to **after** `ResolveBodies()` (engagements read this tick's contacts).

- [ ] **Step 3: Write `ResolveEngagements`**

```csharp
    /// Blocking is contact (spec §2). A blocker and an opponent whose discs
    /// touch, both standing, are engaged for this tick: the rusher drives
    /// with his team's pass-rush stat, the blocker holds with blocking, and
    /// the pair moves along the rusher's line at the difference. A rusher
    /// who is past his man along that line is free by geometry. A short grace
    /// keeps a one-tick separation from flickering the engagement off.
    const float EngageGrace = 0.12f;
    readonly Dictionary<FootballPlayer, (FootballPlayer blocker, float until)> _engagedUntil = new Dictionary<FootballPlayer, (FootballPlayer, float)>();
    readonly HashSet<FootballPlayer> _wasEngaged = new HashSet<FootballPlayer>();

    void ResolveEngagements(FootballPlayer carrier, float dt)
    {
        view.ClearEngaged();
        for (int i = 0; i < _players.Count; i++)
        {
            var p = _players[i];
            p.speedScale = 1f; p.blocking = false; p.leanField = Vector3.zero; p.wantTangentOf = null;
        }
        if (carrier == null) { _engagedUntil.Clear(); return; }
        float ts = view.timeSinceSnap;
        carrier.speedScale = carrier == _catcher && ts < _catchDipUntil ? CatchDipSpeed
                           : carrier.role == FootballRole.QB ? 0.97f : CarrierSpeed;

        // Fresh contacts first: a defender touching a blocker who is between him and the ball.
        for (int i = 0; i < view.contacts.Count; i++)
        {
            var c = view.contacts[i];
            FootballPlayer d, b;
            if (c.a.team == carrier.team) { b = c.a; d = c.b; } else { b = c.b; d = c.a; }
            if (b.team == d.team || b == carrier || b.IsDown || d.IsDown || b.IsDiving || d.IsDiving) continue;
            if (d.wrapping != null) continue;
            Vector3 line = carrier.Pos - d.Pos; line.y = 0f;
            if (line.sqrMagnitude < 0.01f) continue;
            line.Normalize();
            Vector3 toB = b.Pos - d.Pos; toB.y = 0f;
            if (Vector3.Dot(toB.normalized, line) < 0.1f) continue;              // he is past him: free by geometry
            _engagedUntil[d] = (b, ts + EngageGrace);
        }
        // Engaged this tick = fresh contact, or within the grace of the last one.
        var expired = new List<FootballPlayer>();
        foreach (var kv in _engagedUntil)
        {
            var d = kv.Key; var b = kv.Value.blocker;
            if (ts > kv.Value.until || d.IsDown || b.IsDown || b == carrier || d.wrapping != null) { expired.Add(d); continue; }
            view.SetEngaged(d, b);
            if (_wasEngaged.Add(d)) _stats.engagements++;
            Vector3 line = carrier.Pos - d.Pos; line.y = 0f; line.Normalize();
            bool swim = d.brain is DLBrain dl && dl.move == DLBrain.RushMove.Swim;
            float pushStat = d.team == view.defense ? d.team.passRush : d.team.blocking;
            float push = FootballBodies.Push(pushStat, d.BodyMass, d.contactSwing);
            float hold = FootballBodies.Hold(b.team.blocking, b.BodyMass, b.contactSwing, FootballBodies.IsLineman(b));
            float drive = FootballBodies.BlockDrive(push, hold) * (swim ? FootballBodies.SwimDriveScale : 1f);
            Vector3 shift = line * (drive * dt);
            d.ShiftBody(shift); b.ShiftBody(shift);
            if (drive > 0f) _stats.pushbackMetres += drive * dt;
            // The rusher is pinned on a bull; on a swim he works the tangent at speed.
            d.speedScale = swim ? SwimSpeed : PinnedSpeed;
            d.wantTangentOf = swim ? b : null;
            b.speedScale = LateralCap(b);
            d.blocking = b.blocking = true;
            b.FaceHint(d.Pos - b.Pos); d.FaceHint(b.Pos - d.Pos);
            d.leanField = line * Mathf.Clamp01(0.5f + drive); b.leanField = -line * Mathf.Clamp01(0.5f - drive);
        }
        foreach (var d in expired)
        {
            _engagedUntil.Remove(d);
            if (_wasEngaged.Remove(d))
            {
                // Who won: he is past the blocker (or the blocker is on the ground) → the rush; otherwise nobody yet.
                if (d.IsDown) _stats.blocksWonHold++; else _stats.blocksWonRush++;
            }
        }
    }

    public const float PinnedSpeed = 0.05f;       // bull-rushing: the drive moves him, not his legs
    public const float SwimSpeed = 0.70f;         // of max, along the tangent
    /// How fast a man shuffles while engaged: linemen are slow sideways.
    public static float LateralCap(FootballPlayer b)
        => b.role == FootballRole.OL || b.role == FootballRole.DL ? 0.55f : b.role == FootballRole.C ? 0.75f : 0.85f;
```

`_wasEngaged` is per play; both constructors already create the instance fresh so no reset is needed. Roll `contactSwing` in `BuildScrimmage` and `BuildKickoff` (after the brains are assigned): `foreach (var p in _players) p.contactSwing = 0.8f + 0.4f * (float)_rng.NextDouble();`.

- [ ] **Step 4: The geometric pocket**

In `TickLive`, after `ResolveEngagements(...)`:

```csharp
        if (!view.isKickoff && !view.pocketCollapsed && _qb != null && view.Carrier == _qb && view.Downfield(_qb.Pos) < 0f && PocketCollapsed())
        {
            view.pocketCollapsed = true; _stats.pocketLife = view.timeSinceSnap;
            log?.Invoke("Pressure — the pocket collapses on " + _qb.team.shortName + " QB");
        }
```

and the predicate:

```csharp
    /// A rusher with a clear line inside 3.5 m, or a blocker driven back to within 1.5 m of the QB.
    bool PocketCollapsed()
    {
        for (int i = 0; i < _players.Count; i++)
        {
            var d = _players[i];
            if (d.team == _qb.team || d.IsDown) continue;
            float dist = Vector3.Distance(d.Pos, _qb.Pos);
            if (dist < 3.5f && !view.IsEngaged(d) && ClearLine(d.Pos, _qb.Pos)) return true;
        }
        for (int i = 0; i < _players.Count; i++)
        {
            var b = _players[i];
            if (b.team != _qb.team || b == _qb || !b.blocking) continue;
            if (Vector3.Distance(b.Pos, _qb.Pos) < 1.5f) return true;
        }
        return false;
    }

    /// No standing offensive body (other than the QB) within 1.2 m of the segment.
    bool ClearLine(Vector3 from, Vector3 to)
    {
        Vector3 seg = to - from; seg.y = 0f; float len = seg.magnitude; if (len < 0.01f) return true;
        Vector3 dir = seg / len;
        for (int i = 0; i < _players.Count; i++)
        {
            var o = _players[i];
            if (o.team != _qb.team || o == _qb || o.IsDown) continue;
            Vector3 rel = o.Pos - from; rel.y = 0f;
            float t = Mathf.Clamp(Vector3.Dot(rel, dir), 0f, len);
            if ((rel - dir * t).magnitude < 1.2f) return false;
        }
        return true;
    }
```

`view.pocketCollapsed` must be reset to false per play: it is a field on a fresh `PlayView` per `PlayInstance`, so it is.

- [ ] **Step 5: The pass-set (`OLBrain`, `CenterBrain`)**

Replace the "Stay on your man" block in `OLBrain.Tick` (from `if (carrier != null && _man != null && Vector3.Distance(_man.Pos, self.Pos) < 7f)` to its `return;`) with:

```csharp
        if (carrier != null && _man != null && Vector3.Distance(_man.Pos, self.Pos) < 7f)
        {
            // The pass-set: mirror him. My spot is on his line to the ball, one
            // body in front of him. Engaged, PlayInstance caps my shuffle
            // (LateralCap) — a lineman is slow sideways, and that is what a
            // swim beats. The kick-slide point shapes the cup for the first half second.
            Vector3 line = carrier.Pos - _man.Pos; line.y = 0f;
            if (line.sqrMagnitude < 0.01f) { o.move = Vector3.zero; return; }
            line.Normalize();
            Vector3 spot = _man.Pos + line * (self.BodyRadius + _man.BodyRadius + 0.05f);
            bool qbInPocket = carrier.role == FootballRole.QB && view.Downfield(carrier.Pos) < 0f;
            if (qbInPocket && view.timeSinceSnap < 0.5f && view.EngagedWith(_man) == null) spot = Vector3.Lerp(spot, _pocketSpot, 0.65f);
            // Past me toward the ball by half a metre: turn and chase like anyone else.
            if (Vector3.Dot(self.Pos - _man.Pos, line) > 0.5f) { o.move = Steer.Pursue(self, _man); return; }
            o.move = Steer.To(self.Pos, spot, 0.5f);
            o.face = _man.Pos - self.Pos; o.faceMoving = true;
            return;
        }
```

(`o.faceMoving`/`o.face` exist — the backpedal uses them; check `BrainOutput` in `IPlayerBrain.cs` for the exact names and use those.)

In `CenterBrain.Tick`, replace `if (d.speedScale < 0.5f) continue;   // someone has him` with `if (view.IsEngaged(d)) continue;`. Replace the final `Steer.BlockMan(self, threat, carrier)` return with the same mirror logic against `threat`:

```csharp
        if (threat != null)
        {
            Vector3 line = carrier.Pos - threat.Pos; line.y = 0f;
            if (line.sqrMagnitude > 0.01f)
            {
                line.Normalize();
                o.move = Steer.To(self.Pos, threat.Pos + line * (self.BodyRadius + threat.BodyRadius + 0.05f), 0.5f);
                o.face = threat.Pos - self.Pos; o.faceMoving = true;
                return;
            }
        }
```

Update the three other reads: `FootballBrains.cs:188` → `if (view.IsEngaged(q)) threat *= 0.8f;`; `:347` → `Vector3 spot = view.IsEngaged(_man) ? ...` (this block is replaced by Step 5 above, so it disappears); `:353` (same block, disappears). Grep `speedScale < 0.5f` afterwards: the only remaining hit must be none in `FootballBrains.cs`.

- [ ] **Step 6: The rush move (`DLBrain`)**

```csharp
public class DLBrain : IPlayerBrain
{
    public enum RushMove { Bull, Swim }
    public readonly RushMove move;
    readonly Vector3 _edge;
    bool _pastEdge;
    /// `edgeSpot`: the point outside the tackle he bends round before turning
    /// up at the QB. The move is rolled per snap: quick men swim, strong men bull.
    public DLBrain(Vector3 edgeSpot, FootballTeam team, System.Random rng)
    {
        _edge = edgeSpot;
        float swimChance = Mathf.Clamp(0.35f + 0.5f * (team.speed - team.passRush), 0.1f, 0.8f);
        move = rng.NextDouble() < swimChance ? RushMove.Swim : RushMove.Bull;
    }
    public bool KeepsControlWhenCarrying => false;

    Vector3 Rush(FootballPlayer self, PlayView view, FootballPlayer qb)
    {
        var blocker = view.EngagedWith(self);
        if (blocker != null && qb != null)
        {
            if (move == RushMove.Bull) return Steer.To(self.Pos, qb.Pos, 0f);          // pinned: the drive moves him
            // Swim: work the tangent on the side with the shorter line to the QB.
            Vector3 n = blocker.Pos - self.Pos; n.y = 0f; n.Normalize();
            Vector3 t = Vector3.Cross(Vector3.up, n);
            if (Vector3.Dot(t, qb.Pos - self.Pos) < 0f) t = -t;
            return t;
        }
        if (!_pastEdge && qb != null && view.Downfield(qb.Pos) < 0f)
        {
            if (Vector3.Distance(self.Pos, _edge) < 1.0f || view.timeSinceSnap > 1.7f) _pastEdge = true;
            else return Steer.To(self.Pos, _edge, 0f);
        }
        return qb != null ? Steer.Pursue(self, qb) : Vector3.zero;
    }
    // Tick unchanged
}
```

Delete `public bool free;`. In `BuildScrimmage`: `_live[dl[i]] = new DLBrain(F(i == 0 ? -4.3f : 4.3f, -1.8f), def, _rng);`. Grep `\.free` across the folder: no hits left.

- [ ] **Step 7: The lean**

`FootballAlienRig.cs`: add `[System.NonSerialized] public Vector3 lean;   // world: a shove, the spine tips into it` beside `blocking`. At line ~211 before the stumble line: `if (lean.sqrMagnitude > 1e-4f) spineDir = (spineDir + lean * 0.35f).normalized;`.
`FootballPlayer.Apply`, in the `_rig.` block: `_rig.lean = _fieldRoot != null ? _fieldRoot.TransformDirection(leanField) : leanField;`.

- [ ] **Step 8: Compile check, then the soak**

Run the compile check → no errors. Run `FootballSoak` (3 games). Read the three `Summary` lines and the play-by-play. Targets:

| Counter | Target |
|---|---|
| sacks / game | 3–7 |
| pocket avg | 2.5–4.0 s |
| engagements / game | 150–300 (two linemen + the centre engage most snaps) |
| rush won : hold won | roughly 1 : 2 |
| max overlap | < 0.20 (a step's worth before the pass corrects) |
| setup timeouts | 0 |
| INT / game | 3–6 (unchanged from pass 4) |

Knobs, in order: `DriveGain` (pushback too fast/slow), `SwimSpeed` vs `LateralCap` (edge too easy/hard), `swimChance`, `PocketCollapsed` distances. Tune, re-run, once. If sacks exceed 10, the QB brain's `inTheFace` read (3.6 m closing) is now firing constantly because rushers are pinned closer; raise `PinnedSpeed` to 0.15 before touching the QB.

- [ ] **Step 9: Commit**

```bash
git add "Assets/3 - Scripts/Football/PlayInstance.cs" "Assets/3 - Scripts/Football/FootballBrains.cs" "Assets/3 - Scripts/Football/FootballAlienRig.cs" "Assets/3 - Scripts/Football/FootballPlayer.cs" "Assets/3 - Scripts/Football/FootballMatch.cs"
git commit -m "feat(football): blocking is a shoving match — engagements from contact, pass-rush vs blocking drive the pair, linemen mirror at a lateral cap, rushers bull or swim, the pocket collapses by geometry (timer and shed list gone), spine leans into the shove

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: Tackling is contact

**Files:**
- Modify: `Assets/3 - Scripts/Football/PlayInstance.cs` (`TickTackles` 824–938, `EndTackle` 940, `TickWrap` 950, constants 91–103)

- [ ] **Step 1: Counters (the test)**

`PlayStats` gains `public int wraps, armTackles, hitSquare, hitAngled, hitGlancing;` (histogram of `square`: ≥ 0.75 / 0.35–0.75 / < 0.35). `GameStats` the same; `Absorb` sums them; `Summary` appends ` | wraps W arm-tackles A (square S angled N glancing G)`. Soak now: zeros.

- [ ] **Step 2: Rewrite `TickTackles`**

Delete constants `TackleRadius`, `MissedTackleChance`, `MissedTackleBlocked`, `MissedTackleBehind`, `MissedTackleJuked`, `MissedTackleSpun`, `MaxMissChance`. Keep `DiveChance`, `DiveMinDist`, `DiveMaxDist`, `DiveReach` (hurdle-vs-dive only), `TackleRetry`, the down-seconds and fumble constants. Add fields `float _wrapHit; float _wrapCarrierSpeed; bool _breakRolled;`.

```csharp
    /// Tackles are contact (spec §3): a defender's disc on the carrier's, or a
    /// dive landing on it, starts one. Hit quality from closing speed and how
    /// square the hit is decides wrap (the drag, the pile, the break) vs an arm
    /// tackle (he stumbles through it, the defender falls past). A juke or spin
    /// produces the miss because the BODY moved and the contact went glancing.
    /// Dives from range are unchanged. Returns true if the play ended.
    bool TickTackles(FootballPlayer carrier)
    {
        float ts = view.timeSinceSnap;
        Vector3 carrierDir = carrier.Vel.sqrMagnitude > 0.25f ? carrier.Vel.normalized : Vector3.zero;
        for (int i = 0; i < _players.Count; i++)
        {
            var d = _players[i];
            if (d.team == carrier.team || d.IsDown) continue;
            float dist = Vector3.Distance(d.Pos, carrier.Pos);
            Vector3 toD = d.Pos - carrier.Pos; toD.y = 0f;

            // A hurdle over a diver: the discs never touch (lifted), so this is
            // the one timing test that stays — a clip on a bad leap.
            if (d.IsDiving && carrier.IsHurdling && !_diveResolved.Contains(d))
            {
                float ph = d.DivePhase;
                if (ph < 0.3f || ph > 0.85f || dist >= DiveReach) continue;
                _diveResolved.Add(d);
                float hp = carrier.HurdlePhase;
                float clip = hp < 0.12f || hp > 0.9f ? HurdleLateClipChance : HurdleClipChance;
                if (_rng.NextDouble() >= clip) { log?.Invoke(carrier.team.shortName + " " + carrier.Label + " HURDLES " + d.Label + "!"); continue; }
                _stats.hurdlesClipped++; _stats.diveHits++;
                d.FallDown(MissDownSeconds);
                carrier.HardFall(HardFallDownSeconds);
                if (_rng.NextDouble() < ClipFumbleChance)
                {
                    log?.Invoke(carrier.team.shortName + " " + carrier.Label + " is clipped mid-hurdle — goes down hard and the BALL IS LOOSE!");
                    Fumble(carrier, d);
                    return false;
                }
                log?.Invoke(carrier.team.shortName + " " + carrier.Label + " is clipped mid-hurdle and goes down hard");
                EndTackle(carrier, d, 99f);
                return true;
            }

            if (!view.ContactBetween(d, carrier, out var c))
            {
                // No contact. From range, a free man closing at speed may dive (unchanged).
                if (d.IsDiving || view.IsEngaged(d) || dist < DiveMinDist || dist > DiveMaxDist) continue;
                if (_diveConsider.TryGetValue(d, out float next) && ts < next) continue;
                _diveConsider[d] = ts + 0.22f;
                float closingGap = Vector3.Dot(carrier.Vel - d.Vel, toD.normalized);
                if (closingGap < 2.5f) continue;
                if (_rng.NextDouble() >= DiveChance) continue;
                Vector3 aim = (carrier.Pos + carrier.Vel * 0.25f) - d.Pos; aim.y = 0f;
                d.StartDive(aim);
                _tackleRetry[d] = ts + 1.6f;
                _stats.dives++;
                continue;
            }

            // Contact. A diver connects only in his window; a blocked man only gets an arm out.
            if (d.IsDiving)
            {
                if (_diveResolved.Contains(d)) continue;
                float ph = d.DivePhase;
                if (ph < 0.3f || ph > 0.85f) continue;
                _diveResolved.Add(d);
                _stats.diveHits++;
            }
            Vector3 nFromD = c.NormalFrom(d);                                       // d → carrier
            float square = carrierDir == Vector3.zero ? 1f : Mathf.Abs(Vector3.Dot(nFromD, carrierDir));
            bool stiff = carrier.IsStiffArming && Vector3.Dot(toD.normalized, carrier.StiffArmDir) > 0.5f;
            if (stiff) { square = 0f; d.ShiftBody(toD.normalized * 0.5f); }
            float hit = FootballBodies.TackleHit(Mathf.Max(0f, c.closing), square, d.BodyMass) * (0.85f + 0.3f * (float)_rng.NextDouble());
            if (view.IsEngaged(d)) hit = Mathf.Min(hit, FootballBodies.WrapThreshold - 0.01f);
            if (square >= 0.75f) _stats.hitSquare++; else if (square >= 0.35f) _stats.hitAngled++; else _stats.hitGlancing++;

            if (hit < FootballBodies.WrapThreshold)
            {
                // The arm tackle: he runs through it, the defender falls past.
                _stats.armTackles++;
                d.FallDown(MissDownSeconds);
                _tackleRetry[d] = ts + TackleRetry;
                if (!stiff && _rng.NextDouble() < 0.6) { carrier.StartStumble(); _stats.stumbles++; }
                if (stiff) _stats.stiffArms++;
                string how = stiff ? " stiff-arms " : carrier.IsSpinning ? " spins out of " : carrier.IsJuking ? " jukes past " : " runs through the arm tackle of ";
                log?.Invoke(carrier.team.shortName + " " + carrier.Label + how + d.Label + "!");
                continue;
            }
            // A big hit can jar it loose (scaled by how hard it was).
            if (_rng.NextDouble() < BigHitFumbleChance * Mathf.Clamp(hit / 4f, 0.5f, 2.5f))
            {
                d.FallDown(TackleDownSeconds); carrier.FallDown(TackleDownSeconds);
                log?.Invoke(d.team.shortName + " " + d.Label + " lays him out — the ball comes loose!");
                Fumble(carrier, d);
                return false;
            }
            _stats.wraps++;
            EndTackle(carrier, d, hit);
            return true;
        }
        return false;
    }
```

Check the old code for how `stiffArms` was counted (grep `_stats.stiffArms`) and keep it counted once per stiff-arm, not per contact; if it is already counted in `DoAction`/`StartStiffArm`, drop the `if (stiff) _stats.stiffArms++;` line here.

- [ ] **Step 3: `EndTackle` takes the hit; the break is momentum**

```csharp
    void EndTackle(FootballPlayer carrier, FootballPlayer d, float hit)
    {
        _pendingTackler = d;
        _wrappers.Clear(); _wrappers.Add(d);
        d.wrapping = carrier;
        _tackleEndAt = view.timeSinceSnap + WrapSeconds;
        _wrapHit = hit; _wrapCarrierSpeed = carrier.Vel.magnitude; _breakRolled = false;
    }
```

Update the other `EndTackle(carrier, d)` call sites (the clip path passes `99f` above; grep for any others and pass `99f` — a hit that cannot be broken).

In `TickWrap`: pile-on by contact — replace `if (Vector3.Distance(d.Pos, carrier.Pos) < 1.4f)` with `if (view.ContactBetween(d, carrier, out _))`. Replace the break block:

```csharp
        // Breaking it: one man on him, judged once at the midpoint — the
        // runner's momentum against the hit that wrapped him.
        if (_wrappers.Count == 1 && !_breakRolled && view.timeSinceSnap > _tackleEndAt - WrapSeconds * 0.5f)
        {
            _breakRolled = true;
            float momentum = carrier.BodyMass * _wrapCarrierSpeed * (0.7f + 0.6f * carrier.team.speed);
            if (momentum > _wrapHit * (0.85f + 0.3f * (float)_rng.NextDouble()) * BreakScale)
            {
                var d = _wrappers[0];
                d.wrapping = null; d.FallDown(MissDownSeconds);
                _tackleRetry[d] = view.timeSinceSnap + TackleRetry;
                _wrappers.Clear(); _pendingTackler = null; _tackleEndAt = -1f;
                _stats.brokenTackles++;
                log?.Invoke(carrier.team.shortName + " " + carrier.Label + " BREAKS THE TACKLE of " + d.Label + "!");
            }
        }
```

Add `public const float BreakScale = 2.6f;` (a runner at 7 m/s, speed 0.5, mass 1 → momentum 7; a wrap of hit 2.5 × 2.6 = 6.5 → breaks about half the time; a hit of 4 needs a 10 momentum → rarely). Delete `BreakTackleChance`.

- [ ] **Step 4: Compile check, then the soak**

Targets per game: wraps 40–70, arm tackles 8–20, broken 2–6, fumbles 1–4, sacks 3–7, and `square`/`angled`/`glancing` all non-zero. Knobs: `WrapThreshold`, `TackleBase`, `BreakScale`. If arm tackles are near zero, the carrier moves (juke) are not producing glancing contacts because `carrierDir` uses the velocity after the juke sidestep ends; widen the glancing band by using `square = Mathf.Pow(square, 1.5f)`. Tune once.

Also check the log: every play still ends (`LiveTimeout` whistles must stay rare, ≤ 1 per game). A carrier standing still behind a pile with a defender in contact but `closing` 0 and `square` 1 gives `hit = 1.5 × 1 × mass ≥ 1.5`: below the threshold for a 1.0-mass man, so a standing runner could survive touches. If plays time out, raise `TackleBase` to 2.0.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/Football/PlayInstance.cs" "Assets/3 - Scripts/Football/FootballMatch.cs"
git commit -m "feat(football): tackling is contact — a hit starts on disc contact, closing speed and angle decide wrap vs arm tackle, a juke misses because the body moved, pile-on by touch, the break is momentum against the hit; tackle radius and miss dice gone

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: The human — capsules on the aliens, shoves on the player

**Files:**
- Modify: `Assets/3 - Scripts/Football/FootballPlayer.cs` (`BuildAlien`, `BuildCapsule`, `SetHumanDriven`)
- Modify: `Assets/3 - Scripts/Football/FootballHumanQB.cs` (`SyncSlot` line 328)
- Modify: `Assets/3 - Scripts/Football/FootballBroadcast.cs` (lines 733, 783)

- [ ] **Step 1: The collider**

At the end of `Init` in `FootballPlayer` (after the body is built, before the label):

```csharp
        // Solid to the real player: a kinematic capsule on the slot root (the
        // sim never reads it). Layer 10 "Body" is in PlayerController.walkableMask.
        if (Application.isPlaying)
        {
            var cap = gameObject.AddComponent<CapsuleCollider>();
            cap.center = Vector3.up * (Height * 0.5f); cap.height = Height; cap.radius = BodyRadius;
            var kin = gameObject.AddComponent<Rigidbody>();
            kin.isKinematic = true; kin.useGravity = false;
            gameObject.layer = 10;
        }
```

`Application.isPlaying` keeps the edit-mode soak free of physics components. In `SetHumanDriven(bool on, ...)` disable the collider while the slot is the human (`var cap = GetComponent<CapsuleCollider>(); if (cap != null) cap.enabled = !on;`), and while `Benched`.

Check the interact ray: grep `walkableMask\|interactMask\|LayerMask` in `PlayerPickup.cs`/`InteractPromptUI`'s raycaster; if the look-to-interact ray uses a mask that includes layer 10, an alien standing between you and the red button blocks the prompt, which is acceptable. If it hits **every** layer and reports the alien as "nothing", also acceptable.

- [ ] **Step 2: The shove**

In `FootballHumanQB.SyncSlot`, after `_slot.SyncHuman(...)`:

```csharp
        // The contact pass shoved the slot: move the real body the same way
        // (rb.position + SyncTransforms — transform.position is overwritten by
        // the controller; see TutorialDirector's teleport).
        Vector3 shove = _slot.TakePendingShove();
        if (shove.sqrMagnitude > 1e-8f)
        {
            var rb = _player.Rigidbody;
            if (rb != null) { rb.position += _fieldRoot.TransformDirection(shove) * shoveGain; Physics.SyncTransforms(); }
        }
```

Add `public float shoveGain = 1f;` at the END of `FootballHumanQB`'s serialized fields.

- [ ] **Step 3: Replay ghosts carry no physics**

`FootballBroadcast.cs` line 733 and 783: beside each `foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);` add `foreach (var r in go.GetComponentsInChildren<Rigidbody>(true)) Destroy(r);` (a `Rigidbody` must be destroyed after the colliders that depend on it: keep the order colliders first).

- [ ] **Step 4: Compile check + soak (still runs, physics components skipped in edit mode)**

Expected: compile clean; soak numbers unchanged from Task 4.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/Football/FootballPlayer.cs" "Assets/3 - Scripts/Football/FootballHumanQB.cs" "Assets/3 - Scripts/Football/FootballBroadcast.cs"
git commit -m "feat(football): the player can't walk through aliens (kinematic capsules on the slots) and gets shoved when the contact pass moves his slot; replay ghosts strip rigidbodies

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: F8 contact-disc toggle

**Files:**
- Modify: `Assets/3 - Scripts/Football/FootballMatch.cs` (`OnGUI` ~line 895; serialized field at the END)
- Modify: `Assets/3 - Scripts/Football/FootballPlayer.cs` (`SetRingVisible`, ring update in `Apply`)

- [ ] **Step 1: The ring**

In `FootballPlayer`:

```csharp
    LineRenderer _ring;
    /// Debug (F8 "contact discs"): a ring at the disc's edge, red while touching.
    public void SetRingVisible(bool on)
    {
        if (on && _ring == null)
        {
            var go = new GameObject("ContactRing"); go.transform.SetParent(transform, false);
            go.layer = gameObject.layer;
            _ring = go.AddComponent<LineRenderer>();
            _ring.useWorldSpace = false; _ring.loop = true; _ring.positionCount = 24;
            _ring.startWidth = _ring.endWidth = 0.04f;
            _ring.material = new Material(FootballShader.UnlitColor);
            _ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        if (_ring != null) _ring.gameObject.SetActive(on);
    }
    void UpdateRing()
    {
        if (_ring == null || !_ring.gameObject.activeSelf) return;
        float r = IsDown ? FootballBodies.DownRadius : BodyRadius;
        for (int i = 0; i < 24; i++) { float a = i / 24f * Mathf.PI * 2f; _ring.SetPosition(i, new Vector3(Mathf.Cos(a) * r, 0.03f, Mathf.Sin(a) * r)); }
        _ring.startColor = _ring.endColor = inContact ? Color.red : (blocking ? Color.yellow : Color.white);
    }
```

Call `UpdateRing();` at the top of `Apply(float dt)`. `FootballShader.UnlitColor` — check the helper's member name (`grep -n "public static" FootballShader.cs`) and use the one that returns the null-safe Unlit/Color; if the ring's local rotation should not follow the body's facing, that is fine (a circle).

- [ ] **Step 2: The toggle**

`FootballMatch`: append `public bool showContactDiscs;` at the END of the serialized fields. In `OnGUI` after the "End quarter" button:

```csharp
        y += 28;
        bool discs = GUI.Toggle(new Rect(x + 8, y, w - 16, 20), showContactDiscs, "Contact discs (red = touching)");
        if (discs != showContactDiscs) { showContactDiscs = discs; foreach (var p in _players) p.SetRingVisible(discs); }
```

and grow the panel box height by 28 (`210 + rows * 20` → `238 + rows * 20`).

- [ ] **Step 3: Compile check, commit**

```bash
git add "Assets/3 - Scripts/Football/FootballMatch.cs" "Assets/3 - Scripts/Football/FootballPlayer.cs"
git commit -m "feat(football): F8 'contact discs' — a ring per man at his disc edge, red while touching, yellow while blocking

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: Full verification, docs, memory

**Files:**
- Modify: `docs/FOOTBALL.md`, `tools/football/README.md`
- Modify: `C:\Users\Sammc\.claude\projects\C--2-0-SOUND-OF-SPACE-1aughhh1\memory\alien-football.md` + `MEMORY.md` line

- [ ] **Step 1: The full run**

1. Compile check.
2. `ContactDrill` → all PASS, tables sane.
3. `FootballSoak` (3 games) → every target in Tasks 3 and 4 inside range; **no `!! EXCEPTION`**, every game reaches `GameOver`, `setup timeouts 0`, `OFFICIALS SPOTTED` absent.
4. `FootballAnalyzer` → zero `!! OVERLAP`, no new `frozen` flags (a pinned bull-rusher moves at 0.05× — the analyzer's frozen check must treat `view.IsEngaged(p)` or `p.blocking` as not-frozen; patch the check if it fires on engaged linemen).
5. Paste the three `Summary` lines into the FOOTBALL.md section below.

- [ ] **Step 2: `docs/FOOTBALL.md`**

Rewrite the **Blocking** and **Tackles and dives** paragraphs under "Rules of the sim" to describe contact (discs, push vs hold, lateral cap, bull/swim, geometric pocket; hit = closing × square, wrap vs arm tackle, momentum break). Add a section:

```markdown
## Pass 5 (2026-09-20) — solid bodies

Sam after the Phase 2 playtest: the aliens weren't solid, blocking was dice.
Spec: `superpowers/specs/2026-09-20-football-solid-bodies-design.md`.

- **`FootballBodies.cs`**: every man is a disc (linemen 0.50 m / 1.25 mass,
  LB/S 0.45 / 1.10, skill 0.42 / 1.00, a downed man 0.55 and immovable).
  Once per step, after the brains and before the tackles, overlapping pairs
  separate (heavier moves less), closing momentum is traded (no bounce) and
  a `BodyContact` list lands on `PlayView.contacts`. Solid in every phase;
  `MoveToBrain` steps round a body in the way after 0.3 s.
- **Blocking** = contact (`ResolveEngagements`): push (passRush) vs hold
  (blocking), the pair drives at the difference (±1.2 m/s), a 0.8–1.2 swing
  per man per snap. Linemen mirror at a lateral cap (0.55×; centre 0.75×);
  rushers bull (pinned, the drive moves them) or swim (0.7× on the tangent —
  beats a tackle, not the centre). The pocket collapses by geometry (a clear
  line inside 3.5 m, or a blocker driven to 1.5 m). No timers, no shed list.
- **Tackles** = contact: `hit = (1.5 + closing) × (0.35 + 0.65 square) × mass`;
  ≥ 2.0 wraps (drag, pile-on by touch, a momentum break at the midpoint),
  below is an arm tackle (stumble through, defender falls past). A juke
  misses because the body moved and the contact went glancing. Dives from
  range unchanged; a hurdle lifts the disc over a diver.
- **The human**: the slot is a disc; shoves reach the rigidbody
  (`FootballHumanQB.shoveGain`); aliens carry kinematic capsules (layer 10).
- **F8 → Contact discs** draws the rings (red touching, yellow blocking).
- `tools/football/ContactDrill.cs.txt` tables the two formulas.

Soak (3 games): <paste the Summary lines>
```

Update the "Ideas not built yet" list: remove "stiff-arm" (built earlier) and add "stage 4: batted balls at the line, loose ball off legs; downed bodies stepped over".

`tools/football/README.md`: add the `ContactDrill.cs.txt` bullet.

- [ ] **Step 3: Memory**

Update `alien-football.md` (Sam's memory dir): pass 5 built, playtest pending, the knobs (`DriveGain`, `SwimSpeed`/`LateralCap`, `WrapThreshold`, `TackleBase`, `BreakScale`, `shoveGain`), and the trap "engaged rusher at 0.05× looks frozen to the analyzer". Update the `MEMORY.md` line.

- [ ] **Step 4: Commit**

```bash
git add docs/FOOTBALL.md tools/football/README.md
git commit -m "docs(football): pass 5 — solid bodies, contact blocking and tackling; soak numbers

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

Report to Sam: what changed for the game, what to look for in the playtest (walk into an alien; watch a lineman get driven back or swum; watch a juke turn a tackle into an arm tackle; F8 discs), and that Stage 4 (batted balls) waits on his verdict.

---

## Self-review

**Spec coverage:** §1 discs, masses, separation, momentum, contacts, solid in every phase, sidestep, hurdle lift, human shove + capsules, F8 rings → Tasks 1, 2, 5, 6. §2 engagement = contact, push/hold/swing, drive, pass-set with lateral cap, bull/swim, geometric pocket, receivers same rules, jams kept → Task 3. §3 contact tackles, hit formula, wrap/arm, blocked = arm only, dives via contact window, hurdle clip kept, fumble scaled, lean, human same path → Tasks 3 (lean) and 4. Verification (drill, soak counters, analyzer assertion, pass-4 ranges) → Tasks 1, 2, 3, 4, 7. Docs → Task 7. Stage 4 is explicitly out.

**Placeholders:** none; every code step has code. Two lookups are delegated to the implementer with the exact grep (`BrainOutput.face/faceMoving` names, `FootballShader` member name, `Bench()`), each with the fallback stated.

**Type consistency:** `BodyContact.NormalFrom/Other`, `FootballBodies.Resolve/Push/Hold/BlockDrive/TackleHit/RadiusFor/MassFor/IsLineman/DownRadius/WrapThreshold/MaxDrive/SwimDriveScale`, `FootballPlayer.BodyRadius/BodyMass/BodyLifted/contactSwing/leanField/inContact/wantTangentOf/sidesteps/Benched/ShiftBody/SetVelAlong/TakePendingShove/SetVelForDrill/SetRingVisible`, `PlayView.contacts/EngagedWith/IsEngaged/ClearEngaged/SetEngaged/ContactBetween`, `PlayInstance.ResolveBodies/ResolveEngagements/PocketCollapsed/ClearLine/LateralCap/PinnedSpeed/SwimSpeed/BreakScale`, `DLBrain.move/RushMove`, `EndTackle(carrier, d, hit)` — used with the same names throughout.
