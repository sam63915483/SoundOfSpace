using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SettingsMenu : MonoBehaviour {

	bool inMenu;
	public GameObject menuPanel;
	public InputSettings inputSettings;
	public TMP_InputField mouseSensitivity;
	public UnityEngine.UI.Slider mouseSmoothingSlider;
	public UnityEngine.UI.Slider masterVolumeSlider;
	public UnityEngine.UI.Slider maxTreesSlider;
	public UnityEngine.UI.Slider maxAlienNPCsSlider;
	public UnityEngine.UI.Slider maxMushroomsSlider;
	public UnityEngine.UI.Slider maxAudienceSlider;
	public UnityEngine.UI.Slider viewDistanceSlider;

	void Awake () {
		menuPanel.SetActive (false);
		if (masterVolumeSlider != null)
			masterVolumeSlider.onValueChanged.AddListener (OnMasterVolumeChanged);
		if (maxTreesSlider != null)
			maxTreesSlider.onValueChanged.AddListener (OnMaxTreesChanged);
		if (maxAlienNPCsSlider != null)
			maxAlienNPCsSlider.onValueChanged.AddListener (OnMaxAlienNPCsChanged);
		if (maxMushroomsSlider != null)
			maxMushroomsSlider.onValueChanged.AddListener (OnMaxMushroomsChanged);
		if (maxAudienceSlider != null)
			maxAudienceSlider.onValueChanged.AddListener (OnMaxAudienceChanged);
		if (viewDistanceSlider != null)
			viewDistanceSlider.onValueChanged.AddListener (OnViewDistanceChanged);
	}

	void OnMasterVolumeChanged (float value) {
		if (inputSettings != null) inputSettings.masterVolume = value;
		AudioListener.volume = value;
	}

	void OnMaxTreesChanged (float value) {
		if (inputSettings != null) inputSettings.maxTrees = Mathf.RoundToInt (value);
	}

	void OnMaxAlienNPCsChanged (float value) {
		if (inputSettings != null) inputSettings.maxAlienNPCs = Mathf.RoundToInt (value);
	}

	void OnMaxMushroomsChanged (float value) {
		if (inputSettings != null) inputSettings.maxMushrooms = Mathf.RoundToInt (value);
	}

	void OnMaxAudienceChanged (float value) {
		if (inputSettings != null) inputSettings.maxAudienceSize = Mathf.RoundToInt (value);
	}

	void OnViewDistanceChanged (float value) {
		if (inputSettings != null) inputSettings.viewDistance = Mathf.Clamp (value, 100f, 1000f);
	}

	void Update () {
		// Esc / P / controller Start.
		if (TutorialGate.PausePressed ()) {
			if (inMenu) {
				CloseMenu ();
			} else {
				OpenMenu ();
			}
		}
	}

	public void OpenMenu () {
		inMenu = true;
		Time.timeScale = 0;
		menuPanel.SetActive (true);

		mouseSensitivity.text = inputSettings.mouseSensitivity + "";
		mouseSmoothingSlider.value = inputSettings.mouseSmoothing;
		if (masterVolumeSlider != null)
			masterVolumeSlider.SetValueWithoutNotify (inputSettings.masterVolume);
		if (maxTreesSlider != null)
			maxTreesSlider.SetValueWithoutNotify (inputSettings.maxTrees);
		if (maxAlienNPCsSlider != null)
			maxAlienNPCsSlider.SetValueWithoutNotify (inputSettings.maxAlienNPCs);
		if (maxMushroomsSlider != null)
			maxMushroomsSlider.SetValueWithoutNotify (inputSettings.maxMushrooms);
		if (maxAudienceSlider != null)
			maxAudienceSlider.SetValueWithoutNotify (inputSettings.maxAudienceSize);
		if (viewDistanceSlider != null)
			viewDistanceSlider.SetValueWithoutNotify (inputSettings.viewDistance);

		Cursor.visible = true;
		Cursor.lockState = CursorLockMode.None;
	}

	public void CloseMenu () {
		inMenu = false;
		Time.timeScale = 1;
		menuPanel.SetActive (false);

		int sensitivity;
		if (int.TryParse (mouseSensitivity.text, out sensitivity)) {
			inputSettings.mouseSensitivity = sensitivity;
		}

		inputSettings.mouseSmoothing = mouseSmoothingSlider.value;

		if (masterVolumeSlider != null)
			inputSettings.masterVolume = masterVolumeSlider.value;

		if (maxTreesSlider != null)
			inputSettings.maxTrees = Mathf.RoundToInt (maxTreesSlider.value);

		if (maxAlienNPCsSlider != null)
			inputSettings.maxAlienNPCs = Mathf.RoundToInt (maxAlienNPCsSlider.value);

		if (maxMushroomsSlider != null)
			inputSettings.maxMushrooms = Mathf.RoundToInt (maxMushroomsSlider.value);

		if (maxAudienceSlider != null)
			inputSettings.maxAudienceSize = Mathf.RoundToInt (maxAudienceSlider.value);

		if (viewDistanceSlider != null)
			inputSettings.viewDistance = Mathf.Clamp (viewDistanceSlider.value, 100f, 1000f);

		inputSettings.SaveSettings ();

		if (inputSettings.lockCursor) {
			Cursor.visible = false;
			Cursor.lockState = CursorLockMode.Locked;
		}
	}

	public void ReturnToMainMenu () {
		Time.timeScale = 1f;
		Cursor.visible = true;
		Cursor.lockState = CursorLockMode.None;
		SceneManager.LoadScene ("MainMenu");
	}

	GameObject saveDialogRoot;

	public void OpenSaveDialog () {
		Debug.Log("[SettingsMenu] OpenSaveDialog invoked.");
		if (saveDialogRoot != null) { Debug.Log("[SettingsMenu] Save dialog already open; ignoring."); return; }

		// Parent under the pause-menu styler's GameObject if present (so it shares that
		// sub-canvas's sortingOrder), else fall back to the menuPanel itself, else any
		// active scene Canvas. The save panel adds its own Canvas + GraphicRaycaster
		// with overrideSorting=true so it works regardless.
		Transform parent = null;
		var styler = FindObjectOfType<GalaxyPauseMenuStyler>();
		if (styler != null) parent = styler.transform;
		if (parent == null && menuPanel != null) parent = menuPanel.transform;
		if (parent == null)
		{
			var anyCanvas = FindObjectOfType<Canvas>();
			if (anyCanvas != null) parent = anyCanvas.transform;
		}
		if (parent == null)
		{
			Debug.LogError("[SettingsMenu] No parent transform available for the save dialog.");
			return;
		}

		Debug.Log($"[SettingsMenu] Building save dialog under '{parent.name}'.");
		var panel = SaveLoadUI.Build(
			parent,
			SaveLoadMode.Save,
			onSelect: () => CloseSaveDialog(),
			onPickSlot: (saveName) =>
			{
				Debug.Log($"[SettingsMenu] Overwriting save '{saveName}'.");
				SaveSystem.Save(saveName);
			},
			onCreateOrNew: (name) =>
			{
				Debug.Log($"[SettingsMenu] Creating new save '{name}'.");
				SaveSystem.Save(name);
			},
			onClose: CloseSaveDialog);
		saveDialogRoot = panel.root;
	}

	void CloseSaveDialog () {
		if (saveDialogRoot != null) Destroy(saveDialogRoot);
		saveDialogRoot = null;
	}
}