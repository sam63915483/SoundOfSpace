# B-1 "Routine Stop" — Interrogation Rewrite

**Handoff spec. This is a content + routing swap, not new systems.** Everything the
mission needs is already built. Do not rebuild the chase, the QTE, the pursuit AI,
the translator, or the physics.

---

## The design in one line

Every outcome now leads to the chase. The interrogation decides **how hard the chase
is**, not **whether it happens**.

Previously, three of four outcomes skipped the rocket finale entirely — meaning the
best content in the mission sat behind the choice a rational player won't make. Now
all six paths converge on `b1_run`, and the interrogation sets the corvette's starting
position instead of gating the content.

## The rule the player learns

**Officer Kolb punishes invention.**

| Player does | Result |
|---|---|
| Confesses (the incriminating truth) | ✅ pass |
| Admits ignorance ("I don't know") | ✅ pass |
| Invents a confident explanation | ❌ fail — and Kolb *says so* |
| Makes a joke | 😏 fail + burns the one warning |

Every question has exactly this four-way shape: two passing answers, one invention,
one joke. Tev states the rule out loud in the panic beat, so it is never guess-work.

Kolb is old, mean, and tired. He does not banter, he does not raise his voice, and he
never argues with a lie — he just says **"Mm."** and writes it down. That is what makes
him frightening.

---

## Route table

```
sass >= 2                → conv_b1_arrest    (he stopped asking. no fine, no choice.)
passes == 4              → conv_b1_clean     (no fine — but he wants to board.)
otherwise                → conv_b1_ticket    ($200: pay or refuse.)
```

A sarcastic answer never sets its `_pass` flag, so **one joke already costs the clean
run.** The "that's your one" line is literally true. No conditional logic is needed in
the dialogue engine — all branching lives in `VerdictRoutine`.

| Path | Money | Head start | Flags set |
|---|---|---|---|
| Truth 4/4 → "Yes, come aboard" | — | **Long** (5s) | `b1_run`, `b1_hs_long` |
| Truth 4/4 → "No" | — | Medium (3s) | `b1_run`, `b1_hs_med` |
| Truth 4/4 → "Go fuck yourself" | — | **None** | `b1_run`, `b1_attitude` |
| Failed → paid | −$200 | Medium (3s) | `b1_run`, `b1_pay`, `b1_hs_med` |
| Failed → refused | — | **None** | `b1_run` |
| Two jokes → arrest | — | **None** | `b1_run` |

