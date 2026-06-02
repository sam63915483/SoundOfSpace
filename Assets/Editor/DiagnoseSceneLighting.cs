using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Read-only inspection of the active scene's RenderSettings. Tells us
// whether Standard-shader metallic surfaces have anything to reflect at
// build time: skybox cube, custom reflection, or just black.
public static class DiagnoseSceneLighting
{
    [MenuItem("Tools/Diagnose/Scene Lighting")]
    public static void Run()
    {
        Debug.Log("=== Scene Lighting Diagnosis ===");
        Debug.Log($"Color space:                       {QualitySettings.activeColorSpace}");
        Debug.Log($"Ambient mode:                      {RenderSettings.ambientMode}");
        Debug.Log($"Ambient sky color:                 {RenderSettings.ambientSkyColor}");
        Debug.Log($"Ambient equator color:             {RenderSettings.ambientEquatorColor}");
        Debug.Log($"Ambient ground color:              {RenderSettings.ambientGroundColor}");
        Debug.Log($"Ambient intensity:                 {RenderSettings.ambientIntensity}");
        Debug.Log($"Skybox material:                   {(RenderSettings.skybox != null ? RenderSettings.skybox.name : "(none)")}");
        Debug.Log($"Default reflection mode:           {RenderSettings.defaultReflectionMode}");
        Debug.Log($"Default reflection resolution:     {RenderSettings.defaultReflectionResolution}");
        Debug.Log($"Custom reflection texture:         {(RenderSettings.customReflectionTexture != null ? RenderSettings.customReflectionTexture.name : "(none)")}");
        Debug.Log($"Reflection bounces:                {RenderSettings.reflectionBounces}");
        Debug.Log($"Reflection intensity:              {RenderSettings.reflectionIntensity}");
        Debug.Log($"Subtractive shadow color:          {RenderSettings.subtractiveShadowColor}");

        string lightingDataName = Lightmapping.lightingDataAsset != null
            ? Lightmapping.lightingDataAsset.name : "(no LightingData.asset)";
        Debug.Log($"Lightmapping data asset:           {lightingDataName}");
        Debug.Log($"Lightmapping bake completed:       {(Lightmapping.isRunning ? "(currently baking)" : "(idle)")}");
        Debug.Log($"Reflection probes in scene:        {Object.FindObjectsOfType<ReflectionProbe>(true).Length}");
        Debug.Log($"Light probe groups in scene:       {Object.FindObjectsOfType<LightProbeGroup>(true).Length}");
        Debug.Log($"Directional lights in scene:       {CountLightsOfType(LightType.Directional)}");
        Debug.Log($"Point lights in scene:             {CountLightsOfType(LightType.Point)}");
        Debug.Log($"Spot lights in scene:              {CountLightsOfType(LightType.Spot)}");

        Debug.Log("=== End ===");
    }

    static int CountLightsOfType(LightType t)
    {
        var all = Object.FindObjectsOfType<Light>(true);
        int c = 0;
        for (int i = 0; i < all.Length; i++) if (all[i].type == t) c++;
        return c;
    }
}
