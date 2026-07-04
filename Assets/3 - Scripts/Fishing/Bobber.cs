using UnityEngine;
using System.Collections;

public class Bobber : MonoBehaviour
{
    public float shootSpeed = 5f;

    [Header("Bobbing Settings")]
    public float bobAmplitude = 0.15f;
    public float bobFrequency = 1.5f;

    [Header("Fishing Settings")]
    public float minStrikeWaitTime = 1f;
    public float maxStrikeWaitTime = 10f;
    public float strikeBobFrequency = 8f;
    public float strikeBobAmplitude = 0.3f;

    public float commonFishStrikeDuration = 3f;
    public float uncommonFishStrikeDuration = 2f;
    public float rareFishStrikeDuration = 1f;

    [Header("Sound Effects")]
    public AudioClip waterSplashClip;
    public AudioClip biteClip;
    [Range(0, 1)] public float waterSplashVolume = 0.5f;
    [Range(0, 1)] public float biteVolume        = 0.5f;

    private AudioSource audioSource;
    private AudioSource biteSource;

    private Rigidbody rb;
    private bool hasHitWater = false;
    private bool hasHitEnemy = false;
    private Vector3 baseLocalPosition;
    private bool isFishingActive = false;
    private bool isStriking = false;
    private float strikeEndTime;
    private string currentFishType = "";
    private bool fishCaught = false;
    private Coroutine fishingCoroutine;

    public bool IsInWater => hasHitWater;
    public bool IsStriking => isStriking;
    public System.Action OnFishEscaped;

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        biteSource = gameObject.AddComponent<AudioSource>();
        biteSource.playOnAwake = false;
        biteSource.loop = true;
        biteSource.volume = biteVolume;

