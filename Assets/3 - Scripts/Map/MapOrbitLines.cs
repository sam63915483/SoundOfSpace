using UnityEngine;
using System.Collections.Generic;

// Stable orbital trails for each non-sun CelestialBody + a KSP-style
// analytical orbit for the player's ship.
//
// Strategy (this version): simulate the N-body system forward ONCE on Show()
// with a small accurate timestep. For each non-sun body, detect its orbital
// period and store the path as primary-relative offsets covering one period.
// For the ship, compute a closed-form Kepler conic around the nearest body
// (perfect ellipse for bound orbits, hyperbolic arc for escape) — no drift.
//
// DefaultExecutionOrder(100) puts our LateUpdate AFTER EndlessManager's
// floating-origin shift, so primary.transform.position is the POST-shift
// value when we recompute line positions. Without this guarantee the line
// renders one frame at the pre-shift world coordinates while the planet
// already moved, producing visible "snapping" / "stale lines" until a
// manual re-toggle resets everything. We also subscribe to the manager's
// PostFloatingOriginUpdate event so a forced refresh fires immediately
// after a shift even on frames Unity dispatches LateUpdates out-of-order.
[DefaultExecutionOrder(100)]
public class MapOrbitLines : MonoBehaviour
{
    [Header("Simulation (one-shot at toggle)")]
    [Tooltip("Maximum integration steps. Period detection usually cuts much earlier — this is the upper bound for bodies whose orbits don't close within the horizon.")]
    public int maxSimSteps = 30000;
    [Tooltip("Simulated seconds per integration step. Smaller = more accurate (semi-implicit Euler error scales with step²). 0.1 keeps low-altitude ship orbits as proper ellipses instead of straight-line drift.")]
    public float simTimeStep = 0.1f;

    [Header("Visuals")]
    // 8× thinner than legacy (0.0035 / 25). User feedback: still too thick
    // at typical zoom — wanted paths, not tubes. Tuned in two passes.
    public float widthFraction = 0.0004375f;    // fraction of camera→body distance
    public float minWidth = 3.125f;
    [Range(0.5f, 0.99f)]
    [Tooltip("Fraction of one orbital period rendered as trail. 0.94 ≈ 338° arc behind the body, with the remaining ~22° as a visual gap.")]
    public float trailArcFraction = 0.94f;
    public Color planetColor    = new Color(0.36f, 0.85f, 1f, 0.9f);
    public Color planetEndColor = new Color(0.36f, 0.85f, 1f, 0f);
    public Color moonColor      = new Color(1f, 0.78f, 0.45f, 0.85f);
    public Color moonEndColor   = new Color(1f, 0.78f, 0.45f, 0f);
    public Color shipColor      = new Color(0.45f, 1f, 0.55f, 0.95f);
    public Color shipEndColor   = new Color(0.45f, 1f, 0.55f, 0f);

    public Camera viewCamera;
    // Visible = user's toggle state. Persists across map close/open so the
    // player doesn't have to re-toggle orbit lines every time they enter
    // the map. _mapOpen is the gating "should we actually render right now"
    // bit — when the map is closed, lines are silenced even if Visible is
    // true.
    public bool Visible { get; private set; }
    bool _mapOpen;

    class OrbitEntry
    {
        public CelestialBody body;          // null for the ship/player entry
        public Ship shipRef;                // non-null only on a ship entry
        public Rigidbody playerRb;          // non-null only on the player entry
        public Transform playerXform;       // non-null only on the player entry (for anchor lookup)
        public PlayerController playerPC;   // non-null only on the player entry (for WorldVelocity)
        public CelestialBody primary;
        public LineRenderer line;
        public Vector3[] relativeOffsets;   // anchor position − primary position, per sim step
        public int orbitLength;             // # valid entries in relativeOffsets (one full period for closed; arc length for open)
        public int trailLength;             // # entries actually rendered (= orbitLength * trailArcFraction)
        public Vector3[] worldBuffer;       // reusable
        public int lastIdx;                 // hint for closest-index search
        public bool drawFullPath;
        // Cache of the most recent successful Kepler computation. Used as
        // fallback if a particular frame's solve returns degenerate (e.g.,
        // a one-frame radial-trajectory state right after a physics tick
        // half-updates pos/vel, or NaN from float-precision loss at huge
        // world coords). Without this, the line would briefly flash as a
        // straight degenerate segment between toggles.
        public Vector3[] cachedRelativeOffsets;
        public int cachedCount;
        public bool cachedIsClosed;
    }

