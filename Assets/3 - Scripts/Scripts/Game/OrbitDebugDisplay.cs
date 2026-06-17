using UnityEngine;

[ExecuteInEditMode]
public class OrbitDebugDisplay : MonoBehaviour {

    public int numSteps = 1000;
    public float timeStep = 0.1f;
    public bool usePhysicsTimeStep;

    public bool relativeToBody;
    public CelestialBody centralBody;
    public float width = 100;
    public bool useThickLines;

    void Start () {
        if (Application.isPlaying) {
            HideOrbits ();
        }
    }

    void Update () {

        if (!Application.isPlaying) {
            DrawOrbits ();
        }
    }

    void DrawOrbits () {
        CelestialBody[] bodies = FindObjectsOfType<CelestialBody> ();
        var virtualBodies = new VirtualBody[bodies.Length];
        var drawPoints = new Vector3[bodies.Length][];
        int referenceFrameIndex = 0;
        Vector3 referenceBodyInitialPosition = Vector3.zero;

        // Initialize virtual bodies (don't want to move the actual bodies)
        for (int i = 0; i < virtualBodies.Length; i++) {
            virtualBodies[i] = new VirtualBody (bodies[i]);
            drawPoints[i] = new Vector3[numSteps];

            if (bodies[i] == centralBody && relativeToBody) {
                referenceFrameIndex = i;
                referenceBodyInitialPosition = virtualBodies[i].position;
            }
        }

        // Simulate
        for (int step = 0; step < numSteps; step++) {
            Vector3 referenceBodyPosition = (relativeToBody) ? virtualBodies[referenceFrameIndex].position : Vector3.zero;
            // Update velocities
            for (int i = 0; i < virtualBodies.Length; i++) {
                // Static attractors (the black hole) are fixed and never accelerate —
                // mirror NBodySimulation so the prediction matches the real sim.
                if (virtualBodies[i].isStaticAttractor) continue;
                virtualBodies[i].velocity += CalculateAcceleration (i, virtualBodies) * timeStep;
            }
            // Update positions
            for (int i = 0; i < virtualBodies.Length; i++) {
                if (virtualBodies[i].isStaticAttractor) {
                    drawPoints[i][step] = virtualBodies[i].position; // stays put
                    continue;
                }
                Vector3 newPos = virtualBodies[i].position + virtualBodies[i].velocity * timeStep;
                virtualBodies[i].position = newPos;
                if (relativeToBody) {
                    var referenceFrameOffset = referenceBodyPosition - referenceBodyInitialPosition;
                    newPos -= referenceFrameOffset;
                }
                if (relativeToBody && i == referenceFrameIndex) {
                    newPos = referenceBodyInitialPosition;
                }

                drawPoints[i][step] = newPos;
            }
        }

        // Draw paths
        for (int bodyIndex = 0; bodyIndex < virtualBodies.Length; bodyIndex++) {
            // The black hole / static attractors are fixed and have no orbit line
            // (and no LineRenderer child) — nothing to draw.
            if (bodies[bodyIndex].isStaticAttractor) continue;
            // Tint the orbit line by the body's material colour — but the procedural
            // planet/moon shaders (Celestial/*) don't expose "_Color", so guard the
            // read (otherwise reading .color logs an error every frame, e.g. when the
            // editor Planet Preview generates the real terrain). Falls back to white.
            Color pathColour = Color.white;
            var pathRenderer = bodies[bodyIndex].gameObject.GetComponentInChildren<MeshRenderer> ();
            if (pathRenderer != null && pathRenderer.sharedMaterial != null && pathRenderer.sharedMaterial.HasProperty ("_Color")) {
                pathColour = pathRenderer.sharedMaterial.color;
            }

            if (useThickLines) {
                var lineRenderer = bodies[bodyIndex].gameObject.GetComponentInChildren<LineRenderer> ();
                lineRenderer.enabled = true;
                lineRenderer.positionCount = drawPoints[bodyIndex].Length;
                lineRenderer.SetPositions (drawPoints[bodyIndex]);
                lineRenderer.startColor = pathColour;
                lineRenderer.endColor = pathColour;
                lineRenderer.widthMultiplier = width;
            } else {
                for (int i = 0; i < drawPoints[bodyIndex].Length - 1; i++) {
                    Debug.DrawLine (drawPoints[bodyIndex][i], drawPoints[bodyIndex][i + 1], pathColour);
                }

                // Hide renderer
                var lineRenderer = bodies[bodyIndex].gameObject.GetComponentInChildren<LineRenderer> ();
                if (lineRenderer) {
                    lineRenderer.enabled = false;
                }
            }

        }
    }

    Vector3 CalculateAcceleration (int i, VirtualBody[] virtualBodies) {
        Vector3 acceleration = Vector3.zero;
        for (int j = 0; j < virtualBodies.Length; j++) {
            if (i == j) {
                continue;
            }
            // Static attractors don't pull other bodies (matches NBodySimulation),
            // so the predicted orbits show the planets are NOT perturbed by the black hole.
            if (virtualBodies[j].isStaticAttractor) {
                continue;
            }
            Vector3 forceDir = (virtualBodies[j].position - virtualBodies[i].position).normalized;
            float sqrDst = (virtualBodies[j].position - virtualBodies[i].position).sqrMagnitude;
            acceleration += forceDir * Universe.gravitationalConstant * virtualBodies[j].mass / sqrDst;
        }
        return acceleration;
    }

    void HideOrbits () {
        CelestialBody[] bodies = FindObjectsOfType<CelestialBody> ();

        // Draw paths
        for (int bodyIndex = 0; bodyIndex < bodies.Length; bodyIndex++) {
            var lineRenderer = bodies[bodyIndex].gameObject.GetComponentInChildren<LineRenderer> ();
            // The black hole has no LineRenderer child — guard so this loop doesn't
            // NRE and skip hiding the orbit lines of the real planets.
            if (lineRenderer != null) {
                lineRenderer.positionCount = 0;
            }
        }
    }

    void OnValidate () {
        if (usePhysicsTimeStep) {
            timeStep = Universe.physicsTimeStep;
        }
    }

    class VirtualBody {
        public Vector3 position;
        public Vector3 velocity;
        public float mass;
        public bool isStaticAttractor;

        public VirtualBody (CelestialBody body) {
            position = body.transform.position;
            velocity = body.initialVelocity;
            mass = body.mass;
            isStaticAttractor = body.isStaticAttractor;
        }
    }
}