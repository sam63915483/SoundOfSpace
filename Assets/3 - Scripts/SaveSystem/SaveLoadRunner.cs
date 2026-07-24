using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Runs SaveSystem.Apply on a brief delay so all Awake/Start/first-FixedUpdate
// have completed first. This avoids Start() methods (e.g. PlayerController.Start
// resetting position to spawnPoint) and FixedUpdate physics defaults from
// clobbering the just-applied save state.
//
// Also owns the load fade: the screen is BLACK from the first frame of the
// scene, stays black through the restore teleport (hiding the un-applied
// world + the snap), then fades out over ~0.5s. Sorted at 32767 — above even
// the stasis-pod DOWNLOADING overlay (32766), so a pod-save load fades from
// black straight into the running download effect.
public class SaveLoadRunner : MonoBehaviour
{
    const float FadeSeconds = 0.5f;

    public void Run(SaveData data)
    {
        BuildBlackCover();
        StartCoroutine(RunCoro(data));
    }

    IEnumerator RunCoro(SaveData data)
    {
        yield return null;                       // let all Start() run
        yield return new WaitForFixedUpdate();   // let initial FixedUpdate physics settle

        try { SaveSystem.Apply(data); }
        catch (System.Exception e) { Debug.LogError($"[SaveLoadRunner] Apply failed: {e}"); }

        // The fade-out lives on its own object — this runner destroys itself.
        if (_cover != null) _cover.AddComponent<LoadFadeOut>();
        Destroy(gameObject);
    }

    GameObject _cover;

    void BuildBlackCover()
    {
        _cover = new GameObject("LoadFadeCover");
        var canvas = _cover.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32767;
        var group = _cover.AddComponent<CanvasGroup>();
        group.alpha = 1f;
        group.blocksRaycasts = false;
        group.interactable = false;

        var imgGO = new GameObject("Black");
        imgGO.transform.SetParent(_cover.transform, false);
        var img = imgGO.AddComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = false;
        var rt = img.rectTransform;
        // Oversized so no display/aspect can show an edge gap.
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(-200f, -200f);
        rt.offsetMax = new Vector2(200f, 200f);
    }

    // Fades the cover out and removes it. Unscaled time — timescale-proof.
    class LoadFadeOut : MonoBehaviour
    {
        CanvasGroup _g;
        float _t;

        void Awake() { _g = GetComponent<CanvasGroup>(); }

        void Update()
        {
            _t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(_t / FadeSeconds);
            if (_g != null) _g.alpha = 1f - u;
            if (u >= 1f) Destroy(gameObject);
        }
    }
}
