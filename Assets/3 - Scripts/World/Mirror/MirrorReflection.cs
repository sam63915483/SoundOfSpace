using UnityEngine;
using System.Collections.Generic;

// Built-in-pipeline planar mirror. Renders the scene from a reflected camera into
// a RenderTexture each frame the mirror is visible (OnWillRenderObject), and the
// FX/MirrorReflection shader samples it screen-projected. The mirror plane normal
// is this object's forward (+Z) axis — orient the quad so its front faces the
// viewer. Adapted from the classic Unity planar-reflection recipe.
[ExecuteInEditMode]
[RequireComponent(typeof(Renderer))]
public class MirrorReflection : MonoBehaviour
{
    public bool m_DisablePixelLights = true;
    public int m_TextureSize = 1024;
    public float m_ClipPlaneOffset = 0.05f;
    public LayerMask m_ReflectLayers = -1;
    [Tooltip("Zoom the reflection so the subject looks bigger than true mirror scale. 1 = physically accurate.")]
    public float m_Magnification = 1f;
    [Tooltip("Render the reflected player bigger than life (1 = off). Scales ONLY the reflected copy of the player at render time, so it stays a true, non-swimming mirror — just larger.")]
    public float m_PlayerMagnify = 1f;
    public string m_PlayerTag = "Player";
    [Tooltip("Lock the reflected player's feet to a fixed height so it stays grounded in the cage instead of bobbing up when you jump or stand on bumps.")]
    public bool m_GroundLock = false;
    [Tooltip("Drops the locked feet below the mirror's bottom edge (metres). Bigger = astronaut stands lower / more of the body shows.")]
    public float m_GroundLockOffset = 0f;

    Transform[] _subjectRoots;
    Renderer[] _subjectRends;
    Renderer[] _cageRends;
    Vector3[] _savedPos, _savedScale;

    readonly Dictionary<Camera, Camera> m_ReflectionCameras = new Dictionary<Camera, Camera>();
    RenderTexture m_ReflectionTexture;
    int m_OldReflectionTextureSize;
    static bool s_InsideRendering;

    public void OnWillRenderObject()
    {
        var rend = GetComponent<Renderer>();
        if (!enabled || rend == null || rend.sharedMaterial == null || !rend.enabled) return;

        Camera cam = Camera.current;
        if (cam == null) return;
        if (s_InsideRendering) return; // no recursive mirrors
        s_InsideRendering = true;

        Camera reflectionCamera;
        CreateMirrorObjects(cam, out reflectionCamera);

        Vector3 pos = transform.position;
        Vector3 normal = transform.forward; // wall-mirror normal

        int oldPixelLightCount = QualitySettings.pixelLightCount;
        if (m_DisablePixelLights) QualitySettings.pixelLightCount = 0;

        UpdateCameraModes(cam, reflectionCamera);

        float d = -Vector3.Dot(normal, pos) - m_ClipPlaneOffset;
        Vector4 reflectionPlane = new Vector4(normal.x, normal.y, normal.z, d);

        Matrix4x4 reflection = Matrix4x4.zero;
        CalculateReflectionMatrix(ref reflection, reflectionPlane);
        Vector3 oldpos = cam.transform.position;
        Vector3 newpos = reflection.MultiplyPoint(oldpos);
        reflectionCamera.worldToCameraMatrix = cam.worldToCameraMatrix * reflection;

        Vector4 clipPlane = CameraSpacePlane(reflectionCamera, pos, normal, 1.0f);
        reflectionCamera.projectionMatrix = cam.CalculateObliqueMatrix(clipPlane);
        // Optional zoom: scale the projection's x/y so the reflection subject
        // appears larger than true-mirror scale (stylized "you're in the cage").
        // Pivot the zoom about the MIRROR's on-screen position (not the screen
        // centre) so the reflection stays locked to the mirror as you look
        // around — otherwise it "swims" whenever the mirror is off-centre.
        if (m_Magnification != 1f)
        {
            Matrix4x4 p = reflectionCamera.projectionMatrix;
            p.m00 *= m_Magnification;
            p.m11 *= m_Magnification;
            Vector3 vp = cam.WorldToViewportPoint(pos);
            if (vp.z > 0f)
            {
                p.m02 += (vp.x * 2f - 1f) * (m_Magnification - 1f);
                p.m12 += (vp.y * 2f - 1f) * (m_Magnification - 1f);
            }
            reflectionCamera.projectionMatrix = p;
        }

        reflectionCamera.cullingMask = ~(1 << 4) & m_ReflectLayers.value; // never water layer
        reflectionCamera.targetTexture = m_ReflectionTexture;
        bool oldCulling = GL.invertCulling;
        GL.invertCulling = !oldCulling;
        reflectionCamera.transform.position = newpos;
        Vector3 euler = cam.transform.eulerAngles;
        reflectionCamera.transform.eulerAngles = new Vector3(0f, euler.y, euler.z);
        // Scale up just the reflected copy of the player (about its feet) so it
        // looks bigger in the mirror while still being a true reflection (no
        // swim). Play-mode only so we never dirty the player transform in editor.
        bool adjustPlayer = Application.isPlaying && (m_PlayerMagnify != 1f || m_GroundLock);
        if (adjustPlayer) ScaleSubject(m_PlayerMagnify);
        reflectionCamera.Render();
        if (adjustPlayer) RestoreSubject();
        reflectionCamera.transform.position = oldpos;
        GL.invertCulling = oldCulling;

        rend.sharedMaterial.SetTexture("_ReflectionTex", m_ReflectionTexture);

        if (m_DisablePixelLights) QualitySettings.pixelLightCount = oldPixelLightCount;
        s_InsideRendering = false;
    }