        EndlessManager em = FindObjectOfType<EndlessManager>();
        if (em != null) em.RegisterPhysicsObject(transform);
        Debug.Log("[Bobber] Spawned and registered.");
    }

    void Update()
    {
        if (hasHitWater)
        {
            float frequency = isStriking ? strikeBobFrequency : bobFrequency;
            float amplitude = isStriking ? strikeBobAmplitude : bobAmplitude;
            float bobOffset = Mathf.Sin(Time.time * frequency) * amplitude;
            transform.localPosition = baseLocalPosition + Vector3.up * bobOffset;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (hasHitWater || hasHitEnemy) return;

        if (other.CompareTag("Enemy"))
        {
            HitEnemy(other);
            return;
        }

        if (other.CompareTag("Water"))
        {
            hasHitWater = true;
            StopOnWater(other);
        }
    }

    // Bobber's own colliders aren't triggers, and the enemy's capsule isn't a
    // trigger either, so enemy hits go through the physical-collision path.
    void OnCollisionEnter(Collision collision)
    {
        if (hasHitWater || hasHitEnemy) return;

        if (collision.collider.CompareTag("Enemy"))
            HitEnemy(collision.collider);
    }

    // The bobber prefab has three non-trigger sub-colliders, so a single visual
    // hit on an enemy can fire OnCollisionEnter up to three times in the same
    // frame before Destroy takes effect. The flag guarantees one damage per cast.
    void HitEnemy(Collider enemyCollider)
    {
        hasHitEnemy = true;
        var enemy = enemyCollider.GetComponentInParent<EnemyController>();
        if (enemy != null) enemy.TakeBobberDamage();
        Destroy(gameObject);
    }

    void StopOnWater(Collider waterCollider)
    {
        Debug.Log("[Bobber] Hit water. Stopping and setting up...");

        if (waterSplashClip != null && audioSource != null)
        {
            audioSource.pitch = 1f;
            audioSource.PlayOneShot(waterSplashClip, waterSplashVolume);
        }

        CelestialBody planet = waterCollider.GetComponentInParent<CelestialBody>();
        if (planet == null)
        {
            CelestialBody[] bodies = FindObjectsOfType<CelestialBody>();
            float nearestDist = Mathf.Infinity;
            foreach (CelestialBody body in bodies)
            {
                float dist = Vector3.Distance(transform.position, body.transform.position);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    planet = body;
                }
            }
        }

        EndlessManager em = FindObjectOfType<EndlessManager>();
        if (em != null) em.UnregisterPhysicsObject(transform);

        GravityObjectSimple grav = GetComponent<GravityObjectSimple>();
        if (grav != null) Destroy(grav);

        if (rb != null)
        {
            Destroy(rb);
            rb = null;
        }

        if (planet != null)
            transform.SetParent(planet.transform, true);

        baseLocalPosition = transform.localPosition;
        baseLocalPosition += Vector3.up * 0.05f;
        transform.localPosition = baseLocalPosition;

        if (em != null) em.RegisterPhysicsObject(transform);

        Debug.Log("[Bobber] Physics removed, parented to planet. Starting fishing coroutine...");
        StartFishing();
    }

    public void StartFishing()
    {
        if (!hasHitWater)
        {
            Debug.LogWarning("[Bobber] StartFishing called but not in water!");
            return;
        }
        if (isFishingActive)
        {
            Debug.Log("[Bobber] Fishing already active.");
            return;
        }
        isFishingActive = true;
        fishingCoroutine = StartCoroutine(FishingRoutine());
        Debug.Log("[Bobber] Fishing coroutine started.");
    }

    IEnumerator FishingRoutine()
    {
        Debug.Log("[Bobber] FishingRoutine entered.");
        while (true)
        {
            float waitTime = Random.Range(minStrikeWaitTime, maxStrikeWaitTime);
            Debug.Log($"[Bobber] Waiting {waitTime:F1} seconds for a bite...");
            yield return new WaitForSeconds(waitTime);

            float rand = Random.value;
            if (rand < 0.3f)
            {
                currentFishType = "Rare";
                strikeEndTime = Time.time + rareFishStrikeDuration;
            }
            else if (rand < 0.6f)
            {
                currentFishType = "Uncommon";
                strikeEndTime = Time.time + uncommonFishStrikeDuration;
            }
            else
            {
                currentFishType = "Common";
                strikeEndTime = Time.time + commonFishStrikeDuration;
            }

            isStriking = true;
            fishCaught = false;
            GamepadRumble.Pulse(0.8f, 0.8f, 0.4f);
            Debug.Log($"[Bobber] FISH ON! Type: {currentFishType}, Strike duration: {strikeEndTime - Time.time:F1}s");

            if (biteClip != null && biteSource != null)
            {
                biteSource.clip = biteClip;
                biteSource.volume = biteVolume;
                biteSource.Play();
            }

            while (Time.time < strikeEndTime && !fishCaught)
            {
                yield return null;
            }

            if (biteSource != null && biteSource.isPlaying)
                biteSource.Stop();

            if (fishCaught)
            {
                Debug.Log($"[Bobber] Caught a {currentFishType} fish!");
            }
            else
            {
                Debug.Log("[Bobber] Strike ended - fish got away.");
                OnFishEscaped?.Invoke();
            }

            isStriking = false;
            currentFishType = "";
        }
    }

    // Weight distribution: 15% chance 1 lb, 5% chance 50 lbs, 80% across 2-49 lbs biased low.
    public static int GenerateFishWeight()
    {
        float rand = Random.value;
        if (rand < 0.15f) return 1;   // 15%: 1 lb
        if (rand < 0.20f) return 50;  // 5%: 50 lbs
        // 80%: 2-49 lbs with a low-weight bias (power curve)
        float t = Mathf.Pow(Random.value, 1.5f);
        return Mathf.RoundToInt(Mathf.Lerp(2f, 49f, t));
    }

    public bool TryCatchFish(float spinDegrees = 0f, int spinCombo = 0)
    {
        if (!isStriking || fishCaught) return false;
        fishCaught = true;

        if (biteSource != null && biteSource.isPlaying)
            biteSource.Stop();

        int weight = GenerateFishWeight();
        Debug.Log($"[Bobber] TryCatchFish - caught {weight}lb {currentFishType}! Spin: {spinDegrees:F0}° Combo: {spinCombo}");
        if (FishInventory.Instance != null)
        {
            // Phase 2: log to lifetime dex first (every fish is always recorded).
            var entry = FishInventory.Instance.AddFish(currentFishType, weight);
            // Phase 3: route bag → hotbar → destroy. The bag (if present in
            // any hotbar slot) gets first crack; this lets the player carry
            // up to ~11 fish (5 bag + 6 remaining hotbar slots).
            bool placed =
                (Hotbar.Instance != null && Hotbar.Instance.TryAddFishToBag(entry)) ||
                (Hotbar.Instance != null && Hotbar.Instance.TryAddFish(entry));
            if (!placed) InventoryFullPopup.Show();
        }
        if (FishCatchUI.Instance != null)
            FishCatchUI.Instance.ShowFishCaught(currentFishType, weight, spinDegrees, spinCombo);
        return true;
    }

    void OnDestroy()
    {
        // Explicitly stop the fishing coroutine — Unity stops coroutines when the
        // owning MonoBehaviour is destroyed, but doing it here makes the lifecycle
        // explicit and protects against subtle ordering issues if a derived class
        // ever spawns sub-coroutines that should also be cancelled.
        if (fishingCoroutine != null) { StopCoroutine(fishingCoroutine); fishingCoroutine = null; }

        EndlessManager em = FindObjectOfType<EndlessManager>();
        if (em != null) em.UnregisterPhysicsObject(transform);
    }
}