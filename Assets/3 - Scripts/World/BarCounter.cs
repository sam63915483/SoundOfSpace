using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The bar counter in the village pub. Put this on the counter object (under
/// the planet, inside the house) and give it a child empty called CupSpot
/// where a cup should stand — or leave <see cref="cupSpot"/> empty and it
/// uses the counter's own pivot.
///
/// Two jobs:
///   • <see cref="PourBeer"/> — called by the bartender's dialogue (Custom
///     effect "pourBeer") after the money is taken: a full cup with a
///     <see cref="BeerCupPickup"/> appears on the spot. Any cup already there
///     (an empty one you set down) is cleared first.
///   • It is itself an Interactable: while you hold an EMPTY cup, looking at
///     the counter shows "Press F to set the empty cup down" — the cup leaves
///     the hotbar and an empty cup prop stands on the spot until the next pour.
///
/// Not saved: on reload the counter is bare and the player keeps whatever cup
/// state EquipmentSave restored. One counter per bar; the bartender finds it
/// by reference or by nearest.
/// </summary>
public class BarCounter : Interactable
{
    public static readonly List<BarCounter> All = new List<BarCounter>();

    [Tooltip("Where a cup stands. Leave empty to use this object's pivot.")]
    public Transform cupSpot;
    [Tooltip("Cup mesh to spawn. Leave empty to borrow the Player's BeerCupController.cupPrefab.")]
    public GameObject cupPrefab;
    [Tooltip("How far from the counter the set-down prompt works (a trigger this size is added if the counter has no trigger collider).")]
    public float triggerRadius = 3.5f;

    GameObject _cup;        // the cup currently standing on the spot (full or empty)
    bool _cupIsFull;

    public bool HasFullCup  => _cup != null && _cupIsFull;
    public bool HasEmptyCup => _cup != null && !_cupIsFull;

    void Awake()
    {
        bool hasTrigger = false;
        foreach (var c in GetComponentsInChildren<Collider>(true))
            if (c.isTrigger) { hasTrigger = true; break; }
        if (!hasTrigger)
        {
            var sc = gameObject.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = triggerRadius;
        }
    }

    void OnEnable()  { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    static BeerCupController Controller() => Object.FindObjectOfType<BeerCupController>();

    // ── pouring ───────────────────────────────────────────────────

    /// <summary>A fresh full beer on the spot. Returns false if there is no cup prefab to spawn.</summary>
    public bool PourBeer()
    {
        var ctrl = Controller();
        var prefab = cupPrefab != null ? cupPrefab : (ctrl != null ? ctrl.cupPrefab : null);
        if (prefab == null)
        {
            Debug.LogWarning("[BarCounter] no cup prefab (assign cupPrefab here or on BeerCupController).", this);
            return false;
        }

        ClearCup();
        _cup = SpawnCup(prefab);
        _cup.name = "BeerCup(Full)";
        _cupIsFull = true;

        if (ctrl != null)
            BeerCupArt.AttachLiquid(_cup, ctrl.liquidAxis, ctrl.liquidRadius,
                                    ctrl.liquidFloorY, ctrl.liquidFullY, ctrl.foamThickness, 1f);
        else
            BeerCupArt.AttachLiquid(_cup, new Vector3(0.005f, 0f, -0.001f), 0.07f, 0.035f, 0.19f, 0.02f, 1f);

        var pickup = _cup.AddComponent<BeerCupPickup>();
        pickup.counter = this;
        return true;
    }

    /// <summary>The pickup took the cup off the counter.</summary>
    public void NotifyCupTaken(BeerCupPickup p)
    {
        if (_cup != null && p != null && p.gameObject == _cup) { _cup = null; _cupIsFull = false; }
    }

    GameObject SpawnCup(GameObject prefab)
    {
        Transform spot = cupSpot != null ? cupSpot : transform;
        var go = Instantiate(prefab, spot.position, spot.rotation, spot);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        SetLayerRecursively(go, gameObject.layer);
        // A prop on a counter, not a physics object: no rigidbody, colliders stay
        // solid so the crosshair can pick it. It rides the planet through its parent.
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Destroy(rb);
        return go;
    }

    void ClearCup()
    {
        if (_cup != null) Destroy(_cup);
        _cup = null;
        _cupIsFull = false;
    }

    static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        for (int i = 0; i < go.transform.childCount; i++)
            SetLayerRecursively(go.transform.GetChild(i).gameObject, layer);
    }

    // ── setting the empty cup down (Interactable) ─────────────────

    protected override bool CanInteract()
    {
        if (!TutorialGate.IsUnlocked(TutorialAbility.Pickup)) return false;
        if (HasFullCup) return false;                       // your beer is still waiting there
        var ctrl = Controller();
        return ctrl != null && ctrl.IsUnlocked && ctrl.IsEmpty;
    }

    protected override string BuildInteractMessage() =>
        $"Press {PromptGlyphs.Interact} to set the empty cup down";

    protected override void Interact()
    {
        base.Interact();
        var ctrl = Controller();
        if (ctrl == null) return;

        var prefab = cupPrefab != null ? cupPrefab : ctrl.cupPrefab;
        ctrl.Lock();                        // unequips, hotbar drops the slot next frame

        if (prefab != null)
        {
            ClearCup();
            _cup = SpawnCup(prefab);
            _cup.name = "BeerCup(Empty)";
            _cupIsFull = false;
        }
        GameUI.ClearInteractionPrompt(this);
    }
}
