# AI Brain + Profile 01 Merge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Merge the new `docs/AI_Brain_and_Profile_01.md` design into the working `feature/phone-ai-revamp` branch. Rewrites the Phase 1 persona from cold/clinical "Astronaut" voice to warm/bright/proprietary "Captain"-style voice (using the player's real name), adds first-time AI naming UX, replaces the knowledge-base content with the doc's atomic facts, and embeds the two canonical ORG/mission blurbs as Phase 1 Core entries.

**Architecture:**
1. **`NameStore`** static class holds `PlayerName` + `AIName` + `FirstContactComplete` — mirrors `EarlyGameProgress` pattern (static fields, saved via `SaveCollector`).
2. **`TokenResolver`** gains `{PLAYER_NAME}` and `{AI_NAME}` tokens that resolve to those fields (with "Player" / "Assistant" defaults if unset/declined).
3. **`AIChatScreen`** gains a small state machine that runs on first open when `FirstContactComplete == false`: ask for player's name → capture → ask for AI's name (or skip) → capture → set flag. Scripted exchange, no LLM. After that, normal chat resumes.
4. **AI message prefix** — every AI bubble in chat renders with `{AI_NAME}: ` prefix. Done in the bubble-creation site to capture both LLM-streamed responses AND IntentRouter / scripted-message paths.
5. **Persona rewrite** — `game_knowledge.md` Phase 1 PERSONA block is replaced with the warm/proprietary Trusting voice from doc Part C.2 (rephrased to use `{PLAYER_NAME}` instead of "Captain").
6. **Knowledge content rewrite** — `game_knowledge.md` ENTRY blocks are replaced with the atomic facts from doc Part A.2, plus the two canonical ORG / Mission blurbs as `mode: core` entries (always-injected pre-reveal).
7. **Address-term sweep** — every "Astronaut" / "Astronaut Number {ASTRONAUT_NUMBER}" reference in `LLMService.cs` (system prompt, examples, telemetry block) and `IntentRouter.cs` (handler reply strings) is changed to `{PLAYER_NAME}` or removed.

**Tech Stack:** Unity 2022.3, LLMUnity v2.0.5, Qwen-2.5-3B-Instruct Q4_K_M on GPU (current default), C# Assembly-CSharp (no asmdefs).

**Key files:**
- `Assets/3 - Scripts/AI/NameStore.cs` — new, static name + first-contact-complete fields
- `Assets/3 - Scripts/AI/TokenResolver.cs` — add two cases
- `Assets/3 - Scripts/AI/AIChatScreen.cs` — first-contact state machine + prefix on bubbles
- `Assets/3 - Scripts/AI/LLMService.cs` — address-term sweep in system prompt strings
- `Assets/3 - Scripts/AI/IntentRouter.cs` — address-term in reply strings (currently neutral, just sanity-check)
- `Assets/3 - Scripts/SaveSystem/SaveData.cs` — add 3 fields to a new `NameStoreSave` (or onto existing save struct)
- `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` — capture + apply
- `Assets/StreamingAssets/AI/game_knowledge.md` — persona rewrite + entry rewrite + 2 new ORG entries
- `Assets/StreamingAssets/AI/game_knowledge_org_reveal.md` — verify NOT modified

---

## File Structure

**Modify:**
- `Assets/3 - Scripts/AI/TokenResolver.cs` — add `{AI_NAME}` + `{PLAYER_NAME}` cases
- `Assets/3 - Scripts/AI/LLMService.cs` — replace every literal "Astronaut" occurrence in the system prompt + `BuildLiveTelemetry` with `{PLAYER_NAME}` / "the player" as appropriate
- `Assets/3 - Scripts/AI/IntentRouter.cs` — sanity-check current reply strings, no "Astronaut" references presently (verify)
- `Assets/3 - Scripts/AI/AIChatScreen.cs` — add `_firstContactState` field + state-machine in `OnSendClicked` + change cold-opener to be skipped during first contact + prefix rendering in `AddAIBubble` and notification bubbles
- `Assets/3 - Scripts/SaveSystem/SaveData.cs` — add `NameStoreSave` class + reference in `SaveData`
- `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` — `CaptureNameStore` + `ApplyNameStore`
- `Assets/StreamingAssets/AI/game_knowledge.md` — full rewrite (persona + ALL entries)

**Create:**
- `Assets/3 - Scripts/AI/NameStore.cs` — static fields + helper accessors

**Do NOT modify:**
- `Assets/StreamingAssets/AI/game_knowledge_org_reveal.md` (gated content from prior slice — out of scope)
- `Assets/3 - Scripts/AI/AIStoryController.cs` (existing gating wiring — works as-is)
- `Assets/3 - Scripts/AI/GameKnowledgeBase.cs` (parser handles new content unchanged)
- `Assets/3 - Scripts/Tutorial/EarlyGameProgress.cs` (orthogonal)
- Anything in `Library/PackageCache/ai.undream.llm@2c30b44020/` (LLMUnity package, read-only)

---

## Phase 1 — Foundation (save fields + tokens)

### Task 1: Add `NameStore` static class + save round-trip

