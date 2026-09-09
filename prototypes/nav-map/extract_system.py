"""
Reads Assets/1.6.7.7.7.unity and writes system.json - the real solar system the
NAV map draws.

Everything here is TRUE state pulled out of the scene: orbit radius and angle
(from the body's authored position), day/orbit period (railPeriod), body radius,
moons (satelliteOrbitRadius / satellitePeriod). Nothing is invented except the
per-planet swatch colour and blurb in LOOK below, which exist only so the map
has something to draw.

Re-run after moving any planet:      py -3 extract_system.py

The maths the map does with this data is the same closed form as
Assets/3 - Scripts/World/OrbitRange.cs - circular rails in one plane, so a
distance is the cosine rule and a wait is one divide.
"""
import re, json, io, math, os

HERE  = os.path.dirname(os.path.abspath(__file__))
SCENE = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "1.6.7.7.7.unity"))
OUT   = os.path.join(HERE, "system.json")

# Only Humble Abode runs retrograde (docs/DISTANCE_TABLE.md, generated from the
# live scene - direction comes from angular momentum at runtime, which is not
# serialised into the scene, so it is restated here).
RETROGRADE = {"Humble Abode"}

# Water + fish market per docs/DISTANCE_TABLE.md.
NO_WATER = {"Pebble", "Bruise"}

# Swatch + blurb. The only authored-here values; cosmetic, tune freely.
LOOK = {
    "Sun":                ("#ffcf6b", "The furnace. Everything else is falling around it."),
    "Fiery Twin":         ("#e0673a", "Moat recipe. Scorched, cracked, never far from its sister."),
    "Icey Twin":          ("#a8dcea", "Shattered ice. Locked forever alongside Fiery Twin."),
    "Puddle":             ("#3f93ad", "Drowned archipelago - nearly all of it is sea."),
    "Hearth":             ("#71ac5e", "A small Humble Abode. Green, wet, quiet."),
    "Anvil":              ("#a68d6c", "Mostly land, tall ranges. The dry one."),
    "Ember":              ("#c74b3c", "Alien recipe under a red sky."),
    "Slag":               ("#9d6039", "Fiery Twin's rock rolled again. Ash and moat basins."),
    "Shard":              ("#93a9da", "Icey Twin's shatter pattern at a fraction of the size."),
    "Pebble":             ("#8d8d8d", "Airless. Fourteen hundred craters and nothing else."),
    "Bruise":             ("#7d6c89", "Airless, a handful of enormous impacts."),
    "Humble Abode":       ("#5fae82", "Home. Retrograde - it meets everything more often."),
    "Cyclops":            ("#6aa197", "The big alien world out at the edge, with two moons."),
    "Constant Companion": ("#9aa4a8", "Humble Abode's moon."),
    "Watchful Eye":       ("#9aa4a8", "Cyclops inner moon."),
    "Tumbling Bean":      ("#9aa4a8", "Cyclops outer moon."),
}


