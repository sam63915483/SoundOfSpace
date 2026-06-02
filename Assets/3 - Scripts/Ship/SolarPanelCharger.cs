using UnityEngine;

// Charges Ship Power when the solar panel is attached AND has clear line of sight to the sun.
// Reads attachment state from ThrusterDetachOnImpact.solarPanelChild (active = attached).
public class SolarPanelCharger : MonoBehaviour
{
    [Header("Charging")]
    [Tooltip("Ship power restored per second (out of 100).")]
    public float chargeRate = 5f;

    [Header("References")]
    [Tooltip("Optional explicit reference to the sun. If null, found by CelestialBody.bodyName == 'Sun'.")]
    public CelestialBody sun;
    [Tooltip("Optional explicit reference to the solar panel child. If null, taken from ThrusterDetachOnImpact.")]
    public GameObject solarPanelChild;

    ThrusterDetachOnImpact damage;
    Ship _ship;
    bool isCharging;

    public bool IsCharging => isCharging;

    void Awake()
    {
        damage = GetComponent<ThrusterDetachOnImpact>();
        _ship = GetComponentInParent<Ship>();
    }

    void Start()
    {
        if (sun == null)
        {
            foreach (var body in FindObjectsOfType<CelestialBody>())
            {
                if (body.bodyName == "Sun") { sun = body; break; }
            }
        }
    }

    void Update()
    {
        isCharging = false;
        if (sun == null) return;

        GameObject panel = solarPanelChild != null
            ? solarPanelChild
            : (damage != null ? damage.solarPanelChild : null);
        if (panel == null || !panel.activeInHierarchy) return;

        Vector3 panelPos = panel.transform.position;
        Vector3 sunPos = sun.transform.position;
        Vector3 toSun = sunPos - panelPos;
        float sunRadius = sun.radius;
        float distance = toSun.magnitude - sunRadius;
        if (distance <= 0f) { isCharging = true; }
        else
        {
            Vector3 dir = toSun.normalized;
            RaycastHit[] hits = Physics.RaycastAll(panelPos, dir, distance);
            bool blocked = false;
            for (int i = 0; i < hits.Length; i++)
            {
                if (!hits[i].transform.IsChildOf(transform)) { blocked = true; break; }
            }
            isCharging = !blocked;
        }

        if (isCharging && _ship != null)
            _ship.RestorePower(chargeRate * Time.deltaTime);
    }
}
