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

	// --- runtime ---
	PlayerController player;
	AudioSource audioSource;
	float peakFallSpeed;          // max toward-surface speed seen this airborne stretch
	bool subscribed;
	float nextFindTime;           // throttle for lazy re-find

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

		// A cinematic owns the player's motion — don't bank any of it as a fall.
		if (Suppressed) { peakFallSpeed = 0f; return; }

		// Toward-surface speed: positive = falling onto the planet. Horizontal
		// motion projects to ~0 on transform.up, so it doesn't count.
		float down = -Vector3.Dot(player.RelativeVelocity, player.transform.up);
		if (down > peakFallSpeed) peakFallSpeed = down;
	}

	void HandleLanded()
	{
		float speed = peakFallSpeed;
		peakFallSpeed = 0f;   // reset for the next airborne stretch

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
