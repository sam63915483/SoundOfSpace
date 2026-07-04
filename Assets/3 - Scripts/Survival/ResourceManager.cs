using System.Collections;
using UnityEngine;

public class ResourceManager : MonoBehaviour
{
    public static ResourceManager Instance { get; private set; }

    public event System.Action<float> OnHealthDropped; // arg: damage amount
    public event System.Action OnDeath;
    public static event System.Action OnCleanWaterDrunk;

    // Set by DeathCutsceneController. When it returns true, DeathSequence skips
    // the legacy in-place respawn because the cutscene is driving a full
    // save-reload instead (this manager is DontDestroyOnLoad — its coroutine
    // would otherwise reset vitals on top of the reloaded save). Stays null when
    // no cutscene controller is present, preserving the original behaviour.
    public static System.Func<bool> LegacyRespawnSuppressed;

    [Header("Hunger & Thirst")]
    public float hungerDecayTime = 15f;
    public float thirstDecayTime = 10f;

    [Header("Health Drain Rates (HP/sec)")]
    public float healthDrainRateHungry = 2f;
    public float healthDrainRateThirsty = 4f;

    [Header("Death")]
    public float deathFreezeTime = 2f;

    [Header("Damage SFX")]
    [Tooltip("Sound played each time the player takes damage. Drag any clip here.")]
    public AudioClip damageClip;
    [Range(0f, 1f)] public float damageVolume = 0.7f;
    AudioSource damageSource;

    float hungerCurrent = 100f;
    float thirstCurrent = 100f;
    float healthCurrent = 100f;

    // Total deaths across the run. Read by the phone AI's TokenResolver to
    // resolve {ASTRONAUT_NUMBER} = totalDeaths + 1.
    int totalDeaths;
    public int TotalDeaths => totalDeaths;
    public void SetTotalDeaths(int n) => totalDeaths = Mathf.Max(0, n);

    bool isDead;

    PlayerController playerRef;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        playerRef = FindObjectOfType<PlayerController>();

        damageSource = gameObject.AddComponent<AudioSource>();
        damageSource.playOnAwake = false;
        damageSource.spatialBlend = 0f;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (isDead) return;

        float dt = Time.deltaTime;

        hungerCurrent -= (100f / (hungerDecayTime * 60f)) * dt;
        thirstCurrent -= (100f / (thirstDecayTime * 60f)) * dt;

        hungerCurrent = Mathf.Clamp(hungerCurrent, 0f, 100f);
        thirstCurrent = Mathf.Clamp(thirstCurrent, 0f, 100f);

        if (hungerCurrent <= 0f) healthCurrent -= healthDrainRateHungry * dt;
        if (thirstCurrent <= 0f) healthCurrent -= healthDrainRateThirsty * dt;
        healthCurrent = Mathf.Clamp(healthCurrent, 0f, 100f);

        if (healthCurrent <= 0f && !isDead)
        {
            totalDeaths++;
            OnDeath?.Invoke();
            StartCoroutine(DeathSequence());
        }
    }

    IEnumerator DeathSequence()
    {
        isDead = true;
        PlayerController.isInDialogue = true;

        // If the death cutscene is handling this death (cutscene + save-reload),
        // hand off now and skip the legacy in-place respawn below. The reloaded
        // save restores vitals/position; the cutscene clears isInDialogue.
        if (LegacyRespawnSuppressed != null && LegacyRespawnSuppressed())
            yield break;

        yield return new WaitForSeconds(deathFreezeTime);

        // Re-find the player if our cached ref was destroyed (e.g. this manager
        // persisted across a level-portal scene change and a new player spawned).
        if (playerRef == null) playerRef = FindObjectOfType<PlayerController>();

        if (playerRef != null)
        {
            // Respawn on the currently-piloted ship if any; otherwise leave
            // the player wherever they were (no scene ship now means there's
            // nothing canonical to teleport back to).
            var piloted = Ship.PilotedInstance;
            if (piloted != null)
            {
                playerRef.transform.position = piloted.transform.position + piloted.transform.up * 2f;
            }
            playerRef.SetVelocity(Vector3.zero);
        }

        healthCurrent = 25f;
        hungerCurrent = 10f;
        thirstCurrent = 10f;

        PlayerController.isInDialogue = false;
        isDead = false;
    }

    public void ConsumeFood(float amount)
    {
        hungerCurrent = Mathf.Clamp(hungerCurrent + amount, 0f, 100f);
    }

    public void DrinkWater(float amount)
    {
        thirstCurrent = Mathf.Clamp(thirstCurrent + amount, 0f, 100f);
        OnCleanWaterDrunk?.Invoke();
    }

    public void Heal(float amount)
    {
        healthCurrent = Mathf.Clamp(healthCurrent + amount, 0f, 100f);
    }

    public void TakeDamage(float amount) => TakeDamage(amount, true);

    /// <summary>
    /// Apply damage, optionally suppressing the built-in hurt clip. Fall damage
    /// passes playHurtClip = false because it plays its own tiered pain voice
    /// (ow → OWWWW) and would otherwise double up with this generic "ow".
    /// The camera FX (red flash / vignette / hit-shake via OnHealthDropped) still
    /// fire regardless.
    /// </summary>
    public void TakeDamage(float amount, bool playHurtClip)
    {
        if (amount <= 0f || isDead) return;
        // Debug god-mode (toggled via the backtick debug menu). Block all
        // incoming damage so enemies' spit / contact hits don't drain health.
        if (GravityDebugUI.GodMode) return;
        healthCurrent = Mathf.Clamp(healthCurrent - amount, 0f, 100f);
        OnHealthDropped?.Invoke(amount);
        GamepadRumble.Pulse(Mathf.Clamp01(0.3f + amount * 0.02f), 0.5f, 0.25f);
        if (playHurtClip && damageClip != null && damageSource != null)
            damageSource.PlayOneShot(damageClip, damageVolume);
    }

    public float HungerPercent => hungerCurrent / 100f;
    public float ThirstPercent => thirstCurrent / 100f;
    public float HealthPercent => healthCurrent / 100f;

    public void ApplyState(float hunger, float thirst, float health)
    {
        hungerCurrent = Mathf.Clamp(hunger, 0f, 100f);
        thirstCurrent = Mathf.Clamp(thirst, 0f, 100f);
        healthCurrent = Mathf.Clamp(health, 0f, 100f);
        isDead = false;
    }
}
