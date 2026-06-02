using UnityEngine;

public class MapCameraRig : MonoBehaviour
{
    [Header("Speed scales with distance to nearest CelestialBody")]
    public float slowMin = 5f;
    public float fastMax = 1500f;
    public float scaleDist = 5000f;
    public float sprintMultiplier = 4f;
    public float mouseSensitivity = 3f;

    float yaw;
    float pitch;
    float roll;

    [Header("Roll (Q / E)")]
    [Tooltip("Degrees per second of roll when Q (anti-clockwise) or E (clockwise) is held.")]
    public float rollSpeedDegPerSec = 60f;

    public void Activate()
    {
        Vector3 e = transform.eulerAngles;
        yaw = e.y;
        pitch = e.x;
        roll = e.z;
        if (pitch > 180f) pitch -= 360f;
        if (roll  > 180f) roll  -= 360f;
    }

    public void Tick()
    {
        // Look: RMB-drag (mouse) OR right-stick (controller) OR — when the
        // user has G-locked the cursor — every frame. The locked path lets
        // them look around without holding right-click, KSP/Unity-FPS-style.
        bool mouseLooking = Input.GetMouseButton(1) || Cursor.lockState == CursorLockMode.Locked;
        float lookX = 0f, lookY = 0f;
        if (mouseLooking)
        {
            lookX += Input.GetAxis("Mouse X") * mouseSensitivity;
            lookY += Input.GetAxis("Mouse Y") * mouseSensitivity;
        }
        if (TutorialGate.ControllerEnabled)
        {
            float gain = mouseSensitivity * Time.unscaledDeltaTime * 60f;
            lookX += TutorialGate.RightStickX() * gain;
            lookY += TutorialGate.RightStickY() * gain * (TutorialGate.InvertLookY ? -1f : 1f);
        }
        // Roll: Q (clockwise) / E (anti-clockwise). Accumulated into the
        // camera's z-rotation independent of mouse-look so the player can
        // level the map view or look at it sideways while panning.
        float rollDelta = 0f;
        if (Input.GetKey(KeyCode.Q)) rollDelta += rollSpeedDegPerSec * Time.unscaledDeltaTime;
        if (Input.GetKey(KeyCode.E)) rollDelta -= rollSpeedDegPerSec * Time.unscaledDeltaTime;
        roll += rollDelta;

        if (Mathf.Abs(lookX) > 0.0001f || Mathf.Abs(lookY) > 0.0001f || Mathf.Abs(rollDelta) > 0.0001f)
        {
            yaw   += lookX;
            pitch -= lookY;
            pitch = Mathf.Clamp(pitch, -89f, 89f);
            transform.rotation = Quaternion.Euler(pitch, yaw, roll);
        }

        // Translate: WASD/arrows + Space/Ctrl (keyboard) OR left stick + A/LT (controller).
        // Use the dedicated movement helper so D-pad — which also feeds
        // "Horizontal"/"Vertical" by default — doesn't pan the map. D-pad is
        // reserved for legend navigation while the map is open.
        // Q/E previously bound here are now reserved for roll above.
        Vector3 dir = Vector3.zero;
        float h = TutorialGate.MoveAxisHorizontal(TutorialAbility.Map);
        float v = TutorialGate.MoveAxisVertical(TutorialAbility.Map);
        if (Mathf.Abs(v) > 0.001f) dir += transform.forward * v;
        if (Mathf.Abs(h) > 0.001f) dir += transform.right * h;

        // Up: Space / A button.
        bool up = Input.GetKey(KeyCode.Space) ||
                  TutorialGate.PadHeld(TutorialGate.PadButton.A);
        if (up) dir += transform.up;
        // Down: LeftCtrl / LT pull.
        bool down = Input.GetKey(KeyCode.LeftControl) ||
                    (TutorialGate.ControllerEnabled && TutorialGate.LTValue() > TutorialGate.TriggerThreshold);
        if (down) dir -= transform.up;

        if (dir.sqrMagnitude > 0.0001f)
        {
            float speed = SpeedAtPosition(transform.position);
            // Sprint: LeftShift / L-stick click.
            bool sprint = Input.GetKey(KeyCode.LeftShift) ||
                          TutorialGate.PadHeld(TutorialGate.PadButton.L3);
            if (sprint) speed *= sprintMultiplier;
            transform.position += dir.normalized * speed * Time.deltaTime;
        }
    }

    float SpeedAtPosition(Vector3 pos)
    {
        float minDist = float.MaxValue;
        var bodies = NBodySimulation.Bodies;
        if (bodies != null)
        {
            for (int i = 0; i < bodies.Length; i++)
            {
                var b = bodies[i];
                if (b == null) continue;
                float d = Vector3.Distance(pos, b.Position) - b.radius;
                if (d < minDist) minDist = d;
            }
        }
        if (minDist == float.MaxValue) minDist = scaleDist;
        float t = Mathf.Clamp01(minDist / scaleDist);
        return Mathf.Lerp(slowMin, fastMax, t);
    }
}
