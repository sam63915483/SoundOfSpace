using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Count facade over Hotbar's resource API. `HasFilter` is unrelated one-time
/// unlock state that stays here (not a stack). Save state restored by
/// SaveCollector.ApplySpaceDust (counts) and SaveCollector.ApplyHotbar (slot layout).
/// </summary>
public class SpaceDustInventory : MonoBehaviour
{
    public static SpaceDustInventory Instance { get; private set; }

    public int Count => Hotbar.Instance != null
        ? Hotbar.Instance.GetResourceTotal(Hotbar.ItemId.SpaceDust) : 0;
    public bool HasFilter { get; private set; }

    public event System.Action OnChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("SpaceDustInventory");
        DontDestroyOnLoad(go);
        go.AddComponent<SpaceDustInventory>();
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
        if (id == Hotbar.ItemId.SpaceDust) OnChanged?.Invoke();
    }

    public void Add(int amount)
    {
        if (amount <= 0 || Hotbar.Instance == null) return;
        int leftover = Hotbar.Instance.AddResource(Hotbar.ItemId.SpaceDust, amount);
        if (leftover > 0) InventoryFullPopup.Show();
    }

    public bool Spend(int amount)
    {
        if (amount <= 0) return true;
        if (Hotbar.Instance == null) return false;
        return Hotbar.Instance.SpendResource(Hotbar.ItemId.SpaceDust, amount);
    }

    public void SetCount(int amount)
    {
        if (Hotbar.Instance == null) return;
        Hotbar.Instance.SetResourceTotal(Hotbar.ItemId.SpaceDust, Mathf.Max(0, amount));
    }

    public void SetFilterUnlocked(bool unlocked)
    {
        if (HasFilter == unlocked) return;
        HasFilter = unlocked;
        OnChanged?.Invoke();
    }
}
