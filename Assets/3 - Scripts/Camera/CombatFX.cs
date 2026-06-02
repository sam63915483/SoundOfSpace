using UnityEngine;

/// <summary>
/// Combat-feedback effects driven by ResourceManager events:
/// directional hit shake, damage red flash, damage vignette pulse, death
/// tilt + dim. Subscribes once and lives as long as the manager.
/// </summary>
public class CombatFX : MonoBehaviour
{
    ResourceManager _rm;
    float _damagePulse;
    bool _dead;

    void OnEnable() { TrySubscribe(); }

    void OnDestroy()
    {
        if (_rm != null)
        {
            _rm.OnHealthDropped -= HandleHealthDropped;
            _rm.OnDeath -= HandleDeath;
        }
    }

    void Update()
    {
        if (_rm == null) TrySubscribe();

        // Auto-clear death state once the player respawns. ResourceManager's
        // DeathSequence restores health to 25 (= 0.25 HealthPercent) at the
        // end of the freeze, so the threshold here must be BELOW 0.25 to
        // fire — at 0.5 it never fired and the death tilt + dim vignette
        // persisted for the entire respawn lifetime. 0.1 catches any
        // restoration ≥ 10%, which covers every healing path including
        // partial heals before death-sequence completion.
        if (_dead && ResourceManager.Instance != null && ResourceManager.Instance.HealthPercent > 0.1f)
            ClearDeath();

        if (_damagePulse > 0f)
            _damagePulse = Mathf.MoveTowards(_damagePulse, 0f, Time.unscaledDeltaTime * 2.5f);

        var mgr = CameraEffectsManager.Instance;
        if (mgr == null || mgr.Vignette == null) return;
        var input = mgr.Input;

        if (_damagePulse > 0f && input != null && input.fxDamageVignette)
            mgr.Vignette.Push(new Color(1f, 0.15f, 0.2f, 1f), _damagePulse);

        if (_dead && input != null && input.fxDeathTilt)
            mgr.Vignette.Push(new Color(0.02f, 0.02f, 0.04f, 1f), 0.85f);
    }

    void TrySubscribe()
    {
        if (_rm != null) return;
        _rm = ResourceManager.Instance;
        if (_rm == null) return;
        _rm.OnHealthDropped += HandleHealthDropped;
        _rm.OnDeath += HandleDeath;
    }

    void HandleHealthDropped(float amount)
    {
        var mgr = CameraEffectsManager.Instance;
        if (mgr == null || !mgr.MasterEnabled) return;
        var input = mgr.Input;

        if (input != null && input.fxDamageFlash && mgr.DamageFlash != null)
            mgr.DamageFlash.Flash(0.55f);

        if (input != null && input.fxDamageVignette)
            _damagePulse = Mathf.Max(_damagePulse, 0.7f);

        if (input != null && input.fxDirectionalHitShake && CameraShake.Instance != null)
            CameraShake.Instance.TriggerShake(0.15f, 0.3f + amount * 0.01f, 6f);
    }

    void HandleDeath()
    {
        _dead = true;
        var mgr = CameraEffectsManager.Instance;
        if (mgr == null || !mgr.MasterEnabled) return;
        var input = mgr.Input;
        if (input != null && input.fxDeathTilt && mgr.TransformFX != null)
            mgr.TransformFX.TriggerDeathTilt();
    }

    public void ClearDeath()
    {
        _dead = false;
        var mgr = CameraEffectsManager.Instance;
        if (mgr != null && mgr.TransformFX != null) mgr.TransformFX.ClearDeathTilt();
    }
}
