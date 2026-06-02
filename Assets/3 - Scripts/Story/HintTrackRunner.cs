using UnityEngine;

/// <summary>
/// Drives optional, on-ask HUD walkthroughs through the EXISTING TutorialUI pill (moved to the
/// side). Never engages the ability gate. A track advances on the same gameplay events the
/// objective system uses, and auto-dismisses when its bound objective completes (StopTrack).
/// </summary>
public class HintTrackRunner : MonoBehaviour
{
    public static HintTrackRunner Instance { get; private set; }

    string _activeTrackId;
    HintTrack _track;
    int _entryIndex;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }
    void OnDestroy() { if (Instance == this) { WireAdvance(false); Instance = null; } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("HintTrackRunner");
        DontDestroyOnLoad(go);
        go.AddComponent<HintTrackRunner>();
    }

    public void StartTrack(string trackId)
    {
        var t = StoryContent.GetHintTrack(trackId);
        if (t == null || t.entries == null || t.entries.Length == 0) return;
        // Drop any track already in progress before wiring the new one, or asking for a second
        // topic mid-track would double-subscribe the advance events.
        if (_activeTrackId != null) WireAdvance(false);
        _activeTrackId = trackId;
        _track = t;
        _entryIndex = 0;
        if (TutorialUI.Instance != null) TutorialUI.Instance.SetLeftSide(false);  // top-right, like the old tutorial
        ShowCurrent();
        WireAdvance(true);
    }

    public void StopTrack(string trackId)
    {
        if (_activeTrackId != trackId) return;
        WireAdvance(false);
        _activeTrackId = null; _track = null;
        if (TutorialUI.Instance != null) TutorialUI.Instance.HideAll();
    }

    void ShowCurrent()
    {
        if (_track == null || _entryIndex < 0 || _entryIndex >= _track.entries.Length) return;
        var e = _track.entries[_entryIndex];
        if (TutorialUI.Instance != null)
            TutorialUI.Instance.ShowStep(e.tipText, _entryIndex + 1, _track.entries.Length);
    }

    void Advance(string firedEvent)
    {
        if (_track == null) return;
        if (_track.entries[_entryIndex].advanceEvent != firedEvent) return;
        _entryIndex++;
        if (_entryIndex >= _track.entries.Length) { StopTrack(_activeTrackId); return; }
        ShowCurrent();
    }

    void WireAdvance(bool on)
    {
        if (on)
        {
            WaterBottleController.OnBottlePickedUp += A_Pickup;
            WaterBottleController.OnBottleFilled   += A_Fill;
            ResourceManager.OnCleanWaterDrunk += A_Water;
            BonfireInteraction.OnEat          += A_Food;
            GhostPlacement.OnPlaced           += A_Build;
            FishingRodController.OnBobberCast += A_Cast;
            FishingRodController.OnFishCaught += A_Catch;
        }
        else
        {
            WaterBottleController.OnBottlePickedUp -= A_Pickup;
            WaterBottleController.OnBottleFilled   -= A_Fill;
            ResourceManager.OnCleanWaterDrunk -= A_Water;
            BonfireInteraction.OnEat          -= A_Food;
            GhostPlacement.OnPlaced           -= A_Build;
            FishingRodController.OnBobberCast -= A_Cast;
            FishingRodController.OnFishCaught -= A_Catch;
        }
    }

    void A_Pickup() => Advance("OnBottlePickedUp");
    void A_Fill()   => Advance("OnBottleFilled");
    void A_Water() => Advance("OnCleanWaterDrunk");
    void A_Food()  => Advance("OnCookedFoodEaten");
    void A_Build(BuildableEntry e) => Advance("OnShelterBuilt");
    void A_Cast()  => Advance("OnBobberCast");
    void A_Catch(float spin) => Advance("OnFishCaught");
}
