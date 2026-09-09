"""The only module that knows what a "sound" is: scope, keys, and merging.

Merge rule, and the reason this module exists: a rescan must never overwrite a
decision made in the browser. The scanner owns `dead`, `scanned_clip` and
existence; the human owns `label`, `group`, `volume`, `bus` and `note`.

`clip` is shared, and needs care. Two things must both hold:

  1. If the user swapped a clip in the browser, a rescan must NOT put the old
     one back -- the Editor still has the old clip wired until Part 2 rewires
     it, so the scan would undo every swap on every rescan.
  2. If the user has NOT touched a clip and someone changes the assignment in
     Unity, the rescan SHOULD pick that up.

Telling those apart needs the scanner's own last answer, which is why every
entry carries `scanned_clip`. The user is deemed to have overridden the clip
when `clip != scanned_clip`; then and only then does their value win.
"""
import io
import json
import os
import re

SCHEMA_VERSION = 1

# Human-owned fields: a rescan copies these forward untouched.
HUMAN_FIELDS = ("label", "group", "volume", "bus", "note")

# Folders whose audio belongs to a scene other than 1.6.7.7.7, per the spec.
OUT_OF_SCOPE = (
    "3 - Scripts/Dimensions/",
    "3 - Scripts/Poolrooms/",
    "3 - Scripts/Cutscenes/",
    "5 - External Imports/",
)

# Longest prefix wins, so a single-file rule beats the folder it sits in.
GROUPS = (
    ("3 - Scripts/Scripts/Game/Controllers/PlayerController.cs", "Movement"),
    ("3 - Scripts/Scripts/Game/Controllers/Ship.cs", "Ship"),
    ("3 - Scripts/Audio/PlayerSuitAudio.cs", "Movement"),
    ("3 - Scripts/Story/TevSmugglingMission.cs", "Voice"),
    ("3 - Scripts/Ship/", "Ship"),
    ("3 - Scripts/Shuttle/", "Ship"),
    ("3 - Scripts/Combat/", "Combat"),
    ("3 - Scripts/Survival/", "Survival"),
    ("3 - Scripts/World/", "World"),
    ("3 - Scripts/Fishing/", "Fishing"),
    ("3 - Scripts/Pickups/", "Items"),
    ("3 - Scripts/NPC_Dialogue/", "Voice"),
    ("3 - Scripts/Story/", "Story"),
    ("3 - Scripts/Vendor/", "Vendor"),
    ("3 - Scripts/UI/", "UI"),
    ("3 - Scripts/Progression/", "UI"),
    ("3 - Scripts/AI/", "UI"),
    ("3 - Scripts/Audio/", "Ambience"),
    ("3 - Scripts/Tutorial/", "Tutorial"),
    ("3 - Scripts/Concert/", "Music"),
    ("3 - Scripts/Music/", "Music"),
)

_CAMEL_1 = re.compile(r"(.)([A-Z][a-z]+)")
_CAMEL_2 = re.compile(r"([a-z0-9])([A-Z])")


def in_scope(script_path):
    p = script_path.replace("\\", "/")
    return not any(p.startswith(prefix) for prefix in OUT_OF_SCOPE)


def group_for(script_path):
    p = script_path.replace("\\", "/")
    best, best_len = "Other", -1
    for prefix, group in GROUPS:
        if (p == prefix or p.startswith(prefix)) and len(prefix) > best_len:
            best, best_len = group, len(prefix)
    return best


def snake(name):
    name = name.lstrip("_")
    return _CAMEL_2.sub(r"\1_\2", _CAMEL_1.sub(r"\1_\2", name)).lower()


def make_key(class_name, field_name):
    """Stable forever. Never includes the group, which the user can rename."""
    return "%s.%s" % (snake(class_name), snake(field_name))


def blank(key, label, group, clip, dead=False):
    return {
        "key": key,
        "label": label,
        "group": group,
        "clip": clip,
        "scanned_clip": clip,
        "volume": 1.0,
        "bus": "SFX",
        "note": "",
        "dead": dead,
        "missing": False,
    }


def merge(draft, existing):
    """Fold a fresh scan into the curated manifest without losing edits."""
    old = {s["key"]: s for s in (existing or {}).get("sounds", [])}
    out = []
    seen = set()
    for entry in draft:
        key = entry["key"]
        seen.add(key)
        merged = dict(entry)
        merged["missing"] = False
        prev = old.get(key)
        if prev:
            for field in HUMAN_FIELDS:
                if field in prev:
                    merged[field] = prev[field]
            # The clip is the user's only when they actually changed it away
            # from what the scanner last reported. Otherwise the scan wins, so
            # a reassignment made in Unity still propagates.
            if prev.get("clip") != prev.get("scanned_clip"):
                merged["clip"] = prev["clip"]
        out.append(merged)
    for key, prev in old.items():
        if key in seen:
            continue
        gone = dict(prev)
        gone["missing"] = True
        out.append(gone)
    out.sort(key=lambda s: (s.get("group", ""), s.get("label", ""), s["key"]))
    return {"version": SCHEMA_VERSION, "sounds": out}


def read_manifest(path):
    if not os.path.isfile(path):
        return {"version": SCHEMA_VERSION, "sounds": []}
    with io.open(path, encoding="utf-8") as f:
        return json.load(f)


def write_manifest(path, data):
    parent = os.path.dirname(path)
    if parent:
        os.makedirs(parent, exist_ok=True)
    with io.open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
        f.write("\n")
