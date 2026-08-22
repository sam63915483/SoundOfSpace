using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// CPU-driven instanced grass for a procedural (non-Unity-Terrain) planet via
/// Graphics.DrawMeshInstanced. (Reverted from the GPU compute/indirect version —
/// on this CPU-bound game the compute path + per-frame buffer uploads were
/// slower; this draws the visible cells directly each frame.)
///
///   • Per-blade ground raycast (no float, ray ignores player), stable radial
///     orientation on every side of the sphere, base seated per-mesh.
///   • Incremental streaming keyed to PLAYER BODY position; placed grass never
///     recomputed (no shifting); nearest-first across cube seams; off water,
///     off steep slopes, out from under the cabin.
///   • Per-cell CPU frustum culling — only on-screen cells are drawn.
///   • Lit by the sun (dims at night) via CG_SimpleGrass.
/// Decorative only.
/// </summary>
// Execution order 100 keeps the per-frame work (below, in LateUpdate) running
// AFTER EndlessManager's floating-origin shift (default order 0, also LateUpdate).
// Graphics.DrawMeshInstanced captures its blade matrices at call time, so if the
// draw is submitted before the origin shift, the grass renders one frame at the
// stale pre-shift planet position (~1 km off-screen) — a visible one-frame blink
// every time a shift fires. Submitting after the shift keeps it locked to the
// planet. See also the Draw() notes.
[DefaultExecutionOrder(100)]
public class InstancedGrassRenderer : MonoBehaviour
{
    [Header("Planet")]
    public string onlyBodyName = "Humble Abode";

    [Header("Grass meshes & material")]
    public Mesh[] grassMeshes;
    [Tooltip("Material using the CartoonGrass/SimpleGrass shader (GPU instancing on).")]
    public Material grassMaterial;

    [Header("Coverage / density")]
    public int seed = 45678;
    public float spawnRadius = 80f;
    public float cellSize = 3f;
    [Range(0f, 1f)] public float cellCoverage = 0.97f;
    public int bladesPerCell = 5;

    [Header("Variation / seating")]
    public float minScale = 1.0f;
    public float maxScale = 2.0f;
    [Range(0f, 90f)] public float maxSurfaceAngle = 30f;
    public float waterMargin = 1.0f;
    public float surfaceLift = 0.03f;
    public float surfaceRayHeight = 100f;

    [Header("Streaming / rendering")]
    public float updateInterval = 0.1f;
    public int maxCellsPerUpdate = 120;
    public LayerMask groundMask = ~0;
    public bool receiveShadows = true;
    public bool frustumCull = true;

    [Header("Water-relative height band")]
    [Tooltip("Upper limit on grass elevation above sea level (metres). Keeps grass off rocky hills/cliffs. Only applies on bodies that have an ocean; waterMargin is the lower edge of the band.")]
    public float maxHeightAboveWater = 20f;

    [Header("Patches & slope shaping")]
    [Tooltip("Cells per patch side. Neighbouring cells share a patch value so grass forms clumps with bare gaps between, instead of a uniform carpet. Larger = bigger patches.")]
    public int patchCells = 4;
    [Range(0f, 1f)]
    [Tooltip("Fraction of patches that contain grass. Lower = more bare ground between clumps.")]
    public float patchDensity = 0.6f;
    [Range(0f, 1f)]
    [Tooltip("On the steepest allowed slope, blades-per-cell drops to this fraction — sparser grass on bumpy/sloped ground. 1 = no thinning.")]
    public float slopeBladeFloor = 0.35f;
    [Range(0f, 1f)]
    [Tooltip("On the steepest allowed slope, blade scale drops to this fraction — smaller grass on slopes. 1 = no shrinking.")]
    public float slopeScaleFloor = 0.7f;
    [Range(0f, 1f)]
    [Tooltip("How strongly grass tilts to match the ground slope. 0 = always radial/upright (looks detached on hills), 1 = fully perpendicular to the surface.")]
    public float slopeConform = 0.8f;

    [Header("Distance density fade")]
    [Tooltip("Draw fewer blades per cell the further it is from you, filling back in as you approach. Cuts GPU overdraw and keeps distant grass light.")]
    public bool densityFade = true;
    [Range(0f, 1f)]
    [Tooltip("Fraction of spawnRadius at which the fade starts. Within this distance grass is full density; from here to the edge it thins to nothing.")]
    public float densityFadeStartFrac = 0.45f;
    [Tooltip("Grass cells within this distance of the player are NEVER frustum-culled. Fixes near grass popping out when walking up a slope (the cell's cull box sits at the sphere surface, below hilltop blades).")]
    public float noCullRadius = 15f;
    [Range(0f, 1f)]
    [Tooltip("Lowest density the distance-fade may thin grass to. Keep ~0.5-0.7 so distant / hill-edge grass stays solid instead of going see-through. 1 = no fade.")]
    public float densityFadeFloor = 0.6f;

    [Header("Mini infill patches")]
    [Tooltip("Cells per mini-patch side — small dense clumps that fill the bare gaps between the main patches. Smaller = tinier clumps.")]
    public int miniPatchCells = 3;
    [Range(0f, 1f)]
    [Tooltip("Coverage of the small infill patches in the gaps. 0 = none (just the big patches). Keep low for small, sparse clumps.")]
    public float miniPatchDensity = 0.2f;

    [Header("Per-blade ground seating")]
    [Tooltip("Re-raycast every blade individually onto the terrain so each one sits exactly on the surface you see. The cheaper single-raycast-per-cell path scatters blades on the cell-centre's tangent plane, which floats them over dips and sinks them into bumps wherever a 3 m cell straddles one of the low-poly planet's ~7 m facets (the cause of grass floating around the village and clipping on the far side). Adds a few raycasts per streamed cell, only while moving into new ground. Turn off to revert to the faster, less accurate seating.")]
    public bool perBladeReseat = true;

    [Header("Baked grass (frozen positions)")]
    [Tooltip("Optional. A .bytes file produced by Tools ▸ Bake Planet ▸ Bake Grass. When set, grass is NOT raycast/streamed at runtime — it's loaded from these frozen, editor-verified positions and merely activated/deactivated as you move. This makes the floating-on-respawn bug structurally impossible (no runtime raycast can ever hit the placeholder sphere) and is identical every load. Leave EMPTY to fall back to the live streaming behaviour.")]
    public TextAsset bakedGrass;

    [Header("Horizon wash fix (depth silhouette dilation)")]
    [Range(0f, 4f)]
    [Tooltip("Fattens each blade by this many screen pixels in the DEPTH pre-pass only (never in colour). The atmosphere post tints pixels by depth read from a single-sampled depth texture; with MSAA on, a thin far blade anti-aliases in colour but misses the depth sample, so the atmosphere washes it to sky colour (the 'glass'/see-through blades on the horizon). Dilating the depth silhouette ~1-2px makes the atmosphere read 'near' across the whole blade, killing the wash. 0 = off. Raise if some blades still glass out; lower if grass gets a faint un-hazed fringe.")]
    // With MSAA on, a thin blade anti-aliases into a partly-green pixel in colour
    // but its sliver misses that pixel's single depth sample, so the atmosphere
    // reads "sky" there and washes the blade cyan. This fattens the blade's DEPTH
    // silhouette a couple of px so the depth sample lands inside the blade and the
    // atmosphere reads "near". Only works now that the depth material has
    // instancing enabled (see EnsureDepthCB) — before that the prepass drew
    // nothing and this had no effect at any value. Raise if blades still glass;
    // lower if you see a faint un-hazed fringe. 0 = off.
    public float depthDilatePixels = 0f;

