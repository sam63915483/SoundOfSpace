using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Damage handler for alien NPCs (both spawner-instantiated and pre-placed in
// the scene). Mirrors EnemyController's IDamageable surface (TakeDamage +
// ApplyKnockback) so PistolController and AxeController can hit it through
// the same code path. On death, hands off to AlienRagdollBuilder for full
// bone-physics ragdoll.
//
// SpawnedAlienNPC marker is OPTIONAL — present on streamed aliens, absent on
// hand-placed ones (Alien3/4/6/7 in the scene). Code paths null-check the
// marker so both shapes work.
public class AlienNPCDamageable : MonoBehaviour, IDamageable
{
    [Header("Health")]
    public float maxHealth = 100f;

    [Header("Story Impact (pre-placed NPCs)")]
    [Tooltip("Tick this on hand-placed scene NPCs that the player isn't supposed to kill freely. On death, the corpse still ragdolls but a notification banner pops on the top-centre of the screen.")]
    public bool isStoryImpactful = false;
    [TextArea(2, 4)]
    public string storyImpactMessage = "*this impacts the story, revert to last save or live with consequences*";
    [Tooltip("Seconds the story-impact banner stays on screen.")]
    public float storyImpactDuration = 7f;

    [Header("Hit & Ragdoll Audio")]
    [Tooltip("Played each time the alien takes non-fatal damage (bullet/axe). Assign on AlienNPCSpawner — runtime-spawned aliens copy from there.")]
    public AudioClip hitSound;
    [Range(0f, 1f)] public float hitVolume = 0.7f;
    [Tooltip("Played once when the alien is killed and ragdolls. Assign on AlienNPCSpawner.")]
    public AudioClip deathSound;
    [Range(0f, 1f)] public float deathVolume = 0.8f;

    [Header("Health Bar Position")]
    [Tooltip("World-space metres above the alien's root (its feet) where the bar sits. Constant for every alien regardless of random scale, so the bar appears at a consistent height.")]
    public float healthBarWorldHeight = 2.5f;
    [Tooltip("Approximate world-space width of the bar. Counter-scaled against the alien's random root scale so it stays readable.")]
    public float healthBarWorldWidth = 0.6f;

    [Header("Live Hit Collider")]
    [Tooltip("World-space height of the capsule that registers pistol/axe raycasts. Constant across all aliens regardless of size.")]
    public float liveColliderWorldHeight = 2.5f;
    [Tooltip("World-space radius of the live hit capsule. Larger = easier to hit but visibly fatter than the alien.")]
    public float liveColliderWorldRadius = 0.9f;

    [Header("Live Knockback")]
    [Tooltip("Scales the knockback distance the pistol/axe pass in. 0.2 means a 1.5m pistol-knockback nudges the alien 0.3m. Pistol/axe distances are tuned for kinematic enemies — a smaller fraction reads better on a static-rooted alien.")]
    public float liveKnockbackScale = 0.2f;

    [Header("Death")]
    [Tooltip("Seconds the corpse stays in the world before being destroyed.")]
    public float corpseLifetime = 120f;

    float currentHealth;
    bool _dying;
    public bool IsDying => _dying;

    // Static instance list following the codebase convention (see
    // EnemyController.ActiveEnemies / SpawnedTree.AllTrees). Lets
    // EnemyController scan for nearby NPCs without per-frame
    // FindObjectsOfType. Needed because the enemy is kinematic and the NPC
    // has no Rigidbody, so Unity does NOT fire OnCollisionStay between them
    // — proximity detection is the only way to "see" an NPC is blocking the
    // path.
    static readonly System.Collections.Generic.List<AlienNPCDamageable> s_active =
        new System.Collections.Generic.List<AlienNPCDamageable>();
    public static System.Collections.Generic.IReadOnlyList<AlienNPCDamageable> AllInstances => s_active;
    CapsuleCollider _liveCollider;
    BoxCollider _triggerCollider;
    EnemyHealthBar _healthBar;
    Canvas _healthCanvas;
    SpawnedAlienNPC _marker;
    AlienNPCSpawner _spawner;
    Vector3 _lastHitDir;
    Coroutine _knockbackCoroutine;

    void Awake()
    {
        _marker = GetComponent<SpawnedAlienNPC>();

        // Use fixed *world-space* sizes counter-scaled to alien-local. The
        // alien root has random scale 2-5× and not every alien prefab is
        // humanoid (some have no "head" bone), so deriving sizes from bones
        // produced inconsistent results. Fixed world sizes guarantee the
        // capsule and bar look identical on every alien regardless of size.
        float rootScale = Mathf.Max(0.01f, transform.localScale.x);
        float localHeight = liveColliderWorldHeight / rootScale;
        float localRadius = liveColliderWorldRadius / rootScale;

        _liveCollider = gameObject.AddComponent<CapsuleCollider>();
        _liveCollider.isTrigger = false;
        _liveCollider.radius = localRadius;
        _liveCollider.height = localHeight;
        _liveCollider.center = new Vector3(0f, localHeight * 0.5f, 0f);
        _liveCollider.direction = 1; // Y-axis (upright)

        // Cache the existing F-prompt trigger so we can disable it on death.
        var boxes = GetComponents<BoxCollider>();
        for (int i = 0; i < boxes.Length; i++)
        {
            if (boxes[i].isTrigger) { _triggerCollider = boxes[i]; break; }
        }

        BuildHealthBar(rootScale);
    }

    void Start()
    {
        // Spawner reference resolved late so Awake order doesn't matter.
        _spawner = FindObjectOfType<AlienNPCSpawner>();
    }

    // Pooled aliens get re-enabled on respawn — reset health and hide the
    // bar each time so a previously-damaged alien doesn't return half-dead.
    void OnEnable()
    {
        currentHealth = maxHealth;
        _dying = false;
        _lastHitDir = Vector3.zero;
        if (_healthCanvas != null) _healthCanvas.gameObject.SetActive(false);
        if (_liveCollider != null) _liveCollider.enabled = true;
        if (_triggerCollider != null) _triggerCollider.enabled = true;
        if (!s_active.Contains(this)) s_active.Add(this);
    }

    void OnDisable() { s_active.Remove(this); }

    public void TakeDamage(float amount)
    {
        if (amount <= 0f || _dying) return;
        currentHealth -= amount;
        if (_healthBar != null)
        {
            _healthBar.Show();
            _healthBar.SetFill(Mathf.Clamp01(currentHealth / maxHealth));
        }
        bool fatal = currentHealth <= 0f;
        if (!fatal) PlayOneShot2D(hitSound, hitVolume);
        if (fatal) Die();
    }

    // Unity's AudioSource.PlayClipAtPoint creates a 3D-spatial source with
    // logarithmic rolloff and maxDistance=500 — at typical combat distances
    // (10-50m on a planet's surface) the result is practically silent.
    // Using a 2D source instead so the player always hears the hit at the
    // configured volume regardless of where on the planet the alien is.
    static void PlayOneShot2D(AudioClip clip, float volume)
    {
        if (clip == null) return;
        var go = new GameObject("AlienOneShotAudio");
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.volume = volume;
        src.spatialBlend = 0f;   // 2D — full volume regardless of position
        src.Play();
        Destroy(go, clip.length + 0.5f);
    }

    public void ApplyKnockback(Vector3 worldDir, float distance, float duration)
    {
        // Always cache direction — even on the killing shot, so Die() can
        // pass it to the ragdoll builder for backwards momentum.
        if (worldDir.sqrMagnitude > 0.0001f) _lastHitDir = worldDir.normalized;
        if (_dying || duration <= 0f || distance <= 0f) return;
        if (_knockbackCoroutine != null) StopCoroutine(_knockbackCoroutine);
        _knockbackCoroutine = StartCoroutine(KnockbackRoutine(_lastHitDir, distance * liveKnockbackScale, duration));
    }

    // Lerps localPosition relative to the planet so the alien moves
    // *with* the planet during the nudge instead of drifting in world
    // space. Stopped on Die() so it doesn't fight the ragdoll bones.
    IEnumerator KnockbackRoutine(Vector3 worldDir, float distance, float duration)
    {
        Transform parent = transform.parent;
        Vector3 startLocal = transform.localPosition;
        Vector3 localDir = parent != null ? parent.InverseTransformDirection(worldDir) : worldDir;
        Vector3 targetLocal = startLocal + localDir * distance;
        float t = 0f;
        while (t < duration && !_dying)
        {
            t += Time.deltaTime;
            transform.localPosition = Vector3.Lerp(startLocal, targetLocal, Mathf.Clamp01(t / duration));
            yield return null;
        }
        if (!_dying) transform.localPosition = targetLocal;
        _knockbackCoroutine = null;
    }

    void Die()
    {
        if (_dying) return;
        _dying = true;
        // Drop out of the live-NPC list immediately so EnemyController.Try-
        // BiteNearbyNPC doesn't keep "biting" a ragdolling corpse for the
        // 120s before Destroy runs.
        s_active.Remove(this);

        // Cancel any in-flight live-knockback coroutine — once bones go
        // physical, lerping the root's transform fights PhysX.
        if (_knockbackCoroutine != null) { StopCoroutine(_knockbackCoroutine); _knockbackCoroutine = null; }

        PlayOneShot2D(deathSound, deathVolume);

        // Stop animation/dialogue/wave before bones go physical, otherwise
        // LateUpdate writes from NPCWaveAnimation will fight the joints.
        // Cover all dialogue script types — pre-placed aliens use different
        // ones (NPCDialogue, BonfireNPCDialogue, Alien7Vendor) than spawned
        // ones (RandomAlienDialogue).
        DisableIfPresent<Animator>();
        DisableIfPresent<NPCWaveAnimation>();
        DisableIfPresent<AudienceMember>();
        DisableIfPresent<RandomAlienDialogue>();
        DisableIfPresent<NPCDialogue>();
        DisableIfPresent<BonfireNPCDialogue>();
        DisableIfPresent<Alien7Vendor>();

        if (_liveCollider != null) _liveCollider.enabled = false;
        if (_triggerCollider != null) _triggerCollider.enabled = false;
        if (_healthCanvas != null) _healthCanvas.gameObject.SetActive(false);

        // Hide the shared "Press F to talk" prompt — the dialogue scripts that
        // would normally hide it on trigger-exit are disabled at this point
        // and the trigger collider is off, so the prompt would stay stuck on
        // screen if the player was inside the talk zone at kill time. Other
        // alive NPCs whose Update is still running will reactivate it next
        // frame if the player is also in their range.
        InteractPromptUI.Clear(this);

        // Resolve the planet *before* unparenting (after unparenting,
        // GetComponentInParent<CelestialBody> returns null).
        CelestialBody planet = GetComponentInParent<CelestialBody>();

        // Lazy-resolve the marker. AlienNPCDamageable.Awake runs at AddComponent
        // time, but the spawner adds SpawnedAlienNPC AFTER AlienNPCDamageable
        // in SpawnAlien — so the cache from Awake is null for streamed aliens.
        // Refresh here so the streamed-kill path is taken correctly.
        if (_marker == null) _marker = GetComponent<SpawnedAlienNPC>();

        // Notify the spawner so the kill persists. For streamed aliens we mark
        // the cell so the streaming loop skips it (also keeps the corpse out
        // of the pool path). For pre-placed scene aliens we record the
        // GameObject name so the save system can re-destroy them on load.
        if (_spawner != null)
        {
            if (_marker != null) _spawner.MarkCellKilled(_marker.BodySlot, _marker.CellId);
            else if (isStoryImpactful) _spawner.MarkPrePlacedKilled(gameObject.name);
        }

        // Keep the corpse parented to its planet. Origin shifts move the
        // planet via EndlessManager, and the whole alien hierarchy (wrapper,
        // rig, SkinnedMeshRenderer host, every bone) follows in lockstep
        // via Unity's transform hierarchy. Bones still ragdoll locally via
        // their Rigidbodies + CharacterJoints added in AlienRagdollBuilder.
        Vector3 hitDir = _lastHitDir.sqrMagnitude > 0.0001f ? _lastHitDir : transform.forward;
        AlienRagdollBuilder.Build(transform, planet, hitDir);

        // Push the freshly-built bone hierarchy into PhysX so colliders,
        // contact pairs, and joint anchors are consistent before the first
        // physics step.
        Physics.SyncTransforms();

        if (isStoryImpactful)
            StoryImpactNotice.Show(storyImpactMessage, storyImpactDuration);

        Destroy(gameObject, corpseLifetime);
    }

    void DisableIfPresent<T>() where T : Behaviour
    {
        var c = GetComponent<T>();
        if (c != null) c.enabled = false;
    }

    // Re-applies healthBarWorldHeight to the existing canvas. Called by the
    // spawner after copying the field from inspector, so user inspector
    // tweaks take effect on the next spawn (and on pool reuse).
    public void RefreshHealthBarPosition()
    {
        if (_healthCanvas == null) return;
        float rootScale = Mathf.Max(0.01f, transform.localScale.x);
        _healthCanvas.transform.localPosition = new Vector3(0f, healthBarWorldHeight / rootScale, 0f);
    }

    // Re-applies live capsule size from current world-space fields. Called
    // by the spawner after copy-through so inspector changes take effect on
    // every (re)spawn.
    public void RefreshLiveCollider()
    {
        if (_liveCollider == null) return;
        float rootScale = Mathf.Max(0.01f, transform.localScale.x);
        float localHeight = liveColliderWorldHeight / rootScale;
        float localRadius = liveColliderWorldRadius / rootScale;
        _liveCollider.height = localHeight;
        _liveCollider.radius = localRadius;
        _liveCollider.center = new Vector3(0f, localHeight * 0.5f, 0f);
    }

    // Builds an EnemyHealthBar-shaped Canvas + Image structure as a child of
    // this alien. World-space, billboards via EnemyHealthBar.LateUpdate.
    void BuildHealthBar(float rootScale)
    {
        var canvasGO = new GameObject("HealthBar");
        canvasGO.transform.SetParent(transform, false);
        // Fixed world height above the alien's root (its feet) regardless of
        // random scale or which prefab — this gives a consistent visual
        // height on every NPC. Counter-scale localPosition.y so the world
        // offset stays at healthBarWorldHeight metres.
        canvasGO.transform.localPosition = new Vector3(0f, healthBarWorldHeight / rootScale, 0f);
        // sizeDelta is 200; want world width ≈ healthBarWorldWidth.
        // World width = sizeDelta * lossyScale = 200 * (rootScale * localScale).
        // → localScale = healthBarWorldWidth / 200 / rootScale.
        canvasGO.transform.localScale = Vector3.one * (healthBarWorldWidth / 200f / rootScale);

        _healthCanvas = canvasGO.AddComponent<Canvas>();
        _healthCanvas.renderMode = RenderMode.WorldSpace;
        canvasGO.AddComponent<CanvasScaler>();

        var rt = (RectTransform)_healthCanvas.transform;
        rt.sizeDelta = new Vector2(200f, 30f);

        // Background (dark).
        var bgGO = new GameObject("BG");
        bgGO.transform.SetParent(canvasGO.transform, false);
        var bgRT = bgGO.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;
        var bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0.05f, 0.05f, 0.05f, 0.85f);
        bgImg.raycastTarget = false;

        // Fill (red, horizontal-fill).
        var fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(canvasGO.transform, false);
        var fillRT = fillGO.AddComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = new Vector2(2f, 2f);
        fillRT.offsetMax = new Vector2(-2f, -2f);
        var fillImg = fillGO.AddComponent<Image>();
        fillImg.color = new Color(0.85f, 0.18f, 0.18f, 1f);
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImg.fillAmount = 1f;
        fillImg.raycastTarget = false;

        _healthBar = canvasGO.AddComponent<EnemyHealthBar>();
        _healthBar.fillImage = fillImg;
        canvasGO.SetActive(false);
    }
}