    readonly List<OrbitEntry> orbits = new List<OrbitEntry>();
    CelestialBody[] bodies;
    EndlessManager _endless;
    bool _subscribedToOriginShifts;

    public void Init(CelestialBody[] allBodies)
    {
        bodies = allBodies;
        TrySubscribeOriginShifts();
    }

    void OnEnable()  { TrySubscribeOriginShifts(); }
    void OnDisable() { TryUnsubscribeOriginShifts(); }

    void TrySubscribeOriginShifts()
    {
        if (_subscribedToOriginShifts) return;
        if (_endless == null) _endless = FindObjectOfType<EndlessManager>();
        if (_endless == null) return;
        _endless.PostFloatingOriginUpdate += HandlePostFloatingOriginShift;
        _subscribedToOriginShifts = true;
    }

    void TryUnsubscribeOriginShifts()
    {
        if (!_subscribedToOriginShifts || _endless == null) return;
        _endless.PostFloatingOriginUpdate -= HandlePostFloatingOriginShift;
        _subscribedToOriginShifts = false;
    }

    // EndlessManager fires this every LateUpdate after its origin shift step
    // (the shift itself only fires past the distance threshold). Forcing a
    // line-position refresh here means even if Unity dispatches our LateUpdate
    // before EndlessManager's on a given frame, this catch-up rewrites every
    // LineRenderer's positions with post-shift coordinates AND re-marks the
    // bounds dirty — preventing the "frustum-culled until re-toggle" failure
    // mode caused by stale world-space bounds at far-from-origin positions.
    void HandlePostFloatingOriginShift()
    {
        if (!Visible || !_mapOpen) return;
        UpdateLines();
    }

    public void Toggle()
    {
        if (Visible) Hide(); else Show();
    }

    // Called by SolarSystemMapController when the map opens/closes. Decouples
    // user toggle state (Visible) from actual rendering — closing the map
    // silences the lines but keeps Visible intact so re-opening restores them.
    public void SetMapOpen(bool mapOpen)
    {
        if (_mapOpen == mapOpen) return;
        _mapOpen = mapOpen;
        if (mapOpen && Visible)
        {
            // Refresh ship entries before the first rendered frame — handles
            // ships built / dish-equipped while the map was closed (the initial
            // Show() snapshot only saw ships that existed at toggle time).
            RefreshShipOrbits();
            // Same for the player entry — added so the AI's "PLAYER" map
            // tab can show the same Kepler conic the ships use.
            RefreshPlayerOrbit();
            // Re-rendering after a map close: refresh positions BEFORE
            // enabling line renderers so the first rendered frame already has
            // correct geometry (no diagonal flash from zero-position state).
            UpdateLines();
            SetLineRenderersEnabled(true);
        }
        else
        {
            SetLineRenderersEnabled(false);
        }
    }

    // Tear down stale ship entries (ship gone, or dish removed) and add
    // entries for any dish-equipped ship that doesn't have one yet. Moon /
    // planet entries are left alone — those don't change at runtime.
    void RefreshShipOrbits()
    {
        // Remove stale entries. An entry is stale only if NONE of its
        // target refs are set — body, shipRef, AND playerRb all null.
        // Player entries must survive this pass (RefreshPlayerOrbit owns
        // their lifecycle).
        for (int i = orbits.Count - 1; i >= 0; i--)
        {
            var o = orbits[i];
            if (o == null || (o.shipRef == null && o.body == null && o.playerRb == null))
            {
                if (o != null && o.line != null) Destroy(o.line.gameObject);
                orbits.RemoveAt(i);
                continue;
            }
            if (o.shipRef == null) continue; // not a ship entry, leave it
            // Stale: ship destroyed, or dish removed.
            var detach = o.shipRef.GetComponent<ThrusterDetachOnImpact>();
            if (detach == null || !detach.HasDishAttached)
            {
                if (o.line != null) Destroy(o.line.gameObject);
                orbits.RemoveAt(i);
            }
        }

        // Add new entries for any dish-equipped ship without one.
        var ships = FindObjectsOfType<Ship>();
        if (ships == null) return;
        foreach (var ship in ships)
        {
            if (ship == null) continue;
            var detach = ship.GetComponent<ThrusterDetachOnImpact>();
            if (detach == null || !detach.HasDishAttached) continue;
            bool already = false;
            for (int i = 0; i < orbits.Count; i++)
            {
                if (orbits[i] != null && orbits[i].shipRef == ship) { already = true; break; }
            }
            if (already) continue;
            var rbShip = ship.GetComponent<Rigidbody>();
            if (rbShip == null) continue;
            BuildShipKeplerOrbit(ship, rbShip);
        }
    }

