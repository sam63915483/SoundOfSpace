using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One cluster of fireflies over one patch of night-side ground. The root sits
/// ON the ground, parented to the planet through
/// <c>SpawnerCubeface.ParentToBodyPhysicsFrame</c>, with +Y pointing away from
/// the planet's centre — so every bug's flight is plain local-space maths that
/// rides the orbit and the floating origin for free.
///
/// Owns the fade (in on spawn, out on despawn/daylight) and ticks its bugs; the
/// spawner decides WHEN a swarm exists, this decides what it looks like from
/// moment to moment. Catching a bug goes through <see cref="TryCatch"/> so the
/// hotbar, the popup and the pool are updated in one place.
/// </summary>
public class FireflySwarm : MonoBehaviour
{
    public FireflySpawner Owner { get; private set; }
    public int BodySlot { get; private set; }
    public long CellId { get; private set; }
    public float Radius { get; private set; }
    public float HeightMin { get; private set; }
    public float HeightMax { get; private set; }

    /// 0..1 glow multiplier for every bug in the swarm.
    public float Fade { get; private set; }
    /// The body this swarm sits on and the radius below which no bug may go
    /// (ocean surface + margin; 0 = no ocean). Set by FireflySpawner.SpawnSwarm.
    public CelestialBody Body { get; set; }
    public float MinRadial { get; set; }

    public readonly List<FireflyBug> Bugs = new List<FireflyBug>();

    enum State { FadeIn, Live, FadeOut }
    State _state;
    float _fadeSeconds;
    System.Action _onFadedOut;

    /// A bug may be caught only while the swarm is fully in (not fading either way).
    public bool Catchable => _state == State.Live;
    public bool FadingOut => _state == State.FadeOut;

    public void Init(FireflySpawner owner, int bodySlot, long cellId,
                     float radius, float heightMin, float heightMax, float fadeSeconds)
    {
        Owner = owner;
        BodySlot = bodySlot;
        CellId = cellId;
        Radius = radius;
        HeightMin = heightMin;
        HeightMax = heightMax;
        _fadeSeconds = Mathf.Max(0.05f, fadeSeconds);
        _state = State.FadeIn;
        Fade = 0f;
        _onFadedOut = null;
    }

    public void BeginFadeOut(System.Action onDone)
    {
        if (_state == State.FadeOut) return;
        _state = State.FadeOut;
        _onFadedOut = onDone;
        // Nothing can be prompted or caught on the way out.
        for (int i = 0; i < Bugs.Count; i++)
            if (Bugs[i] != null) GameUI.ClearInteractionPrompt(Bugs[i]);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        float step = dt / _fadeSeconds;
        if (_state == State.FadeIn)
        {
            Fade = Mathf.MoveTowards(Fade, 1f, step);
            if (Fade >= 1f) _state = State.Live;
        }
        else if (_state == State.FadeOut)
        {
            Fade = Mathf.MoveTowards(Fade, 0f, step);
        }

        float time = Time.timeSinceLevelLoad;
        for (int i = 0; i < Bugs.Count; i++)
        {
            var b = Bugs[i];
            if (b != null) b.Tick(dt, time, Fade);
        }

        if (_state == State.FadeOut && Fade <= 0f)
        {
            var done = _onFadedOut;
            _onFadedOut = null;
            done?.Invoke();       // the spawner tears the swarm down
        }
    }

    /// <summary>
    /// Try to put this bug in the player's hotbar. False (and the bug stays)
    /// when the hotbar is full or missing; true once it has been pocketed and
    /// returned to the pool. An empty swarm reports itself depleted.
    /// </summary>
    public bool TryCatch(FireflyBug bug)
    {
        if (bug == null || !Catchable || !Bugs.Contains(bug)) return false;
        var hb = Hotbar.Instance;
        if (hb == null) return false;

        int leftover = hb.AddResource(Hotbar.ItemId.Firefly, 1);
        if (leftover > 0)
        {
            InventoryFullPopup.Show();
            return false;
        }

        FireflyPopup.Spawn(bug.transform.position, 1);
        Bugs.Remove(bug);
        if (Owner != null) Owner.ReleaseBug(bug);

        if (Bugs.Count == 0 && Owner != null) Owner.OnSwarmDepleted(this);
        return true;
    }

    /// Hand every bug back to the pool. The spawner destroys the root after.
    public void ReleaseAll()
    {
        for (int i = 0; i < Bugs.Count; i++)
            if (Bugs[i] != null && Owner != null) Owner.ReleaseBug(Bugs[i]);
        Bugs.Clear();
    }
}