Head start = how long the player flies free before `_cop.StartChase()` is called. The
politer you were, the further away Kolb is when Tev punches it. Paying $200 buys a real
tactical advantage (he's distracted logging the payment), which is what stops "pay" from
being a dominated choice.

---

## ⚠️ [CRITICAL] — the silent-failure trap

`TevSmugglingMission.FindClip(line, table, clips)` matches the **exact line string**
against `CopConvLines[]` / `TevConvLines[]` / `OfferLines[]` and returns the clip at the
**same index** in `copConvClips[]` / `trConvClips[]` / `trOfferClips[]`.

```csharp
static AudioClip FindClip(string line, string[] table, AudioClip[] clips)
{
    if (clips == null) return null;
    for (int i = 0; i < table.Length && i < clips.Length; i++)
        if (table[i] == line) return clips[i];
    return null;
}
```

**If a JSON line is not in the C# table, it plays with no voice AND `_convLineDelay`
stays at 0, so the typewriter silently falls back to default pacing.** No exception, no
console warning. It just goes quiet.

Three rules follow:

1. **Every voiced line in the JSON must appear byte-for-byte in the C# table.** Copy-paste
   the strings; do not retype them. Em-dashes (`—`), ellipses (`...`), and smart quotes
   must match exactly or the lookup fails.
2. **Table index N must line up with clip index N** in the inspector array. Adding a line
   in the middle of the table shifts every clip after it.
3. **Identical strings share a clip for free.** The four `warn_q*` nodes use identical
   officer lines, so they need only **one** set of recordings.

Speaker routing: `OnConvLine` checks `speaker.StartsWith("TEV")` against
`_speakerLabel.text`. Keep Tev's speaker as `"Tev - Translating"`. The officer's speaker
can be anything that doesn't start with "TEV" — `"Galactic Patrol"` is kept below, but
renaming it to `"Officer Kolb"` is safe.

---

## [EXISTS] — already built, do not touch

- `TevSmugglingMission.cs` — phase machine, `ReleaseToGravity`, `PullOverRoutine`,
  `TevRocketRoutine`, `TevCountdown` (hatch QTE), `CopBarkRoutine`, `TevCelebrate`,
  subtitle UI, QTE ring UI, babble + translator audio layering
- `CopShipController.cs` — fly-in, standoff, pursuit, blasts, `HoldFire`, `FireOneNow`,
  `StartChase(onEscaped, onCaught, onFleeDetected)`
- `CopEnergyBlast.cs`, `TevRocket.cs`, `TevScareTrigger.cs`
- `WorldDialogueUI` — node/response/`effects`/`SetFlag`, `OnLineShown`, `LineDelayOverride`
- `conv_b1_offer.json` — **unchanged**
- Existing audio: `b1_cop_*`, `b1_tr_*`, `b1_patrol_siren`, `b1_radar_ping`, `b1_taser_zap`,
  `b1_rocket_fire`, `b1_corvette_explosion`

---

## [AUTHOR] — dialogue

Four files. `[REUSE]` = the clip already exists and the string must stay byte-identical.
`[NEW]` = needs a TTS clip.

### `Assets/StreamingAssets/Story/conv_b1_stop.json` (replace)

```json
{
  "id": "conv_b1_stop",
  "nodes": [
    {
      "id": "open",
      "speaker": "Galactic Patrol",
      "lines": [
        "UNIDENTIFIED VESSEL. CUT YOUR THRUST.",
        "This is Galactic Patrol. Our radar operators recorded your vessel crossing this corridor significantly above the posted limit.",
        "We have reasonable suspicion of a speed violation. This is a routine check. Hold your position.",
        "Officer Kolb, badge four-one-one. Forty-one years on this corridor. I have heard every excuse a mouth can produce.",
        "Four questions. Tell me the truth, or tell me you don't know. Make something up and I will find something to charge you with. I always do."
      ],
      "responses": [
        { "buttonText": "(Reach for the radio.)", "nextNodeId": "panic" }
      ]
    },
    {
      "id": "panic",
      "speaker": "Tev - Translating",
      "lines": [
        "okay okay okay okay.",
        "Do NOT be weird. I need ninety seconds.",
        "And listen — he's a truth guy. I know the type. Tell him the truth, or tell him you don't know. Either one.",
        "Just do NOT make something up. And do NOT be funny."
      ],
      "responses": [
        { "buttonText": "(Deep breath. Answer the radio.)", "nextNodeId": "q1" }
      ]
    },

    {
      "id": "q1",
      "speaker": "Galactic Patrol",
      "lines": [
        "First question. Do you know how fast you were going?"
      ],
      "responses": [
        { "buttonText": "Fast. Too fast. I'm not going to pretend otherwise.", "nextNodeId": "q2", "effects": [ { "kind": "SetFlag", "strArg": "b1_q1_pass", "boolArg": true } ] },
        { "buttonText": "No. Honestly, I wasn't watching the gauge.", "nextNodeId": "q2", "effects": [ { "kind": "SetFlag", "strArg": "b1_q1_pass", "boolArg": true } ] },
        { "buttonText": "I was under the limit the whole way.", "nextNodeId": "q1_lie" },
        { "buttonText": "Faster than you, apparently.", "nextNodeId": "warn_q1", "effects": [ { "kind": "SetFlag", "strArg": "b1_sass_q1", "boolArg": true } ] }
      ]
    },
    {
      "id": "q1_lie",
      "speaker": "Galactic Patrol",
      "lines": [
        "Mm.",
        "...I'm writing that down."
      ],
      "responses": [
        { "buttonText": "(...)", "nextNodeId": "q2" }
      ]
    },

    {
      "id": "q2",
      "speaker": "Galactic Patrol",
      "lines": [
        "Question two. This vessel. Who does it belong to?"
      ],
      "responses": [
        { "buttonText": "It's his. All his. I'm just the driver.", "nextNodeId": "q2_true", "effects": [ { "kind": "SetFlag", "strArg": "b1_q2_pass", "boolArg": true } ] },
        { "buttonText": "My associate's. He can't fly — that's what I'm for.", "nextNodeId": "q2_true", "effects": [ { "kind": "SetFlag", "strArg": "b1_q2_pass", "boolArg": true } ] },
        { "buttonText": "Mine.", "nextNodeId": "q2_lie" },
        { "buttonText": "Finders keepers.", "nextNodeId": "warn_q2", "effects": [ { "kind": "SetFlag", "strArg": "b1_sass_q2", "boolArg": true } ] }
      ]
    },
    {
      "id": "q2_true",
      "speaker": "Galactic Patrol",
      "lines": [
        "Registered to a Tev. Flight licence revoked four years ago.",
        "...So you're the driver."
      ],
      "responses": [
        { "buttonText": "(...)", "nextNodeId": "q3" }
      ]
    },
    {
      "id": "q2_lie",
      "speaker": "Galactic Patrol",
      "lines": [
        "The registry says this hull belongs to someone named Tev.",
        "Mm. ...Writing that down."
      ],
      "responses": [
        { "buttonText": "(...)", "nextNodeId": "q3" }
      ]
    },

    {
      "id": "q3",
      "speaker": "Galactic Patrol",
      "lines": [
        "State your business on Fiery Twin."
      ],
      "responses": [
        { "buttonText": "Cargo run. I'm delivering something for my associate.", "nextNodeId": "q4", "effects": [ { "kind": "SetFlag", "strArg": "b1_q3_pass", "boolArg": true } ] },
        { "buttonText": "Honestly? He asked, I said yes, I didn't ask what for.", "nextNodeId": "q4", "effects": [ { "kind": "SetFlag", "strArg": "b1_q3_pass", "boolArg": true } ] },
        { "buttonText": "Visiting family.", "nextNodeId": "q3_lie" },
        { "buttonText": "Tourism. We heard the sunrise is lethal and wanted to see it.", "nextNodeId": "warn_q3", "effects": [ { "kind": "SetFlag", "strArg": "b1_sass_q3", "boolArg": true } ] }
      ]
    },
    {
      "id": "q3_lie",
      "speaker": "Galactic Patrol",
      "lines": [
        "Family.",
        "Son, the surface of Fiery Twin peels paint off a hull. Nobody has family there.",
        "Mm."
      ],
      "responses": [
        { "buttonText": "(...)", "nextNodeId": "q4" }
      ]
    },

    {
      "id": "q4",
      "speaker": "Galactic Patrol",
      "lines": [
        "...",
        "What is that sound?"
      ],
      "responses": [
        { "buttonText": "That's my associate. He's panicking.", "nextNodeId": "q4_true", "effects": [ { "kind": "SetFlag", "strArg": "b1_q4_pass", "boolArg": true } ] },
        { "buttonText": "I don't hear anything.", "nextNodeId": "q4_static", "effects": [ { "kind": "SetFlag", "strArg": "b1_q4_pass", "boolArg": true } ] },
        { "buttonText": "Hull tick. She does that when she's cold.", "nextNodeId": "q4_lie" },
        { "buttonText": "The ship is haunted.", "nextNodeId": "warn_q4", "effects": [ { "kind": "SetFlag", "strArg": "b1_sass_q4", "boolArg": true } ] }
      ]
    },
    {
      "id": "q4_true",
      "speaker": "Galactic Patrol",
      "lines": [
        "...Panicking.",
        "Huh. At least that's an honest answer."
      ],
      "responses": [
        { "buttonText": "(...)", "nextNodeId": "done" }
      ]
    },
    {
      "id": "q4_static",
      "speaker": "Galactic Patrol",
      "lines": [
        "...Hm.",
        "Could be my end. This channel's been garbage all week.",
        "Forget it."
      ],
      "responses": [
        { "buttonText": "(...)", "nextNodeId": "done" }
      ]
    },
    {
      "id": "q4_lie",
      "speaker": "Galactic Patrol",
      "lines": [
        "That's not a hull tick.",
        "I've flown hulls for forty-one years. That is a voice.",
        "Mm."
      ],
      "responses": [
        { "buttonText": "(...)", "nextNodeId": "done" }
      ]
    },

    {
      "id": "warn_q1",
      "speaker": "Galactic Patrol",
      "lines": [
        "Stop.",
        "You think you're funny. Every one of you thinks you're funny. Forty-one years, and not one of you has been.",
        "That's your one. Try it again and I stop asking questions and start filling out forms.",
        "Answer the rest of them straight."
      ],
      "responses": [
        { "buttonText": "(Answer straight.)", "nextNodeId": "q2" }
      ]
    },
    {
      "id": "warn_q2",
      "speaker": "Galactic Patrol",
      "lines": [
        "Stop.",
        "You think you're funny. Every one of you thinks you're funny. Forty-one years, and not one of you has been.",
        "That's your one. Try it again and I stop asking questions and start filling out forms.",
        "Answer the rest of them straight."
      ],
      "responses": [
        { "buttonText": "(Answer straight.)", "nextNodeId": "q3" }
      ]
    },
    {
      "id": "warn_q3",
      "speaker": "Galactic Patrol",
      "lines": [
        "Stop.",
        "You think you're funny. Every one of you thinks you're funny. Forty-one years, and not one of you has been.",
        "That's your one. Try it again and I stop asking questions and start filling out forms.",
        "Answer the rest of them straight."
      ],
      "responses": [
        { "buttonText": "(Answer straight.)", "nextNodeId": "q4" }
      ]
    },
    {
      "id": "warn_q4",
      "speaker": "Galactic Patrol",
      "lines": [
        "Stop.",
        "You think you're funny. Every one of you thinks you're funny. Forty-one years, and not one of you has been.",
        "That's your one. Try it again and I stop asking questions and start filling out forms.",
        "Answer the rest of them straight."
      ],
      "responses": [
        { "buttonText": "(Answer straight.)", "nextNodeId": "done" }
      ]
    },

    {
      "id": "done",
      "speaker": "Galactic Patrol",
      "lines": [
        "Hold position. Running your answers through central.",
        "..."
      ],
      "responses": [
        { "buttonText": "(Wait.)", "nextNodeId": "end", "effects": [ { "kind": "SetFlag", "strArg": "b1_interrogation_done", "boolArg": true } ] }
      ]
    }
  ]
}
```

**The identical `warn_q*` text is deliberate.** Kolb gives you the exact same canned
speech word-for-word every time, because he has given it ten thousand times. It is
funnier the second time, it is truer to the character, it needs zero conditional logic,
and it shares one set of clips.

### `Assets/StreamingAssets/Story/conv_b1_clean.json` (new — replaces `conv_b1_free.json`)

```json
{
  "id": "conv_b1_clean",
  "nodes": [
    {
      "id": "open",
      "speaker": "Galactic Patrol",
      "lines": [
        "Your story checks out. Somehow.",
        "All four. First time this month.",
        "No citation. Consider the speed a warning.",
        "...",
        "Which leaves one small thing.",
        "If you're doing nothing wrong, you won't mind me boarding your vessel. Just to confirm you're good to go."
      ],
      "responses": [
        { "buttonText": "Yes. Come aboard.", "nextNodeId": "yes_cop" },
        { "buttonText": "No.", "nextNodeId": "no_cop" },
        { "buttonText": "Go fuck yourself.", "nextNodeId": "gfy_cop", "effects": [ { "kind": "SetFlag", "strArg": "b1_attitude", "boolArg": true } ] }
      ]
    },

    {
      "id": "yes_cop",
      "speaker": "Galactic Patrol",
      "lines": [
        "Good. Stand by. I'm bringing my corvette across."
      ],
      "responses": [
        { "buttonText": "(...)", "nextNodeId": "yes_tev" }
      ]
    },
    {
      "id": "yes_tev",
      "speaker": "Tev - Translating",
      "lines": [
        "NO. NO NO NO.",
        "HE CANNOT COME ABOARD. HE CANNOT COME ABOARD—",
        "START THE ENGINE. START IT RIGHT NOW—"
      ],
      "responses": [
        { "buttonText": "(Start the engine.)", "nextNodeId": "end", "effects": [ { "kind": "SetFlag", "strArg": "b1_run", "boolArg": true }, { "kind": "SetFlag", "strArg": "b1_hs_long", "boolArg": true } ] }
      ]
    },

    {
      "id": "no_cop",
      "speaker": "Galactic Patrol",
      "lines": [
        "No.",
        "Hm. That's fine. That's completely fine.",
        "I'll run a full-spectrum scan from right here instead. Sit tight — takes about thirty seconds."
      ],
      "responses": [
        { "buttonText": "(...)", "nextNodeId": "no_tev" }
      ]
    },
    {
      "id": "no_tev",
      "speaker": "Tev - Translating",
      "lines": [
        "THIRTY SEC— NO. NO NO NO.",
        "GO. GO GO GO GO—"
      ],
      "responses": [
        { "buttonText": "(GO.)", "nextNodeId": "end", "effects": [ { "kind": "SetFlag", "strArg": "b1_run", "boolArg": true }, { "kind": "SetFlag", "strArg": "b1_hs_med", "boolArg": true } ] }
      ]
    },

    {
      "id": "gfy_cop",
      "speaker": "Galactic Patrol",
      "lines": [
        "...",
        "...Say again?"
      ],
      "responses": [
        { "buttonText": "(...)", "nextNodeId": "gfy_tev" }
      ]
    },
    {
      "id": "gfy_tev",
      "speaker": "Tev - Translating",
      "lines": [
        "HA! Nice one!",
        "Better have the piloting skills to back that attitude up—",
        "GO! GO!"
      ],
      "responses": [
        { "buttonText": "(Punch it.)", "nextNodeId": "end", "effects": [ { "kind": "SetFlag", "strArg": "b1_run", "boolArg": true } ] }
      ]
    }
  ]
}
```

### `Assets/StreamingAssets/Story/conv_b1_ticket.json` (replace)

```json
{
  "id": "conv_b1_ticket",
  "nodes": [
    {
      "id": "open",
      "speaker": "Galactic Patrol",
      "lines": [
        "Your answers were... partially satisfactory.",
        "I'm not going to tell you which ones. You know which ones.",
        "Citation issued: one (1) count of corridor speeding.",
        "The fine is $200. Payable immediately."
      ],
      "responses": [
        { "buttonText": "Pay the $200.", "nextNodeId": "paid_cop", "effects": [ { "kind": "SetFlag", "strArg": "b1_pay", "boolArg": true } ] },
        { "buttonText": "I'm not paying that.", "nextNodeId": "refused_cop", "effects": [ { "kind": "SetFlag", "strArg": "b1_run", "boolArg": true } ] }
      ]
    },

    {
      "id": "paid_cop",
      "speaker": "Galactic Patrol",
      "lines": [
        "...Received.",
        "Now. While I've got you. Standard procedure on a paid citation — I need to log your cargo manifest.",
        "Open the hold."
      ],
      "responses": [
        { "buttonText": "(...)", "nextNodeId": "paid_tev" }
      ]
    },
    {
      "id": "paid_tev",
      "speaker": "Tev - Translating",
      "lines": [
        "WHAT.",
        "He TOOK the money. He took the money and he's STILL—",
        "GO! GO NOW!"
      ],
      "responses": [
        { "buttonText": "(Punch it.)", "nextNodeId": "end", "effects": [ { "kind": "SetFlag", "strArg": "b1_run", "boolArg": true }, { "kind": "SetFlag", "strArg": "b1_hs_med", "boolArg": true } ] }
      ]
    },

    {
      "id": "refused_cop",
      "speaker": "Galactic Patrol",
      "lines": [
        "...Refusing to pay a lawful citation.",
        "Huh. Well. That's obstruction.",
        "And obstruction means I can legally search your vessel. OPEN UP."
      ],
      "responses": [
        { "buttonText": "(...)", "nextNodeId": "refused_tev" }
      ]
    },
    {
      "id": "refused_tev",
      "speaker": "Tev - Translating",
      "lines": [
        "okay okay okay — let's get the FUCK out of here—",
        "GO!"
      ],
      "responses": [
        { "buttonText": "(Punch it.)", "nextNodeId": "end" }
      ]
    }
  ]
}
```

### `Assets/StreamingAssets/Story/conv_b1_arrest.json` (new)

```json
{
  "id": "conv_b1_arrest",
  "nodes": [
    {
      "id": "open",
      "speaker": "Galactic Patrol",
      "lines": [
        "Central's back. Doesn't matter.",
        "I told you what would happen.",
        "I'm not writing you a ticket. I'm boarding your vessel and taking you in. And then I am going to take my time with the paperwork.",
        "Cut your engine and open the hold."
      ],
      "responses": [
        { "buttonText": "(...)", "nextNodeId": "arrest_tev" }
      ]
    },
    {
      "id": "arrest_tev",
      "speaker": "Tev - Translating",
      "lines": [
        "...what did you SAY to him.",
        "WHAT DID YOU SAY TO HIM—",
        "GO!! GO!!"
      ],
      "responses": [
        { "buttonText": "(Punch it.)", "nextNodeId": "end", "effects": [ { "kind": "SetFlag", "strArg": "b1_run", "boolArg": true } ] }
      ]
    }
  ]
}
```

### Delete

- `Assets/StreamingAssets/Story/conv_b1_free.json` (+ `.meta`)

---

## [BUILD] — code changes to `TevSmugglingMission.cs`

### 1. Phase enum

```csharp
public enum Phase { Idle, EnRoute, PullOver, Interrogation, Verdict, Confrontation, TicketChoice, Chase, Delivering, Done }
```

`AwaitRelease` is now dead — no path releases the player. Remove it and remove `Release()`,
or leave both in place for a future mission. `Phase.Delivering` is now reached only via
`onEscaped`.

### 2. Head-start tuning fields

```csharp
[Header("Chase — head start")]
[Tooltip("Seconds the player flies free before the corvette begins pursuit. Set by how the traffic stop ended.")]
public float headStartLong = 5f;    // he was mid-docking-approach — most committed, slowest to abort
public float headStartMedium = 3f;  // he was charging a scanner / logging a payment
```

### 3. `VerdictRoutine` — three-way route

```csharp
IEnumerator VerdictRoutine()
{
    _busy = true;
    _phase = Phase.Verdict;
    yield return new WaitForSeconds(1.2f);   // "running your answers through central"

    int passes = 0;
    if (Flag("b1_q1_pass")) passes++;
    if (Flag("b1_q2_pass")) passes++;
    if (Flag("b1_q3_pass")) passes++;
    if (Flag("b1_q4_pass")) passes++;

    int sass = 0;
    if (Flag("b1_sass_q1")) sass++;
    if (Flag("b1_sass_q2")) sass++;
    if (Flag("b1_sass_q3")) sass++;
    if (Flag("b1_sass_q4")) sass++;

    StartCoroutine(ConvVoiceover());

    if (sass >= 2)
    {
        // He warned you. He does not warn twice.
        WorldDialogueUI.Begin("conv_b1_arrest");
        _phase = Phase.Confrontation;
    }
    else if (passes >= 4)
    {
        // Clean run: no fine — but he still wants inside the hold.
        WorldDialogueUI.Begin("conv_b1_clean");
        _phase = Phase.Confrontation;
    }
    else
    {
        WorldDialogueUI.Begin("conv_b1_ticket");
        _phase = Phase.TicketChoice;
    }

    _busy = false;
}
```

### 4. `Update` — watch both phases for `b1_run`

Wherever `Phase.TicketChoice` currently watches for `b1_pay` / `b1_run`, `Phase.Confrontation`
must watch for `b1_run` the same way. Both phases converge on `StartChase()`.

`b1_pay` keeps its existing `SpendMoney(200)` call. Note the paid path *also* sets `b1_run`
one node later, so paying no longer ends the stop — see [OPEN] for the broke-player case.

### 5. `StartChase` — honour the head start

```csharp
void StartChase()
{
    if (_ship != null) _ship.canFly = true;
    _decelActive = false;
    _anchorBody = null;
    _phase = Phase.Chase;

    if (_cop == null) { _phase = Phase.Delivering; return; }

    float headStart = Flag("b1_hs_long") ? headStartLong
                    : Flag("b1_hs_med")  ? headStartMedium
                    : 0f;

    StartCoroutine(ChaseRoutine(headStart));
}

IEnumerator ChaseRoutine(float headStart)
{
    // The player flies free while Kolb is still docking / scanning / logging the
    // payment. Zero head start = he was already hot and comes straight after you.
    if (headStart > 0f) yield return new WaitForSeconds(headStart);

    // This chase is scripted: you can't out-RANGE the corvette and it never runs
    // dry — it ends when Tev's rocket connects (or you eat 3 hits).
    _cop.escapeDistance = 999999f;
    _cop.maxBlasts = 999;

    _cop.StartChase(
        onEscaped: () => { SetFlag("b1_outlaw", true); _phase = Phase.Delivering; },
        onCaught:  () => { if (ResourceManager.Instance != null) ResourceManager.Instance.TakeDamage(99999f); },
        onFleeDetected: () =>
        {
            StartCoroutine(TevRocketRoutine());
            StartCoroutine(CopBarkRoutine(Time.time));
        });

    _cop.onBlastFired = () =>
    {
        if (_subtitleCo != null || _countdownActive) return;
        int i = UnityEngine.Random.Range(0, BlastWarnings.Length);
        ShowTevLine(BlastWarnings[i],
            trWarningClips != null && i < trWarningClips.Length ? trWarningClips[i] : null);
    };
}
```

### 6. Optional but recommended — a binding validator (~20 lines)

Because the exact-string lookup fails silently, add an editor-only check that walks every
`conv_b1_*.json`, and for each line whose speaker resolves to a voiced table, logs a warning
if that exact string is absent from `CopConvLines` / `TevConvLines` / `OfferLines`, or if the
matching clip index is null. This catches every future typo-kills-the-voice bug in one pass.

---

## [INTEGRATE] — the tables and the clips

**This is the actual work.** The code above is maybe 40 minutes. The audio is the rest.

### `CopConvLines[]`

**Keep (clips already exist — do not alter these strings):**
1. `UNIDENTIFIED VESSEL. CUT YOUR THRUST.`
2. `This is Galactic Patrol. Our radar operators recorded your vessel crossing this corridor significantly above the posted limit.`
3. `We have reasonable suspicion of a speed violation. This is a routine check. Hold your position.`
4. `First question. Do you know how fast you were going?`
5. `State your business on Fiery Twin.`
6. `What is that sound?`
7. `Hold position. Running your answers through central.`
8. `Your story checks out. Somehow.`
9. `Your answers were... partially satisfactory.`
10. `Citation issued: one (1) count of corridor speeding.`
11. `The fine is $200. Payable immediately.`

**Remove (dead — their clips are freed):**
- `Do you know what the speed limit is in this corridor?`
- `Maintain corridor speed. Have a boring day.`

**Add:** every remaining officer line in the four JSON files above (~45 strings). The four
`warn_q*` nodes are identical, so their four lines are added **once** and matched by all four
nodes automatically.

### `TevConvLines[]`

**Keep:** `okay okay okay okay.` and `Do NOT be weird. I need ninety seconds.`

**Add:** every remaining Tev line in `conv_b1_stop` (panic), `conv_b1_clean`, `conv_b1_ticket`,
and `conv_b1_arrest` (~18 strings).

### Inspector arrays

`copConvClips[]` and `trConvClips[]` must be resized and populated so that **index N matches
table index N**. Batch-generate the new clips through the same TTS pipeline that produced the
existing `b1_cop_*` / `b1_tr_*` files, keeping the naming convention.

Tev's lines run through the suit translator (flat machine TTS over the alien babble), so they
use the same voice as the existing `b1_tr_*` set — not a new one.

---

## [TEST] — six paths, all must reach the rocket

| # | Path | Expect |
|---|---|---|
| 1 | Answer all four honestly → "Yes, come aboard" | no fine, Kolb announces the crossing, Tev panics, **5s** of free flight, then pursuit |
| 2 | Answer all four honestly → "No" | scanner line, Tev panics, **3s** free, then pursuit |
| 3 | Answer all four honestly → "Go fuck yourself" | Kolb's "Say again?", Tev's "Nice one!", **immediate** pursuit, `b1_attitude` set |
| 4 | Lie on one question → pay the $200 | −$200, Kolb demands the hold anyway, **3s** free, then pursuit |
| 5 | Lie on one question → refuse | obstruction line, Tev's "let's get the FUCK out of here", **immediate** pursuit |
| 6 | Joke twice | `conv_b1_arrest`, no ticket offered, **immediate** pursuit |

Also check:
- One joke, then honest on the rest → still a ticket (a joke never sets `_pass`), warn node fires once.
- **Every officer and Tev line has voice.** A silent line means its string is missing from the
  table, or the clip index is off by one. Watch the console with the validator on.
- The hatch QTE, `HoldFire` cease-fire, early-press penalty, and `TevCelebrate` are untouched and
  must still fire on all six paths.

---

## [OPEN] — decisions for Sam

1. **Broke player pays the fine.** `SpendMoney(200)` can fail. Currently that routes to the chase.
   Cheapest fix: if the spend fails, don't set `b1_hs_med` — the payment bounces, Kolb notices, and
   you get no head start. If you want a beat for it, that's one new node in `conv_b1_ticket`.
2. **Speaker label.** Kept as `"Galactic Patrol"` so nothing changes. Renaming to `"Officer Kolb"`
   is safe (only the `"TEV"` prefix is load-bearing) — your call which reads better on the subtitle.
3. **`b1_attitude` has no payoff yet.** It's set when you tell Kolb to go fuck himself. Obvious use:
   an extra Tev line at the Fiery Twin payout, or a small bonus. Currently dangling.
4. **Peak-speed sampling.** One float in `Update` during `EnRoute` would let Kolb quote your real
   number in the opening hail. Not required — the rewrite works without it — but Q1 lands much
   harder when the number is real, and it fixes the fact that the stop currently fires on trip
   distance regardless of how fast you actually flew.
5. **Tev's revoked licence** now appears in-fiction for the first time (`q2_true`). Confirm that's
   where you want it revealed, and that "four years ago" fits the timeline.
