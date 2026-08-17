using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The cassette insert on the shuttle computer.
///
/// **DROP THIS ON YOUR OWN INSERT OBJECT.** No setup tool, no generated
/// geometry, no prefab patching — add the component to whatever mesh you built
/// on the console stand and it wires itself up. Everything it needs a position
/// for is relative to that object.
///
/// Look at it holding a blank tape → "Press F to insert blank cassette". Press
/// F and the blank leaves your hotbar, the real cassette model appears in the
/// air just in front of the insert, and slides in. Press F on a loaded slot and
/// it slides back out and returns to your hotbar.
///
/// ── ⚠️ WHY THE FIRST VERSION OF THIS DIDN'T WORK ─────────────────────────
/// It was built by an editor tool as an invisible object with only a TRIGGER
/// collider, and its `gazeTarget` pointed at ConsoleScreen. `InteractGaze` is a
/// crosshair SphereCast that IGNORES TRIGGERS and must hit geometry belonging
/// to the aim target — so looking at the slot on the stand meant looking at the
/// stand, not the screen, and the prompt never appeared.
///
/// Two rules came out of that, and both are load-bearing here:
///   • **NEVER set gazeTarget on this.** Left null, the gaze test falls through
///     to the object's own mesh silhouette, which is exactly the thing the
///     player is pointing at. If the object has no mesh at all it drops to a
///     tight cone toward its centre, which also works.
///   • **The spawned cassette is stripped of every collider** (see
///     CassetteVisual). A solid tape sitting in the slot would block the
///     crosshair cast to the insert behind it and break the eject prompt — the
///     original bug, one object further along.
///
/// The proximity trigger is added in Awake if the object doesn't already have
/// one, so a bare mesh works with nothing else attached.
/// </summary>
public class CassetteSlot : Interactable
{
    static readonly List<CassetteSlot> _all = new List<CassetteSlot>();

    GameObject _tape;
    Coroutine _slide;
    bool _animating;

    /// <summary>
    /// Is the player looking at a slot that would actually do something?
    ///
    /// ShuttleComputerTerminal asks this and STANDS DOWN when it's true. The
    /// insert sits directly under the console screen, and the screen's collider
    /// is far more forgiving than its size suggests (the crosshair cast is a
    /// 0.1 m sphere, plus a near-miss pass) — so without this rule both claim
    /// the prompt every frame and it flips between "use the computer" and
    /// "insert blank cassette" as you stand there.
    ///
    /// The small, specific control wins over the big panel behind it. Cached
    /// per frame because the terminal asks every Update and the gaze test casts.
    /// </summary>
    static int _gazedFrame = -1;
    static bool _gazedResult;

    public static bool AnyGazed
    {
        get
        {
            if (_gazedFrame == Time.frameCount) return _gazedResult;
            _gazedFrame = Time.frameCount;
            _gazedResult = false;

            for (int i = 0; i < _all.Count; i++)
            {
                CassetteSlot s = _all[i];
                if (s == null || !s.isActiveAndEnabled) continue;
                if (!s.playerInInteractionZone || !s.CanInteract()) continue;
                if (!InteractGaze.IsLookingAt(s)) continue;
                _gazedResult = true;
                break;
            }
            return _gazedResult;
        }
    }

    void Awake()
    {
        EnsureTriggerZone();

        // See the class note: pointing this at anything else is what broke the
        // first build. Enforced rather than documented, because it is invisible
        // in the Inspector until you are stood in front of a slot that ignores
        // you.
        gazeTarget = null;

        // Set in code, not as a serialized default: adding a defaulted field to
        // Interactable would silently switch the latch on for every interactable
        // in the game. Only the machine needs it — it is the only place a small
        // control sits inside a big one's forgiveness.
        if (gazeLatchSeconds <= 0f) gazeLatchSeconds = 0.25f;

        // Direct crosshair hit ONLY. The near-miss forgiveness pass let this
        // slot claim gaze while the player aimed at the console SCREEN above
        // it — and since the screen stands down whenever a slot is gazed
        // (CassetteSlot.AnyGazed), the slot stayed "selected" and the screen
        // became uninteractable (Sam's playtest).
        strictGaze = true;

        // Code-enforced like gazeTarget above: with this UNCHECKED in the
        // inspector, InteractGaze fails open and the slot counts as looked-at
        // from anywhere in the zone — which is exactly what kept it stuck
        // "selected" through three rounds of gaze fixes.
        requireGazeToInteract = true;
    }