    [Header("Depth pre-pass material (build-critical)")]
    [Tooltip("Material asset using the CartoonGrass/GrassDepth shader with GPU Instancing ENABLED. This MUST be a real asset referenced here — not created in code — or the build's variant stripper (Graphics ▸ Instancing Variants = Strip Unused) drops the shader's INSTANCING_ON variant, the CommandBuffer.DrawMeshInstanced depth pre-pass silently draws nothing, and grass goes see-through against the sky in the BUILD only (the Editor compiles variants on demand, so it looks fine there). Leave empty only as a fallback to the old code-created material.")]
    public Material depthMaterial;

    // ── internals ───────────────────────────────────────────────────────────
    class Cell
    {
        public readonly List<int> mesh = new List<int>();
        public readonly List<Matrix4x4> local = new List<Matrix4x4>();
        public Vector3 localAnchor;
    }

    readonly Dictionary<long, Cell> _active = new Dictionary<long, Cell>(2048);
    // Reuse Cell objects (and their Lists) instead of allocating one per
    // streamed cell — running fast streams hundreds of cells/sec, and the
    // churn was driving periodic GC stalls.
    readonly Stack<Cell> _cellPool = new Stack<Cell>();
    readonly List<long> _scratchRemove = new List<long>();
    struct NewCell { public long id; public int face, cu, cv; public float distSq; public Vector3 localAnchor; }
    readonly List<NewCell> _cand = new List<NewCell>(4096);
    readonly HashSet<long> _seen = new HashSet<long>(4096);
    static readonly System.Comparison<NewCell> ByDist = (a, b) => a.distSq.CompareTo(b.distSq);

    float[] _meshBottomY;
    Matrix4x4[][] _batches;
    int[] _counts;
    readonly Plane[] _planes = new Plane[6];

    CelestialBody _body;
    CelestialBodyGenerator _gen;
    Collider _terrainCollider;   // the planet's "Terrain Mesh" collider — grass seats ONLY on this, never on props
    PlayerController _player;
    bool _playerLayerExcluded;
    float _tick;

    // Stream() steady-state skip. When the viewer hasn't crossed into a new
    // grass cell and the previous scan already placed every in-range cell, the
    // ~3000-cell candidate scan would re-project the whole window only to find
    // everything already active. Cache the last scanned cell + grass distance so
    // we can skip the scan entirely while standing still or moving slowly — that
    // skipped scan was the bulk of grass CPU near spawn.
    int _lastPf = int.MinValue, _lastPcu = int.MinValue, _lastPcv = int.MinValue;
    float _lastStreamRadius = -1f;
    bool _streamSettled;

    bool _meshInit;
    bool _baked;                              // true once a bakedGrass asset is loaded
    Dictionary<long, Cell> _bakedCells;       // frozen cells, keyed by cell id (read-only at runtime)
    float _baseSpawnRadius = -1f;             // authored spawnRadius, captured before the settings scale mutates it

    public int TotalBlades { get { int n = 0; foreach (var c in _active.Values) n += c.local.Count; return n; } }

    void Awake()
    {
        EnsureMeshInit();
        _baseSpawnRadius = spawnRadius;   // capture authored distance before any settings scaling
        groundMask &= ~SpawnerCubeface.WorldSpawnExcludeMask;
        if (grassMeshes.Length == 0) Debug.LogWarning("[InstancedGrassRenderer] No grass meshes; idle.");
        LoadBaked();
    }

    // Mesh-derived arrays (bottom-Y, draw batches). Idempotent so the editor bake
    // path can call it before Awake has ever run.
    void EnsureMeshInit()
    {
        if (_meshInit) return;
        var list = new List<Mesh>();
        if (grassMeshes != null) foreach (var m in grassMeshes) if (m != null) list.Add(m);
        grassMeshes = list.ToArray();

        _meshBottomY = new float[grassMeshes.Length];
        _batches = new Matrix4x4[grassMeshes.Length][];
        _counts = new int[grassMeshes.Length];
        for (int i = 0; i < grassMeshes.Length; i++)
        {
            _batches[i] = new Matrix4x4[1023];
            _meshBottomY[i] = grassMeshes[i].bounds.min.y;
        }
        _meshInit = true;
    }

