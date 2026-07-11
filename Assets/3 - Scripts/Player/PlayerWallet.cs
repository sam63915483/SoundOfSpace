using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tracks the player's money. Auto-creates itself on game start — no scene
/// setup required. The old top-left corner HUD (MONEY/AMMO chips) is gone:
/// the balance now shows inside the vendor screens via VendorMoneyBadge,
/// where the number actually matters.
/// </summary>
public class PlayerWallet : MonoBehaviour
{
    public static PlayerWallet Instance { get; private set; }

    public int Money { get; private set; } = 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        GameObject go = new GameObject("PlayerWallet");
        DontDestroyOnLoad(go);
        go.AddComponent<PlayerWallet>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void AddMoney(int amount)
    {
        Money += amount;
        Debug.Log($"[PlayerWallet] +${amount}. Total: ${Money}");
    }

    public bool SpendMoney(int amount)
    {
        if (amount < 0 || Money < amount) return false;
        Money -= amount;
        return true;
    }

    public void SetMoney(int amount)
    {
        Money = amount;
    }
}