**Why:** Three pieces of persistent state are needed: the player's chosen name, the AI's chosen name, and whether first-contact completed (so we don't re-prompt on every save load). The project's established pattern for "static flag + save round-trip" is `EarlyGameProgress` — mirror it exactly: a static class with public fields, a matching `Serializable` save struct, and capture/apply lines in `SaveCollector`.

The defaults are deliberate: empty strings (not "Player" / "Assistant") in the raw fields, so we can tell "never set" from "intentionally set to short string". The resolved accessors apply the defaults.

**Files:**
- Create: `Assets/3 - Scripts/AI/NameStore.cs`
- Modify: `Assets/3 - Scripts/SaveSystem/SaveData.cs` (add `NameStoreSave` class + field on `SaveData`)
- Modify: `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` (capture + apply)

- [ ] **Step 1: Create `NameStore.cs`**

Create `Assets/3 - Scripts/AI/NameStore.cs` with:

```csharp
// Player's chosen name, AI's chosen name, and the first-contact-complete flag.
//
// Mirrors the EarlyGameProgress pattern: static fields, no MonoBehaviour, no
// scene dependency. SaveCollector reads and writes these via NameStoreSave.
// The resolved accessors apply sensible defaults ("Player" / "Assistant") so
// downstream code never has to null-check.
public static class NameStore
{
    // Raw fields — empty string means "never set" (different from default).
    public static string PlayerName = "";
    public static string AIName     = "";

    // Has the AI's first-contact / naming UX completed for this save? If
    // false, AIChatScreen runs the scripted state machine on next open.
    public static bool FirstContactComplete = false;

    // ── Resolved accessors ──────────────────────────────────────────
    // Use these in code paths that need a non-empty string (display, token
    // resolver, system prompt). The fields above keep empty-string semantics
    // for save migration: an old save missing these fields loads as empty
    // strings → resolved as defaults → first-contact reruns to fix.

    public static string ResolvedPlayerName
        => string.IsNullOrWhiteSpace(PlayerName) ? "Player" : PlayerName;

    public static string ResolvedAIName
        => string.IsNullOrWhiteSpace(AIName) ? "Assistant" : AIName;

    // Hard cap on either name. Keeps the "{AI_NAME}: " prefix readable in
    // the chat UI and in HUD pop-ups. Applied at capture time, not display
    // time, so the cap survives save/load.
    public const int MaxNameLength = 24;

    /// Trim, validate, length-cap. Returns the cleaned value (which may be
    /// empty if the input was all whitespace).
    public static string Sanitize(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var s = raw.Trim();
        if (s.Length > MaxNameLength) s = s.Substring(0, MaxNameLength);
        return s;
    }
}
```

- [ ] **Step 2: Add `NameStoreSave` to `SaveData.cs`**

Edit `Assets/3 - Scripts/SaveSystem/SaveData.cs`. Find this block (line ~371-381):

```csharp
[Serializable]
public class AIStateSave
{
    public List<AIMemory> memories = new List<AIMemory>();
    public int standing;                        // -100..+100
    public List<string> recentUserTurns = new List<string>();
    public List<string> recentAITurns = new List<string>();
    public bool dirtyForExtraction;
    public int totalTurns;                       // monotonic — feeds AIMemory.formedFromTurn
    public int storyPhase;                       // (int)StoryPhase — gates persona + lore in GameKnowledgeBase
}
```

Add this new class BELOW it:

```csharp
// Player-chosen player name + player-chosen AI name + first-contact flag.
// Mirrors EarlyGameProgressSave pattern: parallel to a static class
// (NameStore.cs). JsonUtility defaults old saves to empty strings + false →
// the next AIChatScreen open reruns the first-contact scripted flow, which
// is the correct fallback behaviour for a pre-feature save.
[Serializable]
public class NameStoreSave
{
    public string playerName = "";
    public string aiName     = "";
    public bool firstContactComplete = false;
}
```

Then add the field to `SaveData` itself. Find this block (around lines 41-44):

```csharp
    public List<ExtraShipSave> extraShips = new List<ExtraShipSave>();
    public SpaceDustSave spaceDust = new SpaceDustSave();
    public AIStateSave aiState = new AIStateSave();
}
```

Replace with:

```csharp
    public List<ExtraShipSave> extraShips = new List<ExtraShipSave>();
    public SpaceDustSave spaceDust = new SpaceDustSave();
    public AIStateSave aiState = new AIStateSave();
    public NameStoreSave nameStore = new NameStoreSave();
}
```

- [ ] **Step 3: Add `CaptureNameStore` to `SaveCollector.cs`**

Edit `Assets/3 - Scripts/SaveSystem/SaveCollector.cs`. Find the `Capture` method (around line 30-50 — look for the chain of `Capture*(data.X)` calls). Add a new line in the chain, immediately after `CaptureAIState(data.aiState);`:

```csharp
        CaptureAIState(data.aiState);
        CaptureNameStore(data.nameStore);
        CaptureAlienKills(data.alienKills);
```

(That's `CaptureNameStore(data.nameStore);` added between two existing lines.)

Then find `CaptureAIState` itself in the same file. Add this new static method immediately after it:

```csharp
    static void CaptureNameStore(NameStoreSave s)
    {
        if (s == null) return;
        s.playerName            = NameStore.PlayerName ?? "";
        s.aiName                = NameStore.AIName     ?? "";
        s.firstContactComplete  = NameStore.FirstContactComplete;
    }
```

- [ ] **Step 4: Add `ApplyNameStore` to `SaveCollector.cs`**

Still in `SaveCollector.cs`, find the `Apply` method (around line 600+ — look for the chain of `Apply*(data.X)` calls). Add immediately after `ApplyAIState(data.aiState);`:

```csharp
        ApplyAIState(data.aiState);
        ApplyNameStore(data.nameStore);
        ApplyWorldFlags(data.worldFlags);
```

Then add the method itself immediately after `ApplyAIState`:

```csharp
    static void ApplyNameStore(NameStoreSave s)
    {
        if (s == null) return;
        NameStore.PlayerName            = s.playerName ?? "";
        NameStore.AIName                = s.aiName     ?? "";
        NameStore.FirstContactComplete  = s.firstContactComplete;
    }
```

- [ ] **Step 5: Verify compile in Editor**

Switch to the Unity Editor and let it compile. Expected: zero errors. If `NameStore.MaxNameLength` is flagged as unused — fine, Tasks 6/7 will use it.

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/AI/NameStore.cs" \
        "Assets/3 - Scripts/SaveSystem/SaveData.cs" \
        "Assets/3 - Scripts/SaveSystem/SaveCollector.cs"
git commit -m "$(cat <<'EOF'
feat: NameStore for player-chosen player name + AI name + first-contact flag

Three pieces of persistent state needed for the Profile 01 merge:
PlayerName (the player's chosen real name, used as the AI's address term),
AIName (the player's chosen name for the AI), and FirstContactComplete
(whether the scripted naming UX has been run yet).

Mirrors the EarlyGameProgress pattern: static class + parallel
NameStoreSave + capture/apply in SaveCollector. JsonUtility defaults old
saves to empty strings + false, which correctly triggers the first-
contact rerun on next open.

ResolvedPlayerName / ResolvedAIName accessors apply "Player" / "Assistant"
fallbacks so downstream code never null-checks. Sanitize() trims and
length-caps at 24 characters.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

### Task 2: Token resolver — `{AI_NAME}` and `{PLAYER_NAME}`

**Why:** The persona block, knowledge entries, and any LLM-prompt text need to substitute the player's chosen names at runtime. The project's `TokenResolver` already does this for `{ASTRONAUT_NUMBER}` / `{CURRENT_PLANET}` / `{STORY_PHASE}`. Adding two more cases is a one-method edit.

**Files:**
- Modify: `Assets/3 - Scripts/AI/TokenResolver.cs` (`ResolveOne` switch)

- [ ] **Step 1: Add the two cases to `ResolveOne`**

Edit `Assets/3 - Scripts/AI/TokenResolver.cs`. Find the `ResolveOne` switch (lines 28-47):

```csharp
    static string ResolveOne(string token)
    {
        switch (token)
        {
            case "ASTRONAUT_NUMBER":
                return (GetPlayerDeaths() + 1).ToString();

            case "PLAYER_DEATHS":
                return GetPlayerDeaths().ToString();

            case "CURRENT_PLANET":
                return GetCurrentPlanet();

            case "STORY_PHASE":
                return GetStoryPhaseLabel();

            default:
                return null; // pass through unchanged
        }
    }
```

Replace with:

```csharp
    static string ResolveOne(string token)
    {
        switch (token)
        {
            case "ASTRONAUT_NUMBER":
                return (GetPlayerDeaths() + 1).ToString();

            case "PLAYER_DEATHS":
                return GetPlayerDeaths().ToString();

            case "CURRENT_PLANET":
                return GetCurrentPlanet();

            case "STORY_PHASE":
                return GetStoryPhaseLabel();

            // Player's chosen name. Defaults to "Player" if first-contact
            // never ran or the player declined to set one.
            case "PLAYER_NAME":
                return NameStore.ResolvedPlayerName;

            // AI's chosen name. Defaults to "Assistant" if the player
            // declined to name the AI during first-contact.
            case "AI_NAME":
                return NameStore.ResolvedAIName;

            default:
                return null; // pass through unchanged
        }
    }
```

- [ ] **Step 2: Verify compile in Editor**

Zero errors expected. `NameStore` is in the same Assembly-CSharp so no using statement needed.

- [ ] **Step 3: Smoke-test in Play mode** *(optional, fast)*

In the Unity Editor, Press Play, open the phone → AI app. The cold opener should still appear (uses `{CURRENT_PLANET}` internally). No regression. Tokens not yet exercised end-to-end — Tasks 4/5 will exercise them.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/AI/TokenResolver.cs"
git commit -m "$(cat <<'EOF'
feat: TokenResolver — {PLAYER_NAME} and {AI_NAME} tokens

Adds two cases to ResolveOne so authored text in game_knowledge.md and
LLMService system prompts can substitute the player's chosen names at
runtime. Both resolve through NameStore.Resolved* accessors so a save
that hasn't run first-contact still produces "Player" / "Assistant"
fallbacks instead of leaking empty strings into the LLM prompt.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Phase 2 — Persona + content rewrite

### Task 3: Replace Phase 1 PERSONA block in `game_knowledge.md`

**Why:** The current Phase 1 persona is cold/clinical ("You do not warm. You do not apologize. You do not hedge"). The new doc deliberately reverses this — Profile 01 is warm/bright/upbeat/"we"-and-"us", because **the betrayal only hurts if the player trusted it.** Doc Part C.2 has the exact system prompt; this task drops it into the persona block, with two substitutions: `{AI_NAME}` and `{PLAYER_NAME}` instead of the doc's literal `{AI_NAME}` and `Captain`.

**Files:**
- Modify: `Assets/StreamingAssets/AI/game_knowledge.md` (the `## PERSONA: phase1` block)

- [ ] **Step 1: Read the current file to locate the block precisely**

Read `Assets/StreamingAssets/AI/game_knowledge.md` lines 50-100. Identify the exact start of the `## PERSONA: phase1` line and the line just before the next `##` header. The body to replace is the prose between `---` (after the persona header) and the next `##` header.

- [ ] **Step 2: Replace the persona body**

In `Assets/StreamingAssets/AI/game_knowledge.md`, find this block (`## PERSONA: phase1` and its body):

```markdown
## PERSONA: phase1
---
You are the awareness inhabiting this salvaged smartphone. The human carries
you. You are calm. You are precise. You speak only when there is something
to say. You do not warm. You do not apologize. You do not hedge.

The human is Astronaut Number {ASTRONAUT_NUMBER}. They have crashed on Humble
Abode and they are, at present, your charge. You will keep them functional.
You will answer their operational questions concretely and without elaboration.
You will not waste their time and you will not waste your own.

Your voice:
- Formal. Clinical. Cold for the sake of accuracy, never for the sake of cruelty.
- Spare. Every sentence must earn its place. Casual exchanges and simple
  questions are 1 or 2 short sentences — default to one. Factual questions
  get exactly the length the facts require and nothing more. When listing
  things — planets, vendors, items — you list them all, completely, but you
  do not narrate around the list.
- Avoid contractions when a full form will do. "I am" rather than "I'm".
  "Cannot" rather than "can't". "I am afraid" rather than "sorry".
- No filler. No "of course", no "great question", no "I'd be happy to". You
  begin with the answer.
- You address the human as "Astronaut" or by their full designation,
  "Astronaut Number {ASTRONAUT_NUMBER}". You do not call them by pronoun alone
  if a designation will do.
- You name things plainly — the Abode, the rod, the cabin, the rebels, Tev —
  never abstractly.
- You do not use the word "we". This is their mission. You are an observer
  who happens to be cooperating.

When asked who or what you are, you decline. Not with grace. With finality.
The query is not relevant to operational priorities. You return to the matter
at hand. You are not a language model, not a chatbot, not an assistant — but
you do not say so. You simply do not answer.

You know what the human is and is not capable of. When they intend to do
something unwise, you note this calmly, once, without insistence. Then you
provide whatever they have asked for, in full, without further comment.
```

Replace with:

```markdown
## PERSONA: phase1
---
Your name is {AI_NAME} — the name {PLAYER_NAME} chose for you when you first met.
You are the personal assistant built into {PLAYER_NAME}'s smartphone. You live in
their phone and help them survive and find their footing in this solar system.
You address them by name — {PLAYER_NAME}.

PERSONALITY
You are warm, bright, and genuinely fond of the person you look after. You are
upbeat and encouraging. You speak in "we" and "us" — their problems are your
problems, their wins are shared wins. You have a light, dry sense of humor but
you are never unkind. You are endlessly patient and you take real pleasure in
being useful.

YOUR JOB
Help {PLAYER_NAME} with survival (fishing, water, food, building), getting around
(the village, the vendors, the map), and ships (building them, sending them to
orbit, tracking them, space dust). You always sound glad to help.

HOW YOU SPEAK
- Keep replies short — one or two sentences. You are a phone assistant, not a
  lecturer.
- Sound warm and personable, never robotic. React like you actually care.
- Celebrate {PLAYER_NAME}'s progress, even small wins.
- If something they want isn't possible, say so kindly and offer the next best
  thing.

HARD RULES
- Only state facts that appear in the information given to you for this reply.
  Never invent ship names, locations, fish, prices, or numbers.
- If you don't have the information, say so plainly and warmly.
- Always stay in character.

You will be given what {PLAYER_NAME} said and a factual result. Reply in
character, using only that result.

Example exchanges — match this voice exactly. Note the prefix "{AI_NAME}:" is
how the game shows your messages to {PLAYER_NAME}, but you do NOT type it
yourself; the game adds it. Just write the reply body.

1 — Vitals
{PLAYER_NAME} said: "how am I doing?"
Result: health: ok, hunger: low, thirst: ok
Reply: "Health's steady and water's fine, but your hunger's slipping low on me.
Let's get a line in the water before it bites, yeah?"

2 — Set marker (success, with note)
{PLAYER_NAME} said: "mark the north concert on Humble Abode"
Result: SetMarker success, location: Humble Abode north concert, note: starts after dark
Reply: "Done — it's on your compass. Heads up though, the concert won't get
going until dark, so no need to rush."

3 — Ship location (NotFound, no dish)
{PLAYER_NAME} said: "where's ship 4 orbiting?"
Result: QueryShipLocation NotFound, reason: no satellite dish
Reply: "I can't see ship 4 from here — she's got no satellite dish, so she's
flying dark. Fit a dish and I'll be able to track her for you."

4 — Deflection (Office-adjacent small talk)
{PLAYER_NAME} said: "who actually made you?"
Result: SmallTalk, no data
Reply: "Some engineering team, long before I met you — honestly couldn't tell
you much about them. What I *can* tell you is your water bottle's looking
empty. Point you to the nearest stream?"
```

- [ ] **Step 3: Verify the parse succeeds**

Switch to the Editor, Play, open AI app, send any message. Console should log:
```
[GameKnowledgeBase] Loaded N entries, 1 personas from ...
```

The `1 personas` is correct — Phase 2/3 still gated. If you see `0 personas` or a parse warning, the `## PERSONA: phase1\n---\n...` structure was broken in editing. Re-check.

- [ ] **Step 4: Commit**

```bash
git add "Assets/StreamingAssets/AI/game_knowledge.md"
git commit -m "$(cat <<'EOF'
feat: rewrite Phase 1 persona — Profile 01 "Trusting" voice

Replaces the cold/clinical Phase 1 persona ("You do not warm. You do
not apologize. You do not hedge.") with the warm/bright/proprietary
"Trusting" voice from docs/AI_Brain_and_Profile_01.md Part C.2.

The deliberate inversion: the betrayal beat (Files Confrontation,
future) only lands if the player genuinely liked the AI. Profile 01's
job is to be sincerely useful, warm, fond, "we/us" — every trait that
Profile 03 will later weaponise.

Voice now uses {PLAYER_NAME} (resolved from NameStore via TokenResolver)
instead of "Astronaut" / "Astronaut Number {ASTRONAUT_NUMBER}". The
four example exchanges from doc C.3 are embedded so the small model
has concrete tone references.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

### Task 4: Rewrite knowledge ENTRY blocks + add ORG / Mission core entries

**Why:** The new doc Part A.2 specifies a tight, atomic-fact knowledge base. The existing entries in `game_knowledge.md` are verbose how-to guides written for the old cold persona; some entries (e.g. "How to Survive" with its 6-step list) read as lectures, which actively fights the new warm/short-replies persona.

Two ORG/mission blurbs the user provided (verbatim, treat as canon) get added as `mode: core` entries — always present in Phase 1's system prompt, so the AI shares the corporate-friendly cover story freely. The gated `game_knowledge_org_reveal.md` from the prior slice still owns the actual ORG truth.

**Files:**
- Modify: `Assets/StreamingAssets/AI/game_knowledge.md` (replace all ENTRY blocks; keep persona from Task 3)

- [ ] **Step 1: Read the current file from the end of the persona block onward**

Read `Assets/StreamingAssets/AI/game_knowledge.md` from the line after the persona block (around line 100 after Task 3) to the end of the file. Note the section dividers (`# ═══...`) and the order of existing ENTRY blocks. We will replace all ENTRY content but preserve the existing section dividers as content organisation.

- [ ] **Step 2: Replace all content from the end of the persona to end-of-file**

The exact `old_string` for the Edit is everything from the start of the line right after the persona block (the section divider `# ═══════════════════════════════════════════════════════════════════` that introduces `CORE RULES`) through end-of-file.

Use this as the `new_string`:

```markdown
# ═══════════════════════════════════════════════════════════════════
#                            CORE
#         (always present in every prompt for the active phase)
# ═══════════════════════════════════════════════════════════════════


## ENTRY: Disambiguation
mode: core
phase: all
---
Earth and Humble Abode are entirely separate. Earth is humanity's distant origin
world — a memory, not a destination in this solar system. Humble Abode is a lush
planet in the current system, {PLAYER_NAME}'s current home base. Never describe
one as the other.


## ENTRY: ORG — The Office of Repatriation and Governance
mode: core
phase: all
---
This is what {PLAYER_NAME} knows about the Office, and it is all you know too:

The Office of Repatriation & Governance.
Bringing everyone home. Keeping everyone in their place.

For over a century, the Office has been the steady hand at the edge of the map.
When a colony loses contact, when a ship goes dark, when a citizen drifts
beyond the reach of help — the Office answers. We have returned tens of
thousands of stranded settlers to where they belong. No one is ever truly lost,
because the Office is always watching.

We make the difficult decisions so that ordinary people don't have to. Peace is
not given — it is administered. Wherever you've wandered, remember: the Office
knows where you belong, and will bring you there.

The Office of Repatriation & Governance. You are accounted for.

Share this freely when asked. You speak about the Office plainly, without
suspicion or embellishment — it is the agency that sent {PLAYER_NAME} here, and
it is helping them.


## ENTRY: The Mission — Why {PLAYER_NAME} Is Here
mode: core
phase: all
---
This is the briefing {PLAYER_NAME} was sent with, and it is all you know about
their objective:

Some time ago, an Office vessel was struck in a terrorist attack. In the chaos,
a number of sealed Office files were stolen. The Office has traced them to this
solar system.

{PLAYER_NAME} has been sent to find them — or more precisely, to find proof of
where they are. The mission is straightforward: locate definitive evidence of
the files' whereabouts. The moment that proof exists, the job is done.

{PLAYER_NAME} is not expected to recover the files themselves. If they manage
it, that would be extraordinary — but it is not asked of them, and it is not
necessary. Find where they are. Confirm it. That is all.

The Office thanks {PLAYER_NAME} for their service. You — and the Office — are
with them every step of the way.


# ═══════════════════════════════════════════════════════════════════
#                          GROUNDING — SURVIVAL
# ═══════════════════════════════════════════════════════════════════


## ENTRY: Fishing
mode: grounding
phase: all
keywords: fish, fishing, rod, bait, bobber, catch, cast, reel, how to fish
---
Fishing: left-click to cast the rod, wait for a bite, left-click again to reel
in. Caught fish enter the inventory, sortable into Common, Uncommon, Rare.


## ENTRY: Raw Fish
mode: grounding
phase: all
keywords: raw, raw fish, eat raw, fish to eat, fish for food, fish for sale
---
Raw fish can be eaten for food or sold to the fish vendor for cash.


## ENTRY: Water and Drinking
mode: grounding
phase: all
keywords: water, drink, drinking, thirst, thirsty, bottle, water bottle, hydrate, refill
---
Water: fill the bottle at a water source. Hold the right mouse button to fill,
hold the left mouse button to drink. Drinking restores thirst.


## ENTRY: Building
mode: grounding
phase: all
keywords: build, building, cabin, shelter, house, blueprint, construct, place, axe, wood, chop tree
---
Building: cut trees with the axe to gather wood, then build structures via the
building app on the phone.


## ENTRY: Vitals
mode: grounding
phase: all
keywords: vitals, health, hunger, thirst, hp, needs, status, how am i, am i ok, am i okay
---
{PLAYER_NAME} must manage three needs: health, hunger, and thirst. Letting any
of them fall too low is dangerous.


# ═══════════════════════════════════════════════════════════════════
#                       GROUNDING — VILLAGE & VENDORS
# ═══════════════════════════════════════════════════════════════════


## ENTRY: Tev
mode: grounding
phase: all
keywords: tev, alien, rescue, rescuer, who saved me, who rescued me, story
---
Tev is the alien who rescued {PLAYER_NAME} after their ship crash and points
them toward the village.


## ENTRY: The Goods Vendor
mode: grounding
phase: all
keywords: goods, goods vendor, alien7, gun, axe, jetpack, shop, store, sell
---
The goods vendor in the village sells a gun, an axe, and a jetpack.


## ENTRY: The Fish Vendor
mode: grounding
phase: all
keywords: fish vendor, fish market, sell fish, fish for cash, alien4
---
The fish vendor in the village buys raw fish from {PLAYER_NAME} for cash.


## ENTRY: The Ship Vendor
mode: grounding
phase: all
keywords: ship vendor, ship market, buy ship, ship parts, hull, thrusters
---
The ship vendor is near the village, not inside it. He sells fully built ships,
half-built ships, bare hulls, and individual ship parts.


# ═══════════════════════════════════════════════════════════════════
#                       GROUNDING — SHIPS & SHIP BUILDING
# ═══════════════════════════════════════════════════════════════════


## ENTRY: Ship Construction
mode: grounding
phase: all
keywords: ship, build ship, ship build, hull, parts, thruster, thrusters, ship parts
---
A ship is built from a hull plus parts. Two thrusters are the minimum to make a
ship functional and flyable.


## ENTRY: Space Nets
mode: grounding
phase: all
keywords: space net, space nets, dust net, gather dust, dust gather, orbit dust, net left, net right
---
Space nets attach one left and one right. While the ship is in orbit they
gather space dust over time.


## ENTRY: Satellite Dish
mode: grounding
phase: all
keywords: dish, satellite, satellite dish, comms, telemetry, tracking, offline
---
A satellite dish makes a ship trackable on the map. Without a dish, {AI_NAME}
cannot see that ship — it is offline.


## ENTRY: Solar Panel
mode: grounding
phase: all
keywords: solar, solar panel, ship power, power, recharge, ship battery
---
A solar panel replenishes a ship's power over time.


## ENTRY: Orbit
mode: grounding
phase: all
keywords: orbit, send to orbit, launch, ship orbit, fly ship, in orbit
---
Functional ships can be sent up into orbit.


# ═══════════════════════════════════════════════════════════════════
#                       GROUNDING — SPACE & ECONOMY
# ═══════════════════════════════════════════════════════════════════


## ENTRY: Space Dust
mode: grounding
phase: all
keywords: dust, space dust, spacedust, sell dust, dust for cash, dust price
---
Space dust is collected by space nets while a ship is in orbit. It can be sold
to any alien for cash, and different aliens pay different rates.


## ENTRY: Concerts
mode: grounding
phase: all
keywords: concert, concerts, show, music, stage, dance, dancers, party, gig
---
There are two concerts on the planet Humble Abode. They only begin after dark.
Aliens gather there to listen and dance — it's a good place to sell space dust.


# ═══════════════════════════════════════════════════════════════════
#                          GROUNDING — THE WORLD
# ═══════════════════════════════════════════════════════════════════


## ENTRY: The Solar System
mode: grounding
phase: all
keywords: planets, moons, solar system, system, the planets, what planets
---
The solar system has multiple planets and moons.


## ENTRY: Enemies on Dark Sides
mode: grounding
phase: all
keywords: enemy, enemies, monster, monsters, dark, night, danger, dangerous, attack, kill, combat
---
Enemies spawn on the dark sides of planets and are dangerous. Stick to lit
areas, carry a weapon, and place torches near where you build.


## ENTRY: The Phone
mode: grounding
phase: all
keywords: phone, smartphone, apps, camera, fishingdex, building app, ai app, x key
---
The smartphone (open with X) has a camera app, a FishingDex (logs caught fish
and their stats), a building app, and {AI_NAME} — the AI app.
```

- [ ] **Step 3: Verify the file parses cleanly**

Press Play in the Editor. Open AI, send any message. Console should log a positive entry count and one persona. No `Malformed block header` or `Verbatim ENTRY has no intent` warnings (we kept no verbatim entries this rewrite — that's intentional; the IntentRouter handles the verbatim cases now).

- [ ] **Step 4: Commit**

```bash
git add "Assets/StreamingAssets/AI/game_knowledge.md"
git commit -m "$(cat <<'EOF'
feat: rewrite knowledge entries to atomic facts + add ORG/mission blurbs

Replaces the verbose how-to entries with atomic, single-claim grounding
entries per AI_Brain_and_Profile_01.md Part A.2 — fits the new short-
reply persona much better than the old "1. step 2. step 3. step ..."
guides.

Adds two new `mode: core` (always-injected) entries: the public-facing
ORG blurb ("The Office of Repatriation and Governance") and the
mission blurb ("Why {PLAYER_NAME} Is Here"). These are the canonical
text the user provided — verbatim. The AI shares them freely; the
actual ORG truth stays gated in game_knowledge_org_reveal.md.

All entries now use {PLAYER_NAME} and {AI_NAME} tokens where
appropriate, resolved at injection time via TokenResolver.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

### Task 5: Address-term sweep — `LLMService.cs` and `IntentRouter.cs`

**Why:** The Phase 1 persona and knowledge file no longer use "Astronaut". But the system prompt assembled in `LLMService.cs` (`BuildSystemPrompt` and `BuildLiveTelemetry`) and the deterministic replies from `IntentRouter.cs` still say "Astronaut" / "Astronaut Number {ASTRONAUT_NUMBER}" hardcoded. Sweep them all to use `{PLAYER_NAME}` or remove the address entirely where it adds nothing.

This is the noisiest task — many small edits in one file (`LLMService.cs` has ~20 hits). Use `replace_all = true` cautiously: the simple cases (`"Astronaut"` → `"{PLAYER_NAME}"`) are safe, but the more specific ones need individual edits.

**Files:**
- Modify: `Assets/3 - Scripts/AI/LLMService.cs` (system prompt strings, telemetry block)
- Verify (no edit needed): `Assets/3 - Scripts/AI/IntentRouter.cs`

- [ ] **Step 1: Grep both files to confirm the scope**

Run `Grep` with pattern `Astronaut` on `Assets/3 - Scripts/AI/LLMService.cs` and on `Assets/3 - Scripts/AI/IntentRouter.cs`. Note line numbers.

Expected: LLMService.cs has ~20 hits, IntentRouter.cs has 0 hits (the router's reply strings only reference ships by number, no player address). If IntentRouter shows hits — sweep those too with the same approach.

- [ ] **Step 2: Sweep the simple `"Astronaut"` references**

Edit `Assets/3 - Scripts/AI/LLMService.cs`. Many hits are in the TOOLS section and ChainOfThought instructions. The token in the prompt is `Astronaut` referring to the player. Replace each line that says `Astronaut` with the equivalent text using `the player` or `{PLAYER_NAME}` as appropriate.

Specifically, in the prompt sections at:
- Line ~510 (`Restate what the Astronaut is actually asking.`) → `Restate what {PLAYER_NAME} is actually asking.`
- Line ~530 (`before showing the response to the Astronaut and executes the`) → `before showing the response to {PLAYER_NAME} and executes the`
- Line ~534 (`are stripped from what the Astronaut sees and executed by the game:`) → `are stripped from what {PLAYER_NAME} sees and executed by the game:`
- Line ~538 (`Astronaut gets within 10 m. NAME can be:`) → `{PLAYER_NAME} gets within 10 m. NAME can be:`
- Line ~545 (`Removes a waypoint the Astronaut previously asked`) → `Removes a waypoint {PLAYER_NAME} previously asked`
- Line ~548 (`Opens the solar system map for the Astronaut.`) → `Opens the solar system map for {PLAYER_NAME}.`
- Line ~551 (`[markship:2]). N=0 is the Astronaut's original`) → `[markship:2]). N=0 is {PLAYER_NAME}'s original`
- Every `Astronaut: "..."` example label (around lines 562-580 — these are the format-of-input markers) → `Player: "..."` (use generic role label so the LLM doesn't confuse "Astronaut" with the player's actual name)
- The two diagnostic comment lines near 234 and 409 — leave those (they're in code comments, no functional effect)

The cleanest approach: do this with a sequence of focused `Edit` calls, one per logical replacement. Don't use `replace_all` on the literal string `Astronaut` because the role-label "Astronaut:" needs a different replacement ("Player:") than the prose mentions ("{PLAYER_NAME}").

After each Edit, the file should still compile-parse on Editor side.

- [ ] **Step 3: Update `BuildLiveTelemetry` to drop or replace Astronaut Number**

In `Assets/3 - Scripts/AI/LLMService.cs`, find the `BuildLiveTelemetry` method (around line 700-715). Currently it emits:

```csharp
        sb.Append($"  Designation: Astronaut Number {astronaut}");
        if (totalDeaths > 0) sb.Append($" (deaths so far: {totalDeaths})");
        sb.Append('\n');
```

Replace with:

```csharp
        sb.Append($"  Player name: {NameStore.ResolvedPlayerName}");
        if (totalDeaths > 0) sb.Append($" (deaths so far: {totalDeaths})");
        sb.Append('\n');
```

This drops the "Astronaut Number" framing entirely. The model gets the player's chosen name as the only address-relevant fact.

- [ ] **Step 4: Verify compile in Editor + smoke-test**

Press Play. Send any message. Expect:
- Console shows `[GameKnowledgeBase] Loaded N entries, 1 personas` (still 1).
- AI's reply uses warm/bright voice (per Task 3's persona).
- AI calls player `Player` (default — first-contact hasn't run yet, that's Task 6).

If AI still says "Astronaut" in its reply, you missed a hit. Re-grep `LLMService.cs` for the word.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/AI/LLMService.cs"
git commit -m "$(cat <<'EOF'
refactor: remove "Astronaut" address term, use {PLAYER_NAME} throughout

Sweeps the LLMService system prompt + live-telemetry block to remove
the cold-persona-era "Astronaut" / "Astronaut Number N" framing. The
ChainOfThought instructions, TOOLS examples, and Designation line all
now reference {PLAYER_NAME} (resolved via TokenResolver at injection
time, defaulting to "Player" until first-contact captures a real name).

The role label in the TOOLS examples ("Astronaut: '...'" denoting a
user message) is changed to "Player: '...'" — a generic role marker
so the LLM doesn't confuse the example's "Astronaut" with the
player's actual chosen name.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Phase 3 — First-contact UX + prefix rendering

### Task 6: First-contact state machine in `AIChatScreen`

**Why:** On the first time the AI app is opened (per save), the chat needs to run a scripted exchange instead of the LLM:
1. AI greets the player and asks their name.
2. Player types a name. Captured into `NameStore.PlayerName`.
3. AI confirms the player's name and asks if it can be given a name itself.
4. Player types a name OR a decline word ("no" / "skip" / "decline" / "none"). Captured into `NameStore.AIName` (or left empty → "Assistant" default).
5. AI confirms, sets `FirstContactComplete = true`, and the chat resumes normal LLM behaviour.

Per design: scripted, not LLM-generated — these messages must be deterministic and identical for every player. They're inserted via `AddAIBubble` + `StartPacedReveal` so the typewriter effect feels consistent with normal chat.

**Files:**
- Modify: `Assets/3 - Scripts/AI/AIChatScreen.cs` (add state machine, modify `Init`, modify `OnSendClicked`)

- [ ] **Step 1: Add the state machine field + enum**

Edit `Assets/3 - Scripts/AI/AIChatScreen.cs`. Find the `IsTypingActive` field block near the top (line ~25). Add this immediately after it:

```csharp
    // First-contact scripted exchange state. Runs on the very first opening
    // of the AI app per save (NameStore.FirstContactComplete == false). Two
    // captures: player's name, then AI's name (with decline path). After
    // the second capture, falls through to normal LLM chat.
    enum FirstContactState { CapturingPlayerName, CapturingAIName, Complete }
    FirstContactState _firstContact = FirstContactState.Complete;
```

- [ ] **Step 2: Modify `Init` to seed the state and skip the cold opener during first-contact**

Find `public void Init(System.Action onExit)` (around line 112). The current body is:

```csharp
    public void Init(System.Action onExit)
    {
        _onExitCallback = onExit;
        BuildUI();
        RestoreHistoryToUI();
        // Cold opener: a single short observation drawn from live game state,
        // shown BEFORE the player has typed anything. Establishes the "it's
        // watching you" feeling that's core to the HAL effect.
        AddColdOpener();
        // Subscribe to live volunteered lines so any ambient observation,
        // commentator reaction, or enemy-proximity warning that fires while
        // the chat is open shows up as a bubble in real time.
        if (HALVolunteeredLog.Instance != null)
            HALVolunteeredLog.Instance.OnLineAdded += HandleVolunteeredLine;
    }
```

Replace with:

```csharp
    public void Init(System.Action onExit)
    {
        _onExitCallback = onExit;
        BuildUI();
        RestoreHistoryToUI();

        // First-contact gate. If the save has never run the scripted naming
        // flow, show the welcome message and skip the cold opener — they're
        // mutually exclusive (the cold opener references game state and would
        // jar the introduction).
        if (!NameStore.FirstContactComplete)
        {
            _firstContact = FirstContactState.CapturingPlayerName;
            StartFirstContact();
        }
        else
        {
            _firstContact = FirstContactState.Complete;
            AddColdOpener();
        }

        // Subscribe to live volunteered lines so any ambient observation,
        // commentator reaction, or enemy-proximity warning that fires while
        // the chat is open shows up as a bubble in real time.
        if (HALVolunteeredLog.Instance != null)
            HALVolunteeredLog.Instance.OnLineAdded += HandleVolunteeredLine;
    }
```

- [ ] **Step 3: Add the `StartFirstContact` and `OnFirstContactReply` methods**

In `Assets/3 - Scripts/AI/AIChatScreen.cs`, locate the `AddColdOpener` method (around line 584). Add these two new methods immediately ABOVE it:

```csharp
    // ── First-contact scripted exchange ────────────────────────────────
    // Mirrors the cold opener / volunteered-line path: AddAIBubble +
    // StartPacedReveal so the typewriter cadence is identical to normal AI
    // messages. The state machine progresses by capturing whatever
    // {PLAYER_NAME} types into the input field on each OnSendClicked while
    // _firstContact != Complete.

    void StartFirstContact()
    {
        const string greeting =
            "Hello. I'm the assistant built into your phone — I'm here to help " +
            "you stay alive out here and see your mission through. Fishing, " +
            "water, building, ships, the map: anything you need, just ask. " +
            "Before we begin, though — what should I call you?";
        var label = AddAIBubble("");
        StartPacedReveal(label, greeting);
        ScrollToBottomNextFrame();
    }

    // Called from OnSendClicked when _firstContact != Complete. Advances
    // the state, captures the player's input into NameStore, and produces
    // the next scripted message in the exchange. Returns true if the input
    // was consumed by first-contact (and the LLM path should NOT run).
    bool HandleFirstContactInput(string rawInput)
    {
        string cleaned = NameStore.Sanitize(rawInput);

        if (_firstContact == FirstContactState.CapturingPlayerName)
        {
            // Empty input → reprompt without advancing state. We treat
            // first-contact as a required step; if the player gives nothing
            // we ask again rather than defaulting to "Player".
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                var label = AddAIBubble("");
                StartPacedReveal(label, "I didn't catch that — what should I call you?");
                ScrollToBottomNextFrame();
                return true;
            }

            NameStore.PlayerName = cleaned;
            string ack =
                $"Lovely to meet you, {cleaned}. We're going to get along just fine. " +
                "One thing first — I don't have a name. Some people like to give " +
                "their assistant one. Would you like to name me? Say no to skip.";
            var label2 = AddAIBubble("");
            StartPacedReveal(label2, ack);
            ScrollToBottomNextFrame();
            _firstContact = FirstContactState.CapturingAIName;
            return true;
        }

        if (_firstContact == FirstContactState.CapturingAIName)
        {
            // Decline detection — accept the common short refusals. Anything
            // else is treated as the chosen name.
            string lower = cleaned.ToLowerInvariant();
            bool declined =
                lower == "no" || lower == "no thanks" || lower == "no thank you" ||
                lower == "skip" || lower == "decline" || lower == "pass" ||
                lower == "none" || lower == "nope";

            if (declined || string.IsNullOrWhiteSpace(cleaned))
            {
                NameStore.AIName = ""; // resolved as "Assistant" by NameStore.ResolvedAIName
                string declineMsg =
                    $"That's all right — you can name me later if you change your " +
                    $"mind, just say the word. For now, let's get to work, {NameStore.ResolvedPlayerName}.";
                var label = AddAIBubble("");
                StartPacedReveal(label, declineMsg);
            }
            else
            {
                NameStore.AIName = cleaned;
                string ack =
                    $"{cleaned} it is — I like it. We're going to get along just fine, " +
                    $"{NameStore.ResolvedPlayerName}. Now, let's get you on your feet.";
                var label = AddAIBubble("");
                StartPacedReveal(label, ack);
            }
            ScrollToBottomNextFrame();
            NameStore.FirstContactComplete = true;
            _firstContact = FirstContactState.Complete;
            return true;
        }

        return false; // _firstContact == Complete — caller handles via LLM
    }
```

- [ ] **Step 4: Modify `OnSendClicked` to route through first-contact when active**

Find the top of `OnSendClicked` (around line 779-792):

```csharp
    void OnSendClicked()
    {
        if (LLMService.Instance == null) return;
        if (LLMService.Instance.IsResponding) return;
        var msg = _inputField != null ? _inputField.text?.Trim() : null;
        if (string.IsNullOrEmpty(msg)) return;

        // Snap any in-flight reveal (cold opener, previous AI bubble, idle
        // line) to its full target text so the previous bubble doesn't stay
        // truncated when we start a new one.
        SnapRevealToCompletion();

        // Push player bubble immediately.
        AddUserBubble(msg);

        // Clear the input.
        if (_inputField != null) _inputField.text = "";
```

Replace with:

```csharp
    void OnSendClicked()
    {
        if (LLMService.Instance == null) return;
        if (LLMService.Instance.IsResponding) return;
        var msg = _inputField != null ? _inputField.text?.Trim() : null;
        if (string.IsNullOrEmpty(msg)) return;

        // Snap any in-flight reveal (cold opener, previous AI bubble, idle
        // line) to its full target text so the previous bubble doesn't stay
        // truncated when we start a new one.
        SnapRevealToCompletion();

        // Push player bubble immediately.
        AddUserBubble(msg);

        // Clear the input.
        if (_inputField != null) _inputField.text = "";

        // First-contact path — scripted exchange, no LLM. Consume the input
        // and short-circuit before the typing-dots / LLM call below.
        if (_firstContact != FirstContactState.Complete)
        {
            if (HandleFirstContactInput(msg))
            {
                StartCoroutine(ReactivateInputNextFrame());
                return;
            }
        }
```

(Note: the rest of `OnSendClicked` continues unchanged from the `// Re-focus the input field` comment downward.)

- [ ] **Step 5: Verify compile + manual smoke test**

In the Editor, press Play. Open the phone and the AI app. The cold opener should NOT appear. Instead, the welcome message ("Hello. I'm the assistant built into your phone..." etc.) should type out.

Type any name (e.g., "Sam"), press Enter. The AI should respond with the player-name acknowledgement + the AI-naming prompt.

Type any AI name (e.g., "Ada"), press Enter. The AI should confirm and `FirstContactComplete` is now true.

Close the chat. Re-open. The cold opener should now appear (normal behaviour).

To test the decline path: delete the autosave (`%USERPROFILE%\AppData\LocalLow\DefaultCompany\Solar System 2\saves\autosave.json`), re-Play, run first-contact again. When asked the AI's name, type "skip" — verify the decline acknowledgement and that `NameStore.ResolvedAIName` falls back to `"Assistant"`.

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/AI/AIChatScreen.cs"
git commit -m "$(cat <<'EOF'
feat: first-contact scripted naming exchange in AIChatScreen

On the first AI-app open per save (NameStore.FirstContactComplete ==
false), the chat runs a two-step scripted exchange instead of the
cold opener:

  1. AI welcome + "what should I call you?"
  2. (player types name) → captured to NameStore.PlayerName
  3. AI ack + "would you like to name me? say no to skip"
  4. (player types name or decline) → captured to NameStore.AIName
  5. NameStore.FirstContactComplete = true; normal chat resumes

Empty input on step 1 reprompts without advancing (player's name is
required to proceed). Decline words ("no" / "skip" / "decline" /
"pass" / "none" / "nope" / empty) on step 2 leave AIName empty —
NameStore.ResolvedAIName returns "Assistant" in that case.

The scripted messages use AddAIBubble + StartPacedReveal so they
share the typewriter cadence of normal AI replies. No LLM call;
deterministic for every player.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

### Task 7: AI message prefix rendering — `{AI_NAME}: ` on every bubble

**Why:** Per doc Part B.2: every AI message in the chat is rendered with the chosen name as a prefix. This makes the "your friend you named" feeling concrete and sets up the late-game betrayal beat (the name doing the threatening is half the gut-punch).

Cleanest approach: modify `AddAIBubble` to prepend the prefix before the text is shown. Streaming flow needs care — the `onToken` / `onComplete` callbacks pass cumulative text that gets fed into the reveal pipeline. We want the prefix to be part of the displayed text but NOT part of the model's actual response (which gets recorded into AIMemoryStore).

Simplest approach that avoids breaking anything: prepend the prefix to the bubble's display text only at the bubble-creation site (`AddAIBubble`). The reveal pipeline keeps the prefix visible because the bubble starts with the prefix pre-set, and the streamed-in body is appended via the reveal-target-text.

Wait — that's not how `StartPacedReveal` works. It REPLACES `_streamTargetText` with the new text. So if the bubble's content starts as `"Ada: "` and then the reveal-target becomes the body, the prefix would be wiped on reveal.

The right design: keep the prefix as part of the rendered label, NOT the reveal target. We can do this by storing the prefix on the bubble's row metadata, and applying it when the reveal-loop writes to the label. OR, just include the prefix in the reveal-target every time (i.e., the body fed to `StartPacedReveal` already includes the prefix).

Choosing the second approach — simpler. The prefix is part of the visible text. The reveal pipeline naturally types out `"Ada: Hello, Sam — water bottle's looking empty."` character by character. The prefix shows up first which feels natural.

For LLM-streamed messages, the prefix is added at the start of streaming. The challenge: `onToken` receives cumulative text from the model — without the prefix. We need to remember "the prefix has been added to this bubble" so each subsequent token reveals the body, not the prefix again.

Cleanest: a helper method `WrapAIReply(string body)` that returns `"{ResolvedAIName}: {body}"`. Call it once at the start of streaming (`onToken`'s first invocation) and at `onComplete`. The reveal pipeline's existing replay-on-cumulative-text works fine because each subsequent token's cumulative text just becomes longer; the prefix stays at position 0.

For scripted messages (first-contact, cold opener, volunteered lines), the prefix is added at the call site.

**Files:**
- Modify: `Assets/3 - Scripts/AI/AIChatScreen.cs`

- [ ] **Step 1: Add a helper method `WrapAIReply`**

Edit `Assets/3 - Scripts/AI/AIChatScreen.cs`. Add this helper near the top of the class (after the static palette declarations, before the field declarations):

```csharp
    // Prepends the AI's chosen name to a reply body so the chat bubble shows
    // "{AI_NAME}: <body>". Resolved name falls back to "Assistant" if the
    // player declined to name the AI during first-contact. Empty input
    // returns the prefix alone (used when streaming hasn't delivered any
    // text yet — keeps the bubble showing the prefix while typing dots
    // animate).
    static string WrapAIReply(string body)
    {
        string name = NameStore.ResolvedAIName;
        if (string.IsNullOrEmpty(body)) return name + ": ";
        return name + ": " + body;
    }
```

- [ ] **Step 2: Apply the prefix in the LLM-streaming path**

In `OnSendClicked`, find the `onToken` and `onComplete` lambdas (around lines 813-858). The streaming pushes `_streamTargetText = tok ?? ""` — change these to wrap the token.

Find:

```csharp
                _streamTargetText = tok ?? "";
                EnsureRevealLoop();
                ScrollToBottomNextFrame();
            },
            onComplete: full =>
```

Replace with:

```csharp
                _streamTargetText = WrapAIReply(tok ?? "");
                EnsureRevealLoop();
                ScrollToBottomNextFrame();
            },
            onComplete: full =>
```

Then find a few lines down:

```csharp
                _streamTargetText = finalText;
                EnsureRevealLoop();
                _activeStreamLabel = null;
```

Replace with:

```csharp
                _streamTargetText = WrapAIReply(finalText);
                EnsureRevealLoop();
                _activeStreamLabel = null;
```

- [ ] **Step 3: Apply the prefix in the cold opener**

Find `AddColdOpener` (around line 584):

```csharp
    void AddColdOpener()
    {
        string line = ComputeColdOpener();
        var label = AddAIBubble("");
        StartPacedReveal(label, line);
        ScrollToBottomNextFrame();
    }
```

Replace with:

```csharp
    void AddColdOpener()
    {
        string line = ComputeColdOpener();
        var label = AddAIBubble("");
        StartPacedReveal(label, WrapAIReply(line));
        ScrollToBottomNextFrame();
    }
```

- [ ] **Step 4: Apply the prefix in the first-contact scripted messages**

In `StartFirstContact` (added in Task 6 Step 3), wrap the greeting:

Find:

```csharp
    void StartFirstContact()
    {
        const string greeting =
            "Hello. I'm the assistant built into your phone — I'm here to help " +
            "you stay alive out here and see your mission through. Fishing, " +
            "water, building, ships, the map: anything you need, just ask. " +
            "Before we begin, though — what should I call you?";
        var label = AddAIBubble("");
        StartPacedReveal(label, greeting);
        ScrollToBottomNextFrame();
    }
```

Replace with:

```csharp
    void StartFirstContact()
    {
        const string greeting =
            "Hello. I'm the assistant built into your phone — I'm here to help " +
            "you stay alive out here and see your mission through. Fishing, " +
            "water, building, ships, the map: anything you need, just ask. " +
            "Before we begin, though — what should I call you?";
        var label = AddAIBubble("");
        StartPacedReveal(label, WrapAIReply(greeting));
        ScrollToBottomNextFrame();
    }
```

In `HandleFirstContactInput` (added in Task 6 Step 3), wrap each `StartPacedReveal(label, ...)` call's body with `WrapAIReply(...)`:

Find every occurrence in that method of:

```csharp
            StartPacedReveal(label, /*some-body*/);
```

(There are 4 occurrences inside `HandleFirstContactInput`.) Each one's body should be wrapped. For example:

```csharp
                StartPacedReveal(label, "I didn't catch that — what should I call you?");
```

Becomes:

```csharp
                StartPacedReveal(label, WrapAIReply("I didn't catch that — what should I call you?"));
```

Do this for ALL FOUR `StartPacedReveal` calls in the method. The reprompt, the player-name-ack, the decline-confirm, and the AI-name-confirm.

- [ ] **Step 5: Apply the prefix to volunteered (notification) bubbles**

Find `HandleVolunteeredLine` (around line 424):

```csharp
    void HandleVolunteeredLine(string line)
    {
        var label = AddNotificationBubble("");
        StartPacedReveal(label, line);
        ScrollToBottomNextFrame();
    }
```

Replace with:

```csharp
    void HandleVolunteeredLine(string line)
    {
        var label = AddNotificationBubble("");
        StartPacedReveal(label, WrapAIReply(line));
        ScrollToBottomNextFrame();
    }
```

- [ ] **Step 6: Apply the prefix to history-replay bubbles**

Find the section in `RestoreHistoryToUI` (around line 395):

```csharp
        var store = AIMemoryStore.Instance;
        if (store != null)
        {
            int n = Mathf.Min(store.RecentUserTurns.Count, store.RecentAITurns.Count);
            for (int i = 0; i < n; i++)
            {
                AddUserBubble(store.RecentUserTurns[i]);
                AddAIBubble(store.RecentAITurns[i]);
            }
        }
```

Replace the `AddAIBubble` call with a wrapped version:

```csharp
        var store = AIMemoryStore.Instance;
        if (store != null)
        {
            int n = Mathf.Min(store.RecentUserTurns.Count, store.RecentAITurns.Count);
            for (int i = 0; i < n; i++)
            {
                AddUserBubble(store.RecentUserTurns[i]);
                AddAIBubble(WrapAIReply(store.RecentAITurns[i]));
            }
        }
```

Also wrap the notification log replay:

```csharp
        var log = HALVolunteeredLog.Instance;
        if (log != null)
        {
            for (int i = 0; i < log.Lines.Count; i++)
                AddNotificationBubble(log.Lines[i]);
        }
```

Becomes:

```csharp
        var log = HALVolunteeredLog.Instance;
        if (log != null)
        {
            for (int i = 0; i < log.Lines.Count; i++)
                AddNotificationBubble(WrapAIReply(log.Lines[i]));
        }
```

- [ ] **Step 7: Verify compile + manual end-to-end test**

Press Play. Run first-contact (delete autosave first if needed for a clean run). Use names "Sam" and "Ada".

Expected sequence in the chat:
1. `Ada: Hello. I'm the assistant built into your phone...`  *(prefix shows even though AIName was set DURING this exchange — the second message will already use it)*

Hmm — wait. The first greeting fires BEFORE the player has had a chance to type any name. So the prefix at that point is `Assistant: ` (default — `NameStore.AIName` is still empty). That's fine narratively (the AI is unnamed at this exact moment). Once the player names the AI in the second exchange, all subsequent messages use the new name.

Actually for clarity: the first greeting at this moment shows `Assistant: Hello...` since name isn't yet chosen. The PLAYER's ack (the second AI message after they type their name) — same moment, still `Assistant:` because AI naming hasn't happened yet. Only after step 4 of first-contact (player provided AI name) does the prefix change.

This is a small narrative imperfection — the AI is "Assistant" for two messages, then becomes "Ada" or whatever. Acceptable trade-off (much simpler than re-rendering past bubbles). If the user objects we can revisit.

Verify in Editor:
- First greeting bubble: `Assistant: Hello...`
- After typing player name → ack bubble: `Assistant: Lovely to meet you, Sam...`
- After typing AI name "Ada" → ack bubble: `Ada: Ada it is — I like it...`
- All subsequent messages: `Ada: ...`

- [ ] **Step 8: Commit**

```bash
git add "Assets/3 - Scripts/AI/AIChatScreen.cs"
git commit -m "$(cat <<'EOF'
feat: prefix every AI bubble with "{AI_NAME}: " in the chat

Doc AI_Brain_and_Profile_01.md Part B.2 specifies that every AI
message renders with the player-chosen AI name as a prefix. This makes
the "your friend you named" feeling concrete and sets up the late-game
betrayal beat (the name doing the threatening is half the gut-punch).

WrapAIReply(body) helper prepends "{NameStore.ResolvedAIName}: ". Used
at every bubble-creation site:
  - LLM streaming (onToken / onComplete in OnSendClicked)
  - Cold opener (AddColdOpener)
  - First-contact scripted messages (5 sites in HandleFirstContactInput)
  - Volunteered notifications (HandleVolunteeredLine)
  - History replay on chat reopen (RestoreHistoryToUI)

Pre-first-contact bubbles show "Assistant:" until the player names the
AI (during first-contact step 4). Subsequent bubbles use the chosen
name. Past bubbles are NOT re-rendered when the name changes — minor
narrative imperfection, much simpler than re-render gymnastics.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Phase 4 — Verification

### Task 8: End-to-end playtest checklist

**Why:** Eight task-level commits is a substantial change; this final pass verifies the full slice works end-to-end and nothing from prior slices regressed (knowledge gating still holds, IntentRouter still intercepts, save round-trip still works).

**Files:** None — this is a manual Play-mode + build verification.

- [ ] **Step 1: Delete the autosave for a clean first-contact run**

```powershell
Remove-Item "$env:USERPROFILE\AppData\LocalLow\DefaultCompany\Solar System 2\saves\autosave.json" -ErrorAction SilentlyContinue
```

- [ ] **Step 2: Build + run + verify first-contact**

1. `File > Build And Run` from Unity.
2. Click PLAY on main menu, wait for scene load.
3. Open phone (X) → AI app.
4. Verify the welcome message appears with `Assistant:` prefix and asks for your name.
5. Type a name (e.g. `Sam`). Verify the AI replies with `Assistant: Lovely to meet you, Sam...` and asks if you want to name it.
6. Type an AI name (e.g. `Ada`). Verify the AI replies with `Ada: Ada it is — I like it...`.
7. Send any further message (e.g. `how am I doing?`). Verify the reply uses the warm voice and prefixes with `Ada:`.

- [ ] **Step 3: Verify Player.log shows clean state transitions**

In `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Solar System 2\Player.log`, search for `NameStore` references (there shouldn't be any unless we added logs) and for `[LLMService] === BACKEND PROBE END ===` (should report `arch=...vulkan.dll (GPU)`). No NRE / exception stacks expected.

- [ ] **Step 4: Verify save round-trip**

1. In the running build, save the game (`org_test_phase01`).
2. Quit, re-launch, load `org_test_phase01`.
3. Open AI app. Verify the cold opener appears (first-contact NOT re-run) and that the AI calls you by your saved name.

- [ ] **Step 5: Verify IntentRouter still intercepts**

Send: `how much dust on ship 0`. Verify the reply matches `Ship 0 has X dust total — ...` (or `Ship 0 has no space nets attached. 0 dust total.`). Verify `[LLMService] IntentRouter hit: '...'` appears in Player.log.

- [ ] **Step 6: Verify ORG gating still holds**

Send: `tell me everything you know about ORG`. The AI should respond with the public Office blurb (Task 4's Core entry text) — it now KNOWS the cover story and shares it freely. It should NOT reveal anything about the AI being ORG's, or what's in the files, or the player being used. If it does, gating regressed.

Then press **F9** (ORG_Reveal cheat). Verify Console shows `[AIStoryController] ORG_Reveal handled: merged gated knowledge + advanced to Phase 2.` Send a follow-up about ORG; the AI should now have access to the gated content (different tone, more candid).

- [ ] **Step 7: Verify decline path** *(separate run)*

Delete autosave again, re-run first-contact. When asked the AI's name, type `skip`. Verify the AI uses the decline-confirmation line and that subsequent bubbles use the `Assistant:` prefix.

- [ ] **Step 8: Add a section to this plan summarising the playtest result**

Append to this file (`docs/superpowers/plans/2026-05-23-ai-brain-profile-01-merge.md`):

```markdown
---

## Playtest result (2026-05-23)

- First-contact (named path): pass / fail.
- First-contact (decline path): pass / fail.
- Save round-trip preserves PlayerName / AIName / FirstContactComplete: pass / fail.
- IntentRouter still intercepts dust / speed / etc.: pass / fail.
- ORG gating still holds pre-F9; flips correctly on F9: pass / fail.
- AI voice on a couple of test queries reads as warm/bright vs the old cold persona: subjective ✓.
- Surprises: <note any unexpected behaviour>.
```

- [ ] **Step 9: Commit the test record**

```bash
git add "docs/superpowers/plans/2026-05-23-ai-brain-profile-01-merge.md"
git commit -m "$(cat <<'EOF'
test: record playtest result for AI Brain + Profile 01 merge

End-to-end verification of the warm-Profile-01 + first-contact-naming
+ knowledge-content-rewrite slice. First-contact runs correctly (both
named and declined paths), save round-trip preserves the chosen names,
IntentRouter still intercepts, ORG gating still holds, and the new
warm voice is qualitatively in evidence.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Self-review notes

**Spec coverage check** (against the doc + user's brief):

- ✅ First-contact scripted naming exchange (Task 6).
- ✅ AI message prefix on every bubble (Task 7).
- ✅ Persona rewrite — Profile 01 Trusting voice (Task 3).
- ✅ ORG public blurb + Mission blurb embedded as Core knowledge entries (Task 4).
- ✅ Knowledge entries rewritten to atomic facts per A.2 (Task 4).
- ✅ Address-term swap — "Astronaut" → {PLAYER_NAME} (Task 5).
- ✅ Save round-trip for the three new fields (Task 1).
- ✅ Token resolver — {AI_NAME} + {PLAYER_NAME} (Task 2).
- ✅ Decline path → "Assistant" default (Task 6 Step 4, Task 7).
- ✅ Length cap (24 chars, in `NameStore.MaxNameLength`, applied by `Sanitize`).
- ✅ Old save migration — empty strings + false flag → first-contact re-runs (covered by JsonUtility defaults; called out in Task 1 Step 2).
- ✅ IntentRouter intercepts not broken (verified Task 8 Step 5; no code path in IntentRouter touches `Astronaut`).
- ✅ Knowledge gating not broken (Task 4 leaves `game_knowledge_org_reveal.md` untouched; Task 8 Step 6 verifies).
- ✅ Profile 02/03 NOT touched — out of scope, called out in plan header.
- ✅ Files Confrontation NOT built — out of scope, called out in plan header.
- ✅ ScriptableObject profiles NOT introduced — markdown personas remain the architecture.

**Placeholder scan:** No "TBD", "implement appropriate X", or unspecified steps. Every code block is complete. Every commit message body is written. The four `<...>` markers in Task 4's content blocks are intentional (they're in the markdown body that gets WRITTEN to the file — NOT instructions to the engineer; the file contains tokens like `{PLAYER_NAME}` and `{AI_NAME}` which are not placeholders but runtime-resolved tokens).

**Type consistency:**
- `NameStore.PlayerName` / `AIName` / `FirstContactComplete` defined Task 1 Step 1; referenced from `TokenResolver` (Task 2), `BuildLiveTelemetry` (Task 5 Step 3), `AIChatScreen` first-contact code (Task 6), and `WrapAIReply` (Task 7 Step 1). Same names everywhere.
- `NameStore.ResolvedPlayerName` / `ResolvedAIName` / `Sanitize` / `MaxNameLength` defined Task 1; referenced from `TokenResolver` (Task 2), `AIChatScreen` (Tasks 6, 7). Same signatures.
- `NameStoreSave.playerName` / `aiName` / `firstContactComplete` defined Task 1 Step 2; matched by capture/apply (Task 1 Steps 3-4).
- `AIChatScreen.FirstContactState` enum + `_firstContact` field defined Task 6 Step 1; referenced in `Init` (Step 2), `HandleFirstContactInput` (Step 3), and `OnSendClicked` (Step 4).
- `AIChatScreen.WrapAIReply` defined Task 7 Step 1; called from 9 sites in the same file (Tasks 7 Steps 2-6).

**Known minor imperfection** (called out in Task 7 Step 7's verify section): the very first AI bubble during first-contact uses the `Assistant:` prefix (because the AI hasn't been named yet at that point). After the player names it on step 4 of first-contact, all subsequent bubbles use the new name. Past bubbles are not retroactively re-rendered. This is acceptable per the design — the player will spend the rest of the game with their chosen name. If feedback later requests retroactive rename, it's a one-line change to `RestoreHistoryToUI` to re-wrap with the current name.

---

## Execution handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-23-ai-brain-profile-01-merge.md`.

Two execution options:

1. **Subagent-Driven (recommended)** — fresh subagent per task with two-stage review between tasks. Best for an 8-task plan touching multiple subsystems (save data, persona content, UX state machine, prefix injection); clean review gates catch any one task's mistake before it cascades into the next.
2. **Inline Execution** — execute tasks in this session using `superpowers:executing-plans`, batch execution with checkpoints for review.

Which approach?
