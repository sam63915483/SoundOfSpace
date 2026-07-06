using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Key → texture / audio-clip lookup for the dimension polish pass. A single
/// asset (see AssetPath) is registered in PlayerSettings Preloaded Assets so
/// OnEnable fires at app start in builds and sets the static instance; in the
/// Editor the getter falls back to AssetDatabase at the fixed path. All
/// lookups are null-safe: a missing library, key, or asset returns null and
/// callers degrade to the pre-polish look (flat colors / sine tones).
/// </summary>
public class DimensionAssetLibrary : ScriptableObject
{
    public const string AssetPath = "Assets/2 - Materials/Dimensions/DimensionAssetLibrary.asset";

    [System.Serializable] public class TexEntry  { public string key; public Texture2D texture; }
    [System.Serializable] public class ClipEntry { public string key; public AudioClip clip; }

    public List<TexEntry>  textures = new List<TexEntry>();
    public List<ClipEntry> clips    = new List<ClipEntry>();

    static DimensionAssetLibrary _instance;
    Dictionary<string, Texture2D> _texCache;
    Dictionary<string, AudioClip> _clipCache;

    void OnEnable() { _instance = this; }

    static DimensionAssetLibrary Instance
    {
        get
        {
#if UNITY_EDITOR
            if (_instance == null)
                _instance = UnityEditor.AssetDatabase.LoadAssetAtPath<DimensionAssetLibrary>(AssetPath);
#endif
            return _instance;
        }
    }

    /// <summary>Texture for key, or null (caller falls back to flat color).</summary>
    public static Texture2D Tex(string key)
    {
        var lib = Instance;
        if (lib == null || string.IsNullOrEmpty(key)) return null;
        if (lib._texCache == null)
        {
            lib._texCache = new Dictionary<string, Texture2D>();
            foreach (var e in lib.textures)
                if (e != null && !string.IsNullOrEmpty(e.key) && e.texture != null)
                    lib._texCache[e.key] = e.texture;
        }
        lib._texCache.TryGetValue(key, out var tex);
        return tex;
    }

    /// <summary>Clip for key, or null (caller falls back to ToneClip / silence).</summary>
    public static AudioClip Clip(string key)
    {
        var lib = Instance;
        if (lib == null || string.IsNullOrEmpty(key)) return null;
        if (lib._clipCache == null)
        {
            lib._clipCache = new Dictionary<string, AudioClip>();
            foreach (var e in lib.clips)
                if (e != null && !string.IsNullOrEmpty(e.key) && e.clip != null)
                    lib._clipCache[e.key] = e.clip;
        }
        lib._clipCache.TryGetValue(key, out var clip);
        return clip;
    }
}
