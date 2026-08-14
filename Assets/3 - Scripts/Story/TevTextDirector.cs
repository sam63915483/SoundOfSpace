using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tev's texting. Once the free onboarding completes he adds himself to your
/// phone and starts sending you nonsense every few minutes, forever.
///
/// Per player: each player has their own thread, their own timer and their own
/// shuffle, because Tev is the one contact whose whole relationship is
/// per-character (see <see cref="TevFronting"/>). The HOST owns the clock and
/// the pick — house rules — and pushes the resulting line to the owning player.
///
/// The interval is ONE constant pair (<see cref="TevFronting.TextIntervalMinutes"/>).
/// Sam expects to slow it down after the first hour; that is a one-number change
/// and deliberately nothing else.
///
/// Delivery order is a SHUFFLED BAG, not an independent roll per text. A pure
/// random pick repeats within three messages often enough to read as broken, and
/// these are jokes — hearing the same one twice in five minutes kills it. The bag
/// guarantees all twenty land before any repeats.
/// </summary>
public class TevTextDirector : MonoBehaviour
{
    public static TevTextDirector Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("TevTextDirector");
        DontDestroyOnLoad(go);
        go.AddComponent<TevTextDirector>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // ── Content — ALL DRAFT, for Sam to cut and rewrite ───────────────────
    //
    // Brief: short, in Tev's voice, and every one of them touching something
    // that actually exists in this world — the tapes, the aliens, the village,
    // oxygen and trees, the black hole, his revoked pilot licence, and the fact
    // that you parked a shuttle on his lawn.

    public static readonly string[] Texts =
    {
        "your shuttle's still on my lawn by the way",
        "grass'll never grow back there. not that it grew before",
        "saw one of the aliens staring at a rock for forty minutes today. living the dream",
        "do you ever think about how the sky's got a hole in it. anyway",
        "made something myself once. it was terrible. you'll do better",
        "they took my pilot licence for something I still say wasn't my fault",
        "it WAS my fault. but it wasn't ONLY my fault",
        "if a song plays in a cave and nobody buys it, did it even happen",
        "one of the village lot tried to haggle me down. me. incredible",
        "reminder: don't tape over the product. I shouldn't have to keep saying this",
        "you'd get more for those if you walked another 200 metres. just saying",
        "woke up thinking about the black hole again. back to sleep now",
        "the trick with aliens is they all think they're getting a deal",
        "I had a garden once. then a man parked a shuttle on it",
        "some of them pay double for the ugly ones. no idea why. don't question it",
        "some of them only like the slow ones. miserable bunch",
        "everything out here is dying slowly. makes for good listening",
        "check your oxygen. I'm not carrying you back",
        "business tip: the first price is never the price",
        "anyway. that's all I had. carry on",
    };

    // ── Per-player threads ────────────────────────────────────────────────

    class Thread
    {
        public readonly List<string> messages = new List<string>();
        public float nextAt;
        public readonly List<int> bag = new List<int>();
        public bool unread;
    }

    static readonly Dictionary<string, Thread> _threads = new Dictionary<string, Thread>();

    /// The local player's message list, oldest first. Empty until Tev is a
    /// contact. The phone reads this.
    public static IReadOnlyList<string> LocalMessages => Get(TevFronting.LocalId).messages;

    public static bool LocalHasUnread => Get(TevFronting.LocalId).unread;
    public static void MarkLocalRead() => Get(TevFronting.LocalId).unread = false;

    static Thread Get(string id)
    {
        if (string.IsNullOrEmpty(id)) id = "__local__";
        if (!_threads.TryGetValue(id, out var t))
        {
            t = new Thread();
            _threads[id] = t;
        }
        return t;
    }

    /// The message that makes him a contact — sent once, the moment the free
    /// onboarding completes.
    public const string ContactIntroText =
        "shop's open whenever. and if you're ever short on stock, I've always got "
        + "a stack of my old stuff — same deal, half comes back to me";

    void Update()
    {
        var s = TevFronting.Local;
        if (s == null) return;

        // Becoming a contact. Deliberately fires for BOTH routes to Complete,
        // including the one where the player swindled him out of six free
        // batches — he's a dealer, and a broke customer is still a customer.
        if (!s.isContact)
        {
            if (!TevFronting.ShouldBecomeContact) return;
            s.isContact = true;
            var t0 = Get(TevFronting.LocalId);
            t0.messages.Add(ContactIntroText);
            t0.unread = true;
            t0.nextAt = Time.unscaledTime + NextGap();
            return;
        }

        var t = Get(TevFronting.LocalId);
        if (t.nextAt <= 0f) t.nextAt = Time.unscaledTime + NextGap();
        if (Time.unscaledTime < t.nextAt) return;

        t.messages.Add(NextFromBag(t));
        t.unread = true;
        t.nextAt = Time.unscaledTime + NextGap();
    }

    static float NextGap() =>
        Random.Range(TevFronting.TextIntervalMinutes.x, TevFronting.TextIntervalMinutes.y) * 60f;

    /// Shuffled bag: refill and reshuffle when empty, so all twenty land before
    /// any repeats.
    static string NextFromBag(Thread t)
    {
        if (Texts.Length == 0) return "";
        if (t.bag.Count == 0)
        {
            for (int i = 0; i < Texts.Length; i++) t.bag.Add(i);
            for (int i = t.bag.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (t.bag[i], t.bag[j]) = (t.bag[j], t.bag[i]);
            }
        }
        int idx = t.bag[t.bag.Count - 1];
        t.bag.RemoveAt(t.bag.Count - 1);
        return Texts[Mathf.Clamp(idx, 0, Texts.Length - 1)];
    }

    /// New Game must not inherit a thread (statics leak across the main menu).
    public static void ResetAll() => _threads.Clear();
}
