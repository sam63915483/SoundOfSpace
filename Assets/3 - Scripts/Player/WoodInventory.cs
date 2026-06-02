using UnityEngine;
using UnityEngine.SceneManagement;

// Facade over Hotbar's resource API. Public surface kept intact so all existing
// callers (BuildMenuUI, GhostPlacement, BonusTutorial, SpawnedTree, save system,
// AI knowledge) keep working unchanged. Wood now lives in the hotbar's slot model.
public class WoodInventory : MonoBehaviour
{
    public static WoodInventory Instance { get; private set; }

    public int Wood => Hotbar.Instance != null
        ? Hotbar.Instance.GetResourceTotal(Hotbar.ItemId.Wood) : 0;

    public event System.Action OnChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("WoodInventory");
        DontDestroyOnLoad(go);
        go.AddComponent<WoodInventory>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnEnable()
    {
        if (Hotbar.Instance != null) Hotbar.Instance.OnResourceChanged += HandleResourceChanged;
        else StartCoroutine(SubscribeWhenHotbarReady());
    }

    void OnDisable()
    {
        if (Hotbar.Instance != null) Hotbar.Instance.OnResourceChanged -= HandleResourceChanged;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    System.Collections.IEnumerator SubscribeWhenHotbarReady()
    {
        while (Hotbar.Instance == null) yield return null;
        Hotbar.Instance.OnResourceChanged += HandleResourceChanged;
    }

    void HandleResourceChanged(Hotbar.ItemId id)
    {
        if (id == Hotbar.ItemId.Wood) OnChanged?.Invoke();
    }

    public void AddWood(int amount)
    {
        if (amount <= 0 || Hotbar.Instance == null) return;
        int leftover = Hotbar.Instance.AddResource(Hotbar.ItemId.Wood, amount);
        if (leftover > 0) InventoryFullPopup.Show();
        Debug.Log($"[WoodInventory] +{amount} wood ({leftover} overflow). Total: {Wood}");
    }

    public bool SpendWood(int amount)
    {
        if (amount <= 0) return true;
        if (Hotbar.Instance == null) return false;
        return Hotbar.Instance.SpendResource(Hotbar.ItemId.Wood, amount);
    }

    public bool Has(int amount) => Wood >= amount;

    public void SetWood(int amount)
    {
        if (Hotbar.Instance == null) return;
        Hotbar.Instance.SetResourceTotal(Hotbar.ItemId.Wood, Mathf.Max(0, amount));
    }
}
