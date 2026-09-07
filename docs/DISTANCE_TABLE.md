# Distance table — how far apart the planets get

Generated 2026-09-07 14:28 by `Tools ▸ Solar System ▸ Write Distance Table`
(`Assets/3 - Scripts/Editor/DistanceTableTool.cs`). Re-run it after changing any orbit.

Exact, not sampled: every planet rides a circular rail in one plane, so distance
is a closed-form function of the angle between two of them. Distances are metres
(the game's "km" readout is these numbers ÷ 1000).

## The planets

| Planet | Orbit radius | Day (period) | Direction | Water | Fish market |
|---|---:|---:|---|---|---|
| Fiery Twin | 6,019 | 10.0 min | prograde | yes | yes |
| Icey Twin | 6,019 | 10.0 min | prograde | yes | yes |
| Puddle | 6,900 | 10.7 min | prograde | yes | yes |
| Hearth | 7,450 | 11.2 min | prograde | yes | yes |
| Anvil | 8,000 | 11.6 min | prograde | yes | yes |
| Ember | 8,550 | 12.0 min | prograde | yes | yes |
| Slag | 9,100 | 12.5 min | prograde | yes | yes |
| Shard | 9,650 | 12.9 min | prograde | yes | yes |
| Pebble | 10,200 | 13.3 min | prograde | — | — |
| Bruise | 10,750 | 13.8 min | prograde | — | — |
| Humble Abode | 12,247 | 15.0 min | **retrograde** | yes | yes |
| Cyclops | 24,906 | 20.0 min | prograde | yes | yes |

## Every pair

`Closest`/`Furthest` are the true extremes. `Cycle` is how long the pair takes to
lap each other — the clock that matters for waiting, not either planet's day.
`In range` is the share of each cycle spent within the jump range, and `Worst wait`
is the longest you could ever be stuck waiting for the window to come round.

### At a 5 km jump range

| A | B | Closest | Furthest | Cycle | In range | Worst wait |
|---|---|---:|---:|---:|---:|---:|
| Fiery Twin | Icey Twin | 1,029 | 1,029 | always | **always** | — |
| Fiery Twin | Puddle | 881 | 12,919 | 160.0 min | 25%  (39.9 min) | 120.1 min |
| Fiery Twin | Hearth | 1,431 | 13,469 | 95.7 min | 23%  (22.3 min) | 73.4 min |
| Fiery Twin | Anvil | 1,981 | 14,019 | 73.2 min | 21%  (15.7 min) | 57.5 min |
| Fiery Twin | Ember | 2,531 | 14,569 | 60.0 min | 19%  (11.7 min) | 48.3 min |
| Fiery Twin | Slag | 3,081 | 15,119 | 50.0 min | 17%  (8.6 min) | 41.4 min |
| Fiery Twin | Shard | 3,631 | 15,669 | 44.3 min | 14%  (6.4 min) | 37.9 min |
| Fiery Twin | Pebble | 4,181 | 16,219 | 40.0 min | 11%  (4.5 min) | 35.5 min |
| Fiery Twin | Bruise | 4,731 | 16,769 | 36.1 min | 6%  (2.3 min) | 33.8 min |
| Fiery Twin | Humble Abode | 6,228 | 18,266 | 6.0 min | **never** | forever |
| Fiery Twin | Cyclops | 18,887 | 30,924 | 20.0 min | **never** | forever |
| Icey Twin | Puddle | 881 | 12,919 | 160.0 min | 25%  (39.9 min) | 120.1 min |
| Icey Twin | Hearth | 1,431 | 13,469 | 95.7 min | 23%  (22.3 min) | 73.4 min |
| Icey Twin | Anvil | 1,981 | 14,019 | 73.2 min | 21%  (15.7 min) | 57.5 min |
| Icey Twin | Ember | 2,531 | 14,569 | 60.0 min | 19%  (11.7 min) | 48.3 min |
| Icey Twin | Slag | 3,081 | 15,119 | 50.0 min | 17%  (8.6 min) | 41.4 min |
| Icey Twin | Shard | 3,631 | 15,669 | 44.3 min | 14%  (6.4 min) | 37.9 min |
| Icey Twin | Pebble | 4,181 | 16,219 | 40.0 min | 11%  (4.5 min) | 35.5 min |
| Icey Twin | Bruise | 4,731 | 16,769 | 36.1 min | 6%  (2.3 min) | 33.8 min |
| Icey Twin | Humble Abode | 6,228 | 18,266 | 6.0 min | **never** | forever |
| Icey Twin | Cyclops | 18,887 | 30,924 | 20.0 min | **never** | forever |
| Puddle | Hearth | 550 | 14,350 | 238.2 min | 23%  (53.7 min) | 184.5 min |
| Puddle | Anvil | 1,100 | 14,900 | 134.8 min | 21%  (28.7 min) | 106.1 min |
| Puddle | Ember | 1,650 | 15,450 | 96.0 min | 20%  (19.1 min) | 76.9 min |
| Puddle | Slag | 2,200 | 16,000 | 72.7 min | 18%  (13.3 min) | 59.4 min |
| Puddle | Shard | 2,750 | 16,550 | 61.2 min | 16%  (10.1 min) | 51.1 min |
| Puddle | Pebble | 3,300 | 17,100 | 53.3 min | 14%  (7.7 min) | 45.7 min |
| Puddle | Bruise | 3,850 | 17,650 | 46.6 min | 12%  (5.5 min) | 41.1 min |
| Puddle | Humble Abode | 5,347 | 19,147 | 6.2 min | **never** | forever |
| Puddle | Cyclops | 18,006 | 31,806 | 22.9 min | **never** | forever |
| Hearth | Anvil | 550 | 15,450 | 310.4 min | 21%  (64.8 min) | 245.7 min |
| Hearth | Ember | 1,100 | 16,000 | 160.8 min | 20%  (31.8 min) | 129.0 min |
| Hearth | Slag | 1,650 | 16,550 | 104.7 min | 19%  (19.4 min) | 85.3 min |
| Hearth | Shard | 2,200 | 17,100 | 82.4 min | 17%  (14.1 min) | 68.4 min |
| Hearth | Pebble | 2,750 | 17,650 | 68.7 min | 15%  (10.6 min) | 58.1 min |
| Hearth | Bruise | 3,300 | 18,200 | 57.9 min | 13%  (7.8 min) | 50.1 min |
| Hearth | Humble Abode | 4,797 | 19,697 | 6.4 min | 5%  (18 s) | 6.1 min |
| Hearth | Cyclops | 17,456 | 32,356 | 25.3 min | **never** | forever |
| Anvil | Ember | 550 | 16,550 | 333.6 min | 19%  (64.8 min) | 268.8 min |
| Anvil | Slag | 1,100 | 17,100 | 158.0 min | 18%  (29.1 min) | 128.8 min |
| Anvil | Shard | 1,650 | 17,650 | 112.2 min | 17%  (19.4 min) | 92.8 min |
| Anvil | Pebble | 2,200 | 18,200 | 88.3 min | 16%  (14.1 min) | 74.1 min |
| Anvil | Bruise | 2,750 | 18,750 | 71.2 min | 14%  (10.3 min) | 60.9 min |
| Anvil | Humble Abode | 4,247 | 20,247 | 6.5 min | 9%  (33 s) | 6.0 min |
| Anvil | Cyclops | 16,906 | 32,906 | 27.5 min | **never** | forever |
| Ember | Slag | 550 | 17,650 | 300.0 min | 18%  (54.5 min) | 245.5 min |
| Ember | Shard | 1,100 | 18,200 | 169.1 min | 17%  (29.3 min) | 139.8 min |
| Ember | Pebble | 1,650 | 18,750 | 120.0 min | 16%  (19.5 min) | 100.5 min |
| Ember | Bruise | 2,200 | 19,300 | 90.5 min | 15%  (13.6 min) | 76.9 min |
| Ember | Humble Abode | 3,697 | 20,797 | 6.7 min | 11%  (42 s) | 6.0 min |
| Ember | Cyclops | 16,356 | 33,456 | 30.0 min | **never** | forever |
| Slag | Shard | 550 | 18,750 | 387.5 min | 17%  (66.2 min) | 321.3 min |
| Slag | Pebble | 1,100 | 19,300 | 200.0 min | 16%  (32.6 min) | 167.4 min |
| Slag | Bruise | 1,650 | 19,850 | 129.7 min | 15%  (19.9 min) | 109.8 min |
| Slag | Humble Abode | 3,147 | 21,347 | 6.8 min | 12%  (48 s) | 6.0 min |
| Slag | Cyclops | 15,806 | 34,006 | 33.3 min | **never** | forever |
| Shard | Pebble | 550 | 19,850 | 413.3 min | 16%  (66.6 min) | 346.7 min |
| Shard | Bruise | 1,100 | 20,400 | 194.9 min | 15%  (30.0 min) | 164.9 min |
| Shard | Humble Abode | 2,597 | 21,897 | 6.9 min | 13%  (52 s) | 6.1 min |
| Shard | Cyclops | 15,256 | 34,556 | 36.5 min | **never** | forever |
| Pebble | Bruise | 550 | 20,950 | 368.9 min | 15%  (56.3 min) | 312.6 min |
| Pebble | Humble Abode | 2,047 | 22,447 | 7.1 min | 13%  (55 s) | 6.1 min |
| Pebble | Cyclops | 14,706 | 35,106 | 40.0 min | **never** | forever |
| Bruise | Humble Abode | 1,497 | 22,997 | 7.2 min | 13%  (58 s) | 6.2 min |
| Bruise | Cyclops | 14,156 | 35,656 | 44.9 min | **never** | forever |
| Humble Abode | Cyclops | 12,659 | 37,153 | 8.6 min | **never** | forever |

### At a 8 km jump range

| A | B | Closest | Furthest | Cycle | In range | Worst wait |
|---|---|---:|---:|---:|---:|---:|
| Fiery Twin | Icey Twin | 1,029 | 1,029 | always | **always** | — |
| Fiery Twin | Puddle | 881 | 12,919 | 160.0 min | 42%  (67.7 min) | 92.3 min |
| Fiery Twin | Hearth | 1,431 | 13,469 | 95.7 min | 40%  (38.3 min) | 57.4 min |
| Fiery Twin | Anvil | 1,981 | 14,019 | 73.2 min | 38%  (27.6 min) | 45.6 min |
| Fiery Twin | Ember | 2,531 | 14,569 | 60.0 min | 35%  (21.3 min) | 38.7 min |
| Fiery Twin | Slag | 3,081 | 15,119 | 50.0 min | 33%  (16.6 min) | 33.4 min |
| Fiery Twin | Shard | 3,631 | 15,669 | 44.3 min | 31%  (13.7 min) | 30.6 min |
| Fiery Twin | Pebble | 4,181 | 16,219 | 40.0 min | 29%  (11.5 min) | 28.5 min |
| Fiery Twin | Bruise | 4,731 | 16,769 | 36.1 min | 26%  (9.5 min) | 26.6 min |
| Fiery Twin | Humble Abode | 6,228 | 18,266 | 6.0 min | 19%  (68 s) | 4.9 min |
| Fiery Twin | Cyclops | 18,887 | 30,924 | 20.0 min | **never** | forever |
| Icey Twin | Puddle | 881 | 12,919 | 160.0 min | 42%  (67.7 min) | 92.3 min |
| Icey Twin | Hearth | 1,431 | 13,469 | 95.7 min | 40%  (38.3 min) | 57.4 min |
| Icey Twin | Anvil | 1,981 | 14,019 | 73.2 min | 38%  (27.6 min) | 45.6 min |
| Icey Twin | Ember | 2,531 | 14,569 | 60.0 min | 35%  (21.3 min) | 38.7 min |
| Icey Twin | Slag | 3,081 | 15,119 | 50.0 min | 33%  (16.6 min) | 33.4 min |
| Icey Twin | Shard | 3,631 | 15,669 | 44.3 min | 31%  (13.7 min) | 30.6 min |
| Icey Twin | Pebble | 4,181 | 16,219 | 40.0 min | 29%  (11.5 min) | 28.5 min |
| Icey Twin | Bruise | 4,731 | 16,769 | 36.1 min | 26%  (9.5 min) | 26.6 min |
| Icey Twin | Humble Abode | 6,228 | 18,266 | 6.0 min | 19%  (68 s) | 4.9 min |
| Icey Twin | Cyclops | 18,887 | 30,924 | 20.0 min | **never** | forever |
| Puddle | Hearth | 550 | 14,350 | 238.2 min | 38%  (89.5 min) | 148.7 min |
| Puddle | Anvil | 1,100 | 14,900 | 134.8 min | 36%  (48.3 min) | 86.5 min |
| Puddle | Ember | 1,650 | 15,450 | 96.0 min | 34%  (32.7 min) | 63.3 min |
| Puddle | Slag | 2,200 | 16,000 | 72.7 min | 32%  (23.5 min) | 49.3 min |
| Puddle | Shard | 2,750 | 16,550 | 61.2 min | 30%  (18.6 min) | 42.6 min |
| Puddle | Pebble | 3,300 | 17,100 | 53.3 min | 29%  (15.3 min) | 38.1 min |
| Puddle | Bruise | 3,850 | 17,650 | 46.6 min | 27%  (12.4 min) | 34.2 min |
| Puddle | Humble Abode | 5,347 | 19,147 | 6.2 min | 21%  (78 s) | 4.9 min |
| Puddle | Cyclops | 18,006 | 31,806 | 22.9 min | **never** | forever |
| Hearth | Anvil | 550 | 15,450 | 310.4 min | 35%  (107.4 min) | 203.1 min |
| Hearth | Ember | 1,100 | 16,000 | 160.8 min | 33%  (53.2 min) | 107.6 min |
| Hearth | Slag | 1,650 | 16,550 | 104.7 min | 32%  (33.0 min) | 71.7 min |
| Hearth | Shard | 2,200 | 17,100 | 82.4 min | 30%  (24.7 min) | 57.7 min |
| Hearth | Pebble | 2,750 | 17,650 | 68.7 min | 28%  (19.5 min) | 49.2 min |
| Hearth | Bruise | 3,300 | 18,200 | 57.9 min | 27%  (15.5 min) | 42.5 min |
| Hearth | Humble Abode | 4,797 | 19,697 | 6.4 min | 22%  (84 s) | 5.0 min |
| Hearth | Cyclops | 17,456 | 32,356 | 25.3 min | **never** | forever |
| Anvil | Ember | 550 | 16,550 | 333.6 min | 32%  (106.9 min) | 226.7 min |
| Anvil | Slag | 1,100 | 17,100 | 158.0 min | 31%  (48.6 min) | 109.4 min |
| Anvil | Shard | 1,650 | 17,650 | 112.2 min | 29%  (33.0 min) | 79.2 min |
| Anvil | Pebble | 2,200 | 18,200 | 88.3 min | 28%  (24.7 min) | 63.5 min |
| Anvil | Bruise | 2,750 | 18,750 | 71.2 min | 27%  (18.9 min) | 52.3 min |
| Anvil | Humble Abode | 4,247 | 20,247 | 6.5 min | 22%  (87 s) | 5.1 min |
| Anvil | Cyclops | 16,906 | 32,906 | 27.5 min | **never** | forever |
| Ember | Slag | 550 | 17,650 | 300.0 min | 30%  (89.7 min) | 210.3 min |
| Ember | Shard | 1,100 | 18,200 | 169.1 min | 29%  (48.6 min) | 120.5 min |
| Ember | Pebble | 1,650 | 18,750 | 120.0 min | 28%  (33.0 min) | 87.0 min |
| Ember | Bruise | 2,200 | 19,300 | 90.5 min | 26%  (23.8 min) | 66.8 min |
| Ember | Humble Abode | 3,697 | 20,797 | 6.7 min | 23%  (1.5 min) | 5.2 min |
| Ember | Cyclops | 16,356 | 33,456 | 30.0 min | **never** | forever |
| Slag | Shard | 550 | 18,750 | 387.5 min | 28%  (108.5 min) | 279.0 min |
| Slag | Pebble | 1,100 | 19,300 | 200.0 min | 27%  (54.0 min) | 146.0 min |
| Slag | Bruise | 1,650 | 19,850 | 129.7 min | 26%  (33.6 min) | 96.1 min |
| Slag | Humble Abode | 3,147 | 21,347 | 6.8 min | 23%  (1.5 min) | 5.3 min |
| Slag | Cyclops | 15,806 | 34,006 | 33.3 min | **never** | forever |
| Shard | Pebble | 550 | 19,850 | 413.3 min | 26%  (108.9 min) | 304.4 min |
| Shard | Bruise | 1,100 | 20,400 | 194.9 min | 25%  (49.6 min) | 145.3 min |
| Shard | Humble Abode | 2,597 | 21,897 | 6.9 min | 23%  (1.6 min) | 5.4 min |
| Shard | Cyclops | 15,256 | 34,556 | 36.5 min | **never** | forever |
| Pebble | Bruise | 550 | 20,950 | 368.9 min | 25%  (91.8 min) | 277.1 min |
| Pebble | Humble Abode | 2,047 | 22,447 | 7.1 min | 22%  (1.6 min) | 5.5 min |
| Pebble | Cyclops | 14,706 | 35,106 | 40.0 min | **never** | forever |
| Bruise | Humble Abode | 1,497 | 22,997 | 7.2 min | 22%  (1.6 min) | 5.6 min |
| Bruise | Cyclops | 14,156 | 35,656 | 44.9 min | **never** | forever |
| Humble Abode | Cyclops | 12,659 | 37,153 | 8.6 min | **never** | forever |

### At a 15 km jump range

| A | B | Closest | Furthest | Cycle | In range | Worst wait |
|---|---|---:|---:|---:|---:|---:|
| Fiery Twin | Icey Twin | 1,029 | 1,029 | always | **always** | — |
| Fiery Twin | Puddle | 881 | 12,919 | 160.0 min | **always** | — |
| Fiery Twin | Hearth | 1,431 | 13,469 | 95.7 min | **always** | — |
| Fiery Twin | Anvil | 1,981 | 14,019 | 73.2 min | **always** | — |
| Fiery Twin | Ember | 2,531 | 14,569 | 60.0 min | **always** | — |
| Fiery Twin | Slag | 3,081 | 15,119 | 50.0 min | 92%  (45.9 min) | 4.1 min |
| Fiery Twin | Shard | 3,631 | 15,669 | 44.3 min | 81%  (35.8 min) | 8.5 min |
| Fiery Twin | Pebble | 4,181 | 16,219 | 40.0 min | 74%  (29.7 min) | 10.3 min |
| Fiery Twin | Bruise | 4,731 | 16,769 | 36.1 min | 69%  (25.0 min) | 11.1 min |
| Fiery Twin | Humble Abode | 6,228 | 18,266 | 6.0 min | 58%  (3.5 min) | 2.5 min |
| Fiery Twin | Cyclops | 18,887 | 30,924 | 20.0 min | **never** | forever |
| Icey Twin | Puddle | 881 | 12,919 | 160.0 min | **always** | — |
| Icey Twin | Hearth | 1,431 | 13,469 | 95.7 min | **always** | — |
| Icey Twin | Anvil | 1,981 | 14,019 | 73.2 min | **always** | — |
| Icey Twin | Ember | 2,531 | 14,569 | 60.0 min | **always** | — |
| Icey Twin | Slag | 3,081 | 15,119 | 50.0 min | 92%  (45.9 min) | 4.1 min |
| Icey Twin | Shard | 3,631 | 15,669 | 44.3 min | 81%  (35.8 min) | 8.5 min |
| Icey Twin | Pebble | 4,181 | 16,219 | 40.0 min | 74%  (29.7 min) | 10.3 min |
| Icey Twin | Bruise | 4,731 | 16,769 | 36.1 min | 69%  (25.0 min) | 11.1 min |
| Icey Twin | Humble Abode | 6,228 | 18,266 | 6.0 min | 58%  (3.5 min) | 2.5 min |
| Icey Twin | Cyclops | 18,887 | 30,924 | 20.0 min | **never** | forever |
| Puddle | Hearth | 550 | 14,350 | 238.2 min | **always** | — |
| Puddle | Anvil | 1,100 | 14,900 | 134.8 min | **always** | — |
| Puddle | Ember | 1,650 | 15,450 | 96.0 min | 85%  (81.1 min) | 14.9 min |
| Puddle | Slag | 2,200 | 16,000 | 72.7 min | 77%  (56.1 min) | 16.6 min |
| Puddle | Shard | 2,750 | 16,550 | 61.2 min | 72%  (44.0 min) | 17.3 min |
| Puddle | Pebble | 3,300 | 17,100 | 53.3 min | 67%  (36.0 min) | 17.4 min |
| Puddle | Bruise | 3,850 | 17,650 | 46.6 min | 64%  (29.7 min) | 16.9 min |
| Puddle | Humble Abode | 5,347 | 19,147 | 6.2 min | 55%  (3.4 min) | 2.8 min |
| Puddle | Cyclops | 18,006 | 31,806 | 22.9 min | **never** | forever |
| Hearth | Anvil | 550 | 15,450 | 310.4 min | 85%  (262.6 min) | 47.8 min |
| Hearth | Ember | 1,100 | 16,000 | 160.8 min | 77%  (124.3 min) | 36.5 min |
| Hearth | Slag | 1,650 | 16,550 | 104.7 min | 72%  (75.5 min) | 29.2 min |
| Hearth | Shard | 2,200 | 17,100 | 82.4 min | 68%  (55.9 min) | 26.5 min |
| Hearth | Pebble | 2,750 | 17,650 | 68.7 min | 64%  (44.1 min) | 24.6 min |
| Hearth | Bruise | 3,300 | 18,200 | 57.9 min | 61%  (35.3 min) | 22.6 min |
| Hearth | Humble Abode | 4,797 | 19,697 | 6.4 min | 53%  (3.4 min) | 3.0 min |
| Hearth | Cyclops | 17,456 | 32,356 | 25.3 min | **never** | forever |
| Anvil | Ember | 550 | 16,550 | 333.6 min | 72%  (240.9 min) | 92.7 min |
| Anvil | Slag | 1,100 | 17,100 | 158.0 min | 68%  (107.5 min) | 50.5 min |
| Anvil | Shard | 1,650 | 17,650 | 112.2 min | 64%  (72.4 min) | 39.8 min |
| Anvil | Pebble | 2,200 | 18,200 | 88.3 min | 61%  (54.1 min) | 34.1 min |
| Anvil | Bruise | 2,750 | 18,750 | 71.2 min | 59%  (41.7 min) | 29.5 min |
| Anvil | Humble Abode | 4,247 | 20,247 | 6.5 min | 52%  (3.4 min) | 3.2 min |
| Anvil | Cyclops | 16,906 | 32,906 | 27.5 min | **never** | forever |
| Ember | Slag | 550 | 17,650 | 300.0 min | 65%  (193.9 min) | 106.1 min |
| Ember | Shard | 1,100 | 18,200 | 169.1 min | 62%  (104.1 min) | 64.9 min |
| Ember | Pebble | 1,650 | 18,750 | 120.0 min | 59%  (70.6 min) | 49.4 min |
| Ember | Bruise | 2,200 | 19,300 | 90.5 min | 56%  (51.0 min) | 39.5 min |
| Ember | Humble Abode | 3,697 | 20,797 | 6.7 min | 50%  (3.4 min) | 3.3 min |
| Ember | Cyclops | 16,356 | 33,456 | 30.0 min | **never** | forever |
| Slag | Shard | 550 | 18,750 | 387.5 min | 59%  (228.7 min) | 158.8 min |
| Slag | Pebble | 1,100 | 19,300 | 200.0 min | 57%  (113.2 min) | 86.8 min |
| Slag | Bruise | 1,650 | 19,850 | 129.7 min | 54%  (70.5 min) | 59.2 min |
| Slag | Humble Abode | 3,147 | 21,347 | 6.8 min | 49%  (3.3 min) | 3.5 min |
| Slag | Cyclops | 15,806 | 34,006 | 33.3 min | **never** | forever |
| Shard | Pebble | 550 | 19,850 | 413.3 min | 55%  (225.3 min) | 188.0 min |
| Shard | Bruise | 1,100 | 20,400 | 194.9 min | 53%  (102.3 min) | 92.6 min |
| Shard | Humble Abode | 2,597 | 21,897 | 6.9 min | 48%  (3.3 min) | 3.6 min |
| Shard | Cyclops | 15,256 | 34,556 | 36.5 min | **never** | forever |
| Pebble | Bruise | 550 | 20,950 | 368.9 min | 51%  (187.3 min) | 181.6 min |
| Pebble | Humble Abode | 2,047 | 22,447 | 7.1 min | 46%  (3.3 min) | 3.8 min |
| Pebble | Cyclops | 14,706 | 35,106 | 40.0 min | 6%  (2.4 min) | 37.6 min |
| Bruise | Humble Abode | 1,497 | 22,997 | 7.2 min | 45%  (3.2 min) | 4.0 min |
| Bruise | Cyclops | 14,156 | 35,656 | 44.9 min | 10%  (4.3 min) | 40.5 min |
| Humble Abode | Cyclops | 12,659 | 37,153 | 8.6 min | 15%  (76 s) | 7.3 min |

## Where you can get to, from each planet

At the design range of 15 km. **always** = the hop is open whenever you want it;
a percentage = you have to wait for the window.

**Fiery Twin**
- always: Icey Twin, Puddle, Hearth, Anvil, Ember
- sometimes: Slag (92%), Shard (81%), Pebble (74%), Bruise (69%), Humble Abode (58%)
- never: Cyclops

**Icey Twin**
- always: Fiery Twin, Puddle, Hearth, Anvil, Ember
- sometimes: Slag (92%), Shard (81%), Pebble (74%), Bruise (69%), Humble Abode (58%)
- never: Cyclops

**Puddle**
- always: Fiery Twin, Icey Twin, Hearth, Anvil
- sometimes: Ember (85%), Slag (77%), Shard (72%), Pebble (67%), Bruise (64%), Humble Abode (55%)
- never: Cyclops

**Hearth**
- always: Fiery Twin, Icey Twin, Puddle
- sometimes: Anvil (85%), Ember (77%), Slag (72%), Shard (68%), Pebble (64%), Bruise (61%), Humble Abode (53%)
- never: Cyclops

**Anvil**
- always: Fiery Twin, Icey Twin, Puddle
- sometimes: Hearth (85%), Ember (72%), Slag (68%), Shard (64%), Pebble (61%), Bruise (59%), Humble Abode (52%)
- never: Cyclops

**Ember**
- always: Fiery Twin, Icey Twin
- sometimes: Puddle (85%), Hearth (77%), Anvil (72%), Slag (65%), Shard (62%), Pebble (59%), Bruise (56%), Humble Abode (50%)
- never: Cyclops

**Slag**
- always: —
- sometimes: Fiery Twin (92%), Icey Twin (92%), Puddle (77%), Hearth (72%), Anvil (68%), Ember (65%), Shard (59%), Pebble (57%), Bruise (54%), Humble Abode (49%)
- never: Cyclops

**Shard**
- always: —
- sometimes: Fiery Twin (81%), Icey Twin (81%), Puddle (72%), Hearth (68%), Anvil (64%), Ember (62%), Slag (59%), Pebble (55%), Bruise (53%), Humble Abode (48%)
- never: Cyclops

**Pebble**
- always: —
- sometimes: Fiery Twin (74%), Icey Twin (74%), Puddle (67%), Hearth (64%), Anvil (61%), Ember (59%), Slag (57%), Shard (55%), Bruise (51%), Humble Abode (46%), Cyclops (6%)
- never: —

**Bruise**
- always: —
- sometimes: Fiery Twin (69%), Icey Twin (69%), Puddle (64%), Hearth (61%), Anvil (59%), Ember (56%), Slag (54%), Shard (53%), Pebble (51%), Humble Abode (45%), Cyclops (10%)
- never: —

**Humble Abode**
- always: —
- sometimes: Fiery Twin (58%), Icey Twin (58%), Puddle (55%), Hearth (53%), Anvil (52%), Ember (50%), Slag (49%), Shard (48%), Pebble (46%), Bruise (45%), Cyclops (15%)
- never: —

**Cyclops**
- always: —
- sometimes: Pebble (6%), Bruise (10%), Humble Abode (15%)
- never: Fiery Twin, Icey Twin, Puddle, Hearth, Anvil, Ember, Slag, Shard

## Gap check

Every planet needs at least one neighbour it can actually reach, or landing there
is a one-way trip. Worst neighbour = the closest planet it can ever get to.

| Planet | Nearest reachable | Its closest approach | Reachable at 15 km? |
|---|---|---:|---|
| Fiery Twin | Puddle | 881 | yes — 10 planet(s) |
| Icey Twin | Puddle | 881 | yes — 10 planet(s) |
| Puddle | Hearth | 550 | yes — 10 planet(s) |
| Hearth | Puddle | 550 | yes — 10 planet(s) |
| Anvil | Ember | 550 | yes — 10 planet(s) |
| Ember | Anvil | 550 | yes — 10 planet(s) |
| Slag | Ember | 550 | yes — 10 planet(s) |
| Shard | Slag | 550 | yes — 10 planet(s) |
| Pebble | Shard | 550 | yes — 11 planet(s) |
| Bruise | Pebble | 550 | yes — 11 planet(s) |
| Humble Abode | Bruise | 1,497 | yes — 11 planet(s) |
| Cyclops | Humble Abode | 12,659 | yes — 3 planet(s) |