    /// <summary>
    /// Interactable needs a trigger collider for its proximity zone. Sam's
    /// insert mesh may have a solid collider, no collider, or be part of the
    /// console's own mesh collider — so rather than assume, add a trigger
    /// sphere if there isn't already one.
    ///
    /// Sized in WORLD metres and divided back out of the object's scale: the
    /// console hierarchy is scaled, and a raw radius here would come out as
    /// either a pinhead or half the shuttle.
    /// </summary>
    void EnsureTriggerZone()
    {
        foreach (var c in GetComponentsInChildren<Collider>(true))
            if (c.isTrigger) return;

        var sphere = gameObject.AddComponent<SphereCollider>();
        sphere.isTrigger = true;

        Vector3 ls = transform.lossyScale;
        float scale = Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
        if (scale < 1e-4f) scale = 1f;
        sphere.radius = reachMetres / scale;
    }

    void OnEnable()
    {
        if (!_all.Contains(this)) _all.Add(this);
        CassetteDeck.OnChanged += OnDeckChanged;
        SnapToDeck();
    }

    void OnDisable()
    {
        _all.Remove(this);
        CassetteDeck.OnChanged -= OnDeckChanged;

        // Disabling stops the coroutine dead, so a print interrupted by the
        // shuttle being unloaded (or the object toggled off) would otherwise
        // leave _animating stuck true and the slot permanently unusable. The
        // deck state is authoritative; OnEnable's SnapToDeck rebuilds from it.
        _animating = false;
        _slide = null;
    }

    // ── prompt ───────────────────────────────────────────────────────────

    protected override bool CanInteract()
    {
        // While the computer is up it owns the input, and F is its close key —
        // the same reason ShuttleComputerTerminal stands down.
        if (ShuttleComputerUI.IsOpen) return false;
        if (_animating) return false;

        // A finished tape left sticking out (only happens when the hotbar was
        // full at the moment it printed) can be taken by hand.
        return CassetteDeck.HasEjected
            || CassetteDeck.HasCassette
            || CassetteDeck.HeldBlankTier() > 0;
    }

    protected override string BuildInteractMessage()
    {
        if (CassetteDeck.HasEjected)
            return "Press " + PromptGlyphs.Interact + " to take \"" +
                   TraxPrints.DisplayName(CassetteDeck.EjectedPrintId) + "\"";

        if (CassetteDeck.HasCassette)
            return "Press " + PromptGlyphs.Interact + " to take the blank back";

        int held = CassetteDeck.HeldBlankTier();
        return "Press " + PromptGlyphs.Interact + " to insert blank cassette" +
               (held >= 2 ? " II" : "");
    }

    protected override void Interact()
    {
        base.Interact();

        // The computer closes on F too. Without this, an F that closed the
        // screen would be read again here in the same frame.
        if (ShuttleComputerUI.FConsumedThisFrame) return;
        if (_animating) return;

        if (CassetteDeck.HasEjected)      TakePrinted();
        else if (CassetteDeck.HasCassette) StartSlide(SlideOut());
        else                               StartSlide(SlideIn());
    }

    /// Collect a printed tape that stayed in the mouth because there was no
    /// room for it when it came out.
    void TakePrinted()
    {
        string name = TraxPrints.DisplayName(CassetteDeck.EjectedPrintId);
        if (!CassetteDeck.TakeEjected())
        {
            StoryImpactNotice.Show("NO ROOM IN YOUR HOTBAR.", 2.5f);
            return;
        }
        _showingEjected = false;
        DestroyTape();
        StoryImpactNotice.Show(name.ToUpperInvariant() + " — TAKEN.", 2f);
    }

    void StartSlide(IEnumerator routine)
    {
        if (_slide != null) StopCoroutine(_slide);
        _slide = StartCoroutine(routine);
    }

