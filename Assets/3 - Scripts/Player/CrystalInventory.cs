using UnityEngine;
using UnityEngine.SceneManagement;

// Facade over Hotbar's resource API. Public surface kept intact so all existing
// callers keep working unchanged. Crystal count now lives in the hotbar's slot model.
public class CrystalInventory : MonoBehaviour
{
    public static CrystalInventory Instance { get; private set; }

    public int Count => Hotbar.Instance != null
        ? Hotbar.Instance.GetResourceTotal(Hotbar.ItemId.Crystal) : 0;

    public event System.Action OnChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("CrystalInventory");
        DontDestroyOnLoad(go);
        go.AddComponent<CrystalInventory>();
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
        if (id == Hotbar.ItemId.Crystal) OnChanged?.Invoke();
    }

    public void Add(int amount)
    {
        if (amount <= 0 || Hotbar.Instance == null) return;
        int leftover = Hotbar.Instance.AddResource(Hotbar.ItemId.Crystal, amount);
        if (leftover > 0) InventoryFullPopup.Show();
        Debug.Log($"[CrystalInventory] +{amount} crystal ({leftover} overflow). Total: {Count}");
    }

    public bool Spend(int amount)
    {
        if (amount <= 0) return true;
        if (Hotbar.Instance == null) return false;
        return Hotbar.Instance.SpendResource(Hotbar.ItemId.Crystal, amount);
    }

    public bool Has(int amount) => Count >= amount;

    public void SetCount(int amount)
    {
        if (Hotbar.Instance == null) return;
        Hotbar.Instance.SetResourceTotal(Hotbar.ItemId.Crystal, Mathf.Max(0, amount));
    }
}
