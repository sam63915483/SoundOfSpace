using UnityEngine;

/// <summary>
/// Inspector home for the death-cutscene's sound effects.
///
/// DeathCutsceneController is an auto-singleton built entirely in code (it has no
/// scene/prefab instance), so there's nowhere on IT to drag AudioClips. This tiny
/// scene-placed component is that home: drop one on a GameObject in the gameplay
/// scene, assign the clips, and DeathCutsceneController finds it (via Instance) when
/// a death cutscene starts and plays each clip at the right beat.
///
/// Scene-placed on purpose (mirrors ResourceManager): it lives in 1.6.7.7.7.unity and
/// is re-found after every scene reload. Nothing here is saved — clips are authoring
/// data baked into the scene.
/// </summary>
public class DeathCutsceneAudio : MonoBehaviour
{
    public static DeathCutsceneAudio Instance { get; private set; }

    [Header("Cutscene SFX — assign clips here (find them online)")]
    [Tooltip("Loops UNDER the whole cutscene. Low cosmic drone / space hum.")]
    public AudioClip ambienceBed;
    [Tooltip("Plays at the OPEN, on the red death tip. Somber low boom / 'you died' sting.")]
    public AudioClip deathImpact;
    [Tooltip("Plays as the camera ZOOMS OUT to reveal the tree. Rising reveal whoosh.")]
    public AudioClip revealSwell;
    [Tooltip("OPTIONAL. Soft tick/shimmer as the 'ASTRONAUT i' labels appear. Leave empty to skip.")]
    public AudioClip labelBlip;
    [Tooltip("Plays while the new BLUE timeline GROWS upward. Electric crackle / rising energy (~4s).")]
    public AudioClip branchGrowth;
    [Tooltip("Plays when 'ASTRONAUT N+1' appears. Hopeful chime / rebirth sting.")]
    public AudioClip rebirthChime;
    [Tooltip("Plays during the WORMHOLE DIVE at the end. Deep building whoosh / rumble.")]
    public AudioClip vortexDive;

    [Header("Levels")]
    [Range(0f, 1f)] public float ambienceVolume = 0.45f;
    [Range(0f, 1f)] public float sfxVolume = 0.9f;

    [Header("Wake-up transition (buildup → boom → quiet)")]
    [Tooltip("Plays at the VERY end over the blue void. The cabin is revealed exactly on the boom.")]
    public AudioClip wakeStinger;
    [Tooltip("Seconds from the START of the clip to the BOOM. The cabin begins revealing at this moment — set it to where the boom hits in your clip.")]
    public float wakeBoomOffset = 2.5f;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
