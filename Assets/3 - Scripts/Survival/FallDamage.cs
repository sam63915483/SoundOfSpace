using UnityEngine;

/// <summary>
/// Player fall damage. Lives as a component on the Player GameObject.
///
/// Only VERTICAL (toward-surface) speed at landing matters — horizontal running
/// speed is ignored. The planets orbit and move, so we measure speed relative to
/// the body the player is standing on (PlayerController.RelativeVelocity already
/// subtracts the reference body's orbital velocity) and project it onto the
/// surface-up axis (player.transform.up). Sprinting off a ledge and dropping
/// straight down are judged purely on how fast you're moving DOWNWARD.
///
/// Impact speed is the max downward speed over the last ~0.25 s before the
/// landing (sampled at physics rate), NOT the all-time peak of the airborne
/// stretch. The old peak model banked the fastest moment of the whole fall and
/// spent it at the NEXT OnLanded, whenever that was — so braking softly with
/// the down-thrust still hurt, and landing in water (which fires no OnLanded
/// and bleeds the speed) then wading ashore dealt the cliff-dive damage at the
/// shoreline. The window means only speed you actually carried into the ground
/// counts; touching water clears it outright.
///
/// Three tiers (light / medium / hard) each play an impact thud + a player pain
/// voice and deal speed-scaled damage. A normal jump launches/lands at ~20 m/s
/// (PlayerController.jumpForce = 20, applied as VelocityChange), so the light
/// tier starts above that — a plain jump never triggers a tier.
///
/// Damage goes through ResourceManager.TakeDamage, which already drives the
/// red-flash / vignette / hit-shake FX and fires the death cutscene at 0 HP, so
/// a hard enough slam can be lethal on its own.
/// </summary>
public class FallDamage : MonoBehaviour
{
	[Header("Tier speed thresholds (m/s, toward surface)")]
	[Tooltip("Below this, no fall damage and no impact/pain sound — the player's " +
	         "existing small land sound still plays. NOTE: a plain jump lands at " +
	         "~20 m/s, so at 18 a normal jump may deal a little light damage. Raise " +
	         "this above ~22 if you don't want jumps to ever hurt.")]
	public float lightThreshold = 18f;
	[Tooltip("At/above this downward speed, the landing is MEDIUM.")]
	public float mediumThreshold = 28f;
	[Tooltip("At/above this downward speed, the landing is HARD.")]
	public float hardThreshold = 38f;

	[Header("Damage curve")]
	[Tooltip("Damage dealt right at lightThreshold. Damage above that scales with speed.")]
	public float lightDamage = 8f;
	[Tooltip("Extra damage per 1 m/s of downward speed above lightThreshold.")]
	public float damagePerSpeed = 2f;
	[Tooltip("Maximum fall damage from a single landing. 100 = a hard enough slam can kill.")]
	public float maxFallDamage = 100f;

	[Header("Light landing sounds")]
	public AudioClip impactLight;
	public AudioClip painLight;

	[Header("Medium landing sounds")]
	public AudioClip impactMedium;
	public AudioClip painMedium;

	[Header("Hard landing sounds")]
	public AudioClip impactHard;
	public AudioClip painHard;

	[Header("Volumes")]
	[Range(0f, 1f)] public float impactVolume = 0.8f;
	[Range(0f, 1f)] public float painVolume = 0.8f;

	// Set true by scripted cinematics that fly/teleport the player (e.g. the
	// stasis-pod arrival). While set, we don't accumulate toward-surface speed and
	// keep the peak at zero, so the descent doesn't deal fall damage that would
	// otherwise land on the player the moment they're placed in the cabin.
	public static bool Suppressed;

	// Spawn grace: SaveSystem.Apply stamps this a few seconds ahead whenever a
	// save is applied. The restore teleport + first physics frames can read as
	// a lethal impact (player killed the instant they load in — the stasis-pod
	// save spawn-death loop, 2026-07-24); until this deadline passes, fall
	// samples are cleared just like Suppressed.
	public static float LoadGraceUntil;

	static bool InLoadGrace => Time.unscaledTime < LoadGraceUntil;

	// --- runtime ---
	PlayerController player;
	AudioSource audioSource;
	bool subscribed;
	float nextFindTime;           // throttle for lazy re-find

