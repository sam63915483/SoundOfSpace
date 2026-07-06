using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Shared runtime world-building helpers for the black-hole dimension controllers.
/// The dimension scenes are nearly empty; controllers build everything through these.
/// </summary>
public static class DimensionSceneUtil
{
    /// <summary>Fog + flat ambient + camera background for a dimension (no baked lighting anywhere).</summary>
    public static void ApplyAtmosphere(Color ambient, Color fog, float fogDensity, Color background)
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = ambient;
        RenderSettings.fog = fogDensity > 0f;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = fog;
        RenderSettings.fogDensity = fogDensity;
        RenderSettings.skybox = null;                       // solid colour void
        var cam = ObserverState.Cam;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;   // scene-local interior camera
            cam.backgroundColor = background;
        }
    }

    public static Light CreateDirectionalLight(Color color, float intensity, Vector3 euler, bool shadows)
    {
        var go = new GameObject("DimensionSun");
        var l = go.AddComponent<Light>();
        l.type = LightType.Directional;
        l.color = color;
        l.intensity = intensity;
        l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        go.transform.rotation = Quaternion.Euler(euler);
        return l;
    }

    /// <summary>Invisible trigger box wired to the existing LevelPortal plumbing.</summary>
    public static GameObject CreatePortal(string name, Vector3 pos, Vector3 size,
        LevelPortal.PortalAction action, string targetScene, Transform parent = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = size;
        var p = go.AddComponent<LevelPortal>();
        p.action = action;
        p.targetScene = targetScene;
        return go;
    }

    /// <summary>Opaque tinted Standard material. (Standard ships in builds via existing material assets.)</summary>
    public static Material Mat(Color c, float smoothness = 0.1f)
    {
        var m = new Material(Shader.Find("Standard"));
        m.color = c;
        m.SetFloat("_Glossiness", smoothness);
        return m;
    }

    /// <summary>Emissive tinted Standard material (glows through fog; no light needed).</summary>
    public static Material EmissiveMat(Color c, float emission = 1.5f)
    {
        var m = Mat(c);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * emission);
        return m;
    }

    /// <summary>Fade-mode Standard material for the dissolving bridge (alpha animatable).</summary>
    public static Material FadeMat(Color c)
    {
        var m = Mat(c);
        m.SetFloat("_Mode", 2f);                            // Fade
        m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON");
        m.EnableKeyword("_ALPHABLEND_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = 3000;
        return m;
    }

    /// <summary>Player ground checks only accept the Ship/Body layers (PlayerController.walkableMask
    /// = 1536) — runtime geometry must sit on Body or the player slides and can't jump.</summary>
    public static int WalkableLayer => LayerMask.NameToLayer("Body");

    /// <summary>Primitive with material, position, scale — the basic building block. Walkable.</summary>
    public static GameObject Block(PrimitiveType type, string name, Vector3 pos, Vector3 scale,
        Material mat, Transform parent = null)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.layer = WalkableLayer;
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    /// <summary>Looping sine-hum AudioClip generated in code (placeholder until a generated-SFX pass).</summary>
    public static AudioClip ToneClip(float frequency, float seconds = 2f, float volume = 0.5f)
    {
        int rate = 44100;
        int samples = (int)(rate * seconds);
        var clip = AudioClip.Create("tone" + frequency, samples, 1, rate, false);
        var data = new float[samples];
        for (int i = 0; i < samples; i++)
            data[i] = Mathf.Sin(2f * Mathf.PI * frequency * i / rate) * volume;
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>Standard mat with a library texture (flat tint fallback if the key
    /// doesn't resolve). Pass Color.white for an untinted texture.</summary>
    public static Material TexMat(string texKey, Color tint, Vector2 tiling, float smoothness = 0.1f)
    {
        var m = Mat(tint, smoothness);
        var t = DimensionAssetLibrary.Tex(texKey);
        if (t != null)
        {
            m.mainTexture = t;
            m.mainTextureScale = tiling;
        }
        return m;
    }

    /// <summary>Emissive Standard mat with a library texture driving both albedo and
    /// emission (stained glass, neon signs, CRT screens). Falls back to EmissiveMat(tint).</summary>
    public static Material EmissiveTexMat(string texKey, Color tint, float emission = 1.5f)
    {
        var t = DimensionAssetLibrary.Tex(texKey);
        if (t == null) return EmissiveMat(tint, emission);
        var m = Mat(Color.white);
        m.mainTexture = t;
        m.EnableKeyword("_EMISSION");
        m.SetTexture("_EmissionMap", t);
        m.SetColor("_EmissionColor", Color.white * emission);
        return m;
    }

    /// <summary>Library ambience clip, or a ToneClip fallback so pre-polish behavior
    /// is preserved when the generated asset is missing.</summary>
    public static AudioClip AmbienceClip(string key, float fallbackHz, float fallbackVol)
    {
        var c = DimensionAssetLibrary.Clip(key);
        return c != null ? c : ToneClip(fallbackHz, 2f, fallbackVol);
    }

    /// <summary>2D looping ambience bed on an object (replaces the root sine hums).</summary>
    public static AudioSource AmbienceLoop2D(GameObject on, string key, float fallbackHz, float fallbackVol, float volume)
    {
        var src = on.AddComponent<AudioSource>();
        src.clip = AmbienceClip(key, fallbackHz, fallbackVol);
        src.loop = true;
        src.spatialBlend = 0f;
        src.volume = volume;
        src.Play();
        return src;
    }

    /// <summary>Fire-and-forget 3D one-shot from the library at a world position
    /// (the "room moved behind you" stingers). Silent no-op if the key is missing.</summary>
    public static void PlayOneShot3D(string key, Vector3 pos, float volume = 1f, float maxDist = 25f)
    {
        var clip = DimensionAssetLibrary.Clip(key);
        if (clip == null) return;
        var go = new GameObject("OneShot_" + key);
        go.transform.position = pos;
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.maxDistance = maxDist;
        src.volume = volume;
        src.Play();
        Object.Destroy(go, clip.length + 0.2f);
    }

    /// <summary>3D looping audio source (linear rolloff to maxDist) — proximity tells.</summary>
    public static AudioSource LoopingAudio(GameObject on, AudioClip clip, float maxDist, float volume = 1f)
    {
        var src = on.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = true;
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.maxDistance = maxDist;
        src.volume = volume;
        src.Play();
        return src;
    }
}
