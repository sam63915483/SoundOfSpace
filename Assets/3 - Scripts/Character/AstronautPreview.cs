using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The live, draggable 3D astronaut on the character create/edit screen.
///
/// ── How it renders into a UI panel ───────────────────────────────────────
/// A private rig — model + camera + three lights — is built in code, parked far
/// from the origin, and rendered to a RenderTexture that a RawImage displays.
/// Nothing is added to MainMenu.unity; the only authored dependency is the
/// astronaut prefab reference on MainMenuController (see AstronautPreview.Build).
///
/// ── Two layers of isolation ──────────────────────────────────────────────
/// The rig must not appear in the real camera, and the real scene must not
/// appear in the preview:
///   1. A dedicated layer (the highest unnamed one) that the preview camera
///      exclusively renders and the lights exclusively light.
///   2. Parked 10,000 units away with a 50-unit far clip, so even if every layer
///      is taken and we fall back to Default, the preview camera still sees
///      nothing but the model.
///
/// ── Framing ──────────────────────────────────────────────────────────────
/// The camera is fitted to the model's actual renderer bounds rather than to
/// hard-coded numbers, so it frames correctly whatever the FBX import scale
/// happens to be.
/// </summary>
public class AstronautPreview : MonoBehaviour
{
    // Far enough that no plausible menu-scene geometry shares the neighbourhood.
    static readonly Vector3 ParkPosition = new Vector3(0f, -10000f, 0f);

    const float IdleSpinDegreesPerSecond = 14f;   // slow drift when untouched
    const float DragDegreesPerPixel      = 0.42f;
    const float MinPitch                 = -22f;  // don't let it flip over
    const float MaxPitch                 =  28f;
    const float PitchReturnSpeed         =  38f;  // pitch eases back, yaw does not

    Transform     _model;
    Camera        _camera;
    RenderTexture _rt;
    GameObject    _rig;

    float _yaw = 155f;   // start slightly off-front so it reads as 3D immediately
    float _pitch;
    bool  _dragging;
    int   _swatch;

    public Texture Texture => _rt;

    /// <summary>
    /// Builds the rig. Returns null if no prefab was supplied — the caller then
    /// falls back to a flat colour chip, so a missing inspector reference
    /// degrades the screen instead of breaking it.
    /// </summary>
    public static AstronautPreview Build(GameObject astronautPrefab, int width, int height)
    {
        if (astronautPrefab == null) return null;

        var host = new GameObject("AstronautPreviewRig");
        DontDestroyOnLoad(host);
        var preview = host.AddComponent<AstronautPreview>();
        preview.Construct(astronautPrefab, host, width, height);
        return preview;
    }