	// Pre-impact window: ring buffer of toward-surface speeds sampled every
	// FixedUpdate. 32 slots covers 0.32 s at the default 0.02 s timestep —
	// comfortably more than the 0.25 s window read at landing.
	const float impactWindow = 0.25f;
	readonly float[] sampleSpeeds = new float[32];
	readonly float[] sampleTimes  = new float[32];
	int sampleIndex;

	void Awake()
	{
		// Dedicated 2D one-shot source (mirrors PlayerController's sfxSource) so
		// our impact/pain clips don't share or interrupt ResourceManager's damage
		// audio, and aren't positional.
		audioSource = gameObject.AddComponent<AudioSource>();
		audioSource.playOnAwake = false;
		audioSource.spatialBlend = 0f;
	}

	void OnDestroy()
	{
		if (player != null) player.OnLanded -= HandleLanded;
	}

	void Update()
	{
		// Cache the player once; lazy-refind (throttled) only if it's missing.
		// Never search every frame — repo convention.
		if (player == null)
		{
			if (Time.unscaledTime < nextFindTime) return;
			nextFindTime = Time.unscaledTime + 1f;
			player = FindObjectOfType<PlayerController>();
			if (player == null) return;
		}

		if (!subscribed)
		{
			player.OnLanded += HandleLanded;
			subscribed = true;
		}
	}

	void FixedUpdate()
	{
		if (player == null) return;

		// A cinematic owns the player's motion (don't bank any of it as a fall),
		// and water IS the landing — entering it dissipates the speed, so the
		// eventual shoreline OnLanded must see nothing.
		if (Suppressed || InLoadGrace || player.IsInWater) { ClearSamples(); return; }

		// Toward-surface speed: positive = falling onto the planet. Horizontal
		// motion projects to ~0 on transform.up, so it doesn't count.
		float down = -Vector3.Dot(player.RelativeVelocity, player.transform.up);
		sampleSpeeds[sampleIndex] = Mathf.Max(0f, down);
		sampleTimes[sampleIndex]  = Time.fixedTime;
		sampleIndex = (sampleIndex + 1) % sampleSpeeds.Length;
	}

	void ClearSamples()
	{
		for (int i = 0; i < sampleSpeeds.Length; i++) sampleSpeeds[i] = 0f;
	}

	// Max toward-surface speed recorded inside the impact window. Landing
	// detection (a grounded spherecast in the player's Update) can run a frame
	// or two after the physics contact has already killed the velocity, so the
	// instantaneous speed at OnLanded reads ~0 — the short window reliably
	// captures the speed the player actually carried into the ground while
	// forgetting anything older (water entries, bounces off props, thrust
	// braking high in the fall).
	float RecentImpactSpeed()
	{
		float cutoff = Time.fixedTime - impactWindow;
		float best = 0f;
		for (int i = 0; i < sampleSpeeds.Length; i++)
			if (sampleTimes[i] >= cutoff && sampleSpeeds[i] > best) best = sampleSpeeds[i];
		return best;
	}

	void HandleLanded()
	{
		float speed = RecentImpactSpeed();
		ClearSamples();   // reset for the next airborne stretch

		if (speed < lightThreshold) return;   // soft enough — leave it to the land sound

		// Pick the tier's sound pair.
		AudioClip impact, pain;
		if (speed >= hardThreshold)        { impact = impactHard;   pain = painHard;   }
		else if (speed >= mediumThreshold) { impact = impactMedium; pain = painMedium; }
		else                               { impact = impactLight;  pain = painLight;  }

		if (audioSource != null)
		{
			if (impact != null) audioSource.PlayOneShot(impact, impactVolume);
			if (pain != null)   audioSource.PlayOneShot(pain, painVolume);
		}

		// Continuous, speed-scaled damage (tier only chose the sounds).
		float damage = Mathf.Clamp(
			lightDamage + (speed - lightThreshold) * damagePerSpeed,
			0f, maxFallDamage);

		// playHurtClip = false: we play our own tiered pain voice above, so skip
		// ResourceManager's generic "ow". Camera FX still fire.
		if (damage > 0f && ResourceManager.Instance != null)
			ResourceManager.Instance.TakeDamage(damage, false);
	}
}