def parse():
    """Pull every CelestialBody out of the scene YAML with a world position."""
    txt = io.open(SCENE, encoding="utf-8", errors="replace").read()
    transforms, gameobjects, raw = {}, {}, []
    for b in txt.split("--- !u!"):
        m = re.match(r"(\d+) &(\d+)", b.split("\n", 1)[0])
        if not m:
            continue
        cls, fid = m.group(1), m.group(2)
        if cls == "4":                       # Transform
            go = re.search(r"m_GameObject: \{fileID: (\d+)\}", b)
            lp = re.search(r"m_LocalPosition: \{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE+]+)\}", b)
            fa = re.search(r"m_Father: \{fileID: (\d+)\}", b)
            if go and lp:
                transforms[go.group(1)] = (fid, [float(lp.group(i)) for i in (1, 2, 3)],
                                           fa.group(1) if fa else "0")
        elif cls == "1":                     # GameObject
            nm = re.search(r"m_Name: (.*)", b)
            if nm:
                gameobjects[fid] = nm.group(1).strip()
        elif cls == "114" and "bodyName:" in b and "railPeriod:" in b:   # CelestialBody
            go = re.search(r"m_GameObject: \{fileID: (\d+)\}", b)

            def f(k, d="0"):
                mm = re.search(r"\n  %s: (.*)" % k, b)
                return mm.group(1).strip() if mm else d

            raw.append(dict(go=go.group(1) if go else None,
                            name=f("bodyName", "?"),
                            bodyType=int(f("bodyType")),
                            radius=float(f("radius")),
                            gravity=float(f("surfaceGravity")),
                            period=float(f("railPeriod")),
                            group=f("orbitGroup", ""),
                            coOrbitAngle=float(f("coOrbitAngle")),
                            moonRadius=float(f("satelliteOrbitRadius")),
                            moonPeriod=float(f("satellitePeriod"))))

    by_tf = {v[0]: (k, v[1], v[2]) for k, v in transforms.items()}

    def world(goid):
        if goid not in transforms:
            return None
        _, pos, father = transforms[goid]
        pos, guard = list(pos), 0
        while father != "0" and father in by_tf and guard < 24:
            _, ppos, father = by_tf[father]
            pos = [pos[i] + ppos[i] for i in range(3)]
            guard += 1
        return pos

    for b in raw:
        b["pos"] = world(b["go"]) if b["go"] else None
    return raw


def build():
    raw = parse()
    sun = next((b for b in raw if b["name"] == "Sun"), None)
    if sun is None:
        raise SystemExit("No Sun in the scene - is the scene path right?")
    sx, sy = sun["pos"][0], sun["pos"][1]

    planets, loose_moons = [], []
    for b in raw:
        if b["pos"] is None or b["name"] == "Sun":
            continue
        x, y = b["pos"][0] - sx, b["pos"][1] - sy
        colour, blurb = LOOK.get(b["name"], ("#8d9aa0", ""))

        if b["bodyType"] == 1:               # moon - orbits its planet, not the sun
            loose_moons.append(dict(name=b["name"], bodyRadius=b["radius"],
                                    orbitRadius=b["moonRadius"], period=b["moonPeriod"],
                                    colour=colour, sunDist=math.hypot(x, y)))
            continue
        if b["bodyType"] != 0:               # star, black hole - not destinations
            continue

        # A co-orbital follower (Icey Twin) has no rail of its own: it rides the
        # leader's period at a fixed angular offset, so borrow the leader's.
        period = b["period"]
        if period <= 0 and b["group"]:
            lead = next((o for o in raw if o["group"] == b["group"] and o["period"] > 0), None)
            if lead:
                period = lead["period"]
        if period <= 0:
            continue                          # not on a rail

        planets.append(dict(
            name=b["name"],
            orbitRadius=round(math.hypot(x, y), 1),
            angleDeg=round(math.degrees(math.atan2(y, x)) % 360.0, 3),
            periodSec=period,
            retrograde=b["name"] in RETROGRADE,
            bodyRadius=b["radius"],
            gravity=b["gravity"],
            water=b["name"] not in NO_WATER,
            market=b["name"] not in NO_WATER,
            colour=colour,
            blurb=blurb,
            group=b["group"] or None,
            moons=[]))

    planets.sort(key=lambda p: p["orbitRadius"])

    # Hang each moon off the planet whose orbit it sits nearest to.
    for m in loose_moons:
        d = m.pop("sunDist")
        host = min(planets, key=lambda p: abs(p["orbitRadius"] - d))
        host["moons"].append(m)

    data = dict(
        generated="from Assets/1.6.7.7.7.unity by extract_system.py",
        sunRadius=sun["radius"],
        planets=planets,
        # ShuttleFuel on Shuttle_Lander.prefab - keep in step with the component.
        fuel=dict(fuelMax=100.0, maxJumpKm=15.0, launchLandCost=5.0,
                  jumpCostExponent=1.6, newGameFuel=50.0),
        countdownSeconds=10.0)               # ShuttleAutopilot.CountdownSeconds

    with io.open(OUT, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1)
    print("wrote %s - %d planets, %d moons"
          % (OUT, len(planets), sum(len(p["moons"]) for p in planets)))


if __name__ == "__main__":
    build()
