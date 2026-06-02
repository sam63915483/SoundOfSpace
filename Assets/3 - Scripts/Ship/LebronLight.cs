using UnityEngine;

public class LebronLight : MonoBehaviour
{
    public static LebronLight Instance { get; private set; }

    [Header("Lebron Light")]
    [Tooltip("Local offset above the ship where the light is placed.")]
    public float lightHeightAboveShip = 5f;
    [Tooltip("Radius (metres) inside which enemies cannot spawn while the light is active.")]
    public float protectionRadius = 50f;
    [Tooltip("Ship power drained per second while the light is active (0-100 scale).")]
    public float usageRate = 1f;

    [Header("Light Visuals")]
    public Color lightColor = new Color(1f, 0.96f, 0.85f);
    public float lightIntensity = 5f;
    public LightType lightType = LightType.Point;

    [Header("Lens Flare")]
    [Tooltip("Drag a .flare asset here (e.g. Lens Flares/Sun (from space).flare). Requires a FlareLayer on the camera.")]
    public Flare lensFlare;

    [Header("Enemy Damage")]
    [Tooltip("Health-per-second dealt to enemies inside protectionRadius while the light is active.")]
    public float enemyDamagePerSecond = 10f;

    [Header("Sun Sprite")]
    [Tooltip("PNG sprite drawn on the light. Assign the sun PNG here.")]
    public Sprite sunSprite;
    [Range(0f, 1f)]
    [Tooltip("Alpha multiplier on the sun sprite.")]
    public float sunOpacity = 1f;
    [Tooltip("World scale of the sun sprite.")]
    public float sunScale = 8f;
    [Tooltip("Sun sprite billboards toward the player only while they are within this distance of the light.")]
    public float sunFollowRadius = 60f;

    [Header("Spinner Sprite")]
    [Tooltip("PNG sprite drawn behind the sun that spins. Assign here.")]
    public Sprite spinnerSprite;
    [Range(0f, 1f)]
    [Tooltip("Alpha multiplier on the spinner sprite.")]
    public float spinnerOpacity = 1f;
    [Tooltip("World scale of the spinner sprite (independent of sunScale).")]
    public float spinnerScale = 10f;
    [Tooltip("Spinner rotation speed in degrees per second around its facing axis.")]
    public float spinnerSpeed = 30f;

    [Header("Sun Pulse")]
    [Tooltip("Sun scale oscillates by ±this fraction of sunScale. 0 disables pulsing. (0.1 = ±10%)")]
    [Range(0f, 1f)]
    public float pulseAmplitude = 0.1f;
    [Tooltip("Pulses per second.")]
    public float pulseSpeed = 0.5f;
    [Tooltip("If true, the atmosphere halo pulses with the sun.")]
    public bool pulseHaloWithSun = true;

    [Header("Audio Loop")]
    [Tooltip("Audio clip played on loop while the light is active.")]
    public AudioClip lightLoopClip;
    [Range(0f, 1f)]
    public float lightLoopVolume = 0.5f;

    [Header("Atmosphere Halo")]
    [Tooltip("Soft glow drawn behind the sun to simulate atmospheric scatter.")]
    public bool atmosphereEnabled = true;
    [Tooltip("Inner / main halo color (centre of the glow).")]
    public Color atmosphereColor = new Color(1f, 0.65f, 0.35f, 1f);
    [Tooltip("Outer / secondary halo color (edge of the glow). The texture fades from the inner color toward this one as it approaches the edge.")]
    public Color atmosphereEdgeColor = new Color(0.6f, 0.1f, 0.05f, 1f);
    [Tooltip("Halo size as a multiplier of sunScale.")]
    public float atmosphereScale = 2.5f;
    [Range(0f, 1f)]
    public float atmosphereOpacity = 0.5f;

    Light light;
    GameObject lightHost;
    SpriteRenderer sunRenderer;
    Transform sunTransform;
    SpriteRenderer haloRenderer;
    Transform haloTransform;
    Texture2D haloTex;
    Color cachedHaloInner;
    Color cachedHaloEdge;
    Sprite cachedSunSprite;
    SpriteRenderer spinnerRenderer;
    Transform spinnerTransform;
    Sprite cachedSpinnerSprite;
    float spinnerAngle;
    Flare cachedFlare;
    AudioSource loopSource;
    AudioClip cachedLoopClip;
    bool isActive;
    PlayerController playerCtl;

    // The ship whose power this artificial sun drains. Resolved when the
    // light is toggled on; cleared when it's toggled off. Defaults to the
    // currently piloted ship if any.
    Ship _owningShip;

