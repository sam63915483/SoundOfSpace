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
	// RETIRED 2026-09-08. The counts are derived from view distance now (see
	// InputSettings), so these four sliders have nothing left to set. The FIELDS
	// stay so the scene's existing references are not orphaned mid-flight — and
	// so Awake can switch the slider objects OFF, which is what actually takes
	// them out of this panel without an Editor pass. Delete the GameObjects (and
	// then these four lines) whenever convenient.
	public UnityEngine.UI.Slider maxTreesSlider;
	public UnityEngine.UI.Slider maxAlienNPCsSlider;
	public UnityEngine.UI.Slider maxMushroomsSlider;
	public UnityEngine.UI.Slider maxAudienceSlider;
	public UnityEngine.UI.Slider viewDistanceSlider;

	void Awake () {
		menuPanel.SetActive (false);
		if (masterVolumeSlider != null)
			masterVolumeSlider.onValueChanged.AddListener (OnMasterVolumeChanged);
		HideRetiredSlider (maxTreesSlider);
		HideRetiredSlider (maxAlienNPCsSlider);
		HideRetiredSlider (maxMushroomsSlider);
		HideRetiredSlider (maxAudienceSlider);
		if (viewDistanceSlider != null)
			viewDistanceSlider.onValueChanged.AddListener (OnViewDistanceChanged);
	}

	void OnMasterVolumeChanged (float value) {
		if (inputSettings != null) inputSettings.masterVolume = value;
		AudioListener.volume = value;
	}

	/// A slider whose setting no longer exists. Switching the whole row off is
	/// better than leaving a control that moves and does nothing — and it takes
	/// the label with it, since the label is a child of the row.
	static void HideRetiredSlider (UnityEngine.UI.Slider s) {
		if (s == null) return;
		var row = s.transform.parent != null ? s.transform.parent.gameObject : s.gameObject;
		row.SetActive (false);
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