using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PlayModeDiagnostic
{
    // Inspect cone mesh material at runtime — call AFTER entering play mode and
    // letting the scene load + ConcertConeLight.EnsureRig run.
    public static void Execute()
    {
        var cones = Object.FindObjectsOfType<ConcertConeLight>(true);
        Debug.Log($"[PlayModeDiagnostic] Cones in scene: {cones.Length}");
        foreach (var c in cones)
        {
            var rig = c.transform.Find("_ConeRig");
            if (rig == null) { Debug.LogWarning($"  cone '{c.name}' has no _ConeRig (EnsureRig didn't run yet)"); continue; }
            var visual = rig.Find("_ConeVisual");
            if (visual == null) { Debug.LogWarning($"  cone '{c.name}' has no _ConeVisual"); continue; }
            var rend = visual.GetComponent<MeshRenderer>();
            if (rend == null) { Debug.LogWarning($"  cone '{c.name}' has no MeshRenderer"); continue; }
            var mat = rend.sharedMaterial;
            var light = rig.GetComponent<Light>();
            Debug.Log($"  cone '{c.name}' goActive={c.gameObject.activeInHierarchy} scriptEnabled={c.enabled} rendEnabled={rend.enabled} matShader={(mat != null && mat.shader != null ? mat.shader.name : "<null>")} matTint={(mat != null ? mat.GetColor("_TintColor").ToString() : "<no mat>")} lightEnabled={(light != null && light.enabled).ToString()} lightColor={(light != null ? light.color.ToString() : "<no>")} lightIntensity={(light != null ? light.intensity.ToString() : "<no>")}");
        }

        var lasers = Object.FindObjectsOfType<ConcertLaser>(true);
        Debug.Log($"[PlayModeDiagnostic] Lasers in scene: {lasers.Length}");
        foreach (var l in lasers)
        {
            // Lasers spawn LineRenderers inside Start. Find them via children.
            var lrs = l.GetComponentsInChildren<LineRenderer>(true);
            Debug.Log($"  laser '{l.name}' goActive={l.gameObject.activeInHierarchy} scriptEnabled={l.enabled} lineRenderers={lrs.Length} firstMatShader={(lrs.Length > 0 && lrs[0].sharedMaterial != null && lrs[0].sharedMaterial.shader != null ? lrs[0].sharedMaterial.shader.name : "<null>")}");
        }
    }
}
