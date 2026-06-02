using UnityEngine;

[RequireComponent(typeof(Light))]
public class LightToggle : MonoBehaviour
{
    private Light lightComponent;
    private Ship ship; // Reference to the ship script

    void Start()
    {
        lightComponent = GetComponent<Light>();
        ship = FindObjectOfType<Ship>(); // Find the ship in the scene
    }

    void Update()
    {
        // Only allow toggle if the player is NOT piloting the ship
        if (ship != null && ship.IsPiloted)
            return;

        if (TutorialGate.GetKeyDown(KeyCode.E, TutorialAbility.Flashlight))
        {
            lightComponent.enabled = !lightComponent.enabled;
        }
    }
}