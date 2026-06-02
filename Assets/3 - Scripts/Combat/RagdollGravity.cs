using UnityEngine;

/// <summary>
/// Runtime helper added to a ragdolled enemy at death. Applies the n-body
/// gravity acceleration to every supplied Rigidbody each FixedUpdate so each
/// limb falls toward whichever celestial body it's nearest to — without this
/// the bones would just float in the place they died at.
/// </summary>
public class RagdollGravity : MonoBehaviour
{
    Rigidbody[] _rbs;

    public void Init(Rigidbody[] rbs) { _rbs = rbs; }

    void FixedUpdate()
    {
        if (_rbs == null) return;
        for (int i = 0; i < _rbs.Length; i++)
        {
            var rb = _rbs[i];
            if (rb == null || rb.isKinematic) continue;
            Vector3 g = NBodySimulation.CalculateAcceleration(rb.position);
            rb.AddForce(g, ForceMode.Acceleration);
        }
    }
}
