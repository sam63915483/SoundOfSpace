using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Owns the character list and which character you are right now.
///
/// ── Why it mirrors MultiplayerSession, not the gameplay singletons ───────
/// Every OTHER auto-singleton in this project early-returns on MainMenu and is
/// therefore re-seeded in MainMenuController.EnsureGameplaySingletons (CLAUDE.md
/// trap #1 — the two-day torch-flicker bug). This one does NOT skip MainMenu,
/// for the same reason MultiplayerSession does not: the menu is where it does
/// its job. Because it never skips, it also never needs seeding — the trap is
/// avoided by not stepping into it rather than by patching around it.
///
/// It is NOT gated behind FeatureVault.Multiplayer. Solo play uses the character
/// too (it is your name and your suit); only the network sync of that identity
/// sits behind the multiplayer gate.
///
/// ── Persistence ──────────────────────────────────────────────────────────
/// One JSON file at `Application.persistentDataPath/characters.json`, beside the
/// `saves/` folder rather than inside it. Written on every mutation — the file
/// is a few hundred bytes and losing a character to an unsaved edit would be far
/// worse than the write cost.
/// </summary>
public class CharacterStore : MonoBehaviour
{
    public static CharacterStore Instance { get; private set; }

    const string FileName = "characters.json";

    CharacterBook _book = new CharacterBook();
    bool _loaded;

    /// Raised whenever the list or the selection changes, so open UI can redraw
    /// without polling.
    public event Action Changed;

    // ── static, null-safe accessors ──────────────────────────────────────
    // Callers outside the menu (NameStore, NetworkPlayerIdentity) may run in a
    // scene where the singleton has not been created yet — in the Editor, or in
    // a stripped test scene. These never throw and never force creation.

    /// The character you are playing as, or null if none exists yet.
    public static CharacterProfile ActiveProfile
        => Instance != null ? Instance.Active : null;

    public static string ActiveName
    {
        get { var p = ActiveProfile; return p != null ? p.name : ""; }
    }

    public static int ActiveSwatch
    {
        get { var p = ActiveProfile; return p != null ? SuitPalette.Clamp(p.swatchIndex) : 0; }
    }

    // ── instance surface ─────────────────────────────────────────────────

    public IReadOnlyList<CharacterProfile> All
    {
        get { EnsureLoaded(); return _book.characters; }
    }

    public bool HasAny
    {
        get { EnsureLoaded(); return _book.characters.Count > 0; }
    }

    /// The remembered character. Falls back to the first in the list if the
    /// remembered id is stale (deleted on another machine, hand-edited file),
    /// so this is only ever null when there are genuinely zero characters.
    public CharacterProfile Active
    {
        get
        {
            EnsureLoaded();
            if (_book.characters.Count == 0) return null;
            var byId = Find(_book.lastSelectedId);
            if (byId != null) return byId;
            // Self-heal a stale pointer rather than nagging the player.
            _book.lastSelectedId = _book.characters[0].id;
            Save();
            return _book.characters[0];
        }
    }

    // ── lifecycle ────────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        // See the class comment: deliberately does NOT skip MainMenu, and
        // therefore deliberately is NOT in EnsureGameplaySingletons.
        var go = new GameObject("CharacterStore");
        DontDestroyOnLoad(go);
        go.AddComponent<CharacterStore>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        EnsureLoaded();
        SceneManager.sceneLoaded += OnSceneLoaded;

        // AutoCreate runs on AfterSceneLoad, i.e. AFTER sceneLoaded has already
        // fired for the scene we were born into. Pressing Play directly in the
        // gameplay scene — the normal Editor workflow, and the source of CLAUDE.md
        // trap #1 — would therefore never tint the local player. Catch that case
        // here; the booted-from-MainMenu path is handled by OnSceneLoaded.
        if (SceneManager.GetActiveScene().name != "MainMenu")
            StartCoroutine(TintLocalPlayerWhenReady());
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // ── applying the character to the world ──────────────────────────────

