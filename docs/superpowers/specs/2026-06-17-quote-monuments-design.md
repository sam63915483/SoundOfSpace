# Quote Monuments — Design

**Date:** 2026-06-17
**Status:** Approved, implementing

## Goal

Add decorative, lore-bearing "monuments" to the solar system: weathered, ancient
standing stones (Stonehenge-like) carved into upright slabs, each bearing a song
lyric the player finds meaningful. Walking up to one shows a "Press F to play"
prompt; pressing F opens the song's YouTube link in the browser.

First monument:
- Quote: *"There's too many home fires burning and not enough trees"*
- Attribution: Pink Floyd — *Not Now John*
- Link: https://www.youtube.com/watch?v=ajvk1CFIM1M

The author places + parents the monument to a planet manually. This task only
builds the asset and wires the interaction.

## Architecture

Reuse the existing interaction system — do NOT roll a new one.

- `Interactable` (base, `Assets/3 - Scripts/Scripts/Game/Interactions/Interactable.cs`)
  already handles: trigger-collider proximity (`OnTriggerEnter/Exit`, `CompareTag("Player")`),
  F-key / controller-X polling, and the shared "Press [F] to …" prompt pill UI
  (`InteractPromptUI` via `GameUI.ShowInteractionPrompt/ClearInteractionPrompt`).

### New component: `MonumentInteractable.cs`
Location: `Assets/3 - Scripts/World/MonumentInteractable.cs`

- Subclasses `Interactable`.
- Serialized fields (so future monuments are field edits, no new code):
  - `string songUrl` — the YouTube link.
  - `string songLabel` — e.g. "Not Now John"; used in the prompt.
- `BuildInteractMessage()` → `$"Press {PromptGlyphs.Interact} to play {songLabel}"`
  (falls back to "play music" when `songLabel` is empty).
- `Interact()` → guards empty/whitespace URL, then `Application.OpenURL(songUrl)`.
  No `Destroy` (monument persists, unlike pickups).

### The monument object (in scene + prefab)
- Mesh: Coplay/Meshy-generated weathered grey-granite standing-stone slab with a
  flat front face (`Assets/1 - samsPrefabs/Monuments/monument_stone.glb`).
- Child `TextMeshPro` 3D text just proud of the front face:
  > "There's too many home fires burning and not enough trees"
  > — Pink Floyd, *Not Now John*
- `SphereCollider` (isTrigger) sized so the prompt appears at a natural walk-up
  distance.
- `MonumentInteractable` component with `songUrl` + `songLabel` filled in.

## Delivery
- Built in gameplay scene `Assets/1.6.7.7.7.unity` near origin, **unparented** —
  author repositions and parents to a `CelestialBody`.
- Saved as a prefab in `Assets/1 - samsPrefabs/Monuments/` so future quote-stones
  are copy → edit two fields → edit text.

## Risks / notes
- Meshy mesh gen is slow + the MCP call times out at 60s but the `.glb` lands
  asynchronously (known Coplay behaviour). If the mesh has no usable flat face,
  fit text on the cleanest face or regenerate.
- Built-in RP, NOT URP — keep materials Standard.
- Not a save-system concern: a static decorative object with no runtime state.
- Future idea (separate task): a burning house + dead trees scene near the
  monument as environmental storytelling for this exact lyric. Fire must use
  baked/faked glow, not many real lights (spawn area is already draw/shadow bound).
```