    void Construct(GameObject prefab, GameObject host, int width, int height)
    {
        _rig = host;
        _rig.transform.position = ParkPosition;

        int layer = FindPreviewLayer();

        // ── model ────────────────────────────────────────────────────────
        var instance = Instantiate(prefab, ParkPosition, Quaternion.identity, _rig.transform);
        instance.name = "PreviewModel";
        _model = instance.transform;
        SetLayerRecursive(instance, layer);

        // The FBX ships with an Animator (Astronaut.controller). Leave it
        // running — a breathing/idle model looks far better than a bind-pose
        // T-stance — but kill root motion, or the default clip walks it out of
        // frame over a few seconds.
        var animator = instance.GetComponentInChildren<Animator>();
        if (animator != null) animator.applyRootMotion = false;

        // Anything that would try to simulate, collide, or drive the transform
        // is dead weight in a preview. The prefab may be the full player rig.
        foreach (var c in instance.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (var rb in instance.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
        // (Animator is a Behaviour, not a MonoBehaviour, so it is not in this
        // sweep and keeps running — which is what we want.)
        foreach (var mb in instance.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null) mb.enabled = false;
        // A player prefab carries its own camera; two enabled cameras would
        // fight over the preview.
        foreach (var cam in instance.GetComponentsInChildren<Camera>(true)) cam.gameObject.SetActive(false);

        // ── render target ────────────────────────────────────────────────
        _rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
        {
            name = "AstronautPreviewRT",
            antiAliasing = 4,           // the suit silhouette is all long edges
            filterMode = FilterMode.Bilinear,
        };
        _rt.Create();

        // ── camera ───────────────────────────────────────────────────────
        var camGO = new GameObject("PreviewCamera");
        camGO.transform.SetParent(_rig.transform, false);
        _camera = camGO.AddComponent<Camera>();
        _camera.clearFlags      = CameraClearFlags.SolidColor;
        _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);   // transparent — panel shows through
        _camera.cullingMask     = 1 << layer;
        _camera.targetTexture   = _rt;
        _camera.fieldOfView     = 32f;
        _camera.nearClipPlane   = 0.05f;
        _camera.farClipPlane    = 50f;    // the second half of the isolation
        _camera.allowHDR        = false;
        _camera.allowMSAA       = true;
        // Never let the menu's post-processing stack or an AudioListener ride
        // along on a second camera.
        var listener = camGO.GetComponent<AudioListener>();
        if (listener != null) Destroy(listener);

        FrameModel();

        // ── lighting ─────────────────────────────────────────────────────
        // The suit is Standard shader, so it needs real lights. All three are
        // masked to the preview layer so they cannot spill into the menu scene.
        MakeLight(layer, "Key",  new Vector3(28f, 205f, 0f), new Color(1f, 0.97f, 0.92f), 1.15f);
        MakeLight(layer, "Fill", new Vector3(12f, 320f, 0f), new Color(0.60f, 0.72f, 1f), 0.55f);
        MakeLight(layer, "Rim",  new Vector3(-8f, 25f,  0f), new Color(0.75f, 0.55f, 1f), 0.85f);

        ApplySwatch();
    }

    /// Fits the camera to the model's real bounds so import scale never matters.
    void FrameModel()
    {
        var renderers = _model.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;

        var bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        float height = Mathf.Max(bounds.size.y, 0.001f);
        // Distance that fits the model's height in the vertical FOV, plus 18%
        // margin so the helmet never kisses the top edge as it spins.
        float distance = (height * 0.5f) / Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        distance *= 1.18f;

        // Aim slightly above centre: the helmet is the interesting part, and a
        // dead-centre framing makes the boots feel heavy.
        Vector3 focus = bounds.center + Vector3.up * (height * 0.06f);
        _camera.transform.position = focus + new Vector3(0f, 0f, -distance);
        _camera.transform.LookAt(focus);
    }

    void MakeLight(int layer, string name, Vector3 euler, Color color, float intensity)
    {
        var go = new GameObject("PreviewLight_" + name);
        go.transform.SetParent(_rig.transform, false);
        go.transform.rotation = Quaternion.Euler(euler);
        var l = go.AddComponent<Light>();
        l.type        = LightType.Directional;
        l.color       = color;
        l.intensity   = intensity;
        l.shadows     = LightShadows.None;   // a lone model has nothing to catch them
        l.cullingMask = 1 << layer;
        go.layer = layer;
    }

    // ── public control ───────────────────────────────────────────────────

    public void SetSwatch(int swatchIndex)
    {
        _swatch = SuitPalette.Clamp(swatchIndex);
        ApplySwatch();
    }

    void ApplySwatch()
    {
        if (_model == null) return;
        SuitTinter.Apply(_model, _swatch);
    }

    /// Called by the drag handler on the RawImage.
    public void Drag(Vector2 delta)
    {
        _dragging = true;
        _yaw   -= delta.x * DragDegreesPerPixel;
        _pitch  = Mathf.Clamp(_pitch + delta.y * DragDegreesPerPixel, MinPitch, MaxPitch);
    }

    public void EndDrag() => _dragging = false;

    void Update()
    {
        if (_model == null) return;

        // Idle drift, so the preview always advertises that it is 3D and
        // draggable without a label saying so.
        if (!_dragging) _yaw += IdleSpinDegreesPerSecond * Time.unscaledDeltaTime;

        // Yaw is yours to keep; pitch eases home so the model can never be left
        // stuck at a bad angle after a careless flick.
        if (!_dragging && !Mathf.Approximately(_pitch, 0f))
            _pitch = Mathf.MoveTowards(_pitch, 0f, PitchReturnSpeed * Time.unscaledDeltaTime);

        _model.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }

    void OnDestroy()
    {
        if (_camera != null) _camera.targetTexture = null;
        if (_rt != null) { _rt.Release(); Destroy(_rt); _rt = null; }
    }

    /// Tears down the whole rig — model, camera, lights, texture.
    public void Dispose()
    {
        if (_rig != null) Destroy(_rig);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    /// Highest unnamed user layer. Scans DOWN from 31 because projects assign
    /// layers upward from 8, so the top of the range is the least contested.
    /// Falls back to Default (0) — safe here only because the 50-unit far clip
    /// and the 10,000-unit park distance isolate the camera anyway.
    static int FindPreviewLayer()
    {
        for (int i = 31; i >= 8; i--)
            if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
        return 0;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        for (int i = 0; i < go.transform.childCount; i++)
            SetLayerRecursive(go.transform.GetChild(i).gameObject, layer);
    }
}

/// <summary>
/// Turns drags on the preview RawImage into rotation. Separate component
/// because the RawImage is UI and the model is not — this is the only thing
/// that bridges them.
/// </summary>
public class AstronautPreviewDrag : MonoBehaviour, IDragHandler, IEndDragHandler, IBeginDragHandler
{
    public AstronautPreview Target;

    public void OnBeginDrag(PointerEventData e) { }
    public void OnDrag(PointerEventData e)
    {
        if (Target != null) Target.Drag(e.delta);
    }
    public void OnEndDrag(PointerEventData e)
    {
        if (Target != null) Target.EndDrag();
    }
}