    void SetLineRenderersEnabled(bool enabled)
    {
        for (int i = 0; i < orbits.Count; i++)
            if (orbits[i].line != null) orbits[i].line.enabled = enabled;
    }

    public void Show()
    {
        BuildAndSimulate();
        Visible = true;
        // CRITICAL ORDER: fill positions BEFORE enabling renderers. Unity's
        // LineRenderer can render its zero-position default geometry for a
        // frame if enabled with stale data; computing positions first avoids
        // the brief diagonal/straight-line flash on toggle.
        UpdateLines();
        if (_mapOpen) SetLineRenderersEnabled(true);
    }

    public void Hide()
    {
        Visible = false;
        SetLineRenderersEnabled(false);
    }

    void LateUpdate()
    {
        if (!Visible || !_mapOpen) return;
        UpdateLines();
    }

    // ── One-shot setup ────────────────────────────────────────────────────
    // Simulates the full N-body system forward, builds primary-relative orbit
    // tables, and creates the LineRenderers.
    void BuildAndSimulate()
    {
        for (int i = 0; i < orbits.Count; i++)
            if (orbits[i] != null && orbits[i].line != null) Destroy(orbits[i].line.gameObject);
        orbits.Clear();

        if (bodies == null || bodies.Length == 0) return;
        int n = bodies.Length;

        int sunIdx = -1;
        for (int i = 0; i < n; i++)
            if (bodies[i] != null && bodies[i].bodyType == CelestialBody.BodyType.Sun) { sunIdx = i; break; }

        var pos = new Vector3[n];
        var vel = new Vector3[n];
        var mass = new float[n];
        for (int i = 0; i < n; i++)
        {
            if (bodies[i] == null) continue;
            pos[i] = bodies[i].transform.position;
            vel[i] = bodies[i].velocity;
            mass[i] = bodies[i].mass;
        }

        // Pick primary per body: planet → sun, moon → nearest planet.
        var primaryIdx = new int[n];
        for (int i = 0; i < n; i++)
        {
            primaryIdx[i] = -1;
            if (bodies[i] == null) continue;
            switch (bodies[i].bodyType)
            {
                case CelestialBody.BodyType.Sun: break;
                case CelestialBody.BodyType.Planet: primaryIdx[i] = sunIdx; break;
                case CelestialBody.BodyType.Moon:
                {
                    float best = float.MaxValue; int bestIdx = -1;
                    for (int j = 0; j < n; j++)
                    {
                        if (bodies[j] == null) continue;
                        if (bodies[j].bodyType != CelestialBody.BodyType.Planet) continue;
                        float d = (pos[j] - pos[i]).sqrMagnitude;
                        if (d < best) { best = d; bestIdx = j; }
                    }
                    primaryIdx[i] = bestIdx >= 0 ? bestIdx : sunIdx;
                    break;
                }
            }
        }

        // Allocate and run the simulation. Semi-implicit Euler — matches the
        // game's own NBodySimulation kinematics, so orbits look identical.
        var paths = new Vector3[n][];
        for (int i = 0; i < n; i++) paths[i] = new Vector3[maxSimSteps];

        // Ship orbits are computed analytically (Kepler, see
        // BuildShipKeplerOrbit below). Each Ship in the scene with a
        // satellite dish attached gets its own predicted orbit line — the
        // dish acts as the "uplink" the map listens to, so a ship without
        // the dish is invisible on the map. Players can build a fleet of
        // tracked satellites by buying dishes for each ship.
        Ship[] ships = FindObjectsOfType<Ship>();

        var curPos = (Vector3[])pos.Clone();
        var curVel = (Vector3[])vel.Clone();
        float G = Universe.gravitationalConstant;
        for (int step = 0; step < maxSimSteps; step++)
        {
            for (int i = 0; i < n; i++)
            {
                if (bodies[i] == null) continue;
                Vector3 acc = Vector3.zero;
                for (int j = 0; j < n; j++)
                {
                    if (i == j || bodies[j] == null) continue;
                    Vector3 d = curPos[j] - curPos[i];
                    float sqrDst = d.sqrMagnitude;
                    if (sqrDst < 0.0001f) continue;
                    acc += d / Mathf.Sqrt(sqrDst) * G * mass[j] / sqrDst;
                }
                curVel[i] += acc * simTimeStep;
            }
            for (int i = 0; i < n; i++)
            {
                if (bodies[i] == null) continue;
                curPos[i] += curVel[i] * simTimeStep;
                paths[i][step] = curPos[i];
            }
        }

        // Build per-body relative paths.
        // Planets → analytical Kepler ellipse around the sun (KSP-style:
        //   one perfect closed ring, identical every toggle).
        // Moons   → simulated trail with period detection + walking trail
        //   (the sun's perturbation is too strong for Kepler to be accurate
        //   on a body orbiting a planet, so keep the simulated path).
        for (int i = 0; i < n; i++)
        {
            if (bodies[i] == null) continue;
            int p = primaryIdx[i];
            if (p < 0) continue;

            bool isPlanet = bodies[i].bodyType == CelestialBody.BodyType.Planet;
            bool isMoon   = bodies[i].bodyType == CelestialBody.BodyType.Moon;

            Vector3[] rel;
            int orbitLength;
            int trailLength;
            bool drawFullPath;

            if (isPlanet)
            {
                // Planets render as a continuous-update Kepler ellipse around
                // the sun (recomputed each LateUpdate). At toggle time we
                // just preallocate a buffer big enough for max samples + 1;
                // UpdateLines fills it from current state every frame so
                // perturbations from other bodies don't show up as drift.
                const int kKeplerSamples = 361;
                rel = new Vector3[kKeplerSamples];
                orbitLength = kKeplerSamples;
                trailLength = kKeplerSamples;
                drawFullPath = true;
            }
            else
            {
                // Moon (or other): simulated trail.
                rel = new Vector3[maxSimSteps];
                for (int s = 0; s < maxSimSteps; s++)
                    rel[s] = paths[i][s] - paths[p][s];

                orbitLength = DetectPeriod(rel, maxSimSteps);
                if (orbitLength < maxSimSteps)
                {
                    var trimmed = new Vector3[orbitLength];
                    System.Array.Copy(rel, trimmed, orbitLength);
                    rel = trimmed;
                }
                trailLength = Mathf.Clamp((int)(orbitLength * Mathf.Clamp(trailArcFraction, 0.5f, 0.99f)), 2, orbitLength);
                drawFullPath = false;
            }

            var go = new GameObject("Orbit_" + bodies[i].bodyName);
            go.transform.SetParent(transform, worldPositionStays: false);

            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = false;
            // Kepler entries: positionCount stays 0 here. UpdateLines sets it
            // atomically with SetPositions on the first frame, so the renderer
            // never has zero-position geometry queued. Moon trails need their
            // full count up front because the walking-trail render path
            // doesn't touch positionCount.
            line.positionCount = drawFullPath ? 0 : trailLength;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.numCapVertices = 2;
            line.material = new Material(Shader.Find("Sprites/Default"));

            if (isPlanet)
            {
                // Full closed ellipse — uniform brightness so the ring looks
                // like a map orbit, not a directional trail.
                line.startColor = planetColor;
                line.endColor   = planetColor;
            }
            else
            {
                // Moon: directional gradient (bright leading edge, fading tail).
                line.startColor = isMoon ? moonColor : planetColor;
                line.endColor   = isMoon ? moonEndColor : planetEndColor;
            }
            line.enabled = false;

            orbits.Add(new OrbitEntry
            {
                body = bodies[i],
                primary = bodies[p],
                line = line,
                relativeOffsets = rel,
                orbitLength = orbitLength,
                trailLength = trailLength,
                worldBuffer = new Vector3[trailLength],
                lastIdx = 0,
                drawFullPath = drawFullPath,
                cachedRelativeOffsets = drawFullPath ? new Vector3[rel.Length] : null,
                cachedCount = 0,
                cachedIsClosed = false,
            });
        }

        // ── Ship trajectories (analytical Kepler, per-ship) ──────────────
        // Only ships with a satellite dish attached are tracked. Player can
        // see multiple ship orbits at once if they own multiple satellite-
        // equipped ships.
        if (ships != null)
        {
            foreach (var ship in ships)
            {
                if (ship == null) continue;
                var detach = ship.GetComponent<ThrusterDetachOnImpact>();
                if (detach == null) continue;          // no part-tracker on this ship — skip
                if (!detach.HasDishAttached) continue; // no uplink → no map blip
                var rbShip = ship.GetComponent<Rigidbody>();
                if (rbShip == null) continue;
                BuildShipKeplerOrbit(ship, rbShip);
            }
        }
    }

