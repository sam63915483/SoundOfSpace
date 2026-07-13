using UnityEngine;

/// A cockpit rear-view screen: renders `sourceCamera` into a RenderTexture and
/// shows it on this object's mesh (the curved square screens in Tevsship).
/// The camera is driven manually at renderHz — and only while THIS ship is
/// being piloted — so parked ships and on-foot play pay zero GPU cost.
/// mirrorHorizontal flips the image left-right like a real mirror / backup
/// cam, so "dodge left when the shot drifts right" reads correctly.
public class RearViewMirror : MonoBehaviour
{
    public Camera sourceCamera;
    public int resolution = 384;
    public float renderHz = 30f;
    public bool mirrorHorizontal = true;

    Ship _ship;
    RenderTexture _rt;
    Material _mat;
    float _nextRenderAt;

    void Awake()
    {
        _ship = GetComponentInParent<Ship>();
        if (sourceCamera == null)
        {
            Debug.LogWarning($"[RearViewMirror] {name} has no sourceCamera assigned.");
            return;
        }

        _rt = new RenderTexture(resolution, resolution, 16);
        _rt.name = name + "_RT";
        sourceCamera.targetTexture = _rt;
        sourceCamera.enabled = false;   // manual Render() below owns the schedule

        var r = GetComponent<Renderer>();
        if (r != null)
        {
            _mat = new Material(Shader.Find("Unlit/Texture"));   // opaque queue — hides behind atmosphere correctly
            _mat.mainTexture = _rt;
            if (mirrorHorizontal)
            {
                _mat.mainTextureScale = new Vector2(-1f, 1f);
                _mat.mainTextureOffset = new Vector2(1f, 0f);
            }
            r.material = _mat;
        }
    }

    void LateUpdate()
    {
        if (sourceCamera == null || _rt == null) return;
        if (_ship != null && Ship.PilotedInstance != _ship) return;   // screens sleep unless piloted
        if (Time.unscaledTime < _nextRenderAt) return;
        _nextRenderAt = Time.unscaledTime + 1f / Mathf.Max(1f, renderHz);
        sourceCamera.Render();
    }

    void OnDestroy()
    {
        if (_rt != null) _rt.Release();
    }
}