    /// <summary>
    /// Keeps the tape's rotation on `tapeEuler` every frame, so the field can be
    /// dragged in the Inspector DURING PLAY and the cassette turns as you drag.
    ///
    /// Chains to base.Update, which is where Interactable does its prompt and
    /// interact handling — dropping that call would silently kill the F key.
    /// </summary>
    protected override void Update()
    {
        base.Update();

        // Rotation is re-asserted EVERY frame, including mid-slide. The slide
        // only ever writes localPosition, so this is free — and it means a
        // freshly instantiated tape cannot be left facing the wrong way by
        // anything that touches its transform before it settles. (It was: see
        // CassetteVisual.Strip on the deferred-destroy physics window.)
        if (_tape != null) _tape.transform.localRotation = Quaternion.Euler(tapeEuler);

        if (_animating) return;

        // And its POSITION, for the same reason. Whatever the tape's transform
        // is written by — a stray physics frame, an Instantiate that placed it
        // in world space, anything added to that prefab later — it is put back
        // where the slot says it belongs on the very next frame. A tape has
        // teleported into mid-air once; it cannot stay there now.
        if (_tape != null) _tape.transform.position = WorldFor(_tapeLocal);

        // SELF-HEAL. The deck is the truth; the prop is only a picture of it.
        // If a tape is waiting at the mouth, make sure there IS one and that it
        // is where it should be — so nothing that quietly drifts, culls or
        // destroys the prop can leave the player looking at an empty slot the
        // machine still thinks is full. That exact confusion cost a playtest.
        if (CassetteDeck.HasEjected && !_showingEjected) { SnapToDeck(); return; }
        if (CassetteDeck.HasEjected) EnsureTape(EjectRestLocal);
    }

    // ── the movement ─────────────────────────────────────────────────────

    /// <summary>
    /// A slot offset turned into a WORLD point, in true metres.
    ///
    /// ── Why not localPosition ────────────────────────────────────────────
    /// It was, and the offsets silently weren't metres. The tape hangs off a
    /// scale-cancelling anchor (CassetteVisual.EnsureAnchor) whose job is to
    /// make anchor-local == world metres, and on this console it doesn't
    /// manage it — `lossyScale` is only exact when no rotation sits between
    /// non-uniformly scaled parents, and the console chain has both. The result
    /// was an offset that came out several times smaller than asked for, so
    /// raising 0.06 to 0.12 to 0.18 all looked identical: a barely-visible
    /// nudge every time.
    ///
    /// `transform.rotation` carries NO scale, so this path cannot be shrunk by
    /// anything in the hierarchy. 0.18 is 18 centimetres, on any object, at any
    /// scale. The anchor still handles the tape's SIZE; it just no longer has
    /// any say over distances.
    /// </summary>
    Vector3 WorldFor(Vector3 offsetMetres)
    {
        return transform.position + transform.rotation * offsetMetres;
    }


    /// Where the tape rests once it is in. Defaults to the insert's own origin,
    /// so a slot mesh authored with its pivot at the mouth needs no tuning.
    Vector3 SeatedLocal => seatedOffset;

    /// Where it appears from — out in the air in front of the insert.
    Vector3 ApproachLocal => seatedOffset + approachOffset;

    /// <summary>
    /// Where a PRINTED tape comes to rest, poking out of the mouth.
    ///
    /// Deliberately NOT approachOffset. That one is the insert distance and is
    /// meant to be big — the tape spawns out in the air and flies in. Reusing it
    /// for the ejection threw the finished tape a quarter of a metre clear of
    /// the slot, which both looked wrong and buried it inside the console mesh,
    /// so it read as "the tape vanished" even though the machine still had it.
    ///
    /// This should be a few centimetres: enough that the tape is visibly
    /// half-out and grabbable, not enough to leave the machine.
    /// </summary>
    Vector3 EjectRestLocal
    {
        get
        {
            // Direction from the field; DISTANCE from the tape's own size. See
            // CassetteVisual.WorldLength for why metres are the wrong unit on
            // this console. Falls back to the raw offset only if there is no
            // tape to measure yet.
            float len = CassetteVisual.WorldLength(_tape);
            if (len <= 0f) return seatedOffset + ejectOffset;

            Vector3 dir = ejectOffset.sqrMagnitude > 1e-6f
                ? ejectOffset.normalized : Vector3.forward;
            return seatedOffset + dir * (len * ejectTapeLengths);
        }
    }

