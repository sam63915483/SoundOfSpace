using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Injects a "Grow Pot" entry into the build menu at runtime, the same way
/// DomeBuildRegistrar injects the Bubble Dome — so the pot needs no scene
/// wiring and no authored prefab to be playable.
///
/// If Sam later authors a real Grow Pot entry in BuildMenuUI.buildables (or
/// drops a nicer prefab at Resources/GrowPot/GrowPot), this registrar sees it
/// and steps aside.
///
/// Unlock gate lives in BuildableUnlocks ("Grow Pot" at Colonizer 2) — matching
/// is by displayName, so the name below has to stay in sync with that table.
/// </summary>
public class GrowPotRegistrar : MonoBehaviour
{
    public static GrowPotRegistrar Instance { get; private set; }

    /// Must match the BuildableUnlocks.ByLevel entry exactly (normalized).
    public const string EntryName = "Grow Pot";

    const int PotWoodCost = 4;

    bool _done;
    GameObject _template;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("GrowPotRegistrar");
        DontDestroyOnLoad(go);
        go.AddComponent<GrowPotRegistrar>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // Same reason DomeBuildRegistrar does this: this singleton survives scene
    // loads but BuildMenuUI does not, so a stale _done would leave the fresh
    // menu without the pot after a death reload or menu round-trip.
    void OnEnable()  { SceneManager.sceneLoaded += OnSceneLoaded; }
    void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }
    void OnSceneLoaded(Scene s, LoadSceneMode m) { _done = false; }

    void Update()
    {
        if (_done) return;
        var menu = BuildMenuUI.Instance;
        if (menu == null) return;   // wait for the scene's build menu to exist

        if (menu.buildables != null)
        {
            foreach (var be in menu.buildables)
                if (be != null && be.displayName == EntryName) { _done = true; return; }
        }

        var entry = new BuildableEntry
        {
            displayName = EntryName,
            description = "A tub of worked soil. Spores planted in it grow faster "
                        + "and throw off more spores than open ground — and unlike "
                        + "wild caps, a pot never runs out.",
            prefab = GetTemplate(),
            addBonfireInteractionOnPlace = false,
            woodCost = PotWoodCost,
            category = BuildableCategory.General,
        };

        var list = new System.Collections.Generic.List<BuildableEntry>();
        if (menu.buildables != null) list.AddRange(menu.buildables);
        list.Add(entry);
        menu.buildables = list.ToArray();
        _done = true;
    }

    GameObject GetTemplate()
    {
        if (_template != null) return _template;

        var authored = Resources.Load<GameObject>("GrowPot/GrowPot");
        if (authored != null) { _template = authored; return _template; }

        // Placeholder: a squat open-topped tub. Deliberately crude — it exists so
        // the pot is playable before art, and it is the first thing to replace.
        var root = new GameObject("GrowPot_Placeholder");
        root.SetActive(false);
        DontDestroyOnLoad(root);

        var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        body.name = "Tub";
        body.transform.SetParent(root.transform, false);
        body.transform.localScale = new Vector3(1.4f, 0.35f, 1.4f);
        body.transform.localPosition = new Vector3(0f, 0.35f, 0f);
        Paint(body, new Color(0.30f, 0.22f, 0.16f));

        var soil = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        soil.name = "Soil";
        soil.transform.SetParent(root.transform, false);
        soil.transform.localScale = new Vector3(1.25f, 0.06f, 1.25f);
        soil.transform.localPosition = new Vector3(0f, 0.68f, 0f);
        Paint(soil, new Color(0.16f, 0.12f, 0.09f));
        // The soil cap is decoration sitting inside the tub; leaving its collider
        // on makes the player bump the lip when trying to plant into the pot.
        var soilCol = soil.GetComponent<Collider>();
        if (soilCol != null) Destroy(soilCol);

        root.AddComponent<GrowPot>();

        _template = root;
        return _template;
    }

    static void Paint(GameObject go, Color c)
    {
        var r = go.GetComponent<Renderer>();
        if (r == null) return;
        // Standard, not URP — this project is Built-in RP and a URP-authored
        // material renders magenta here.
        var m = new Material(Shader.Find("Standard"));
        m.color = c;
        r.sharedMaterial = m;
    }
}
