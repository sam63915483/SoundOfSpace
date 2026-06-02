using UnityEngine;

/// <summary>
/// Component on a SpaceNet pickup prefab. Tags the pickup as a Left or Right
/// net so the SpaceNetMountController on a ship knows which dormant SpaceNet
/// child it would activate on install. Mirrors ThrusterPickup's role for the
/// existing thruster reattach flow.
/// </summary>
public class SpaceNetPickup : MonoBehaviour
{
    public enum Side { Left, Right }
    [Tooltip("Which side of the ship this net pickup installs on.")]
    public Side side = Side.Left;
}
