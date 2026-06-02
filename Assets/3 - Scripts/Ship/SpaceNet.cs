using UnityEngine;

/// <summary>
/// Drop this on a child GameObject of a Ship to make that ship gather space
/// dust while parked in orbit. Author also provides the visual mesh for the
/// "filter / net" prop — this component is purely behaviour.
///
/// Orbit definition (all must hold):
///   1. Owning Ship.IsPiloted is false (the player parked it).
///   2. Owning Ship.IsLanded is false (no contact with anything grounded).
///   3. Distance from the net to the nearest CelestialBody is between
///      body.radius * 1.05 (just above surface) and body.radius * 5 (within
///      the body's gravitational neighbourhood — matches the save system's
///      body-relative attach threshold).
///
/// Accumulation rate scales by altitude (closeOrbitMultiplier at the inner
/// threshold falling to 0 at the outer). Buffer is float internally so
/// partial-second accumulation works; exposed as floor int via BufferedDust.
///
/// Collection zone — the player presses F to drain the buffer when standing
/// inside a trigger BoxCollider parented to this GameObject. Author drops
/// the BoxCollider (isTrigger=true) where they want the zone — its size and
/// center are edited directly in the Inspector for precise per-net tuning.
/// </summary>
public class SpaceNet : MonoBehaviour
{
    [Tooltip("Base dust per second. Actual accumulation scales by orbit altitude: 2x at the closest orbit (just above surface) falling off to 0 at the outer attach threshold.")]
    public float dustPerSecond = 0.1f;
    [Tooltip("Maximum multiplier on dustPerSecond at the closest orbit. Set 1 to disable altitude rolloff entirely.")]
    public float closeOrbitMultiplier = 2f;
    [Tooltip("Maximum dust the net can hold before the player must come collect.")]
    public int bufferCapacity = 500;

    Ship _owningShip;
    float _buffer;
    bool _playerInRange;

    public int BufferedDust => Mathf.FloorToInt(_buffer);
    public Ship OwningShip => _owningShip;
    public float RawBuffer => _buffer;
    public void SetRawBuffer(float value) => _buffer = Mathf.Clamp(value, 0f, bufferCapacity);

    // True when the net is live on a ship — the GameObject is active in the
    // hierarchy AND it found an owning Ship in its parent chain. Used by
    // FleetTelemetry and HALCommentator to skip dust accounting for nets
    // that have been deactivated (e.g. damaged off the ship in the same
    // flow that drops a SpaceNetPickup into the world).
    public bool IsAttached => gameObject.activeInHierarchy && _owningShip != null;

    void Awake()
    {
        _owningShip = GetComponentInParent<Ship>();
        if (_owningShip == null)
        {
            Debug.LogWarning($"[SpaceNet] '{name}' is not parented to a Ship; net will be inactive.");
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player")) _playerInRange = true;
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            _playerInRange = false;
            InteractPromptUI.Clear(this);
        }
    }

    void Update()
    {
        // Accumulation while in orbit. Rate is altitude-scaled.
        if (IsCurrentlyInOrbit() && _buffer < bufferCapacity)
        {
            float mul = ComputeAltitudeMultiplier();
            _buffer = Mathf.Min(bufferCapacity, _buffer + dustPerSecond * mul * Time.deltaTime);
        }

        if (!_playerInRange) return;

        int n = BufferedDust;
        if (n >= 1)
        {
            InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to collect {n} space dust");
            if (TutorialGate.InteractPressed(TutorialAbility.TalkToNPC))
            {
                int drained = Drain(n);
                if (drained > 0 && SpaceDustInventory.Instance != null)
                {
                    SpaceDustInventory.Instance.Add(drained);
                    // Follow the ship so the popup doesn't whoosh off-screen
                    // while the ship is in fast orbit.
                    Transform follow = _owningShip != null ? _owningShip.transform : transform;
                    DustPopup.Spawn(transform.position, drained, follow);
                }
            }
        }
        else
        {
            InteractPromptUI.Clear(this);
        }
    }

    void OnDisable()
    {
        InteractPromptUI.Clear(this);
        _playerInRange = false;
    }

    bool IsCurrentlyInOrbit()
    {
        if (_owningShip == null) return false;
        if (_owningShip.IsPiloted) return false;
        if (_owningShip.IsLanded) return false;
        var body = ClosestBody();
        if (body == null) return false;
        float dist = Vector3.Distance(transform.position, body.Position);
        float minR = body.radius * 1.05f;
        float maxR = body.radius * 5f;
        return dist >= minR && dist <= maxR;
    }

    CelestialBody ClosestBody()
    {
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return null;
        CelestialBody best = null;
        float bestSq = float.MaxValue;
        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            if (b == null) continue;
            float d = (b.Position - transform.position).sqrMagnitude;
            if (d < bestSq) { bestSq = d; best = b; }
        }
        return best;
    }

    // Scales accumulation by ship altitude over the nearest body. Returns
    // closeOrbitMultiplier (peak) at the inner attach threshold (body.radius*1.05)
    // and 0 at the outer threshold (body.radius*5). Outside the orbit band,
    // IsCurrentlyInOrbit already returns false so this path isn't reached.
    float ComputeAltitudeMultiplier()
    {
        var body = ClosestBody();
        if (body == null) return 1f;
        float dist = Vector3.Distance(transform.position, body.Position);
        float minR = body.radius * 1.05f;
        float maxR = body.radius * 5f;
        if (dist <= minR) return Mathf.Max(0f, closeOrbitMultiplier);
        if (dist >= maxR) return 0f;
        float t = (dist - minR) / (maxR - minR);
        return Mathf.Lerp(closeOrbitMultiplier, 0f, t);
    }

    /// <summary>Drain up to `requested` dust from the net's buffer; returns what was actually drained.</summary>
    public int Drain(int requested)
    {
        if (requested <= 0) return 0;
        int available = BufferedDust;
        int drained = Mathf.Min(available, requested);
        _buffer -= drained;
        if (_buffer < 0f) _buffer = 0f;
        return drained;
    }
}
