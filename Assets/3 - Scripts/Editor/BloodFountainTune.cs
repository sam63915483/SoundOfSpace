using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-off editor utility to inspect / tune the Blood_Fountain_2 prefab's
/// particle sub-emitters (cone size, droplet emission). Run via Unity MCP
/// execute_script.
/// </summary>
public static class BloodFountainTune
{
    const string PrefabPath = "Assets/Piloto Studio/Blood VFX Essentials/Bloody Fountains/Blood_Fountain_2.prefab";

    public static void Inspect()
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (go == null) { Debug.LogError("[BloodFountainTune] prefab not found: " + PrefabPath); return; }

        var sb = new StringBuilder();
        sb.AppendLine("[BloodFountainTune] " + PrefabPath);
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            var em = ps.emission;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            string mat = (r != null && r.sharedMaterial != null) ? r.sharedMaterial.name : "(none)";
            string render = (r != null) ? r.renderMode.ToString() : "?";
            sb.AppendLine(
                $"  '{ps.gameObject.name}' mat={mat} render={render} " +
                $"startSizeMode={main.startSize.mode} startSize=[{main.startSize.constantMin:0.###}..{main.startSize.constantMax:0.###}] " +
                $"rateOverTime={em.rateOverTime.constant:0.##} rateOverDist={em.rateOverDistance.constant:0.##} " +
                $"burstCount={em.burstCount} maxParticles={main.maxParticles}");
        }
        Debug.Log(sb.ToString());
    }

    // One-off (NOT idempotent — re-running compounds): shrink the mesh "cone"
    // sub-emitters by half and double the billboard "particle" emission.
    public static void Apply()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null) { Debug.LogError("[BloodFountainTune] could not load prefab contents: " + PrefabPath); return; }

        int cones = 0, particles = 0;
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            bool isMeshCone = r != null && r.renderMode == ParticleSystemRenderMode.Mesh;

            if (isMeshCone)
            {
                // Cone: halve start size.
                var main = ps.main;
                main.startSize = Scale(main.startSize, 0.5f);
                cones++;
            }
            else
            {
                // Particles/droplets: double emission (continuous + bursts).
                var em = ps.emission;
                em.rateOverTime = Scale(em.rateOverTime, 2f);
                em.rateOverDistance = Scale(em.rateOverDistance, 2f);
                int bc = em.burstCount;
                if (bc > 0)
                {
                    var bursts = new ParticleSystem.Burst[bc];
                    em.GetBursts(bursts);
                    for (int i = 0; i < bc; i++)
                        bursts[i].count = Scale(bursts[i].count, 2f);
                    em.SetBursts(bursts);
                }
                particles++;
            }
        }

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
        Debug.Log($"[BloodFountainTune] Applied: halved {cones} mesh cone system(s), doubled emission on {particles} particle system(s).");
    }

    static ParticleSystem.MinMaxCurve Scale(ParticleSystem.MinMaxCurve c, float f)
    {
        switch (c.mode)
        {
            case ParticleSystemCurveMode.Constant:
                c.constant *= f;
                break;
            case ParticleSystemCurveMode.TwoConstants:
                c.constantMin *= f;
                c.constantMax *= f;
                break;
            default: // curve modes
                c.curveMultiplier *= f;
                break;
        }
        return c;
    }
}