    /// <summary>
    /// Take the held blank, show it in the air, slide it home.
    ///
    /// THE DECK MOVES FIRST, then the animation follows. If Insert() is refused
    /// (nothing held, slot already full) nothing is spawned at all — the
    /// alternative is a cassette flying into a slot that then rejects it.
    /// </summary>
    IEnumerator SlideIn()
    {
        _animating = true;

        if (!CassetteDeck.Insert()) { _animating = false; yield break; }

        DestroyTape();
        _tape = CassetteVisual.Spawn(cassettePrefab, transform, tapeScale, tapeEuler);
        if (_tape != null)
        {
            yield return Slide(ApproachLocal, SeatedLocal);
        }

        _animating = false;
        SnapToDeck();
    }

    /// <summary>
    /// Slide it back out and into the player's hands.
    ///
    /// The deck is asked FIRST here too, because EjectBlank can legitimately
    /// fail — a full hotbar leaves the blank in the machine rather than
    /// destroying it, and animating an ejection that didn't happen would be a
    /// lie the player then can't undo.
    /// </summary>
    IEnumerator SlideOut()
    {
        _animating = true;

        if (!CassetteDeck.EjectBlank())
        {
            StoryImpactNotice.Show("NO ROOM IN YOUR HOTBAR FOR THE BLANK.", 2.5f);
            _animating = false;
            yield break;
        }

        if (_tape != null) yield return Slide(SeatedLocal, ApproachLocal);

        DestroyTape();
        _animating = false;
        SnapToDeck();
    }

    /// Smoothstepped so it eases into the slot instead of arriving at full
    /// speed — a cassette going in has weight to it.
    IEnumerator Slide(Vector3 from, Vector3 to)
    {
        if (_tape == null) yield break;

        float dur = Mathf.Max(0.01f, slideSeconds);
        float t = 0f;
        while (t < 1f && _tape != null)
        {
            // Unscaled: the pause menu and any timeScale work shouldn't freeze a
            // cassette halfway into the machine.
            t += Time.unscaledDeltaTime / dur;
            float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            // Both ends re-evaluated every frame, so a shuttle that moves (or a
            // floating-origin shift) carries the animation with it instead of
            // leaving the tape behind in world space.
            _tape.transform.position = Vector3.Lerp(WorldFor(from), WorldFor(to), e);
            yield return null;
        }

        _tapeLocal = to;
        if (_tape != null) _tape.transform.position = WorldFor(to);
    }

    /// <summary>
    /// THE PRINT COMING BACK OUT.
    ///
    /// The computer has already closed by the time this runs (ShuttleComputerUI
    /// closes before it touches the deck), so the player is looking at the
    /// machine and sees the tape emerge rather than finding it in their pocket
    /// afterwards.
    ///
    /// Three beats, in order:
    ///   1. A PAUSE with the tape still inside. This is the machine working, and
    ///      it is the whole reason the print reads as a physical event — an
    ///      instant ejection feels like a menu closing.
    ///   2. The tape slides out to the mouth.
    ///   3. It STAYS THERE. Nothing is collected automatically; the player has
    ///      to look at it and press F. That last beat is the point — the tape is
    ///      a thing in the world, and picking it up is a deliberate act.
    /// </summary>
    IEnumerator SlidePrintedOut()
    {
        _animating = true;
        _showingEjected = true;

        // Reuse the blank already sitting in the slot — it is the same physical
        // object, now with a song on it. It stays at the seated position (i.e.
        // inside the machine) for the whole delay.
        if (_tape == null)
            _tape = CassetteVisual.Spawn(cassettePrefab, transform, tapeScale, tapeEuler);
        _tapeLocal = SeatedLocal;
        if (_tape != null) _tape.transform.position = WorldFor(SeatedLocal);

        // Unscaled, like the slide: a pause menu mid-print shouldn't stall the
        // machine forever.
        float wait = Mathf.Max(0f, printDelaySeconds);
        for (float t = 0f; t < wait; t += Time.unscaledDeltaTime) yield return null;

        if (_tape != null)
        {
            // Tuned to 0.84 tape-lengths in play, 2026-08-14. The diagnostic
            // that got it there lives in CassetteVisual.LogOrientation — turn
            // that on if the geometry ever needs re-reading.
            yield return Slide(SeatedLocal, EjectRestLocal);
        }

        // Deliberately does NOT collect it. CanInteract/Interact take over from
        // here — the tape waits at the mouth until the player takes it.
        _animating = false;
    }

    // ── state sync ───────────────────────────────────────────────────────

    /// True while the visible tape is a finished print waiting at the mouth,
    /// rather than a blank seated inside.
    bool _showingEjected;

