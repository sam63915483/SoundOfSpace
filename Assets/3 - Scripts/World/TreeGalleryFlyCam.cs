using UnityEngine;

/// <summary>
/// Minimal fly camera for the Tree Gallery test scene (built by
/// Tools ▸ Tree Gallery ▸ Build Scene). WASD + Q/E to move, Shift = fast,
/// mouse wheel = speed, mouse look while the cursor is locked (click to lock,
/// Esc to release). Deliberately self-contained: the gallery scene has no
/// player rig, no managers, nothing from the game.
/// </summary>
public class TreeGalleryFlyCam : MonoBehaviour
{
    public float moveSpeed = 25f;
    public float fastMultiplier = 4f;
    public float lookSensitivity = 2.5f;

    float _yaw, _pitch;

    void Start()
    {
        var e = transform.eulerAngles;
        _yaw = e.y;
        _pitch = e.x > 180f ? e.x - 360f : e.x;
        SetLocked(true);
    }

    static void SetLocked(bool on)
    {
        Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !on;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) SetLocked(false);
        if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked) SetLocked(true);

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            _yaw += Input.GetAxis("Mouse X") * lookSensitivity;
            _pitch = Mathf.Clamp(_pitch - Input.GetAxis("Mouse Y") * lookSensitivity, -89f, 89f);
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0f) moveSpeed = Mathf.Clamp(moveSpeed * (1f + scroll), 2f, 400f);

        Vector3 d = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
        if (Input.GetKey(KeyCode.E)) d.y += 1f;
        if (Input.GetKey(KeyCode.Q)) d.y -= 1f;
        if (d.sqrMagnitude < 1e-4f) return;
        float s = moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? fastMultiplier : 1f);
        transform.position += transform.TransformDirection(d.normalized) * (s * Time.unscaledDeltaTime);
    }
}
