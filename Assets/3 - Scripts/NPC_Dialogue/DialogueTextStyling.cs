using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// White text with a black outline at a uniform font size, for NPC dialogue.
// Handles both TextMeshProUGUI (NPCDialogue / BonfireNPCDialogue / FishMarketNPC)
// and legacy Text (GuitarShopNPC) so Alien6's bar matches the others. Idempotent.
public static class DialogueTextStyling
{
    // Reveal a TMP string one character at a time using maxVisibleCharacters,
    // which is zero-allocation per character — far better than the
    // `text += c` pattern (O(n²) string concatenation, allocates for every
    // character in every line of every dialogue).
    //
    // For legacy Unity Text (no maxVisibleCharacters), use RevealCharsLegacy.
    public static IEnumerator RevealCharsTMP(TextMeshProUGUI text, string line, float charDelay, System.Func<bool> skipPredicate)
    {
        if (text == null || string.IsNullOrEmpty(line)) yield break;
        text.text = line;
        text.maxVisibleCharacters = 0;
        // Force a mesh update so TMP knows the character count immediately —
        // otherwise the first frame can render with a stale max-visible value.
        text.ForceMeshUpdate();
        for (int i = 1; i <= line.Length; i++)
        {
            if (skipPredicate != null && skipPredicate())
            {
                text.maxVisibleCharacters = line.Length;
                yield break;
            }
            text.maxVisibleCharacters = i;
            yield return new WaitForSeconds(charDelay);
        }
        text.maxVisibleCharacters = line.Length;
    }

    // Legacy fallback for UnityEngine.UI.Text (no maxVisibleCharacters).
    // Uses Substring per character — still allocates, but each allocation is
    // a single string of length i instead of building it via repeated `+=`,
    // so the total allocation is O(n²/2) chars instead of O(n²).
    public static IEnumerator RevealCharsLegacy(Text text, string line, float charDelay, System.Func<bool> skipPredicate)
    {
        if (text == null || string.IsNullOrEmpty(line)) yield break;
        for (int i = 1; i <= line.Length; i++)
        {
            if (skipPredicate != null && skipPredicate()) { text.text = line; yield break; }
            text.text = line.Substring(0, i);
            yield return new WaitForSeconds(charDelay);
        }
        text.text = line;
    }

    const float DefaultFontSize = 44f;

    public static void ApplyOutline(Component text) => ApplyOutline(text, DefaultFontSize);

    public static void ApplyOutline(Component text, float fontSize)
    {
        if (text == null) return;

        if (text is TextMeshProUGUI tmp)
        {
            tmp.color = Color.white;
            // Auto-sizing would otherwise cap us below the requested size.
            tmp.enableAutoSizing = false;
            tmp.fontSize = fontSize;
            // Bold thickens the glyph face to match the visual weight that
            // legacy Text + Outline gives Alien6 from its 4-offset stroke copies.
            tmp.fontStyle |= FontStyles.Bold;

            // fontMaterial returns a per-instance material so this won't bleed
            // into other text using the same shared font asset.
            var mat = tmp.fontMaterial;
            mat.EnableKeyword("OUTLINE_ON");
            mat.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.2f);
            tmp.UpdateMeshPadding();
        }
        else if (text is Text legacy)
        {
            legacy.color = Color.white;
            legacy.resizeTextForBestFit = false;
            legacy.fontSize = Mathf.RoundToInt(fontSize);

            var outline = legacy.GetComponent<Outline>() ?? legacy.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(2f, -2f);
        }
    }
}
