using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GravityDebugUI : MonoBehaviour {
	// Static flag read by ResourceManager.TakeDamage. When true, all combat
	// damage to the player is dropped on the floor. Toggled via the backtick
	// debug menu's God Mode button. Static so it survives scene reloads /
	// the GravityDebugUI being torn down and recreated.
	public static bool GodMode = false;

	public TMP_Text info;

	[Header("Debug Money Button")]
	[Tooltip("Amount granted when the green +Money button is clicked. Toggled on/off with the gravity panel via the backtick key.")]
	public int debugMoneyAmount = 2000;

	[Header("Debug Wood Button")]
	[Tooltip("Wood granted when the brown +Wood button is clicked.")]
	public int debugWoodAmount = 100;

	bool show;

	GameObject _moneyButton;
	RectTransform _moneyButtonRT;
	RectTransform _gravityRT;
	TextMeshProUGUI _moneyButtonLabel;
	int _lastShownAmount = int.MinValue;

	GameObject _crashButton;
	RectTransform _crashButtonRT;
	TextMeshProUGUI _crashButtonLabel;
	Image _crashButtonImage;
	bool? _lastShownCrashState;

	GameObject _woodButton;
	RectTransform _woodButtonRT;
	TextMeshProUGUI _woodButtonLabel;
	int _lastShownWoodAmount = int.MinValue;

	GameObject _jetpackButton;
	RectTransform _jetpackButtonRT;
	TextMeshProUGUI _jetpackButtonLabel;
	bool? _lastShownJetpackUnlocked;
	PlayerController _cachedPlayer;

	GameObject _shipButton;
	RectTransform _shipButtonRT;
	TextMeshProUGUI _shipButtonLabel;

	GameObject _godButton;
	RectTransform _godButtonRT;
	TextMeshProUGUI _godButtonLabel;
	Image _godButtonImage;
	bool? _lastShownGodMode;

	void Update () {
		if (Input.GetKeyDown (KeyCode.BackQuote)) {
			show = !show;
			SetCursorState(show);
		}

		info.text = "";

		if (show) {
			var grav = GetGravityInfo (Camera.main.transform.position);
			for (int i = 0; i < grav.Length; i++) {
				info.text += grav[i] + "\n";
			}
		}

		EnsureMoneyButton();
		if (_moneyButton != null) {
			if (_moneyButton.activeSelf != show) _moneyButton.SetActive(show);
			if (show) {
				PositionMoneyButton();
				if (debugMoneyAmount != _lastShownAmount && _moneyButtonLabel != null) {
					_lastShownAmount = debugMoneyAmount;
					_moneyButtonLabel.text = $"+${debugMoneyAmount} (debug)";
				}
			}
		}

		EnsureCrashButton();
		if (_crashButton != null) {
			if (_crashButton.activeSelf != show) _crashButton.SetActive(show);
			if (show) {
				PositionCrashButton();
				bool disabled = ThrusterDetachOnImpact.DisableHardCrashes;
				if (!_lastShownCrashState.HasValue || _lastShownCrashState.Value != disabled) {
					_lastShownCrashState = disabled;
					if (_crashButtonLabel != null)
						_crashButtonLabel.text = disabled
							? "Hard Crashes: <b>OFF</b> (debug)"
							: "Hard Crashes: <b>ON</b> (debug)";
					if (_crashButtonImage != null)
						_crashButtonImage.color = disabled
							? new Color32(110, 35, 55, 235)  // muted red — hard crashes blocked
							: new Color32(35, 75, 130, 235); // muted blue — normal behaviour
				}
			}
		}

		EnsureWoodButton();
		if (_woodButton != null) {
			if (_woodButton.activeSelf != show) _woodButton.SetActive(show);
			if (show) {
				PositionWoodButton();
				if (debugWoodAmount != _lastShownWoodAmount && _woodButtonLabel != null) {
					_lastShownWoodAmount = debugWoodAmount;
					_woodButtonLabel.text = $"+{debugWoodAmount} Wood (debug)";
				}
			}
		}

		EnsureJetpackButton();
		if (_jetpackButton != null) {
			if (_jetpackButton.activeSelf != show) _jetpackButton.SetActive(show);
			if (show) {
				PositionJetpackButton();
				if (_cachedPlayer == null) _cachedPlayer = FindObjectOfType<PlayerController>();
				bool unlocked = _cachedPlayer != null && _cachedPlayer.JetpackUnlocked;
				if (!_lastShownJetpackUnlocked.HasValue || _lastShownJetpackUnlocked.Value != unlocked) {
					_lastShownJetpackUnlocked = unlocked;
					if (_jetpackButtonLabel != null)
						_jetpackButtonLabel.text = unlocked
							? "Jetpack: <b>UNLOCKED</b> (debug)"
							: "+Jetpack (debug)";
				}
			}
		}

		EnsureShipButton();
		if (_shipButton != null) {
			if (_shipButton.activeSelf != show) _shipButton.SetActive(show);
			if (show) PositionShipButton();
		}

		EnsureGodButton();
		if (_godButton != null) {
			if (_godButton.activeSelf != show) _godButton.SetActive(show);
			if (show) {
				PositionGodButton();
				if (!_lastShownGodMode.HasValue || _lastShownGodMode.Value != GodMode) {
					_lastShownGodMode = GodMode;
					if (_godButtonLabel != null)
						_godButtonLabel.text = GodMode
							? "God Mode: <b>ON</b> (debug)"
							: "God Mode: <b>OFF</b> (debug)";
					if (_godButtonImage != null)
						_godButtonImage.color = GodMode
							? new Color32(180, 145, 35, 235)  // gold while active
							: new Color32(80,  80,  90,  235); // neutral grey while off
				}
			}
		}
	}

	void EnsureMoneyButton() {
		if (_moneyButton != null) return;
		if (info == null) return;
		_gravityRT = info.GetComponent<RectTransform>();
		if (_gravityRT == null || _gravityRT.parent == null) return;

		var btnGo = new GameObject("Debug+Money", typeof(RectTransform));
		btnGo.transform.SetParent(_gravityRT.parent, false);
		_moneyButtonRT = btnGo.GetComponent<RectTransform>();
		// Inherit the gravity panel's anchor + scale so we sit in the same
		// coordinate space and the button looks proportional next to the text.
		_moneyButtonRT.anchorMin = _gravityRT.anchorMin;
		_moneyButtonRT.anchorMax = _gravityRT.anchorMax;
		_moneyButtonRT.pivot     = new Vector2(0f, 1f); // top-left so anchoredPosition.y is the top edge
		_moneyButtonRT.sizeDelta = new Vector2(260f, 48f);
		_moneyButtonRT.localScale = _gravityRT.localScale;

		var img = btnGo.AddComponent<Image>();
		img.color = new Color32(35, 110, 55, 235);

		var btn = btnGo.AddComponent<Button>();
		btn.targetGraphic = img;
		var colors = btn.colors;
		colors.normalColor      = new Color32(35,  110, 55, 235);
		colors.highlightedColor = new Color32(55,  150, 75, 245);
		colors.pressedColor     = new Color32(20,  75,  35, 245);
		colors.selectedColor    = new Color32(35,  110, 55, 235);
		btn.colors = colors;
		btn.onClick.AddListener(GrantMoney);

		var lblGo = new GameObject("Label", typeof(RectTransform));
		lblGo.transform.SetParent(btnGo.transform, false);
		var lblRT = lblGo.GetComponent<RectTransform>();
		lblRT.anchorMin = Vector2.zero;
		lblRT.anchorMax = Vector2.one;
		lblRT.offsetMin = Vector2.zero;
		lblRT.offsetMax = Vector2.zero;
		_moneyButtonLabel = lblGo.AddComponent<TextMeshProUGUI>();
		_moneyButtonLabel.text = $"+${debugMoneyAmount} (debug)";
		_moneyButtonLabel.alignment = TextAlignmentOptions.Center;
		_moneyButtonLabel.fontSize = 26;
		_moneyButtonLabel.fontStyle = FontStyles.Bold;
		_moneyButtonLabel.color = Color.white;
		_moneyButtonLabel.raycastTarget = false;
		_lastShownAmount = debugMoneyAmount;

		_moneyButton = btnGo;
	}

	void PositionMoneyButton() {
		if (_gravityRT == null || _moneyButtonRT == null || info == null) return;
		// The gravity rect has pivot (0,0) (bottom-left). Top-aligned text
		// starts at the rect's TOP edge (anchoredY + sizeDelta.y) and overflows
		// downward as more bodies are listed. Place the button just below the
		// rendered bottom of that text using TMP's preferredHeight.
		Vector2 gravPos = _gravityRT.anchoredPosition;
		float gravTopY = gravPos.y + _gravityRT.rect.height;
		float textBottomY = gravTopY - info.preferredHeight;
		_moneyButtonRT.anchoredPosition = new Vector2(gravPos.x, textBottomY - 14f);
	}

	void EnsureCrashButton() {
		if (_crashButton != null) return;
		if (info == null) return;
		if (_gravityRT == null) _gravityRT = info.GetComponent<RectTransform>();
		if (_gravityRT == null || _gravityRT.parent == null) return;

		var btnGo = new GameObject("Debug+NoHardCrash", typeof(RectTransform));
		btnGo.transform.SetParent(_gravityRT.parent, false);
		_crashButtonRT = btnGo.GetComponent<RectTransform>();
		// Same coordinate space as the +Money button so the two stack cleanly.
		_crashButtonRT.anchorMin = _gravityRT.anchorMin;
		_crashButtonRT.anchorMax = _gravityRT.anchorMax;
		_crashButtonRT.pivot     = new Vector2(0f, 1f);
		_crashButtonRT.sizeDelta = new Vector2(260f, 48f);
		_crashButtonRT.localScale = _gravityRT.localScale;

		_crashButtonImage = btnGo.AddComponent<Image>();
		_crashButtonImage.color = new Color32(35, 75, 130, 235);

		var btn = btnGo.AddComponent<Button>();
		btn.targetGraphic = _crashButtonImage;
		// Highlight / press tints derive from base each click since the base
		// flips between blue (on) and red (off). Keeping the base in sync via
		// the Update label refresh is enough — Button.colors auto-tint these.
		btn.onClick.AddListener(ToggleHardCrashes);

		var lblGo = new GameObject("Label", typeof(RectTransform));
		lblGo.transform.SetParent(btnGo.transform, false);
		var lblRT = lblGo.GetComponent<RectTransform>();
		lblRT.anchorMin = Vector2.zero;
		lblRT.anchorMax = Vector2.one;
		lblRT.offsetMin = Vector2.zero;
		lblRT.offsetMax = Vector2.zero;
		_crashButtonLabel = lblGo.AddComponent<TextMeshProUGUI>();
		_crashButtonLabel.text = "Hard Crashes: <b>ON</b> (debug)";
		_crashButtonLabel.alignment = TextAlignmentOptions.Center;
		_crashButtonLabel.fontSize = 22;
		_crashButtonLabel.fontStyle = FontStyles.Bold;
		_crashButtonLabel.color = Color.white;
		_crashButtonLabel.raycastTarget = false;

		_crashButton = btnGo;
	}

	void PositionCrashButton() {
		if (_moneyButtonRT == null || _crashButtonRT == null) return;
		// Sit directly below the +Money button. Both buttons use top-left pivot
		// so the lower button's Y is the upper button's Y minus its height
		// minus a small gap.
		Vector2 moneyPos = _moneyButtonRT.anchoredPosition;
		float moneyHeight = _moneyButtonRT.sizeDelta.y;
		_crashButtonRT.anchoredPosition = new Vector2(moneyPos.x, moneyPos.y - moneyHeight - 8f);
	}

	void ToggleHardCrashes() {
		ThrusterDetachOnImpact.DisableHardCrashes = !ThrusterDetachOnImpact.DisableHardCrashes;
	}

	void EnsureWoodButton() {
		if (_woodButton != null) return;
		if (info == null) return;
		if (_gravityRT == null) _gravityRT = info.GetComponent<RectTransform>();
		if (_gravityRT == null || _gravityRT.parent == null) return;

		var btnGo = new GameObject("Debug+Wood", typeof(RectTransform));
		btnGo.transform.SetParent(_gravityRT.parent, false);
		_woodButtonRT = btnGo.GetComponent<RectTransform>();
		_woodButtonRT.anchorMin = _gravityRT.anchorMin;
		_woodButtonRT.anchorMax = _gravityRT.anchorMax;
		_woodButtonRT.pivot     = new Vector2(0f, 1f);
		_woodButtonRT.sizeDelta = new Vector2(260f, 48f);
		_woodButtonRT.localScale = _gravityRT.localScale;

		var img = btnGo.AddComponent<Image>();
		img.color = new Color32(120, 75, 35, 235); // wood/oak tint

		var btn = btnGo.AddComponent<Button>();
		btn.targetGraphic = img;
		var colors = btn.colors;
		colors.normalColor      = new Color32(120, 75,  35, 235);
		colors.highlightedColor = new Color32(150, 100, 50, 245);
		colors.pressedColor     = new Color32(85,  55,  25, 245);
		colors.selectedColor    = new Color32(120, 75,  35, 235);
		btn.colors = colors;
		btn.onClick.AddListener(GrantWood);

		var lblGo = new GameObject("Label", typeof(RectTransform));
		lblGo.transform.SetParent(btnGo.transform, false);
		var lblRT = lblGo.GetComponent<RectTransform>();
		lblRT.anchorMin = Vector2.zero;
		lblRT.anchorMax = Vector2.one;
		lblRT.offsetMin = Vector2.zero;
		lblRT.offsetMax = Vector2.zero;
		_woodButtonLabel = lblGo.AddComponent<TextMeshProUGUI>();
		_woodButtonLabel.text = $"+{debugWoodAmount} Wood (debug)";
		_woodButtonLabel.alignment = TextAlignmentOptions.Center;
		_woodButtonLabel.fontSize = 26;
		_woodButtonLabel.fontStyle = FontStyles.Bold;
		_woodButtonLabel.color = Color.white;
		_woodButtonLabel.raycastTarget = false;
		_lastShownWoodAmount = debugWoodAmount;

		_woodButton = btnGo;
	}

	void PositionWoodButton() {
		if (_crashButtonRT == null || _woodButtonRT == null) return;
		// Stack directly under the Crash button (which sits under +Money).
		Vector2 crashPos = _crashButtonRT.anchoredPosition;
		float crashHeight = _crashButtonRT.sizeDelta.y;
		_woodButtonRT.anchoredPosition = new Vector2(crashPos.x, crashPos.y - crashHeight - 8f);
	}

	void GrantWood() {
		if (WoodInventory.Instance != null) {
			WoodInventory.Instance.AddWood(debugWoodAmount);
		} else {
			Debug.LogWarning("[GravityDebugUI] WoodInventory.Instance is null — wood not granted.");
		}
	}

	void EnsureJetpackButton() {
		if (_jetpackButton != null) return;
		if (info == null) return;
		if (_gravityRT == null) _gravityRT = info.GetComponent<RectTransform>();
		if (_gravityRT == null || _gravityRT.parent == null) return;

		var btnGo = new GameObject("Debug+Jetpack", typeof(RectTransform));
		btnGo.transform.SetParent(_gravityRT.parent, false);
		_jetpackButtonRT = btnGo.GetComponent<RectTransform>();
		_jetpackButtonRT.anchorMin = _gravityRT.anchorMin;
		_jetpackButtonRT.anchorMax = _gravityRT.anchorMax;
		_jetpackButtonRT.pivot     = new Vector2(0f, 1f);
		_jetpackButtonRT.sizeDelta = new Vector2(260f, 48f);
		_jetpackButtonRT.localScale = _gravityRT.localScale;

		var img = btnGo.AddComponent<Image>();
		img.color = new Color32(110, 70, 175, 235); // purple — distinct from money/wood/crash colours

		var btn = btnGo.AddComponent<Button>();
		btn.targetGraphic = img;
		var colors = btn.colors;
		colors.normalColor      = new Color32(110, 70,  175, 235);
		colors.highlightedColor = new Color32(140, 95,  210, 245);
		colors.pressedColor     = new Color32(80,  50,  130, 245);
		colors.selectedColor    = new Color32(110, 70,  175, 235);
		btn.colors = colors;
		btn.onClick.AddListener(GrantJetpack);

		var lblGo = new GameObject("Label", typeof(RectTransform));
		lblGo.transform.SetParent(btnGo.transform, false);
		var lblRT = lblGo.GetComponent<RectTransform>();
		lblRT.anchorMin = Vector2.zero;
		lblRT.anchorMax = Vector2.one;
		lblRT.offsetMin = Vector2.zero;
		lblRT.offsetMax = Vector2.zero;
		_jetpackButtonLabel = lblGo.AddComponent<TextMeshProUGUI>();
		_jetpackButtonLabel.text = "+Jetpack (debug)";
		_jetpackButtonLabel.alignment = TextAlignmentOptions.Center;
		_jetpackButtonLabel.fontSize = 24;
		_jetpackButtonLabel.fontStyle = FontStyles.Bold;
		_jetpackButtonLabel.color = Color.white;
		_jetpackButtonLabel.raycastTarget = false;

		_jetpackButton = btnGo;
	}

	void PositionJetpackButton() {
		if (_woodButtonRT == null || _jetpackButtonRT == null) return;
		// Stack directly under the +Wood button (which sits under +NoHardCrash + +Money).
		Vector2 woodPos = _woodButtonRT.anchoredPosition;
		float woodHeight = _woodButtonRT.sizeDelta.y;
		_jetpackButtonRT.anchoredPosition = new Vector2(woodPos.x, woodPos.y - woodHeight - 8f);
	}

	void GrantJetpack() {
		if (_cachedPlayer == null) _cachedPlayer = FindObjectOfType<PlayerController>();
		if (_cachedPlayer != null) {
			_cachedPlayer.UnlockJetpack();
			// Buying from Alien7 only flips jetpackUnlocked and then the bonus
			// tutorial drip-feeds Jump/Boost/DownThrust/DirectionalThrust
			// through TutorialGate one step at a time. The debug button is a
			// shortcut, so open all four gates immediately — otherwise the
			// HUD shows up but Space/Ctrl/Shift+WASD do nothing.
			TutorialGate.Unlock(TutorialAbility.Jump);
			TutorialGate.Unlock(TutorialAbility.Boost);
			TutorialGate.Unlock(TutorialAbility.DownThrust);
			TutorialGate.Unlock(TutorialAbility.DirectionalThrust);
		} else {
			Debug.LogWarning("[GravityDebugUI] PlayerController not found — jetpack not granted.");
		}
	}

	void GrantMoney() {
		if (PlayerWallet.Instance != null) {
			PlayerWallet.Instance.AddMoney(debugMoneyAmount);
		} else {
			Debug.LogWarning("[GravityDebugUI] PlayerWallet.Instance is null — money not granted.");
		}
	}

	void EnsureShipButton() {
		if (_shipButton != null) return;
		if (info == null) return;
		if (_gravityRT == null) _gravityRT = info.GetComponent<RectTransform>();
		if (_gravityRT == null || _gravityRT.parent == null) return;

		var btnGo = new GameObject("Debug+Ship", typeof(RectTransform));
		btnGo.transform.SetParent(_gravityRT.parent, false);
		_shipButtonRT = btnGo.GetComponent<RectTransform>();
		_shipButtonRT.anchorMin = _gravityRT.anchorMin;
		_shipButtonRT.anchorMax = _gravityRT.anchorMax;
		_shipButtonRT.pivot     = new Vector2(0f, 1f);
		_shipButtonRT.sizeDelta = new Vector2(260f, 48f);
		_shipButtonRT.localScale = _gravityRT.localScale;

		var img = btnGo.AddComponent<Image>();
		img.color = new Color32(35, 130, 130, 235); // teal — distinct from money/wood/jetpack/crash

		var btn = btnGo.AddComponent<Button>();
		btn.targetGraphic = img;
		var colors = btn.colors;
		colors.normalColor      = new Color32(35,  130, 130, 235);
		colors.highlightedColor = new Color32(55,  165, 165, 245);
		colors.pressedColor     = new Color32(20,  90,  90,  245);
		colors.selectedColor    = new Color32(35,  130, 130, 235);
		btn.colors = colors;
		btn.onClick.AddListener(SpawnShip44);

		var lblGo = new GameObject("Label", typeof(RectTransform));
		lblGo.transform.SetParent(btnGo.transform, false);
		var lblRT = lblGo.GetComponent<RectTransform>();
		lblRT.anchorMin = Vector2.zero;
		lblRT.anchorMax = Vector2.one;
		lblRT.offsetMin = Vector2.zero;
		lblRT.offsetMax = Vector2.zero;
		_shipButtonLabel = lblGo.AddComponent<TextMeshProUGUI>();
		_shipButtonLabel.text = "+Ship44 (debug)";
		_shipButtonLabel.alignment = TextAlignmentOptions.Center;
		_shipButtonLabel.fontSize = 24;
		_shipButtonLabel.fontStyle = FontStyles.Bold;
		_shipButtonLabel.color = Color.white;
		_shipButtonLabel.raycastTarget = false;

		_shipButton = btnGo;
	}

	void PositionShipButton() {
		if (_jetpackButtonRT == null || _shipButtonRT == null) return;
		// Stack directly under the +Jetpack button.
		Vector2 jetpackPos = _jetpackButtonRT.anchoredPosition;
		float jetpackHeight = _jetpackButtonRT.sizeDelta.y;
		_shipButtonRT.anchoredPosition = new Vector2(jetpackPos.x, jetpackPos.y - jetpackHeight - 8f);
	}

	void EnsureGodButton() {
		if (_godButton != null) return;
		if (info == null) return;
		if (_gravityRT == null) _gravityRT = info.GetComponent<RectTransform>();
		if (_gravityRT == null || _gravityRT.parent == null) return;

		var btnGo = new GameObject("Debug+God", typeof(RectTransform));
		btnGo.transform.SetParent(_gravityRT.parent, false);
		_godButtonRT = btnGo.GetComponent<RectTransform>();
		_godButtonRT.anchorMin = _gravityRT.anchorMin;
		_godButtonRT.anchorMax = _gravityRT.anchorMax;
		_godButtonRT.pivot     = new Vector2(0f, 1f);
		_godButtonRT.sizeDelta = new Vector2(260f, 48f);
		_godButtonRT.localScale = _gravityRT.localScale;

		_godButtonImage = btnGo.AddComponent<Image>();
		_godButtonImage.color = new Color32(80, 80, 90, 235);

		var btn = btnGo.AddComponent<Button>();
		btn.targetGraphic = _godButtonImage;
		btn.onClick.AddListener(ToggleGodMode);

		var lblGo = new GameObject("Label", typeof(RectTransform));
		lblGo.transform.SetParent(btnGo.transform, false);
		var lblRT = lblGo.GetComponent<RectTransform>();
		lblRT.anchorMin = Vector2.zero;
		lblRT.anchorMax = Vector2.one;
		lblRT.offsetMin = Vector2.zero;
		lblRT.offsetMax = Vector2.zero;
		_godButtonLabel = lblGo.AddComponent<TextMeshProUGUI>();
		_godButtonLabel.text = "God Mode: <b>OFF</b> (debug)";
		_godButtonLabel.alignment = TextAlignmentOptions.Center;
		_godButtonLabel.fontSize = 22;
		_godButtonLabel.fontStyle = FontStyles.Bold;
		_godButtonLabel.color = Color.white;
		_godButtonLabel.raycastTarget = false;

		_godButton = btnGo;
	}

	void PositionGodButton() {
		if (_shipButtonRT == null || _godButtonRT == null) return;
		// Stack directly under the +Ship button.
		Vector2 shipPos = _shipButtonRT.anchoredPosition;
		float shipHeight = _shipButtonRT.sizeDelta.y;
		_godButtonRT.anchoredPosition = new Vector2(shipPos.x, shipPos.y - shipHeight - 8f);
	}

	void ToggleGodMode() {
		GodMode = !GodMode;
	}

	void SpawnShip44() {
		if (_cachedPlayer == null) _cachedPlayer = FindObjectOfType<PlayerController>();
		if (_cachedPlayer == null) {
			Debug.LogWarning("[GravityDebugUI] PlayerController not found — ship not spawned.");
			return;
		}
		var vendor = FindObjectOfType<ShipMarketNPC>(true);
		if (vendor == null || vendor.shipPrefab == null) {
			Debug.LogWarning("[GravityDebugUI] ShipMarketNPC / shipPrefab not found.");
			return;
		}

		// Route through the vendor's spawn path so the debug-spawned ship
		// gets the BoughtShip marker (save round-trip), the persistent
		// shipNumber (legend ordering), the Full-tier attachment config,
		// and EndlessManager registration. Same behavior as if the player
		// had bought it — just at the player's location, free.
		Vector3 spawnPos = _cachedPlayer.transform.position + _cachedPlayer.transform.up * 3f;
		Quaternion spawnRot = _cachedPlayer.transform.rotation;
		vendor.SpawnShipInstance(vendor.shipPrefab, ShopItemKind.ShipFull, spawnPos, spawnRot, matchNearestVelocity: true);
	}

	void SetCursorState(bool unlocked) {
		// Unlock when the debug panel opens so the player can click the +Money
		// button; relock on close so normal gameplay resumes.
		Cursor.lockState = unlocked ? CursorLockMode.None : CursorLockMode.Locked;
		Cursor.visible   = unlocked;
	}

	void OnDisable() {
		// If the script gets disabled (e.g. on scene unload) while the panel
		// was open, leave the cursor locked so we don't strand the next scene
		// with a free cursor.
		if (show) {
			show = false;
			SetCursorState(false);
		}
	}

	static string[] GetGravityInfo (Vector3 point, CelestialBody ignore = null) {
		CelestialBody[] bodies = GameObject.FindObjectsOfType<CelestialBody> ();
		Vector3 totalAcc = Vector3.zero;

		// gravity
		var forceAndName = new List<FloatAndString> ();
		foreach (CelestialBody body in bodies) {
			if (body != ignore) {
				var offsetToBody = body.Position - point;
				var sqrDst = offsetToBody.sqrMagnitude;
				float dst = Mathf.Sqrt (sqrDst);
				var dirToBody = offsetToBody / Mathf.Sqrt (sqrDst);
				var acceleration = Universe.gravitationalConstant * body.mass / sqrDst;
				totalAcc += dirToBody * acceleration;
				forceAndName.Add (new FloatAndString () { floatVal = acceleration, stringVal = body.gameObject.name });

			}
		}
		forceAndName.Sort ((a, b) => (b.floatVal.CompareTo (a.floatVal)));
		string[] info = new string[forceAndName.Count + 1];
		//info[0] = $"acceleration: {totalAcc.magnitude:0.00})";
		info[0] = "Acceleration due to bodies: (m/s^2)";
		for (int i = 0; i < forceAndName.Count; i++) {
			info[i + 1] = $"{forceAndName[i].stringVal}: {forceAndName[i].floatVal:0.00}".Replace (",", ".");
		}
		return info;
	}

	struct FloatAndString {
		public float floatVal;
		public string stringVal;
	}
}
