# Cat Perks — design mockup

> **Status: MOCKUP ONLY.** Nothing in this folder touches the game. It exists so
> Sam can *feel* three different case-opening presentations and dial the odds
> before a line of C# gets written.

**Run:** double-click `Cat Perks.bat` (or `py -3 tools/cat-perks/serve.py`)
→ <http://localhost:8767>

Port 8767, after Dialogue Studio (8765) and Audio Studio (8766).

## The idea

A low-poly cat sits somewhere on a planet. Look at it → green outline + **F**.
It says *"Meow! Spare a fish?"*. **No** does nothing. **Yes** opens your fish
pockets; hand over any fish, any species, and the cat rolls you a temporary
**fishing perk** out of a CS:GO-style case.

This is the game's one bit of gambling: junk commons you would never bother
flying to a market become lottery tickets.

## The three perks

Every one of these drives a knob that **already exists** in the fishing code —
none of it is invented.

| Perk | What the player feels | Real hook |
|---|---|---|
| 🍀 **Lucky Whiskers** | bites sooner, better fish, bigger fish | `FishingRules.TierWeights` (uncommon+rare ×1.20), `WaitMultiplier` (×0.90), `RollWeight` exponent (×0.92) |
| ⚖️ **Weight Gain** | same fish, fatter | `RollWeight` exponent ×0.80 — the exact dial bait already uses (Voidmaggots is 0.78) |
| ⚡ **Fishing Frenzy** | bites land almost instantly, odds untouched | `Bobber._biteOddsBonus` ×2.0 — the bite countdown already multiplies by this field |

Each comes in **1 / 2 / 3 minutes**. Magnitude is constant; the **duration** is
the prize.

## Rules (decided by Sam, 2026-09-10)

- **One slot. Perks never stack.**
- **Re-rolling while a perk is running is allowed**, and whatever comes out
  **replaces** it. The timer is set to the *new* tier's full length — never
  topped up, never added to the old one.
- No confirmation box. The fish picker shows what is running and how long is
  left, then lets you do it anyway. The result screen tells you what you threw
  away and how many seconds were on it.
- **Tiers are duration only.** A 3-min +20% luck is a longer 1-min +20% luck.

### The consequence worth knowing

`SHOULD I RE-ROLL?` in the right-hand panel: holding a **1-minute** perk, a
re-roll **cannot lose you duration** — the worst case is another 1-minute with
a fresh timer. So a 1-min is always worth re-rolling, at any rarity setting.
Nothing currently stops a player standing at one cat feeding it until a 3-min
drops. The **CAT NAP** slider (off by default, matching the spec) is there to
feel what a cooldown fixes.

## The three presentations

| | Shape | Where the tension is | Run time |
|---|---|---|---|
| **A · REEL** | classic horizontal CS:GO strip, ticker in the middle | the deceleration — will it creep one more card? | ~7 s |
| **B · TAG DROP** | the same reel turned vertical, 264 px wide | same, but it fits down the side of the helmet HUD instead of eating the screen | ~6 s |
| **C · STAR CLIMB** | perk settles in 1 s, then the minutes light up one at a time | the third pip — the only thing you were ever gambling on | ~4 s |

## Odds

Geometric ladder. The slider sets how many times rarer a 3-minute perk is than
a 1-minute one; the 2-minute sits exactly halfway up.

At the default **3×**:

| | chance | 1 in |
|---|---|---|
| any 1-min | 52.3 % | 2 |
| any 2-min | 30.2 % | 3 |
| any 3-min | **17.4 %** | **6** |

…so about six fish per 3-minute buff. `SIMULATE 10,000 ROLLS` checks the
generator actually produces that.

## Files

```
serve.py       static server, stdlib only
index.html     scene + phosphor plate + right-hand panel
styles.css     PHOSPHOR palette, copied from PhosphorUI in PhosphorDialogueBox.cs
app.js         odds engine, the three case animations, WebAudio blips
```