    // Set up the ship orbit entry. The actual Kepler conic is recomputed
    // every frame in UpdateLines from current ship+primary state, so this
    // method just creates the LineRenderer and reserves a buffer.
    // Player orbit entry — single entry, kept across map opens. Rebuilt
    // only if the player exists and we don't already have one.
    void RefreshPlayerOrbit()
    {
        // Drop the stale entry if its target is gone.
        for (int i = orbits.Count - 1; i >= 0; i--)
        {
            var o = orbits[i];
            if (o == null) { orbits.RemoveAt(i); continue; }
            if (o.playerRb == null && o.playerXform == null) continue;
            // Live re-check — destroyed/disabled player Transform → drop entry.
            if (o.playerRb == null || o.playerRb.transform == null)
            {
                if (o.line != null) Destroy(o.line.gameObject);
                orbits.RemoveAt(i);
            }
        }

        // Build a player entry if one doesn't exist yet.
        bool already = false;
        for (int i = 0; i < orbits.Count; i++)
            if (orbits[i] != null && orbits[i].playerRb != null) { already = true; break; }
        if (already) return;

        var pc = FindObjectOfType<PlayerController>();
        if (pc == null) return;
        var rb = pc.GetComponent<Rigidbody>();
        if (rb == null) return;
        BuildPlayerKeplerOrbit(pc, pc.transform, rb);
    }