    static Ship ResolveOwningShip()
    {
        var piloted = Ship.PilotedInstance;
        if (piloted != null) return piloted;
        var allShips = Object.FindObjectsOfType<Ship>(true);
        if (allShips == null || allShips.Length == 0) return null;
        if (allShips.Length == 1) return allShips[0];
        Ship best = null;
        float bestSqr = float.MaxValue;
        var instTransform = Instance != null ? Instance.transform : null;
        for (int i = 0; i < allShips.Length; i++)
        {
            if (allShips[i] == null) continue;
            float dsq = instTransform != null
                ? (allShips[i].transform.position - instTransform.position).sqrMagnitude
                : 0f;
            if (dsq < bestSqr) { bestSqr = dsq; best = allShips[i]; }
        }
        return best;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        lightHost = new GameObject("LebronLight");
        lightHost.transform.SetParent(transform, false);
        lightHost.transform.localPosition = Vector3.up * lightHeightAboveShip;
        lightHost.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        light = lightHost.AddComponent<Light>();
        light.type = lightType;
        light.color = lightColor;
        light.intensity = lightIntensity;
        light.range = protectionRadius;
        light.shadows = LightShadows.Soft;
        light.flare = lensFlare;
        cachedFlare = lensFlare;

        GameObject haloObj = new GameObject("AtmosphereHalo");
        haloObj.transform.SetParent(lightHost.transform, false);
        haloObj.transform.localPosition = Vector3.zero;
        haloTransform = haloObj.transform;
        haloRenderer = haloObj.AddComponent<SpriteRenderer>();
        haloRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        haloRenderer.receiveShadows = false;
        haloRenderer.sprite = CreateHaloSprite();
        haloRenderer.sortingOrder = 0;

        GameObject spinnerObj = new GameObject("SpinnerSprite");
        spinnerObj.transform.SetParent(lightHost.transform, false);
        spinnerObj.transform.localPosition = Vector3.zero;
        spinnerTransform = spinnerObj.transform;
        spinnerRenderer = spinnerObj.AddComponent<SpriteRenderer>();
        spinnerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        spinnerRenderer.receiveShadows = false;
        spinnerRenderer.sortingOrder = 1;
        ApplySpinnerSprite();

        GameObject sunObj = new GameObject("SunSprite");
        sunObj.transform.SetParent(lightHost.transform, false);
        sunObj.transform.localPosition = Vector3.zero;
        sunTransform = sunObj.transform;
        sunRenderer = sunObj.AddComponent<SpriteRenderer>();
        sunRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sunRenderer.receiveShadows = false;
        sunRenderer.sortingOrder = 2;
        ApplySunSprite();

        loopSource = lightHost.AddComponent<AudioSource>();
        loopSource.playOnAwake = false;
        loopSource.loop = true;
        loopSource.spatialBlend = 0f;
        loopSource.volume = lightLoopVolume;
        loopSource.clip = lightLoopClip;
        cachedLoopClip = lightLoopClip;

        lightHost.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (!isActive) return;

        if (light != null)
        {
            light.range = protectionRadius;
            light.color = lightColor;
            light.intensity = lightIntensity;
            if (cachedFlare != lensFlare)
            {
                light.flare = lensFlare;
                cachedFlare = lensFlare;
            }
        }
        if (lightHost != null && Mathf.Abs(lightHost.transform.localPosition.y - lightHeightAboveShip) > 0.001f)
            lightHost.transform.localPosition = Vector3.up * lightHeightAboveShip;

        ApplySunSprite();
        ApplySpinnerSprite();
        ApplyHalo();
        ApplyLoopAudio();

        DamageEnemiesInRadius();

        // Power drain comes from the ship that ignited the light.
        if (_owningShip == null) _owningShip = ResolveOwningShip();
        if (_owningShip != null)
        {
            _owningShip.DrainPower(usageRate * Time.deltaTime);
            if (!_owningShip.CanRunLebronLight) SetActive(false);
        }
        else
        {
            // No ship to draw power from — kill the light.
            SetActive(false);
        }
    }

    void LateUpdate()
    {
        if (!isActive || sunTransform == null || sunRenderer == null) return;

        if (playerCtl == null) playerCtl = FindObjectOfType<PlayerController>();
        Camera cam = playerCtl != null ? playerCtl.Camera : Camera.main;
        if (cam == null)
        {
            sunRenderer.enabled = false;
            if (haloRenderer != null) haloRenderer.enabled = false;
            return;
        }

        Vector3 lightPos = lightHost.transform.position;
        Transform playerT = playerCtl != null ? playerCtl.transform : cam.transform;
        float dist = Vector3.Distance(playerT.position, lightPos);
        bool inRange = dist <= sunFollowRadius;
        sunRenderer.enabled = inRange;
        if (haloRenderer != null) haloRenderer.enabled = inRange && atmosphereEnabled;
        if (spinnerRenderer != null) spinnerRenderer.enabled = inRange && spinnerSprite != null;
        if (!inRange) return;

        float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed * 2f * Mathf.PI) * pulseAmplitude;
        sunTransform.localScale = Vector3.one * sunScale * pulse;
        Vector3 toCam = sunTransform.position - cam.transform.position;
        Quaternion billboard = toCam.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(toCam.normalized, cam.transform.up)
            : sunTransform.rotation;
        sunTransform.rotation = billboard;