    /// On entering gameplay, paint the local player's suit.
    ///
    /// The NAME needs no work here: NameStore.ResolvedPlayerName reads the
    /// active character directly, which sidesteps SaveCollector's apply order
    /// entirely (a save loading its own stale playerName can no longer stomp it).
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "MainMenu") return;
        StopAllCoroutines();
        StartCoroutine(TintLocalPlayerWhenReady());
    }

    /// The player rig is not guaranteed to exist on the first frame of the
    /// gameplay scene, and on a guest it is repositioned by SecondPlayerArrival
    /// well after load. Throttled retries rather than a per-frame search — the
    /// LightLookAt pattern for "may never appear" targets.
    IEnumerator TintLocalPlayerWhenReady()
    {
        const int MaxAttempts = 20;
        var wait = new WaitForSeconds(0.25f);

        for (int i = 0; i < MaxAttempts; i++)
        {
            var profile = Active;
            if (profile != null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    SuitTinter.Apply(player.transform, profile.swatchIndex);
                    yield break;
                }
            }
            yield return wait;
        }
        // Falling out is fine and silent — a scene with no tagged player (a
        // cutscene, the backrooms) simply has no suit to paint.
    }

    // ── mutations ────────────────────────────────────────────────────────

    public CharacterProfile Create(string rawName, int swatchIndex)
    {
        EnsureLoaded();
        string clean = CharacterProfile.Sanitize(rawName);
        if (string.IsNullOrEmpty(clean)) return null;   // caller validates first

        var p = CharacterProfile.Create(clean, swatchIndex);
        _book.characters.Add(p);
        // A newly made character is obviously the one you want to play.
        _book.lastSelectedId = p.id;
        Save();
        return p;
    }

    /// Rename + recolour in one call — the edit screen changes both at once.
    /// Returns false if the name was empty after trimming.
    public bool Edit(string id, string rawName, int swatchIndex)
    {
        EnsureLoaded();
        var p = Find(id);
        if (p == null) return false;

        string clean = CharacterProfile.Sanitize(rawName);
        if (string.IsNullOrEmpty(clean)) return false;

        p.name = clean;
        p.swatchIndex = SuitPalette.Clamp(swatchIndex);
        Save();
        return true;
    }

    public void Delete(string id)
    {
        EnsureLoaded();
        int idx = _book.characters.FindIndex(c => c != null && c.id == id);
        if (idx < 0) return;

        _book.characters.RemoveAt(idx);

        // Deleting the character you were playing as must leave a valid
        // selection, or the menu would launch with a null identity.
        if (_book.lastSelectedId == id)
            _book.lastSelectedId = _book.characters.Count > 0 ? _book.characters[0].id : "";

        Save();
    }

    public void Select(string id)
    {
        EnsureLoaded();
        if (Find(id) == null) return;
        if (_book.lastSelectedId == id) return;
        _book.lastSelectedId = id;
        Save();
    }

    public CharacterProfile Find(string id)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < _book.characters.Count; i++)
            if (_book.characters[i] != null && _book.characters[i].id == id)
                return _book.characters[i];
        return null;
    }

    // ── disk ─────────────────────────────────────────────────────────────

    public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

    void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;   // set FIRST — a failed load must not retry every call

        try
        {
            string path = FilePath;
            if (!File.Exists(path)) { _book = new CharacterBook(); return; }

            string json = File.ReadAllText(path);
            var book = JsonUtility.FromJson<CharacterBook>(json);
            _book = book ?? new CharacterBook();
            if (_book.characters == null) _book.characters = new List<CharacterProfile>();
            Normalise();
        }
        catch (Exception e)
        {
            // A corrupt characters.json must not brick the main menu. Start
            // empty; the file is overwritten on the next mutation.
            Debug.LogError($"[CharacterStore] Couldn't read {FilePath}: {e.Message}");
            _book = new CharacterBook();
        }
    }

    /// Repairs anything the file could plausibly contain: a hand-edited entry,
    /// a character written by a future build with a bigger palette, a null row.
    void Normalise()
    {
        for (int i = _book.characters.Count - 1; i >= 0; i--)
        {
            var c = _book.characters[i];
            if (c == null) { _book.characters.RemoveAt(i); continue; }

            if (string.IsNullOrEmpty(c.id)) c.id = Guid.NewGuid().ToString("N");
            c.name = CharacterProfile.Sanitize(c.name);
            if (string.IsNullOrEmpty(c.name)) c.name = "Colonist";
            c.swatchIndex = SuitPalette.Clamp(c.swatchIndex);
            if (string.IsNullOrEmpty(c.createdAt)) c.createdAt = DateTime.UtcNow.ToString("o");

            Migrate(c);
        }
    }

    /// Version-gated upgrades. Nothing to do at v1 — this exists so the first
    /// person to add `level` to CharacterProfile has an obvious place to put the
    /// "old file, field absent" handling instead of scattering null checks.
    static void Migrate(CharacterProfile c)
    {
        if (c.schemaVersion < 1) c.schemaVersion = 1;
        // if (c.schemaVersion < 2) { ...defaults for the v2 fields...; c.schemaVersion = 2; }
        c.schemaVersion = CharacterProfile.CurrentSchemaVersion;
    }

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
}
