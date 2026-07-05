# Cut dimensions archive (2026-07-05)

Nine of the D9–D28 test-reel dimensions were cut after the first playtest. **Nothing
is lost** — every file exists in git history, and the exact pre-cut state is tagged:

```
git tag: archive/dimensions-v1-all20        (commit 371878de)
```

To inspect or restore any file:
```
git show archive/dimensions-v1-all20:"Assets/3 - Scripts/Dimensions/<Controller>.cs"
git checkout archive/dimensions-v1-all20 -- "Assets/3 - Scripts/Dimensions/<Controller>.cs" "Assets/3 - Scripts/Dimensions/<Controller>.cs.meta"
git checkout archive/dimensions-v1-all20 -- "Assets/4 - Scenes/Dimensions/<Scene>.unity" "Assets/4 - Scenes/Dimensions/<Scene>.unity.meta"
```
(after restoring a scene, re-add it to Build Settings; the dev loader still knows all
28 names, so Shift+D + its number works again immediately.)

## What was cut and why it existed

| # | Scene | Controller | Concept (one line) |
|---|-------|------------|--------------------|
| D10 | D10_SaltFlats | SaltFlatsController | Blinding white plain, black sky; cracks gape open where you are NOT looking, swallow you back to spawn; black ziggurat exit. |
| D14 | D14_GlacierThroat | GlacierThroatController | Ice canyon; sliver-tile ghost-ice bridge (D3 rule) over a blue-glow crevasse. |
| D17 | D17_TidePools | TidePoolsController | Moonlit shallows; sliver-tile stepping rocks across a deep channel; bioluminescent far shore. |
| D19 | D19_BoneGarden | BoneGardenController | Bone spires/ribs; true arch relocates unseen + heartbeat gaze-audio; decoy arches scatter you. |
| D20 | D20_CloudShelf | CloudShelfController | FULL inversion — clouds solid only while UNOBSERVED; cross the sunset sky walking backwards. |
| D21 | D21_ArchiveStacks | ArchiveStacksController | Library tower; helix of sliver-tile ledges climbing to a whispering door. |
| D26 | D26_Ferry | FerryController | Stone pier over black water; segments crumble forever behind you; bell-toll boat exit. |
| D27 | D27_InvertedRain | InvertedRainController | Plaza where rain falls UP; doors swap slots; the one down-rain door is real (particle tell). |
| D28 | D28_LongTable | LongTableController | Banquet-table gauntlet: chairs invade the tabletop, candelabra risers, fixed far door → Backrooms. |

Mechanics worth re-mining later: the **full inversion** (D20) and the **one-way
crumble** (D26) playtested cleanly and are strong rules with weak dressing; the
**particle tell** (D27) is a novel puzzle language.

`SliverTileSet.cs` (+ `DimensionRespawnVolume`) stays in the project — it's the
shared bridge engine for D14/D17/D21 and still useful.

## Post-cut chain (the 11 keepers)

D9 RedForest → D11 Shelves (server-farm remake) → D12 MirrorLake → D13 Orchard →
D15 Congregation → D16 NeonGrid → D18 StaticField → D22 RustSea → D23 WheatAtDusk →
D24 WaitingRoom → D25 CandleSea → R1_Backrooms. (D1–D8 main chain untouched.)