        if (haloTransform != null)
        {
            float haloPulse = pulseHaloWithSun ? pulse : 1f;
            haloTransform.localScale = Vector3.one * sunScale * atmosphereScale * haloPulse;
            haloTransform.rotation = billboard;
        }

        if (spinnerTransform != null)
        {
            spinnerAngle = (spinnerAngle + spinnerSpeed * Time.deltaTime) % 360f;
            spinnerTransform.localScale = Vector3.one * spinnerScale;
            spinnerTransform.rotation = billboard * Quaternion.AngleAxis(spinnerAngle, Vector3.forward);
        }
    }

    void DamageEnemiesInRadius()
    {
        if (enemyDamagePerSecond <= 0f) return;
        var enemies = EnemyController.ActiveEnemies;
        if (enemies == null || enemies.Count == 0) return;

        Vector3 lightPos = lightHost != null ? lightHost.transform.position : transform.position;
        float r2 = protectionRadius * protectionRadius;
        float dmg = enemyDamagePerSecond * Time.deltaTime;

        for (int i = enemies.Count - 1; i >= 0; i--)
        {
            var e = enemies[i];
            if (e == null) continue;
            if ((e.transform.position - lightPos).sqrMagnitude > r2) continue;
            e.TakeDamage(dmg, creditPlayer: false);
        }
    }

    void ApplySunSprite()
    {
        if (sunRenderer == null) return;
        if (cachedSunSprite != sunSprite)
        {
            sunRenderer.sprite = sunSprite;
            cachedSunSprite = sunSprite;
        }
        sunRenderer.color = new Color(1f, 1f, 1f, sunOpacity);
    }

    void ApplySpinnerSprite()
    {
        if (spinnerRenderer == null) return;
        if (cachedSpinnerSprite != spinnerSprite)
        {
            spinnerRenderer.sprite = spinnerSprite;
            cachedSpinnerSprite = spinnerSprite;
        }
        spinnerRenderer.color = new Color(1f, 1f, 1f, spinnerOpacity);
    }

    void ApplyHalo()
    {
        if (haloRenderer == null) return;
        if (cachedHaloInner != atmosphereColor || cachedHaloEdge != atmosphereEdgeColor)
            BakeHaloGradient();
        haloRenderer.color = new Color(1f, 1f, 1f, atmosphereOpacity);
    }

    void ApplyLoopAudio()
    {
        if (loopSource == null) return;
        if (cachedLoopClip != lightLoopClip)
        {
            loopSource.Stop();
            loopSource.clip = lightLoopClip;
            cachedLoopClip = lightLoopClip;
            if (isActive && lightLoopClip != null) loopSource.Play();
        }
        loopSource.volume = lightLoopVolume;
    }

    Sprite CreateHaloSprite()
    {
        const int res = 128;
        haloTex = new Texture2D(res, res, TextureFormat.ARGB32, false);
        haloTex.wrapMode = TextureWrapMode.Clamp;
        BakeHaloGradient();
        return Sprite.Create(haloTex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100f);
    }

    void BakeHaloGradient()
    {
        if (haloTex == null) return;
        cachedHaloInner = atmosphereColor;
        cachedHaloEdge = atmosphereEdgeColor;
        int res = haloTex.width;
        Color[] px = new Color[res * res];
        Vector2 c = new Vector2(res * 0.5f, res * 0.5f);
        float maxR = res * 0.5f;
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float d = Mathf.Clamp01(Vector2.Distance(new Vector2(x, y), c) / maxR);
                Color rgba = Color.Lerp(atmosphereColor, atmosphereEdgeColor, d);
                float falloff = Mathf.Pow(1f - d, 2.2f);
                rgba.a *= falloff;
                px[y * res + x] = rgba;
            }
        haloTex.SetPixels(px);
        haloTex.Apply();
    }

    public bool IsActive => isActive;

    public void Toggle() => SetActive(!isActive);

    public void SetActive(bool on)
    {
        if (on)
        {
            // Refuse to ignite if no owning ship has power. Resolve here so a
            // newly-piloted ship is picked up between toggles.
            _owningShip = ResolveOwningShip();
            if (_owningShip == null || !_owningShip.CanRunLebronLight) on = false;
        }
        else
        {
            _owningShip = null;
        }

        isActive = on;
        if (lightHost != null) lightHost.SetActive(on);

        if (loopSource != null)
        {
            if (on && lightLoopClip != null)
            {
                loopSource.clip = lightLoopClip;
                loopSource.volume = lightLoopVolume;
                if (!loopSource.isPlaying) loopSource.Play();
            }
            else
            {
                loopSource.Stop();
            }
        }
    }

    public static bool IsPositionProtected(Vector3 worldPos)
    {
        var inst = Instance;
        if (inst == null || !inst.isActive) return false;
        Vector3 lightPos = inst.lightHost != null ? inst.lightHost.transform.position : inst.transform.position;
        return (worldPos - lightPos).sqrMagnitude <= inst.protectionRadius * inst.protectionRadius;
    }
}