    // Parse the optional frozen-grass blob. On success, Stream() switches to baked
    // mode: it activates these cells near the player instead of raycasting, so a
    // blade can never be re-seated against the wrong surface.
    void LoadBaked()
    {
        _baked = false;
        _bakedCells = null;
        if (bakedGrass == null) return;
        byte[] bytes = bakedGrass.bytes;
        if (bytes == null || bytes.Length < 16) { Debug.LogWarning("[InstancedGrassRenderer] bakedGrass is empty/too short; using live streaming."); return; }
        try
        {
            using (var ms = new MemoryStream(bytes))
            using (var br = new BinaryReader(ms))
            {
                if (br.ReadInt32() != BakeMagic) { Debug.LogWarning("[InstancedGrassRenderer] bakedGrass bad magic; using live streaming."); return; }
                br.ReadInt32();                       // version (currently unused)
                int bakedSeed = br.ReadInt32();
                int cellCount = br.ReadInt32();
                _bakedCells = new Dictionary<long, Cell>(cellCount);
                for (int c = 0; c < cellCount; c++)
                {
                    long id = br.ReadInt64();
                    var cell = new Cell();
                    cell.localAnchor = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                    int bc = br.ReadInt32();
                    for (int k = 0; k < bc; k++)
                    {
                        cell.mesh.Add(br.ReadInt32());
                        Matrix4x4 m = new Matrix4x4();
                        for (int e = 0; e < 16; e++) m[e] = br.ReadSingle();
                        cell.local.Add(m);
                    }
                    _bakedCells[id] = cell;
                }
                _baked = _bakedCells.Count > 0;
                if (_baked && bakedSeed != seed)
                    Debug.LogWarning($"[InstancedGrassRenderer] baked seed {bakedSeed} differs from current seed {seed}; re-bake to match the live layout.");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[InstancedGrassRenderer] failed to parse bakedGrass; using live streaming. " + e.Message);
            _baked = false;
            _bakedCells = null;
        }
    }

    const int BakeMagic = 0x42535247;  // 'GRSB'

    // How many no-grass volumes were accounted for at the last prune. A prefab
    // dropped in later (or a hole punched a few frames after load) raises the
    // count, which re-runs the pass. Pruning only ever REMOVES blades, so
    // re-running is idempotent.
    int _prunedVolumeCount = -1;

    // Baked grass is FROZEN — no raycast runs, so it has no idea the terrain
    // under it was cut away by a PlanetHolePuncher after the bake. That's how
    // grass ends up floating in mid-air over a cave mouth: the blades are exactly
    // where the bake put them and the ground simply isn't there any more.
    //
    // Rather than force a re-bake every time a cave moves, drop the blades that
    // fall inside a hole (or an explicit NoGrassVolume, which also covers the
    // rock collar sitting on top of the ground) once, at load.
    void PruneBakedForHoles()
    {
        if (!_baked || _bakedCells == null || _body == null) return;

        int volumeCount = NoGrassVolume.All.Count + TerrainHole.All.Count;
        if (volumeCount == _prunedVolumeCount) return;
        _prunedVolumeCount = volumeCount;
        if (volumeCount == 0) return;

        Matrix4x4 l2w = _body.transform.localToWorldMatrix;
        int removed = 0;
        foreach (var kv in _bakedCells)
        {
            var cell = kv.Value;
            for (int i = cell.local.Count - 1; i >= 0; i--)
            {
                // Blade matrices are body-LOCAL (Draw multiplies by the body's
                // localToWorld), so column 3 is the blade's local position.
                Vector3 world = l2w.MultiplyPoint3x4(cell.local[i].GetColumn(3));
                if (!NoGrassVolume.AnyContainsOrHole(world)) continue;
                cell.local.RemoveAt(i);
                cell.mesh.RemoveAt(i);
                removed++;
            }
        }
        if (removed > 0)
            Debug.Log($"[InstancedGrassRenderer] Removed {removed} baked blades inside " +
                      $"{volumeCount} no-grass volume(s) — cave mouths / punched holes.");
    }

    bool Resolve()
    {
        if (grassMeshes == null || grassMeshes.Length == 0 || grassMaterial == null) return false;
        if (_body == null)
        {
            var sim = NBodySimulation.Bodies;
            if (sim == null) return false;
            for (int i = 0; i < sim.Length; i++)
                if (sim[i] != null && sim[i].bodyName == onlyBodyName)
                { _body = sim[i]; _gen = _body.GetComponentInChildren<CelestialBodyGenerator>(); break; }
            if (_body == null) return false;
        }
        // The runtime terrain (with elevation) lives on the generator's
        // "Terrain Mesh" child collider. Grass must seat on THIS only — raycasting
        // against everything seats blades on tree canopies / roofs / the launchpad
        // the ray passes through, which is what made grass float around the
        // village. Resolved lazily because the generator builds it in Start.
        if (_terrainCollider == null && _body != null)
        {
            foreach (var mc in _body.GetComponentsInChildren<MeshCollider>(true))
                if (mc.gameObject.name == "Terrain Mesh") { _terrainCollider = mc; break; }
        }
        if (_player == null)
        {
            _player = FindObjectOfType<PlayerController>(true);
            if (_player == null) return false;
        }
        if (!_playerLayerExcluded)
        {
            groundMask &= ~(1 << _player.gameObject.layer); // ray ignores the player
            _playerLayerExcluded = true;
        }
        return true;
    }

    float Hash01(int face, int cu, int cv, int salt)
        => (SpawnerCubeface.Hash(seed, face, cu, cv, salt) & 0xFFFFu) / 65535f;

    // The per-cell patch + coverage gate, shared by live streaming and the editor
    // bake so both produce the identical set of grass cells. (ccfu/ccfv = cell-centre
    // face-UV; r = body radius.) Slope/water/blocker rejection happens later, in
    // BuildCell.
    bool CellHasGrass(int cf, int ccu, int ccv, float ccfu, float ccfv, float r)
    {
        // Organic patch field via low-frequency Perlin noise — grass forms natural
        // blobs of bare/covered ground instead of a uniform carpet. patchCells*cellSize
        // ≈ patch size in metres; patchDensity = fraction of ground inside patches.
        float patchWorld = Mathf.Max(1f, patchCells * cellSize);
        bool inPatch = Mathf.PerlinNoise(ccfu * r / patchWorld + 1000f, ccfv * r / patchWorld + 1000f) <= patchDensity;
        // Second, higher-frequency octave: small dense clumps that fill the bare gaps
        // between the big patches (different offset so they don't line up).
        if (!inPatch && miniPatchDensity > 0f)
        {
            float miniWorld = Mathf.Max(1f, miniPatchCells * cellSize);
            inPatch = Mathf.PerlinNoise(ccfu * r / miniWorld + 5000f, ccfv * r / miniWorld + 5000f) <= miniPatchDensity;
        }
        if (!inPatch) return false;                                  // bare ground
        if (Hash01(cf, ccu, ccv, 1) > cellCoverage) return false;    // soft per-cell gaps within a patch
        return true;
    }

    // LateUpdate (not Update) so the draw is submitted AFTER EndlessManager's
    // origin shift this frame — otherwise DrawMeshInstanced renders the grass at
    // the stale pre-shift position for one frame on every shift. See the
    // [DefaultExecutionOrder] note on the class.
    void LateUpdate()
    {
        if (!Resolve()) return;
        ApplyRenderScale();
        InjectGrassPointLights(_player.transform.position);
        InjectGrassSunLight();
        if (spawnRadius <= 0.01f)            // grass turned all the way down → none
        {
            ClearActive();
            if (_depthCB != null) _depthCB.Clear();   // don't leave stale grass in the depth prepass
            return;
        }
        _tick += Time.deltaTime;
        if (_tick >= updateInterval) { _tick = 0f; Stream(); }
        Draw();
    }

    // Scale the live spawn distance by the GRASS DISTANCE graphics setting
    // (0 = off, 1 = authored, up to 3×). Read every frame so the slider applies
    // instantly. Falls back to full distance if settings aren't loaded yet.
    void ApplyRenderScale()
    {
        if (_baseSpawnRadius < 0f) _baseSpawnRadius = spawnRadius;
        var s = InputSettings.Active;
        float scale = s != null ? Mathf.Clamp(s.grassRenderScale, 0f, 3f) : 1f;
        spawnRadius = _baseSpawnRadius * scale;
    }

    void ClearActive()
    {
        _streamSettled = false;   // next Stream() must repopulate from scratch
        if (_active.Count == 0) return;
        foreach (var c in _active.Values) ReturnCell(c);   // no-op in baked mode; pools in live mode
        _active.Clear();
    }

    // ── grass point-light injection ─────────────────────────────────────────
    // DrawMeshInstanced grass never receives Unity's additive forward lights, so
    // lanterns (and any GrassPointLight) over the grass leave it dark. Each frame
    // we hand the shader the nearby ones as faked point lights — same trick the
    // flashlight uses. Count is 0 when none are near, so the shader loop is free.
    const int GrassMaxPointLights = 16;   // raised from 8 so a dense concert rig's flood/blinder lights reach the grass (the ground gets all real lights uncapped; grass only gets this many injected). Only costs GPU where this many lights are actually near.
    static readonly int _gplPosId    = Shader.PropertyToID("_GrassPointLightPos");
    static readonly int _gplColorId  = Shader.PropertyToID("_GrassPointLightColor");
    static readonly int _gplParamsId = Shader.PropertyToID("_GrassPointLightParams");
    static readonly int _gplDirId    = Shader.PropertyToID("_GrassPointLightDir");
    static readonly int _gplCountId  = Shader.PropertyToID("_GrassPointLightCount");
    static readonly int _grassSpotCenterId = Shader.PropertyToID("_GrassSpotCenter");
    static readonly Vector4[] _gplPos    = new Vector4[GrassMaxPointLights];
    static readonly Vector4[] _gplColor  = new Vector4[GrassMaxPointLights];
    static readonly Vector4[] _gplParams = new Vector4[GrassMaxPointLights];
    static readonly Vector4[] _gplDir    = new Vector4[GrassMaxPointLights];
    // Per-slot CONTRIBUTION (not distance) -- what the ranking evicts on.
    static readonly float[]   _gplW = new float[GrassMaxPointLights];
    /// Best contribution among lights that did NOT make the cut this frame.
    float _wRejectMax;
    /// A light dimmer than this is invisible on grass, so it must never hold a
    /// slot -- and it makes membership changes happen at ~zero contribution.
    const float GrassLightMinContribution = 0.002f;
    /// The weakest held light fades out below this multiple of the best rejected
    /// contribution, so an exchange happens at zero on both sides.
    const float GrassLightSwapFadeBand = 1.6f;
    /// Must mirror CG_SimpleGrass.shader's _LanternGrassRadius / _LanternGrassTail
    /// defaults. Used only to ORDER the injection list, so a drift from the
    /// material costs ranking nicety, never the continuity of the fade-in.
    const float GrassLanternRadiusFrac = 0.5f;
    const float GrassLanternTailAmount = 0.35f;

    void InjectGrassPointLights(Vector3 viewer)
    {
        int n = 0;
        _wRejectMax = 0f;
        var all = GrassPointLight.All;
        for (int i = 0; i < all.Count; i++)
        {
            var gp = all[i];
            if (gp == null || gp.Light == null || !gp.isActiveAndEnabled) continue;
            var lt = gp.Light;
            // A switched-off or zeroed light must not glow on the grass —
            // matters now that toggleable player/shuttle lights carry markers
            // (GrassLightAutoMarker, 2026-08-18); harmless for lanterns.
            if (!lt.enabled || lt.intensity <= 0f) continue;
            float reach = lt.range;
            // Only inject lights whose reach can touch grass near the viewer — keeps
            // the per-fragment loop empty (and free) everywhere but the lit area.
            float gate = reach + spawnRadius;
            float dsq = (lt.transform.position - viewer).sqrMagnitude;
            if (dsq > gate * gate) continue;

            // Keep the GrassMaxPointLights that actually CONTRIBUTE most. A dense
            // village has far more lanterns + torches in range than there are
            // slots, so something must be dropped; the question is what.
            //
            // This used to keep the NEAREST, which is wrong the moment ranges
            // differ -- and they differ a lot now that shuttle floods and player
            // lights share the pool with 10 m torches. Worked example, using the
            // shader's own falloff:
            //
            //   torch,         dist 12, range  10  ->  contributes 0.0000
            //   shuttle flood, dist 40, range 100  ->  contributes 0.3360
            //
            // By distance the flood is "farthest", so it was evicted FIRST -- by
            // a torch contributing exactly nothing, because it is past its own
            // range. Which lights survived depended on precisely where the
            // player stood, so stepping back and forth across a spot swapped
            // them and the whole field jumped in brightness and hue. That is the
            // "certain spots get brighter" pulse.
            //
            // Ranking on the contribution the shader will actually compute fixes
            // it: an invisible light can never displace a visible one, and
            // whatever does get dropped is by definition the dimmest.
            // ⚠️ SCORE THE LIGHT AT THE NEAREST GRASS THAT IS ACTUALLY DRAWN,
            // not at the player. Both the old nearest-N rule and the first pass
            // at this measured the light's strength AT THE VIEWER — but the
            // shader evaluates it PER BLADE, and grass is drawn out to
            // spawnRadius (80 m). A lantern 60 m away lights the grass around
            // its own base, which the player can plainly see, yet scores zero
            // at the player and was dropped entirely. Walk closer and it
            // crossed the threshold and switched on at FULL strength over
            // grass that was already on screen. That is exactly the "lights
            // turn on when you get close enough" pop:
            //
            //   lantern range 15, scored at the player: 0 until ~15 m, then snaps
            //   scored at nearest drawn grass: 0 at 95 m, rising smoothly to full at 80 m
            //
            // The nearest drawn grass to a light D away is max(0, D - spawnRadius)
            // from it, so a light reaches zero score exactly as its reach stops
            // touching the drawn field — admission and eviction both happen at
            // zero visible effect, which is what makes them invisible.
            float dist = Mathf.Sqrt(dsq);
            float dEff = Mathf.Max(0f, dist - spawnRadius);
            float pdn = dEff / Mathf.Max(reach, 1e-4f);
            // Mirrors the falloff in CG_SimpleGrass.shader. Only has to agree at
            // the OUTER EDGE (both curves hit 0 at pdn = 1) — that is what makes
            // the fade-in continuous; the mid-range shape only orders the list.
            float patten;
            if (lt.type == LightType.Spot)
            {
                float e = Mathf.InverseLerp(1f, 0.85f, pdn);
                patten = (1f / (1f + 15f * pdn * pdn)) * (e * e * (3f - 2f * e));
            }
            else
            {
                float pt = pdn / GrassLanternRadiusFrac;
                float core = Mathf.Clamp01(1f - pt * pt) / (1f + 25f * pt * pt);
                float tail = Mathf.Clamp01(1f - pdn * pdn) / (1f + 8f * pdn * pdn);
                patten = Mathf.Max(core, tail * GrassLanternTailAmount);
            }
            Color lc = lt.color;
            float vis = patten * lt.intensity * Mathf.Max(0f, gp.grassStrength)
                        * (lc.r + lc.g + lc.b) * (1f / 3f);
            // Gate on the light's REAL visible effect. Below this it cannot be
            // seen on the grass, so it must not hold a slot — and membership
            // changes land at ~zero effect.
            if (vis <= GrassLightMinContribution) continue;

            // Rank on that, nudged toward lights near the player: once several
            // lanterns are all inside spawnRadius they score an identical 1.0,
            // and the tie would otherwise be broken by registration order —
            // which is the original "drop the lantern right next to the player"
            // bug. Never affects the zero crossing, so continuity is untouched.
            float dv = dist / Mathf.Max(spawnRadius, 1f);
            float w = vis / (1f + dv * dv);

            int slot;
            if (n < GrassMaxPointLights) { slot = n; n++; }
            else
            {
                int weakest = 0;
                for (int k = 1; k < GrassMaxPointLights; k++)
                    if (_gplW[k] < _gplW[weakest]) weakest = k;
                if (w <= _gplW[weakest]) { if (w > _wRejectMax) _wRejectMax = w; continue; }
                if (_gplW[weakest] > _wRejectMax) _wRejectMax = _gplW[weakest];
                slot = weakest;
            }

            _gplPos[slot] = lt.transform.position;
            Color c = lt.color * (lt.intensity * Mathf.Max(0f, gp.grassStrength));
            _gplColor[slot] = new Vector4(c.r, c.g, c.b, 1f);
            _gplW[slot] = w;

            // Spot lights (concert cone/strobe/blinder) only light grass inside their
            // beam; point lights (lanterns/torches) are omnidirectional. Pass the spot
            // forward + cone cosines so the shader can gate the cone; w = 0 = omni.
            if (lt.type == LightType.Spot)
            {
                Vector3 f = lt.transform.forward;
                float cosOuter = Mathf.Cos(lt.spotAngle * 0.5f * Mathf.Deg2Rad);
                float cosInner = Mathf.Cos(Mathf.Max(0f, lt.innerSpotAngle) * 0.5f * Mathf.Deg2Rad);
                _gplParams[slot] = new Vector4(reach, cosOuter, cosInner, 0f);
                _gplDir[slot] = new Vector4(f.x, f.y, f.z, 1f);
            }
            else
            {
                _gplParams[slot] = new Vector4(reach, 0f, 0f, 0f);
                _gplDir[slot] = new Vector4(0f, 0f, 0f, 0f);
            }
        }
        // Make the exchange itself continuous. Contribution ranking guarantees
        // the light dropped is the dimmest, but in a village of near-identical
        // lanterns the winner and loser can be almost equal, and a straight swap
        // would still step. So fade the single WEAKEST held light out as its
        // contribution approaches the best REJECTED one: at the moment they
        // cross, the outgoing light is already at zero and the incoming one
        // arrives at zero, so the exchange cannot be seen. Touches one light,
        // and only while it is both the weakest AND close to being displaced --
        // so a dense village keeps every lantern at full strength.
        if (n == GrassMaxPointLights && _wRejectMax > 0f)
        {
            int weakest = 0;
            for (int k = 1; k < n; k++) if (_gplW[k] < _gplW[weakest]) weakest = k;
            float t = Mathf.InverseLerp(_wRejectMax, _wRejectMax * GrassLightSwapFadeBand, _gplW[weakest]);
            _gplColor[weakest] *= t * t * (3f - 2f * t);   // smoothstep
        }

        Shader.SetGlobalFloat(_gplCountId, n);
        if (n > 0)
        {
            Shader.SetGlobalVectorArray(_gplPosId, _gplPos);
            Shader.SetGlobalVectorArray(_gplColorId, _gplColor);
            Shader.SetGlobalVectorArray(_gplParamsId, _gplParams);
            Shader.SetGlobalVectorArray(_gplDirId, _gplDir);
        }

        // Concert centre = centroid of the injected SPOT lights (w=1). The shader fades
        // spot light off the grass past _SpotGrassReach metres from here, so grass on
        // far hills darkens with the terrain. (Lanterns are point lights, w=0, excluded.)
        Vector3 spotCenter = Vector3.zero;
        int spotCount = 0;
        for (int i = 0; i < n; i++)
            if (_gplDir[i].w > 0.5f) { spotCenter += new Vector3(_gplPos[i].x, _gplPos[i].y, _gplPos[i].z); spotCount++; }
        if (spotCount > 0) Shader.SetGlobalVector(_grassSpotCenterId, spotCenter / spotCount);
    }

    // ── faked sun point-light injection (sunrise/sunset grass fill) ─────────
    // The Sun carries an UNSHADOWED point light ("Point Light (Sun)") that lights
    // the ground through a ForwardAdd pass the DrawMeshInstanced grass never
    // receives. At sunrise/sunset that light is most of the ground's
    // illumination (the shadowed directional collapses at grazing angles), so
    // the ground glowed warm while the grass stayed dark. Hand the shader its
    // position/colour/range so it can fake it — same trick as the lanterns.
    // Cached once; re-find throttled (2s) so a missing sun (main menu,
    // dimensions) costs one scan every couple of seconds, not one per frame.
    static readonly int _grassSunPosId   = Shader.PropertyToID("_GrassSunPos");
    static readonly int _grassSunColorId = Shader.PropertyToID("_GrassSunColor");
    static readonly int _grassSunRangeId = Shader.PropertyToID("_GrassSunRange");
    static Light _sunPointLight;
    static float _sunFindCooldown;

    static void InjectGrassSunLight()
    {
        if (_sunPointLight == null)
        {
            _sunFindCooldown -= Time.deltaTime;
            if (_sunFindCooldown > 0f) { Shader.SetGlobalColor(_grassSunColorId, Color.black); return; }
            _sunFindCooldown = 2f;
            var lights = FindObjectsOfType<Light>();
            for (int i = 0; i < lights.Length; i++)
            {
                var l = lights[i];
                if (l.type != LightType.Point) continue;
                var parent = l.transform.parent;
                if (parent != null && parent.name == "Sun") { _sunPointLight = l; break; }
            }
            if (_sunPointLight == null) { Shader.SetGlobalColor(_grassSunColorId, Color.black); return; }
        }
        // Respect the light being toggled off (LightingDebugToolbox F8, etc.) —
        // black colour = the shader's fill contributes exactly zero.
        if (!_sunPointLight.isActiveAndEnabled)
        {
            Shader.SetGlobalColor(_grassSunColorId, Color.black);
            return;
        }
        Shader.SetGlobalVector(_grassSunPosId, _sunPointLight.transform.position);
        Shader.SetGlobalColor(_grassSunColorId, _sunPointLight.color * _sunPointLight.intensity);
        Shader.SetGlobalFloat(_grassSunRangeId, _sunPointLight.range);
    }

    void Stream()
    {
        PruneBakedForHoles();
        Vector3 viewer = _player.transform.position;
        Vector3 vLocal = _body.transform.InverseTransformPoint(viewer);
        float r = _body.radius;

        float keep = spawnRadius * 1.2f;
        float keepSq = keep * keep;
        _scratchRemove.Clear();
        foreach (var kv in _active)
            if ((kv.Value.localAnchor - vLocal).sqrMagnitude > keepSq) _scratchRemove.Add(kv.Key);
        for (int i = 0; i < _scratchRemove.Count; i++)
        {
            if (_active.TryGetValue(_scratchRemove[i], out var oldCell)) ReturnCell(oldCell);
            _active.Remove(_scratchRemove[i]);
        }

        Vector3 vDir = vLocal.normalized;
        SpawnerCubeface.DirectionToFaceUV(vDir, out int pf, out float pu, out float pv);
        float faceUVPerCell = cellSize / Mathf.Max(0.001f, r);
        int pcu = Mathf.RoundToInt(pu / faceUVPerCell - 0.5f);
        int pcv = Mathf.RoundToInt(pv / faceUVPerCell - 0.5f);
        int win = Mathf.CeilToInt(spawnRadius / Mathf.Max(0.01f, cellSize)) + 1;
        float inRange = spawnRadius + cellSize;
        float inRangeSq = inRange * inRange;

        // ── Steady-state skip ────────────────────────────────────────────────
        // If the viewer is still in the same grass cell, the grass distance is
        // unchanged, the previous scan placed every in-range cell, and nothing
        // was culled this tick, then the scan below would re-project ~thousands
        // of cells only to find them all already active. The one-cell margin in
        // `inRange` (spawnRadius + cellSize) means the prior scan already covered
        // everything reachable without leaving this cell, so skipping is safe.
        bool sameCell = pf == _lastPf && pcu == _lastPcu && pcv == _lastPcv;
        if (sameCell && spawnRadius == _lastStreamRadius && _streamSettled
            && _scratchRemove.Count == 0)
            return;
        _lastPf = pf; _lastPcu = pcu; _lastPcv = pcv; _lastStreamRadius = spawnRadius;

        _cand.Clear();
        _seen.Clear();
        for (int cu = pcu - win; cu <= pcu + win; cu++)
        for (int cv = pcv - win; cv <= pcv + win; cv++)
        {
            float fu = (cu + 0.5f) * faceUVPerCell;
            float fv = (cv + 0.5f) * faceUVPerCell;

            int cf, ccu, ccv;
            float ccfu, ccfv;
            // Fast path: a cell whose face-UV stays within the face ([-1,1] on
            // both axes) lies on the current face pf and maps to itself — the
            // FaceUVToDirection → DirectionToFaceUV round-trip is the exact
            // identity for in-face points, so skip it. Only cells spilling past
            // a cube edge (|fu|>1 or |fv|>1) need the reprojection to find which
            // neighbouring face they wrap onto. Eliminating the round-trip for
            // the in-face bulk is what makes the scan cheap while walking.
            if (fu >= -1f && fu <= 1f && fv >= -1f && fv <= 1f)
            {
                cf = pf; ccu = cu; ccv = cv; ccfu = fu; ccfv = fv;
            }
            else
            {
                Vector3 d = SpawnerCubeface.FaceUVToDirection(pf, fu, fv);
                if (d.sqrMagnitude < 1e-6f) continue;
                SpawnerCubeface.DirectionToFaceUV(d, out cf, out float cuu, out float cvv);
                ccu = Mathf.RoundToInt(cuu / faceUVPerCell - 0.5f);
                ccv = Mathf.RoundToInt(cvv / faceUVPerCell - 0.5f);
                ccfu = (ccu + 0.5f) * faceUVPerCell;
                ccfv = (ccv + 0.5f) * faceUVPerCell;
            }

            long id = SpawnerCubeface.EncodeCell(cf, ccu, ccv);
            if (!_seen.Add(id)) continue;
            if (_active.ContainsKey(id)) continue;

            if (ccfu < -1f || ccfu > 1f || ccfv < -1f || ccfv > 1f) continue;

            // In baked mode every patch/coverage/slope/water decision was already
            // made at bake time — only surviving cells exist in the frozen set — so
            // we just test membership. In live mode, run the same patch+coverage
            // gate as before (the slope/water checks happen later in BuildCell).
            if (_baked) { if (!_bakedCells.ContainsKey(id)) continue; }
            else if (!CellHasGrass(cf, ccu, ccv, ccfu, ccfv, r)) continue;
            Vector3 localAnchor = SpawnerCubeface.FaceUVToDirection(cf, ccfu, ccfv) * r;
            float dsq = (localAnchor - vLocal).sqrMagnitude;
            if (dsq > inRangeSq) continue;
            _cand.Add(new NewCell { id = id, face = cf, cu = ccu, cv = ccv, distSq = dsq, localAnchor = localAnchor });
        }

        _cand.Sort(ByDist);
        // Use transform.position (not rb.position) so the ray centre, the
        // surface-up vector and the worldToLocal matrix below all reference the
        // same frame — the collider is a child of the transform.
        Vector3 center = _body.transform.position;
        float oceanR = _gen != null ? _gen.GetOceanRadius() : 0f;
        Matrix4x4 w2l = _body.transform.worldToLocalMatrix;
        int added = 0;
        for (int i = 0; i < _cand.Count && added < maxCellsPerUpdate; i++)
        {
            var nc = _cand[i];
            // Store EVERY evaluated cell — including ones whose blades were all
            // rejected (water/height/slope). The empty cell costs only a
            // frustum test in Draw, but its presence stops Stream re-raycasting
            // it every tick. Counting it toward the per-tick budget caps the
            // work at maxCellsPerUpdate cells regardless of how many reject —
            // this is what keeps a hilly/cliffy region from churning the whole
            // candidate window each tick (the ~18 ms hitch).
            if (_baked)
            {
                // Frozen cell — just reference it (shared, immutable). No raycast,
                // so it can never seat against the placeholder sphere.
                if (_bakedCells.TryGetValue(nc.id, out var bakedCell)) _active[nc.id] = bakedCell;
            }
            else
            {
                _active[nc.id] = BuildCell(nc.face, nc.cu, nc.cv, faceUVPerCell, center, r, oceanR, w2l, nc.localAnchor);
            }
            added++;
        }

        // We placed every candidate this tick iff the set fit within the per-tick
        // budget. If it overflowed (e.g. a teleport repopulating a fresh area),
        // leftover cells still need placing on later ticks, so stay unsettled and
        // keep scanning until the area is full; otherwise the steady-state skip
        // above can engage next tick.
        _streamSettled = _cand.Count <= maxCellsPerUpdate;
    }

    // Rent a cleared Cell from the pool (or allocate if the pool is empty).
    Cell RentCell(Vector3 localAnchor)
    {
        Cell c = _cellPool.Count > 0 ? _cellPool.Pop() : new Cell();
        c.mesh.Clear();
        c.local.Clear();
        c.localAnchor = localAnchor;
        return c;
    }

    // In baked mode the cells in _active are shared, immutable references into
    // _bakedCells — never pool/clear them (RentCell clears, which would corrupt the
    // shared data). The pool is only used by live BuildCell.
    void ReturnCell(Cell c) { if (_baked || c == null) return; _cellPool.Push(c); }

    Cell BuildCell(int face, int cu, int cv, float faceUVPerCell,
                   Vector3 center, float r, float oceanR, Matrix4x4 w2l, Vector3 localAnchor)
    {
        // Rented (cleared) cell — returned empty if the single surface raycast
        // misses or the cell is rejected. Empty cells are still stored by the
        // caller so they're never re-evaluated/re-raycast on later ticks.
        Cell cell = RentCell(localAnchor);

        // One surface raycast per cell, at the cell centre. The blades are then
        // scattered on the hit's tangent plane (+ per-blade re-seat below), so
        // they still follow the local slope.
        float cfu = (cu + 0.5f) * faceUVPerCell;
        float cfv = (cv + 0.5f) * faceUVPerCell;
        if (cfu < -1f || cfu > 1f || cfv < -1f || cfv > 1f) return cell;

        Vector3 dirLocal = SpawnerCubeface.FaceUVToDirection(face, cfu, cfv);
        Vector3 worldDir = _body.transform.TransformDirection(dirLocal);
        Vector3 ro = center + worldDir * (r + surfaceRayHeight);

        // Honour GrassBlocker props (buildings that explicitly suppress grass):
        // a normal raycast against everything tells us if a blocker sits here.
        if (Physics.Raycast(ro, -worldDir, out RaycastHit blockHit, r * 2f, groundMask, QueryTriggerInteraction.Ignore)
            && blockHit.collider != null && blockHit.collider.GetComponentInParent<GrassBlocker>() != null)
            return cell;

        // The actual surface comes from the TERRAIN collider only, so grass never
        // seats on a tree canopy / roof / launchpad the ray happens to pass
        // through (the cause of grass floating in the village). Falls back to the
        // generic ray in the editor preview where no runtime terrain collider exists.
        RaycastHit hit;
        if (_terrainCollider != null)
        {
            if (!_terrainCollider.Raycast(new Ray(ro, -worldDir), out hit, r * 2f)) return cell;
        }
        else if (!Physics.Raycast(ro, -worldDir, out hit, r * 2f, groundMask, QueryTriggerInteraction.Ignore))
        {
            return cell;
        }
        if (oceanR > 0f)
        {
            float heightAboveWater = (hit.point - center).magnitude - oceanR;
            if (heightAboveWater < waterMargin) return cell;          // too low / in the surf
            if (heightAboveWater > maxHeightAboveWater) return cell;  // too high — rocky hills/cliffs
        }

        Vector3 up = (hit.point - center).normalized;
        float slopeDeg = Vector3.Angle(hit.normal, up);
        if (slopeDeg > maxSurfaceAngle) return cell;
        if (SpawnExclusionZone.IsExcluded(hit.point)) return cell;

        // Flatness: 1 on flat ground → 0 at the steepest allowed slope. Drives
        // sparser + smaller grass on bumpy ground and the tilt-to-surface amount.
        float flat = Mathf.Clamp01(1f - slopeDeg / Mathf.Max(1f, maxSurfaceAngle));
        int bladeCount = Mathf.Max(1, Mathf.RoundToInt(bladesPerCell * Mathf.Lerp(slopeBladeFloor, 1f, flat)));
        float slopeScaleMul = Mathf.Lerp(slopeScaleFloor, 1f, flat);

        // Grass "up" tilts toward the surface normal so blades hug the slope
        // instead of standing radially and looking detached on hills.
        Vector3 orientUp = Vector3.Slerp(up, hit.normal, slopeConform).normalized;

        // Tangent basis from the surface normal so the scattered blades lie on
        // the local slope plane rather than a flat radial disc.
        Vector3 n = hit.normal;
        Vector3 tangent = Vector3.Cross(n, Vector3.right);
        if (tangent.sqrMagnitude < 1e-4f) tangent = Vector3.Cross(n, Vector3.forward);
        tangent.Normalize();
        Vector3 bitangent = Vector3.Cross(n, tangent);

        // Forward reference for blade yaw, built from the (constant-per-cell) orientUp.
        Vector3 ofwd = Vector3.Cross(orientUp, Vector3.right);
        if (ofwd.sqrMagnitude < 1e-4f) ofwd = Vector3.Cross(orientUp, Vector3.forward);
        ofwd.Normalize();

        for (int j = 0; j < bladeCount; j++)
        {
            int b = 10 + j * 4;
            float ju = (Hash01(face, cu, cv, b + 0) - 0.5f) * cellSize;
            float jv = (Hash01(face, cu, cv, b + 1) - 0.5f) * cellSize;
            Vector3 bladePoint = hit.point + tangent * ju + bitangent * jv;
            // Per-blade up/forward default to the cell's (tangent-plane) values;
            // overwritten below if the per-blade re-seat ray lands.
            Vector3 bladeUp = orientUp;
            Vector3 bladeFwd = ofwd;

            // Re-seat THIS blade onto the actual terrain under it. The cell-centre
            // hit's tangent plane is only exact within a single facet; a blade
            // scattered across a facet edge floats over a dip or pokes through a
            // bump. A short radial raycast at the blade's own position snaps it to
            // the surface you see. Only re-seat onto the SAME collider the cell
            // hit (never a building/rock/tree the blade happened to land under —
            // those keep the safe tangent-plane fallback).
            if (perBladeReseat)
            {
                Vector3 bradial = (bladePoint - center).normalized;
                Vector3 bro = center + bradial * (r + surfaceRayHeight);
                RaycastHit bhit;
                bool gotHit = _terrainCollider != null
                    ? _terrainCollider.Raycast(new Ray(bro, -bradial), out bhit, r * 2f)
                    : (Physics.Raycast(bro, -bradial, out bhit, r * 2f, groundMask, QueryTriggerInteraction.Ignore) && bhit.collider == hit.collider);
                if (gotHit)
                {
                    bladePoint = bhit.point;
                    Vector3 bup = (bhit.point - center).normalized;
                    bladeUp = Vector3.Slerp(bup, bhit.normal, slopeConform).normalized;
                    bladeFwd = Vector3.Cross(bladeUp, Vector3.right);
                    if (bladeFwd.sqrMagnitude < 1e-4f) bladeFwd = Vector3.Cross(bladeUp, Vector3.forward);
                    bladeFwd.Normalize();
                }
            }

            float yaw = Hash01(face, cu, cv, b + 2) * 360f;
            float scale = Mathf.Lerp(Mathf.Min(minScale, maxScale), Mathf.Max(minScale, maxScale),
                                     Hash01(face, cu, cv, b + 3)) * slopeScaleMul;
            int mi = grassMeshes.Length == 1 ? 0 : (int)(Hash01(face, cu, cv, 200 + j) * grassMeshes.Length) % grassMeshes.Length;

            Vector3 pos = bladePoint + bladeUp * (surfaceLift - _meshBottomY[mi] * scale);
            Quaternion rot = Quaternion.AngleAxis(yaw, bladeUp) * Quaternion.LookRotation(bladeFwd, bladeUp);

            cell.mesh.Add(mi);
            cell.local.Add(w2l * Matrix4x4.TRS(pos, rot, Vector3.one * scale));
        }
        return cell;
    }

    static readonly int _grassPlanetCenterId = Shader.PropertyToID("_GrassPlanetCenter");
    // Depth-only pre-pass. DrawMeshInstanced grass is NOT in Unity's camera
    // depth-texture prepass, so the atmosphere post reads the background (sky)
    // depth behind silhouetted blades and washes them cyan. We re-render the
    // same visible grass batches depth-only into _CameraDepthTexture via a
    // CommandBuffer at AfterDepthTexture so the atmosphere reads the grass's
    // true depth. Keeps grass OPAQUE (so it still receives shadows + the
    // atmosphere's day/night) while killing the see-through.
    Material _depthMat;
    CommandBuffer _depthCB;
    Camera _depthCBCam;
    static readonly int _depthDilateId = Shader.PropertyToID("_DepthDilatePixels");
    bool _warnedNoDepthShader;   // gates the one-time "shader stripped" error

    /// <summary>Bisect switch for GrassPopDiagnostic. False detaches the depth
    /// pre-pass, so grass stops appearing in _CameraDepthTexture and the
    /// atmosphere washes it to sky colour — the known symptom of that pre-pass
    /// failing. Flipping this at a bug spot tells you instantly whether the
    /// pre-pass is involved.</summary>
    public static bool DepthPrePassEnabled = true;

    CommandBuffer EnsureDepthCB(Camera cam)
    {
        if (cam == null) return null;
        if (!DepthPrePassEnabled)
        {
            if (_depthCBCam != null && _depthCB != null)
                _depthCBCam.RemoveCommandBuffer(CameraEvent.AfterDepthTexture, _depthCB);
            _depthCBCam = null;
            return null;
        }
        if (_depthMat == null)
        {
            // Use the assigned depth MATERIAL ASSET's shader when present. That asset
            // (instancing ON, referenced from this component → shipped in the build)
            // is what forces the shader's INSTANCING_ON variant to survive build-time
            // variant stripping (Graphics ▸ Instancing Variants = Strip Unused). A
            // code-created material via Shader.Find does NOT mark that variant used,
            // so the build strips it and CommandBuffer.DrawMeshInstanced draws NOTHING
            // — grass then has no depth and washes see-through against the sky in the
            // BUILD only (the Editor compiles variants on demand, so it looks fine).
            Shader sh = depthMaterial != null ? depthMaterial.shader : Shader.Find("CartoonGrass/GrassDepth");
            if (sh == null)
            {
                if (!_warnedNoDepthShader)
                {
                    _warnedNoDepthShader = true;
                    Debug.LogError("[InstancedGrassRenderer] 'CartoonGrass/GrassDepth' shader not found — " +
                                   "it was stripped from the build. Assign a GrassDepth material asset to " +
                                   "'depthMaterial' (or add the shader to Graphics ▸ Always Included Shaders). " +
                                   "Grass will glass out against the sky until then.");
                }
                return null;
            }
            // enableInstancing is MANDATORY: a code-created material has it OFF by
            // default, and CommandBuffer.DrawMeshInstanced throws ("doesn't enable
            // instancing") and draws nothing without it.
            _depthMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
        }
        if (_depthCB == null) _depthCB = new CommandBuffer { name = "Grass Depth Prepass" };
        if (_depthCBCam != cam)
        {
            if (_depthCBCam != null) _depthCBCam.RemoveCommandBuffer(CameraEvent.AfterDepthTexture, _depthCB);
            cam.AddCommandBuffer(CameraEvent.AfterDepthTexture, _depthCB);
            _depthCBCam = cam;
        }
        return _depthCB;
    }

    void RemoveDepthCB()
    {
        if (_depthCBCam != null && _depthCB != null)
            _depthCBCam.RemoveCommandBuffer(CameraEvent.AfterDepthTexture, _depthCB);
        _depthCBCam = null;
    }

    void OnDisable() { RemoveDepthCB(); }
    void OnDestroy()
    {
        RemoveDepthCB();
        if (_depthCB != null) { _depthCB.Release(); _depthCB = null; }
        if (_depthMat != null) { Destroy(_depthMat); _depthMat = null; }
    }

    // Affine 4x4 multiply for the per-blade world-matrix build.
    //
    // `l2w * cell.local[k]` in Draw() is the single hottest statement in the
    // frame — it runs once per visible blade, per frame (tens of thousands of
    // times). Unity's Matrix4x4 operator* is a general 4x4 multiply: it also
    // computes the bottom row and carries the w term through every dot product.
    // Both inputs here are affine — a Transform's localToWorldMatrix and a TRS
    // blade matrix both have bottom row (0,0,0,1) — so that work is provably
    // wasted. Dropping it is ~40% fewer multiply-adds for a bit-identical
    // result on affine input.
    //
    // Taken by ref purely to avoid copying two 64-byte structs per call.
    static Matrix4x4 MulAffine(ref Matrix4x4 a, ref Matrix4x4 b)
    {
        Matrix4x4 r;
        r.m00 = a.m00 * b.m00 + a.m01 * b.m10 + a.m02 * b.m20;
        r.m01 = a.m00 * b.m01 + a.m01 * b.m11 + a.m02 * b.m21;
        r.m02 = a.m00 * b.m02 + a.m01 * b.m12 + a.m02 * b.m22;
        r.m03 = a.m00 * b.m03 + a.m01 * b.m13 + a.m02 * b.m23 + a.m03;

        r.m10 = a.m10 * b.m00 + a.m11 * b.m10 + a.m12 * b.m20;
        r.m11 = a.m10 * b.m01 + a.m11 * b.m11 + a.m12 * b.m21;
        r.m12 = a.m10 * b.m02 + a.m11 * b.m12 + a.m12 * b.m22;
        r.m13 = a.m10 * b.m03 + a.m11 * b.m13 + a.m12 * b.m23 + a.m13;

        r.m20 = a.m20 * b.m00 + a.m21 * b.m10 + a.m22 * b.m20;
        r.m21 = a.m20 * b.m01 + a.m21 * b.m11 + a.m22 * b.m21;
        r.m22 = a.m20 * b.m02 + a.m21 * b.m12 + a.m22 * b.m22;
        r.m23 = a.m20 * b.m03 + a.m21 * b.m13 + a.m22 * b.m23 + a.m23;

        r.m30 = 0f; r.m31 = 0f; r.m32 = 0f; r.m33 = 1f;
        return r;
    }

    void Draw()
    {
        if (grassMaterial == null || grassMeshes.Length == 0 || _body == null) return;
        Matrix4x4 l2w = _body.transform.localToWorldMatrix;

        // Planet centre for the shader's per-patch colour variation (keeps the
        // hash input small instead of using raw world coords at ~±24000).
        Shader.SetGlobalVector(_grassPlanetCenterId, _body.transform.position);

        Camera cam = _player != null && _player.Camera != null ? _player.Camera : Camera.main;
        bool cull = frustumCull && cam != null;
        if (cull) GeometryUtility.CalculateFrustumPlanes(cam, _planes);

        // Depth pre-pass command buffer for THIS camera (mirrors the colour
        // draws below into _CameraDepthTexture). Cleared and refilled each frame.
        CommandBuffer depthCB = EnsureDepthCB(cam);
        if (depthCB != null) depthCB.Clear();
        if (_depthMat != null) _depthMat.SetFloat(_depthDilateId, depthDilatePixels);   // live-tunable screen-space fatten
        // Cull box must cover the cell's blades, which sit on the terrain — up to
        // ~maxHeightAboveWater above the sphere-surface anchor — so the box
        // doesn't fall off-screen while hilltop blades are still visible.
        float h = cellSize + maxScale * 2f + Mathf.Max(0f, maxHeightAboveWater);
        Vector3 ext = new Vector3(h, h, h) * 2f;

        // Per-cell distance to the viewer drives the no-cull radius and the fade.
        bool haveViewer = _player != null;
        Vector3 vLocal = haveViewer
            ? _body.transform.InverseTransformPoint(_player.transform.position) : Vector3.zero;
        float fadeStart = spawnRadius * densityFadeStartFrac;
        float fadeRange = Mathf.Max(0.01f, spawnRadius - fadeStart);
        float noCullSq = noCullRadius * noCullRadius;

        for (int i = 0; i < _counts.Length; i++) _counts[i] = 0;

        foreach (var cell in _active.Values)
        {
            float dSq = haveViewer ? (cell.localAnchor - vLocal).sqrMagnitude : float.MaxValue;
            // Never frustum-cull cells right next to the player — fixes near
            // grass vanishing on slopes (anchor sits below hilltop blades).
            if (cull && dSq > noCullSq)
            {
                Vector3 wa = l2w.MultiplyPoint3x4(cell.localAnchor);
                if (!GeometryUtility.TestPlanesAABB(_planes, new Bounds(wa, ext))) continue;
            }
            int n = cell.local.Count;
            if (densityFade && n > 0 && haveViewer)
            {
                float d = Mathf.Sqrt(dSq);
                float f = Mathf.Clamp01((spawnRadius - d) / fadeRange); // 1 near → 0 at edge
                f = Mathf.Max(f, densityFadeFloor);                     // never thin to see-through
                n = Mathf.Min(n, Mathf.CeilToInt(n * f));
            }
            for (int k = 0; k < n; k++)
            {
                int m = cell.mesh[k];
                Matrix4x4 bl = cell.local[k];
                _batches[m][_counts[m]++] = MulAffine(ref l2w, ref bl);
                if (_counts[m] == 1023)
                {
                    Graphics.DrawMeshInstanced(grassMeshes[m], 0, grassMaterial, _batches[m], 1023, null,
                                               ShadowCastingMode.Off, receiveShadows);
                    if (depthCB != null) depthCB.DrawMeshInstanced(grassMeshes[m], 0, _depthMat, 0, _batches[m], 1023);
                    _counts[m] = 0;
                }
            }
        }

        for (int m = 0; m < _counts.Length; m++)
            if (_counts[m] > 0)
            {
                Graphics.DrawMeshInstanced(grassMeshes[m], 0, grassMaterial, _batches[m], _counts[m], null,
                                           ShadowCastingMode.Off, receiveShadows);
                if (depthCB != null) depthCB.DrawMeshInstanced(grassMeshes[m], 0, _depthMat, 0, _batches[m], _counts[m]);
            }
    }

#if UNITY_EDITOR
    // ── editor bake ───────────────────────────────────────────────────────────
    // Run the live placement logic ONCE, in the editor, across the WHOLE planet,
    // against the real (previewed) terrain collider, and serialise every surviving
    // blade to the binary blob format LoadBaked() reads. Single source of truth:
    // this reuses CellHasGrass + BuildCell, so the baked layout matches what live
    // streaming would have produced — just frozen and seated against the real LOD0
    // surface instead of whatever happened to be present at runtime.
    //
    // Caller (PlanetBakeTool) supplies the body and a terrain collider (the preview
    // "Terrain Mesh" with a MeshCollider). Returns the blade count; `data` is the blob.
    public int EditorBake(CelestialBody body, Collider terrainCollider, out byte[] data)
    {
        data = null;
        if (body == null || terrainCollider == null) return 0;
        EnsureMeshInit();
        if (grassMeshes.Length == 0) return 0;

        // Resolve refs the same way Resolve() would, but without needing a player.
        // _baked stays false here, so BuildCell/RentCell/ReturnCell behave normally.
        _body = body;
        _gen = body.GetComponentInChildren<CelestialBodyGenerator>(true);
        _terrainCollider = terrainCollider;

        float r = _body.radius;
        Vector3 center = _body.transform.position;
        float oceanR = _gen != null ? _gen.GetOceanRadius() : 0f;
        Matrix4x4 w2l = _body.transform.worldToLocalMatrix;
        float faceUVPerCell = cellSize / Mathf.Max(0.001f, r);
        int half = Mathf.CeilToInt(1f / Mathf.Max(0.0001f, faceUVPerCell)) + 1;

        var baked = new List<KeyValuePair<long, Cell>>();
        int blades = 0;
        for (int face = 0; face < 6; face++)
        for (int cu = -half; cu <= half; cu++)
        for (int cv = -half; cv <= half; cv++)
        {
            float ccfu = (cu + 0.5f) * faceUVPerCell;
            float ccfv = (cv + 0.5f) * faceUVPerCell;
            if (ccfu < -1f || ccfu > 1f || ccfv < -1f || ccfv > 1f) continue;
            if (!CellHasGrass(face, cu, cv, ccfu, ccfv, r)) continue;

            Vector3 localAnchor = SpawnerCubeface.FaceUVToDirection(face, ccfu, ccfv) * r;
            Cell cell = BuildCell(face, cu, cv, faceUVPerCell, center, r, oceanR, w2l, localAnchor);
            if (cell.local.Count > 0)
            {
                baked.Add(new KeyValuePair<long, Cell>(SpawnerCubeface.EncodeCell(face, cu, cv), cell));
                blades += cell.local.Count;
            }
            else ReturnCell(cell);   // empty cell → recycle its lists
        }

        data = Serialize(baked, seed);

        // Don't leave the component pointing at preview objects.
        foreach (var kv in baked) ReturnCell(kv.Value);
        _body = null; _gen = null; _terrainCollider = null;
        return blades;
    }

    static byte[] Serialize(List<KeyValuePair<long, Cell>> cells, int seedVal)
    {
        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write(BakeMagic);
            bw.Write(1);              // version
            bw.Write(seedVal);        // for the stale-bake warning in LoadBaked
            bw.Write(cells.Count);
            foreach (var kv in cells)
            {
                Cell cell = kv.Value;
                bw.Write(kv.Key);
                bw.Write(cell.localAnchor.x); bw.Write(cell.localAnchor.y); bw.Write(cell.localAnchor.z);
                bw.Write(cell.local.Count);
                for (int k = 0; k < cell.local.Count; k++)
                {
                    bw.Write(cell.mesh[k]);
                    Matrix4x4 m = cell.local[k];
                    for (int e = 0; e < 16; e++) bw.Write(m[e]);
                }
            }
            bw.Flush();
            return ms.ToArray();
        }
    }
#endif
}
