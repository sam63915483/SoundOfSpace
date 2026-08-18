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

	/// <summary>
	/// ⚠️ NO LONGER SAVES (Sam, 2026-08-18). The stasis pod is the only save
	/// point, so this legacy menu hook tells the player where to go instead of
	/// writing the world from a settings screen.
	///
	/// Kept as a method rather than deleted because it is a public hook that
	/// scene UnityEvents may still be wired to — removing it would leave a
	/// silently dead button rather than one that explains itself.
	/// </summary>
	public void OpenSaveDialog () {
		StoryImpactNotice.Show("SAVE IN THE STASIS POD.", 3f);
	}

	void CloseSaveDialog () {
		if (saveDialogRoot != null) Destroy(saveDialogRoot);
		saveDialogRoot = null;
	}
}