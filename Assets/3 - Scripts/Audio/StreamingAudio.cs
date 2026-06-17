using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Shared loader for runtime audio clips kept in StreamingAssets/ — lets
/// auto-created singletons and prefab components use generated clips without a
/// serialized asset reference or Resources.Load (both awkward for runtime-spawned
/// objects). Mirrors HALVoicePlayer's UnityWebRequest pattern.
///
/// Usage: StartCoroutine(StreamingAudio.Load("Audio/Foo.wav", AudioType.WAV, c => _clip = c));
/// </summary>
public static class StreamingAudio
{
    public static IEnumerator Load(string relativePath, AudioType type, Action<AudioClip> onLoaded)
    {
        string path = Path.Combine(Application.streamingAssetsPath, relativePath);
        string url  = "file://" + path.Replace('\\', '/');
        using (var req = UnityWebRequestMultimedia.GetAudioClip(url, type))
        {
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                var clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip != null) onLoaded?.Invoke(clip);
            }
            else
            {
                Debug.LogWarning($"[StreamingAudio] failed to load '{relativePath}': {req.error}");
            }
        }
    }
}