    // ── Reflected-player magnification: scale only the player's visual roots
    //    about their feet for the reflection render, then restore. Because it's
    //    a real bigger object reflected, it behaves like a true mirror (no swim).
    void EnsureSubject()
    {
        if (_subjectRoots != null && _subjectRoots.Length > 0 && _subjectRoots[0] != null) return;
        var p = GameObject.FindGameObjectWithTag(m_PlayerTag);
        if (p == null) { _subjectRoots = null; return; }
        var rends = new System.Collections.Generic.List<Renderer>();
        var roots = new System.Collections.Generic.List<Transform>();
        foreach (var r in p.GetComponentsInChildren<Renderer>(true))
        {
            if (((1 << r.gameObject.layer) & (int)m_ReflectLayers) == 0) continue;
            rends.Add(r);
            // Skinned meshes skin once per frame by default, so a mid-frame
            // scale change wouldn't show in the reflection render. Force per-render
            // recalculation so the reflection (a 2nd render this frame) re-skins.
            var smr = r as SkinnedMeshRenderer;
            if (smr != null) smr.forceMatrixRecalculationPerRender = true;
            Transform t = r.transform;
            while (t.parent != null && t.parent != p.transform) t = t.parent;
            if (!roots.Contains(t)) roots.Add(t);
        }
        _subjectRends = rends.ToArray();
        _subjectRoots = roots.ToArray();

        // cache the cage's renderers (everything under our parent except this
        // mirror) so we can find its real ground height in world space.
        var cage = transform.parent != null ? transform.parent : transform;
        var myRend = GetComponent<Renderer>();
        var crs = new System.Collections.Generic.List<Renderer>();
        foreach (var r in cage.GetComponentsInChildren<Renderer>(true)) if (r != myRend) crs.Add(r);
        _cageRends = crs.ToArray();
    }

    // The cage's real ground level in world space (lowest point of its meshes).
    float CageFloorY()
    {
        if (_cageRends == null || _cageRends.Length == 0) return transform.position.y;
        Bounds b = _cageRends[0].bounds;
        for (int i = 1; i < _cageRends.Length; i++) b.Encapsulate(_cageRends[i].bounds);
        return b.min.y;
    }

    void ScaleSubject(float k)
    {
        EnsureSubject();
        if (_subjectRoots == null || _subjectRoots.Length == 0 || _subjectRends == null || _subjectRends.Length == 0) return;
        Bounds b = _subjectRends[0].bounds;
        for (int i = 1; i < _subjectRends.Length; i++) b.Encapsulate(_subjectRends[i].bounds);
        float feetY = b.min.y;
        if (m_GroundLock)
            // fixed floor = the cage's real ground in WORLD space (its lowest point),
            // so it works regardless of the cage's scale/orientation. Offset nudges.
            feetY = CageFloorY() - m_GroundLockOffset;
        float dy = feetY - b.min.y; // lift/drop so feet sit at the locked floor (no bob on jumps/bumps)
        Vector3 pivot = new Vector3(b.center.x, feetY, b.center.z);
        _savedPos = new Vector3[_subjectRoots.Length];
        _savedScale = new Vector3[_subjectRoots.Length];
        for (int i = 0; i < _subjectRoots.Length; i++)
        {
            var t = _subjectRoots[i];
            if (t == null) continue;
            _savedPos[i] = t.position;
            _savedScale[i] = t.localScale;
            Vector3 grounded = _savedPos[i] + new Vector3(0f, dy, 0f);
            t.localScale = _savedScale[i] * k;
            t.position = pivot + (grounded - pivot) * k; // ground, then uniform scale about the feet
        }
    }