    /// The deck changed without this slot driving it. A fresh print gets the
    /// emerge animation; everything else — a save load, New Game — snaps,
    /// because there was no gesture to animate.
    void OnDeckChanged()
    {
        if (_animating) return;

        if (CassetteDeck.HasEjected && !_showingEjected)
        {
            StartSlide(SlidePrintedOut());
            return;
        }

        SnapToDeck();
    }

    void SnapToDeck()
    {
        // A printed tape waiting at the mouth wins over a seated blank: the
        // deck can never hold both, and this is the state a save restores into.
        if (CassetteDeck.HasEjected)
        {
            _showingEjected = true;
            EnsureTape(EjectRestLocal);
            return;
        }

        _showingEjected = false;
        if (!CassetteDeck.HasCassette) { DestroyTape(); return; }
        EnsureTape(SeatedLocal);
    }

    /// Where the tape is meant to be sitting right now, in anchor-local metres.
    /// Update pins the prop to this every frame once the slide has finished.
    Vector3 _tapeLocal;

    void EnsureTape(Vector3 localPos)
    {
        if (_tape == null)
            _tape = CassetteVisual.Spawn(cassettePrefab, transform, tapeScale, tapeEuler);
        _tapeLocal = localPos;
        if (_tape != null) _tape.transform.position = WorldFor(localPos);
    }

    void DestroyTape()
    {
        if (_tape == null) return;
        Destroy(_tape);
        _tape = null;
    }

#if UNITY_EDITOR
    /// Fills in the cassette model the moment the component is added, so
    /// dropping this on an object is genuinely the only step.
    void OnValidate()
    {
        if (cassettePrefab != null) return;
        cassettePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/1 - samsPrefabs/CasettePickup.prefab");
    }
#endif

    // -- appended; keep new fields at the END (serialization) --

    [Header("Cassette")]
    [Tooltip("The tape model. Auto-filled with CasettePickup.prefab when the component is added; every collider and script on it is stripped at spawn, so it is pure geometry.")]
    public GameObject cassettePrefab;

    [Tooltip("Extra scale on top of the prefab's own. 1 = the prefab's size.")]
    public float tapeScale = 1f;

    [Tooltip("Rotation of the tape in the slot, in degrees. Plain and direct - type 90 into an axis and the tape turns 90 degrees about it. Nothing is added to it or derived from it. Editable DURING PLAY: drag it while a tape is in the slot and it turns live, so you can find the right value in seconds.")]
    public Vector3 tapeEuler = new Vector3(90f, 0f, 0f);

    [Header("The slide")]
    [Tooltip("Where the tape ends up, in METRES along this object's own axes. Leave at zero if the insert's pivot is already at the slot mouth.\n\nMetres, not local units: the tape hangs off a scale-cancelling anchor, so a stretched insert box doesn't stretch the offsets (or the cassette).")]
    public Vector3 seatedOffset = Vector3.zero;

    [Tooltip("Where the tape appears before sliding in, in METRES from the seated position. This is the 'in front of the insert' offset — push it along whichever axis points OUT of your slot.")]
    public Vector3 approachOffset = new Vector3(0f, 0f, 0.6f);

    [Tooltip("How long the slide takes, in seconds.")]
    public float slideSeconds = 0.85f;

    [Tooltip("Pause after PRINT before the tape comes out, in seconds. This is the machine working - an instant ejection reads like a menu closing rather than a thing happening.")]
    public float printDelaySeconds = 2f;

    [Tooltip("How far the PRINTED tape pokes out of the mouth when it ejects, in metres along this object's axes. A few centimetres - just enough to see and grab. NOT approachOffset: that is the insert distance and is much larger, and reusing it throws the finished tape clear of the slot and inside the console mesh.")]
    public Vector3 ejectOffset = new Vector3(0f, 0f, 1f);

    [Tooltip("How far the printed tape pushes out of the mouth, measured in CASSETTE LENGTHS. 1 = exactly its own length (just fully clear), 1.3 = clear with room to grab. Metres are useless here: this console's scale chain multiplies down to about 0.03, so any value in metres is either invisible or absurd.")]
    public float ejectTapeLengths = 0.84f;

    [Header("Reach")]
    [Tooltip("How close the player must be for the prompt, in WORLD METRES. Only used if this object has no trigger collider of its own — one is added at that size on Awake.")]
    public float reachMetres = 2.6f;
}
