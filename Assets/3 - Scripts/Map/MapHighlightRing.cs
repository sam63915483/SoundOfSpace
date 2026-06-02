using UnityEngine;

public class MapHighlightRing : MonoBehaviour
{
    // Either a CelestialBody (sized from its radius) OR a generic Transform
    // (sized from fallbackRadius). Set ONE of these from the caller. Ships
    // don't have a "radius" field, so they go through targetTransform.
    public CelestialBody target;
    public Transform targetTransform;
    public float fallbackRadius = 5f;     // size used when targetTransform is set (ship)
    public Camera viewCamera;

    [Header("Sizing")]
    public float radiusMultiplier = 1.4f;
    public float minAngularSizeDegrees = 4.5f;
    public float thicknessFraction = 0.022f;

    [Header("Style")]
    public Color colorA = new Color(0.46f, 1f, 1f, 1f);
    public Color colorB = new Color(1f, 0.78f, 0.30f, 1f);

    LineRenderer ringXY;
    LineRenderer ringXZ;
    const int Segments = 80;

    void Awake()
    {
        ringXY = BuildRing("RingXY", Quaternion.identity);
        ringXZ = BuildRing("RingXZ", Quaternion.Euler(90f, 0f, 0f));
    }

    LineRenderer BuildRing(string name, Quaternion localRotation)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, worldPositionStays: false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = localRotation;
        go.transform.localScale    = Vector3.one;

        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop          = true;
        line.positionCount = Segments;
        line.alignment     = LineAlignment.View;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.numCapVertices = 2;
        line.material      = new Material(Shader.Find("Sprites/Default"));
        line.startColor    = colorA;
        line.endColor      = colorB;

        var pts = new Vector3[Segments];
        for (int i = 0; i < Segments; i++)
        {
            float a = (i / (float)Segments) * Mathf.PI * 2f;
            pts[i] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
        }
        line.SetPositions(pts);
        return line;
    }

    void LateUpdate()
    {
        bool hasBody = target != null;
        bool hasXform = !hasBody && targetTransform != null;
        if ((!hasBody && !hasXform) || viewCamera == null)
        {
            if (ringXY != null) ringXY.enabled = false;
            if (ringXZ != null) ringXZ.enabled = false;
            return;
        }

        ringXY.enabled = true;
        ringXZ.enabled = true;
        Vector3 anchor = hasBody ? target.Position : targetTransform.position;
        float refRadius = hasBody ? target.radius : fallbackRadius;
        transform.position = anchor;

        float dist = Vector3.Distance(viewCamera.transform.position, anchor);
        float minRad = dist * Mathf.Tan(minAngularSizeDegrees * Mathf.Deg2Rad * 0.5f);
        float visualScale = Mathf.Max(refRadius * radiusMultiplier, minRad);
        transform.localScale = Vector3.one * visualScale;

        float thickness = visualScale * thicknessFraction;
        ringXY.startWidth = ringXY.endWidth = thickness;
        ringXZ.startWidth = ringXZ.endWidth = thickness;
    }
}
