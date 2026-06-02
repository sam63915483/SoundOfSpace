using UnityEngine;

public class PickupMarker : MonoBehaviour
{
    [Header("Marker Settings")]
    public string displayName = "Part";
    public Sprite customIcon;
    public float hideDistance = 4f;

    [Header("Position Offset")]
    public Vector3 worldOffset = Vector3.zero;   // Add this to center the marker
}