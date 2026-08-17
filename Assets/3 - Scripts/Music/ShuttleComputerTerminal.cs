using UnityEngine;

/// <summary>
/// Look at the shuttle's ConsoleScreen, press F, and the computer opens.
///
/// Lives on a child trigger volume of ConsoleScreen rather than on the screen
/// itself: Interactable needs an isTrigger collider for its zone test, and
/// ConsoleScreen's own BoxCollider is solid geometry the player can walk into.
/// `gazeTarget` points back at the screen mesh so the look-at test uses the
/// thing you can actually see, not the invisible volume around it.
///
/// Add it with Tools ▸ TRAX ▸ Add Computer Terminal To Shuttle Prefab, which
/// patches the prefab via LoadPrefabContents — the Shuttle_Lander prefab is
/// hand-maintained and must never be regenerated.
/// </summary>
public class ShuttleComputerTerminal : Interactable
{
    void Start()
    {
        // The screen SHOWS the computer (Sam's request): the mesh gets the
        // mirror texture the UI snapshots itself into on every close. An
        // instance of the screen's OWN material (shader guaranteed in the
        // build) with the mirror as its texture — dark navy until first use.
        Transform screen = gazeTarget != null ? gazeTarget : transform.parent;
        if (screen == null) return;
        var rend = screen.GetComponent<Renderer>();
        if (rend == null) rend = screen.GetComponentInChildren<Renderer>();
        if (rend == null || rend.sharedMaterial == null) return;

        // Unlit first — a screen should show its pixels, not lighting. The
        // original material's shader may have no _MainTex at all (the flat
        // cyan was a colour-only material, which is why the first mirror
        // attempt changed nothing on it).
        var mirror = ShuttleComputerUI.ScreenMirror;
        Shader unlit = Shader.Find("Unlit/Texture");
        Material mat = unlit != null ? new Material(unlit) : new Material(rend.sharedMaterial);
        if (mat.HasProperty("_Color")) mat.color = Color.white;
        mat.mainTexture = mirror;
        if (mat.HasProperty("_EmissionColor"))
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Color.white);
            mat.SetTexture("_EmissionMap", mirror);
        }
        rend.material = mat;
    }

    protected override string BuildInteractMessage()
    {
        return "Press " + PromptGlyphs.Interact + " to use the computer";
    }

    protected override bool CanInteract()
    {
        // While the screen is up it owns the input; showing "press F to use the
        // computer" over the top of the open computer would be nonsense.
        if (ShuttleComputerUI.IsOpen) return false;

        // ── The screen yields to the machine ─────────────────────────────
        // The cassette insert and the eject sit right under this screen, and
        // this screen is a far bigger gaze target than it looks: the crosshair
        // cast is a 0.1 m sphere and there is a near-miss forgiveness pass on
        // top. So when the player aims at the insert, BOTH pass the look-at
        // test, both call GameUI.ShowInteractionPrompt every frame, and the
        // prompt flips between "use the computer" and "insert blank cassette"
        // depending on script execution order. That reads as the prompt
        // strobing, and it makes the insert feel broken.
        //
        // The rule: the small, specific control beats the big panel behind it.
        // Standing down entirely (rather than fighting over ownership) is what
        // makes it deterministic — this asserts nothing at all while a machine
        // part is being looked at.
        if (CassetteSlot.AnyGazed) return false;

        return true;
    }

    protected override void Interact()
    {
        // The screen closes on F too. Without this, the F that closed it would
        // be read again here in the same frame and reopen it immediately.
        if (ShuttleComputerUI.FConsumedThisFrame) return;

        ShuttleComputerUI.Open();
    }
}
