using UnityEngine;
using System.Collections;

public class GuitarController : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("Icon shown in the hotbar slot when this item is in the bar. Assign on the Player prefab.")]
    public Sprite hotbarIcon;

    [Header("Guitar Settings")]
    public GameObject guitarPrefab;
    public Transform guitarHoldPosition;

    [Header("Hold Offset Adjuster")]
    public Vector3 holdPositionOffset = Vector3.zero;
    public Vector3 holdRotationOffset = Vector3.zero;

    [Header("Equip Animation")]
    public float equipDuration = 0.4f;
    public float equipStartAngle = -120f;
    public Vector3 equipRotationAxis = Vector3.right;

    [Header("Music")]
    public AudioClip musicClip;

    private GameObject _currentGuitarInstance;
    private bool _guitarUnlocked;
    private bool _resetOnNextPlay;
    private bool _isPlaying;
    private AudioSource _audioSource;
    private Coroutine _equipCoroutine;
    private FishingRodController _fishingRodController;
    private PlayerPickup _playerPickup;
    private WaterBottleController _waterBottleController;
    private AxeController _axeController;
    private PistolController _pistolController;

    // Guitars whose unequip animation is still running. EquipGuitar drains
    // this synchronously so rapid equip/unequip spamming can't leak orphans.
    private readonly System.Collections.Generic.List<GameObject> _pendingDestroyGuitars = new System.Collections.Generic.List<GameObject>();

    public bool IsEquipped => _currentGuitarInstance != null;
    public bool IsUnlocked => _guitarUnlocked;

    void Start()
    {
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
            _audioSource = gameObject.AddComponent<AudioSource>();

        _audioSource.clip = musicClip;
        _audioSource.playOnAwake = false;
        _audioSource.loop = false;

        _fishingRodController  = GetComponent<FishingRodController>();
        _playerPickup          = GetComponent<PlayerPickup>();
        _waterBottleController = GetComponent<WaterBottleController>();
        _axeController         = GetComponent<AxeController>();
        _pistolController      = GetComponent<PistolController>();
    }

    void Update()
    {
        // LMB or right-trigger pull (controller).
        if (_currentGuitarInstance != null && TutorialGate.FirePressed())
            HandleMusicClick();
    }

    void HandleMusicClick()
    {
        if (musicClip == null) return;

        if (_resetOnNextPlay)
        {
            _audioSource.clip = musicClip;
            _audioSource.time = 0f;
            _audioSource.Play();
            _isPlaying = true;
            _resetOnNextPlay = false;
        }
        else if (_audioSource.isPlaying)
        {
            _audioSource.Pause();
            _isPlaying = false;
        }
        else
        {
            _audioSource.UnPause();
            _isPlaying = true;
        }
    }

    void EquipGuitar()
    {
        if (guitarPrefab == null || guitarHoldPosition == null) return;
        if (_fishingRodController  != null && _fishingRodController.IsEquipped)      return;
        if (_waterBottleController != null && _waterBottleController.IsEquipped)    return;
        if (_axeController         != null && _axeController.IsEquipped)            return;
        if (_pistolController      != null && _pistolController.IsEquipped)         return;
        if (_playerPickup          != null && _playerPickup.IsHoldingObject)        return;
        if (_equipCoroutine != null) StopCoroutine(_equipCoroutine);
        _resetOnNextPlay = true;

        for (int i = 0; i < _pendingDestroyGuitars.Count; i++)
            if (_pendingDestroyGuitars[i] != null) Destroy(_pendingDestroyGuitars[i]);
        _pendingDestroyGuitars.Clear();

        _currentGuitarInstance = Instantiate(guitarPrefab, guitarHoldPosition);

        Rigidbody rb = _currentGuitarInstance.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;

        foreach (Collider col in _currentGuitarInstance.GetComponentsInChildren<Collider>())
            col.enabled = false;

        Quaternion targetRot = Quaternion.Euler(holdRotationOffset);
        Quaternion startRot  = targetRot * Quaternion.AngleAxis(equipStartAngle, equipRotationAxis);
        _currentGuitarInstance.transform.localPosition = holdPositionOffset;
        _currentGuitarInstance.transform.localRotation = startRot;

        _equipCoroutine = StartCoroutine(AnimateEquip(startRot, targetRot, equipDuration));
    }

    IEnumerator AnimateEquip(Quaternion from, Quaternion to, float duration)
    {
        float elapsed = 0f;
        Transform t = _currentGuitarInstance.transform;
        while (elapsed < duration)
        {
            t.localRotation = Quaternion.Slerp(from, to, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        t.localRotation = to;
        _equipCoroutine = null;
    }

    void UnequipGuitar()
    {
        if (_currentGuitarInstance == null) return;
        if (_equipCoroutine != null) StopCoroutine(_equipCoroutine);

        if (_audioSource.isPlaying || _isPlaying)
        {
            _audioSource.Stop();
            _isPlaying = false;
            _resetOnNextPlay = true;
        }

        Quaternion startRot = _currentGuitarInstance.transform.localRotation;
        Quaternion endRot   = startRot * Quaternion.AngleAxis(180f, equipRotationAxis);

        // Capture and clear immediately so a hotbar swap to another item can
        // proceed during the put-away animation (otherwise IsEquipped stays
        // true for the full equipDuration and the next item refuses to equip).
        var instance = _currentGuitarInstance;
        _pendingDestroyGuitars.Add(instance);
        _currentGuitarInstance = null;

        _equipCoroutine = StartCoroutine(AnimateUnequip(instance, startRot, endRot, equipDuration));
    }

    IEnumerator AnimateUnequip(GameObject guitar, Quaternion from, Quaternion to, float duration)
    {
        if (guitar == null) { _equipCoroutine = null; yield break; }
        float elapsed = 0f;
        Transform t = guitar.transform;
        while (elapsed < duration)
        {
            if (t == null) { _pendingDestroyGuitars.Remove(guitar); _equipCoroutine = null; yield break; }
            t.localRotation = Quaternion.Slerp(from, to, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (guitar != null) Destroy(guitar);
        _pendingDestroyGuitars.Remove(guitar);
        _equipCoroutine = null;
    }

    public void ForceEquipGuitar()
    {
        _guitarUnlocked = true;
        if (_currentGuitarInstance == null)
            EquipGuitar();
    }

    public void ForceUnequipGuitar()
    {
        if (_currentGuitarInstance != null)
            UnequipGuitar();
    }

    public void SetUnlocked(bool v) { _guitarUnlocked = v; }
}