    void BuildPlayerKeplerOrbit(PlayerController playerPC, Transform playerXform, Rigidbody playerRb)
    {
        // Same primary-resolution rule the ship uses — nearest non-Sun body.
        CelestialBody primary = null;
        float bestSqr = float.MaxValue;
        Vector3 pPos = playerRb.position;
        if (bodies != null)
        {
            for (int i = 0; i < bodies.Length; i++)
            {
                var b = bodies[i];
                if (b == null) continue;
                float d = (b.Position - pPos).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; primary = b; }
            }
        }
        if (primary == null) return;

        const int kKeplerSamples = 361;
        var rel = new Vector3[kKeplerSamples];

        var go = new GameObject("Orbit_Player");
        go.transform.SetParent(transform, worldPositionStays: false);

        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = false;
        line.positionCount = 0;
        line.alignment = LineAlignment.View;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.numCapVertices = 2;
        line.material = new Material(Shader.Find("Sprites/Default"));
        // HAL red so the player line is visually distinct from ship lines
        // (green) and planet lines (their own per-body colour).
        line.startColor = new Color(1f, 0.13f, 0.05f, 1f);
        line.endColor   = new Color(1f, 0.13f, 0.05f, 0.35f);
        line.enabled = false;

        orbits.Add(new OrbitEntry
        {
            body = null,
            shipRef = null,
            playerRb = playerRb,
            playerXform = playerXform,
            playerPC = playerPC,
            primary = primary,
            line = line,
            relativeOffsets = rel,
            orbitLength = kKeplerSamples,
            trailLength = kKeplerSamples,
            worldBuffer = new Vector3[kKeplerSamples],
            lastIdx = 0,
            drawFullPath = true,
            cachedRelativeOffsets = new Vector3[kKeplerSamples],
            cachedCount = 0,
            cachedIsClosed = false,
        });
    }

    void BuildShipKeplerOrbit(Ship ship, Rigidbody shipRb)
    {
        CelestialBody primary = null;
        float bestSqr = float.MaxValue;
        Vector3 shipPos = shipRb.position;
        if (bodies != null)
        {
            for (int i = 0; i < bodies.Length; i++)
            {
                var b = bodies[i];
                if (b == null) continue;
                float d = (b.Position - shipPos).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; primary = b; }
            }
        }
        if (primary == null) return;

        const int kKeplerSamples = 361;
        var rel = new Vector3[kKeplerSamples];

        var go = new GameObject("Orbit_Ship");
        go.transform.SetParent(transform, worldPositionStays: false);

        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = false;
        // Same as planets: start at 0; UpdateLines fills count + positions
        // atomically on first frame so there's no diagonal flash.
        line.positionCount = 0;
        line.alignment = LineAlignment.View;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.numCapVertices = 2;
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.startColor = shipColor;
        line.endColor = shipEndColor;
        line.enabled = false;

        orbits.Add(new OrbitEntry
        {
            body = null,
            shipRef = ship,
            primary = primary,
            line = line,
            relativeOffsets = rel,
            orbitLength = kKeplerSamples,
            trailLength = kKeplerSamples,
            worldBuffer = new Vector3[kKeplerSamples],
            lastIdx = 0,
            drawFullPath = true,
            cachedRelativeOffsets = new Vector3[kKeplerSamples],
            cachedCount = 0,
            cachedIsClosed = false,
        });
    }

    // Closed-form orbit computation (KSP-style patched conics). Writes
    // primary-relative orbit positions into outBuffer; returns the number
    // of valid entries (or 0 if degenerate). isClosed = true for e<1.
    //
    // Zero allocation: caller pre-sizes outBuffer to (samples + 1) and we
    // re-use it every frame. This is what lets us recompute the orbit per-
    // frame instead of caching at toggle — the live computation always
    // reflects the body's CURRENT state, so perturbations don't accumulate
    // into "stale ellipse" drift.
    static int ComputeKeplerOrbitPath(Vector3 bodyPos, Vector3 bodyVel,
                                      Vector3 primaryPos, Vector3 primaryVel,
                                      float primaryMass, float primaryRadius,
                                      int samples, Vector3[] outBuffer,
                                      out bool isClosed)
    {
        isClosed = false;
        Vector3 r = bodyPos - primaryPos;
        Vector3 v = bodyVel - primaryVel;
        float mu = Universe.gravitationalConstant * primaryMass;
        float rMag = r.magnitude;
        if (rMag < 0.001f || mu < 1e-6f) return 0;

        Vector3 h = Vector3.Cross(r, v);
        float hMag = h.magnitude;
        if (hMag < 0.001f) return 0;

        Vector3 eVec = Vector3.Cross(v, h) / mu - r / rMag;
        float e = eVec.magnitude;
        float p = (hMag * hMag) / mu;
        if (p < 0.001f) return 0;

        Vector3 nHat = h / hMag;
        Vector3 xHat = e > 1e-4f ? (eVec / e) : (r / rMag);
        Vector3 yHat = Vector3.Cross(nHat, xHat).normalized;

        float dotR = Vector3.Dot(r, xHat);
        float crossR = Vector3.Dot(r, yHat);
        float thetaStart = Mathf.Atan2(crossR, dotR);
        float vTang = Vector3.Dot(v, yHat);
        float dir = vTang >= 0f ? 1f : -1f;

        int count = 0;
        if (e < 1f)
        {
            isClosed = true;
            float thetaStep = (Mathf.PI * 2f / samples) * dir;
            for (int i = 0; i <= samples && count < outBuffer.Length; i++)
            {
                float theta = thetaStart + thetaStep * i;
                float rTheta = p / (1f + e * Mathf.Cos(theta));
                if (rTheta < primaryRadius) break;
                outBuffer[count++] = xHat * (rTheta * Mathf.Cos(theta)) + yHat * (rTheta * Mathf.Sin(theta));
            }
        }
        else
        {
            float thetaInf = Mathf.Acos(Mathf.Clamp(-1f / e, -1f, 1f));
            float thetaEnd = (thetaInf - 0.05f) * dir;
            if (dir > 0f && thetaEnd <= thetaStart) thetaEnd = thetaStart + 0.1f;
            if (dir < 0f && thetaEnd >= thetaStart) thetaEnd = thetaStart - 0.1f;
            for (int i = 0; i <= samples && count < outBuffer.Length; i++)
            {
                float t = (float)i / samples;
                float theta = Mathf.Lerp(thetaStart, thetaEnd, t);
                float rTheta = p / (1f + e * Mathf.Cos(theta));
                if (rTheta < primaryRadius) break;
                if (rTheta > rMag * 50f) break;
                outBuffer[count++] = xHat * (rTheta * Mathf.Cos(theta)) + yHat * (rTheta * Mathf.Sin(theta));
            }
        }
        return count >= 2 ? count : 0;
    }

    // First index s > 0 at which rel[s] returns close to rel[0], after the
    // body has clearly left the starting neighborhood. Falls back to the full
    // simulation horizon if no period is detected (open trajectory).
    static int DetectPeriod(Vector3[] rel, int count)
    {
        Vector3 start = rel[0];
        float startMag = start.magnitude;
        if (startMag < 0.01f) return count;
        // "Returned" = within 3% of the starting radius. "Left" = at least
        // 30% of starting radius away (so we don't fire on adjacent samples).
        float returnSqr = (startMag * 0.03f) * (startMag * 0.03f);
        float leftSqr   = (startMag * 0.30f) * (startMag * 0.30f);
        bool hasLeft = false;
        for (int s = 1; s < count; s++)
        {
            float dSqr = (rel[s] - start).sqrMagnitude;
            if (!hasLeft)
            {
                if (dSqr > leftSqr) hasLeft = true;
            }
            else if (dSqr < returnSqr)
            {
                return s;
            }
        }
        return count;
    }

    // ── Per-frame ─────────────────────────────────────────────────────────
    // For each orbit: find where the body currently sits on the stored orbit,
    // then render a trail of `trailLength` points walking BACKWARD from that
    // index (wrapping at orbit boundaries) — head bright at the planet,
    // tail fading to transparent ~22° before reaching the planet again.
    void UpdateLines()
    {
        Vector3 camPos = viewCamera != null ? viewCamera.transform.position : Vector3.zero;
        for (int oIdx = 0; oIdx < orbits.Count; oIdx++)
        {
            var o = orbits[oIdx];
            if (o.line == null || o.primary == null) continue;
            // Anchor: planet/moon → its own transform; ship entry → ship
            // transform; player entry → player transform.
            Transform anchor = o.body != null
                ? o.body.transform
                : (o.shipRef != null ? o.shipRef.transform
                : (o.playerXform != null ? o.playerXform : null));
            if (anchor == null) continue;

            Vector3 anchorPos  = anchor.position;
            Vector3 primaryPos = o.primary.transform.position;

            if (o.drawFullPath)
            {
                // KSP-style: recompute the Kepler conic from the body's
                // CURRENT state every frame. The orbit always reflects "what
                // would happen right now" — no stale-cache drift, no need to
                // re-toggle. Both planets (around the sun) and ship (around
                // its nearest body) go through this path.
                Vector3 bodyPos, bodyVel;
                if (o.shipRef != null)
                {
                    // Live dish check: if the player removed the dish since
                    // the orbit was built, hide its line immediately. No
                    // dish = no map uplink, regardless of past state.
                    var detach = o.shipRef.GetComponent<ThrusterDetachOnImpact>();
                    if (detach != null && !detach.HasDishAttached)
                    {
                        o.line.positionCount = 0;
                        continue;
                    }
                    var rb = o.shipRef.GetComponent<Rigidbody>();
                    if (rb == null) { o.line.positionCount = 0; continue; }
                    bodyPos = rb.position;
                    bodyVel = rb.velocity;
                }
                else if (o.playerRb != null)
                {
                    // Player entry — position from the rigidbody, but
                    // velocity must come from PlayerController.WorldVelocity
                    // because the player moves via rb.MovePosition which
                    // BYPASSES the velocity integrator. rb.velocity alone
                    // reads near-zero while walking; the actual world motion
                    // lives in (rb.velocity + smoothVelocity) which is what
                    // WorldVelocity returns. Without this the Kepler conic
                    // collapses to a degenerate stub whenever the player is
                    // moving with their planet (i.e. always on the ground).
                    bodyPos = o.playerRb.position;
                    bodyVel = o.playerPC != null ? o.playerPC.WorldVelocity : o.playerRb.velocity;
                }
                else
                {
                    bodyPos = o.body.Position;
                    bodyVel = o.body.velocity;
                }

                int samples = o.relativeOffsets.Length - 1;
                int count = ComputeKeplerOrbitPath(
                    bodyPos, bodyVel,
                    o.primary.Position, o.primary.velocity,
                    o.primary.mass, o.primary.radius,
                    samples, o.relativeOffsets, out bool isClosed);

                // NaN guard: if float precision lost a number, one sample
                // having NaN propagates to a degenerate "line through origin"
                // visual. Sample-check the first valid entry.
                if (count >= 2 && (float.IsNaN(o.relativeOffsets[0].x) ||
                                   float.IsInfinity(o.relativeOffsets[0].x)))
                    count = 0;

                Vector3[] useRel;
                int useCount;
                bool useIsClosed;
                if (count >= 2)
                {
                    // Fresh Kepler succeeded — render it AND cache.
                    useRel = o.relativeOffsets;
                    useCount = count;
                    useIsClosed = isClosed;
                    System.Array.Copy(o.relativeOffsets, o.cachedRelativeOffsets, count);
                    o.cachedCount = count;
                    o.cachedIsClosed = isClosed;
                }
                else if (o.cachedCount >= 2)
                {
                    // Solve failed this frame — render the last known good
                    // orbit shape anchored to the primary's CURRENT position.
                    // No straight-line flicker.
                    useRel = o.cachedRelativeOffsets;
                    useCount = o.cachedCount;
                    useIsClosed = o.cachedIsClosed;
                }
                else
                {
                    // No cache yet AND current solve failed — hide for now.
                    o.line.positionCount = 0;
                    continue;
                }

                for (int s = 0; s < useCount; s++)
                    o.worldBuffer[s] = primaryPos + useRel[s];
                if (o.line.positionCount != useCount) o.line.positionCount = useCount;
                o.line.SetPositions(o.worldBuffer);

                // Colour: closed orbit uses uniform body colour (looks like a
                // map orbit ring); open trajectory uses bright→fade gradient
                // (gives a sense of forward direction for a hyperbolic arc).
                if (o.shipRef != null)
                {
                    o.line.startColor = shipColor;
                    o.line.endColor = useIsClosed ? shipColor : shipEndColor;
                }
                else
                {
                    o.line.startColor = planetColor;
                    o.line.endColor = useIsClosed ? planetColor : planetEndColor;
                }
            }
            else
            {
                // Planet/moon trail: walk backward from the body's current
                // angular position so the line trails behind motion.
                Vector3 currentRel = anchorPos - primaryPos;
                int closest = FindClosestIndex(o.relativeOffsets, o.orbitLength, currentRel, o.lastIdx);
                o.lastIdx = closest;

                int len = o.trailLength;
                int orbitLen = o.orbitLength;
                for (int k = 0; k < len; k++)
                {
                    int idx = closest - k;
                    while (idx < 0) idx += orbitLen;
                    idx %= orbitLen;
                    o.worldBuffer[k] = primaryPos + o.relativeOffsets[idx];
                }
                o.line.SetPositions(o.worldBuffer);
            }

            float dist = (camPos - anchorPos).magnitude;
            float w = Mathf.Max(dist * widthFraction, minWidth);
            o.line.startWidth = w;
            o.line.endWidth = w;
        }
    }

    // Closest-index lookup with a windowed search around `hint`. Falls back
    // to a full scan only when the hint window doesn't contain a clearly
    // close point — handles toggle-on (hint=0) and ensures correctness.
    static int FindClosestIndex(Vector3[] arr, int len, Vector3 target, int hint)
    {
        const int kWindow = 64;
        int loBound = hint - kWindow;
        int hiBound = hint + kWindow;
        int best = hint % len;
        float bestSqr = (arr[best] - target).sqrMagnitude;
        for (int k = loBound; k <= hiBound; k++)
        {
            int i = k;
            while (i < 0) i += len;
            i %= len;
            float d = (arr[i] - target).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = i; }
        }
        // Sanity check: if windowed best is suspiciously far (compared to the
        // sampling resolution), the hint was wrong — do a full scan.
        // Threshold: 4× the typical step distance.
        Vector3 step = arr[1] - arr[0];
        float stepSqr = step.sqrMagnitude * 16f;
        if (bestSqr > stepSqr)
        {
            for (int i = 0; i < len; i++)
            {
                float d = (arr[i] - target).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = i; }
            }
        }
        return best;
    }
}