    void RestoreSubject()
    {
        if (_subjectRoots == null || _savedPos == null) return;
        for (int i = 0; i < _subjectRoots.Length && i < _savedPos.Length; i++)
        {
            var t = _subjectRoots[i];
            if (t == null) continue;
            t.localScale = _savedScale[i];
            t.position = _savedPos[i];
        }
    }

    void OnDisable()
    {
        if (m_ReflectionTexture) { DestroyImmediate(m_ReflectionTexture); m_ReflectionTexture = null; }
        foreach (var kvp in m_ReflectionCameras)
            if (kvp.Value != null) DestroyImmediate((kvp.Value).gameObject);
        m_ReflectionCameras.Clear();
    }

    void UpdateCameraModes(Camera src, Camera dest)
    {
        if (dest == null) return;
        // Clear to transparent so only the (lit) astronaut ends up in the RT;
        // everywhere else is alpha 0 and the mirror shows through (invisible glass).
        dest.clearFlags = CameraClearFlags.SolidColor;
        dest.backgroundColor = new Color(0f, 0f, 0f, 0f);
        dest.farClipPlane = src.farClipPlane;
        dest.nearClipPlane = src.nearClipPlane;
        dest.orthographic = src.orthographic;
        dest.fieldOfView = src.fieldOfView;
        dest.aspect = src.aspect;
        dest.orthographicSize = src.orthographicSize;
        dest.allowMSAA = true; // use the reflection RT's MSAA
        dest.allowHDR = src.allowHDR;
    }

    void CreateMirrorObjects(Camera currentCamera, out Camera reflectionCamera)
    {
        reflectionCamera = null;

        if (m_ReflectionTexture == null || m_OldReflectionTextureSize != m_TextureSize)
        {
            if (m_ReflectionTexture) DestroyImmediate(m_ReflectionTexture);
            m_ReflectionTexture = new RenderTexture(m_TextureSize, m_TextureSize, 16);
            m_ReflectionTexture.antiAliasing = 4; // smooth edges (was visibly aliased/pixely)
            m_ReflectionTexture.filterMode = FilterMode.Bilinear;
            m_ReflectionTexture.name = "__MirrorReflection" + GetInstanceID();
            m_ReflectionTexture.isPowerOfTwo = true;
            m_ReflectionTexture.hideFlags = HideFlags.DontSave;
            m_OldReflectionTextureSize = m_TextureSize;
        }

        m_ReflectionCameras.TryGetValue(currentCamera, out reflectionCamera);
        if (reflectionCamera == null)
        {
            var go = new GameObject("Mirror Refl Camera id" + GetInstanceID() + " for " + currentCamera.GetInstanceID(),
                typeof(Camera), typeof(Skybox));
            reflectionCamera = go.GetComponent<Camera>();
            reflectionCamera.enabled = false;
            reflectionCamera.transform.position = transform.position;
            reflectionCamera.transform.rotation = transform.rotation;
            go.hideFlags = HideFlags.HideAndDontSave;
            m_ReflectionCameras[currentCamera] = reflectionCamera;
        }
    }

    static void CalculateReflectionMatrix(ref Matrix4x4 reflectionMat, Vector4 plane)
    {
        reflectionMat.m00 = (1F - 2F * plane[0] * plane[0]);
        reflectionMat.m01 = (-2F * plane[0] * plane[1]);
        reflectionMat.m02 = (-2F * plane[0] * plane[2]);
        reflectionMat.m03 = (-2F * plane[3] * plane[0]);
        reflectionMat.m10 = (-2F * plane[1] * plane[0]);
        reflectionMat.m11 = (1F - 2F * plane[1] * plane[1]);
        reflectionMat.m12 = (-2F * plane[1] * plane[2]);
        reflectionMat.m13 = (-2F * plane[3] * plane[1]);
        reflectionMat.m20 = (-2F * plane[2] * plane[0]);
        reflectionMat.m21 = (-2F * plane[2] * plane[1]);
        reflectionMat.m22 = (1F - 2F * plane[2] * plane[2]);
        reflectionMat.m23 = (-2F * plane[3] * plane[2]);
        reflectionMat.m30 = 0F; reflectionMat.m31 = 0F; reflectionMat.m32 = 0F; reflectionMat.m33 = 1F;
    }

    Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal, float sideSign)
    {
        Vector3 offsetPos = pos + normal * m_ClipPlaneOffset;
        Matrix4x4 m = cam.worldToCameraMatrix;
        Vector3 cpos = m.MultiplyPoint(offsetPos);
        Vector3 cnormal = m.MultiplyVector(normal).normalized * sideSign;
        return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
    }
}
