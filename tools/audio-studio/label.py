#!/usr/bin/env python3
"""Give every sound a name Sam can read, instead of the name the code uses.

    py -3 tools/audio-studio/label.py            apply labels
    py -3 tools/audio-studio/label.py --report   show what would change

Two layers. OVERRIDES is hand-written for the sounds that matter most, where
the field name does not say what the player actually hears. Everything else
falls through to a humaniser that splits the camelCase, drops the noise words
("clip", "sfx", "audio") and prefixes the system it belongs to.

Re-runnable and safe: it only rewrites `label`, and only when the current
label still looks auto-generated (`Class.fieldName`). A label you have edited
by hand in the browser is left alone.
"""
import io
import os
import re
import sys

from audioscan import manifest

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
MANIFEST = os.path.join(ROOT, "Assets", "StreamingAssets", "Audio", "sounds.json")

# Hand-written where the code name misleads or reads badly.
OVERRIDES = {
    # --- Movement ---------------------------------------------------------
    "player_controller.footstep_walk_clip_a": "Walking - step A",
    "player_controller.footstep_walk_clip_b": "Walking - step B",
    "player_controller.jump_clip": "Jump (dead - suit breath is used)",
    "player_controller.land_clip": "Landing on the ground",
    "player_controller.water_move_clip": "Moving through water",
    "player_controller.up_boost_clip": "Jetpack - boost up",
    "player_controller.down_boost_clip": "Jetpack - boost down",
    "player_controller.dir_boost_clip": "Jetpack - sideways boost",
    "player_controller.item_pickup_clip": "Picking an item up",
    "player_controller.eat_drink_clip": "Eating or drinking",
    "player_controller.death_clip": "Player dies",
    "player_suit_audio.jump_effort_clip": "Jump - effort grunt",
    "player_suit_audio.wind_loop_clip": "Suit wind (loop)",
    "player_suit_audio.equip_clip": "Equipping an item",
    "player_suit_audio.unequip_clip": "Putting an item away",
    "player_suit_audio.acquire_clip": "Acquiring something new",
    "player_suit_audio.eat_loop_clip": "Eating (loop)",
    # --- Survival ---------------------------------------------------------
    "oxygen_manager.alarm_clip": "Oxygen low - alarm",
    "oxygen_manager.beep_clip": "Oxygen - warning beep",
    "vitals_hud.warning_clip": "Vitals - warning",
    "fall_damage.pain_light": "Fall damage - light",
    "fall_damage.pain_medium": "Fall damage - medium",
    "fall_damage.pain_hard": "Fall damage - hard",
    "fall_damage.impact_light": "Fall impact - light",
    "fall_damage.impact_medium": "Fall impact - medium",
    "fall_damage.impact_hard": "Fall impact - hard",
    "fall_damage.death_impact": "Fatal fall",
    # --- Ship -------------------------------------------------------------
    "ship.engine_loop_clip": "Ship engine (loop)",
    "ship.stop_engine_clip": "Ship engine shutting down",
    "ship.thrust_loop_clip": "Ship thrusters (loop)",
    "ship.hatch_clip": "Ship hatch",
    "ship_wind_audio.wind_loop_clip": "Ship wind (loop)",
    "ship_reactor.hum_clip": "Reactor hum (loop)",
    # --- Combat -----------------------------------------------------------
    "enemy_controller.attack_clip": "Enemy attacks",
    "enemy_controller.death_clip": "Enemy dies",
    "enemy_controller.hit_clip": "Enemy takes a hit",
    "enemy_controller.shriek_clip": "Enemy shrieks",
    "enemy_controller.howl_clip": "Enemy howls",
    "enemy_controller.sniff_clip": "Enemy sniffing for you",
    "enemy_controller.spawn_loop_clip": "Enemy spawning (loop)",
    "pistol_controller.shoot_clip": "Pistol - shot",
    "pistol_controller.reload_clip": "Pistol - reload",
    "axe_controller.swing_clip": "Axe - swing",
    "axe_controller.hit_clip": "Axe - hit",
}

# Words that add nothing once the label is in English.
NOISE = ("clip", "sfx", "audio", "sound", "source")

# How a class name should read when it prefixes a label.
SYSTEM_NAMES = {
    "PlayerController": "Player",
    "PlayerSuitAudio": "Suit",
    "TevSmugglingMission": "Radio",
    "ShipInstructorDialogue": "Ship instructor",
    "AtmosphericWind": "Wind",
    "UiSfxPlayer": "UI",
    "MainMenuController": "Main menu",
    "BubbleDome": "Dome",
    "DomeAudio": "Dome",
    "ShuttleDoorField": "Shuttle door",
}

AUTO_LABEL_RE = re.compile(r"^[A-Z]\w*\.[_a-zA-Z]\w*$")
AUTO_RES_RE = re.compile(r"^[A-Z]\w* -> \S+$")

# Split camelCase WITHOUT exploding acronyms: "AlienNPCDamageable" must become
# "Alien NPC Damageable", not "Alien N P C Damageable".
_CAMEL = re.compile(r"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")

# Class-name suffixes that say nothing about the sound.
CLASS_SUFFIXES = ("Controller", "Manager", "Audio", "Handler")


def split_words(name):
    return [w for w in _CAMEL.sub(" ", name.lstrip("_")).split() if w]


def humanise_field(field):
    """`_alarmClip` -> `Alarm`, `footstepWalkClipA` -> `Footstep walk A`."""
    words = split_words(field)
    kept = [w for w in words if w.lower() not in NOISE] or words or [field]
    # Keep acronyms and single letters as-is; lowercase ordinary words.
    out = []
    for i, w in enumerate(kept):
        if w.isupper():
            out.append(w)
        elif i == 0:
            out.append(w[0].upper() + w[1:])
        else:
            out.append(w.lower())
    return " ".join(out)


def humanise_class(klass):
    """`EnemyController` -> `Enemy`, `AlienNPCDamageable` -> `Alien NPC damageable`."""
    words = split_words(klass)
    while len(words) > 1 and words[-1] in CLASS_SUFFIXES:
        words.pop()
    out = []
    for i, w in enumerate(words):
        out.append(w if w.isupper() else (w if i == 0 else w.lower()))
    return " ".join(out)


def humanise(label):
    """Turn `Class.fieldName` or `Class -> res/path` into something readable."""
    if " -> " in label:
        klass, res = label.split(" -> ", 1)
        system = SYSTEM_NAMES.get(klass, humanise_field(klass))
        tail = res.split("/")[-1].replace("_", " ")
        return "%s - %s" % (system, tail)
    if "." not in label:
        return label
    klass, field = label.split(".", 1)
    system = SYSTEM_NAMES.get(klass, humanise_field(klass))
    body = humanise_field(field)
    if body.lower().startswith(system.lower()):
        return body
    return "%s - %s" % (system, body[0].lower() + body[1:])


def is_auto(label):
    return bool(AUTO_LABEL_RE.match(label or "") or AUTO_RES_RE.match(label or ""))


def main():
    report = "--report" in sys.argv
    data = manifest.read_manifest(MANIFEST)
    changed = 0
    still_auto = 0
    for s in data["sounds"]:
        if s["key"] in OVERRIDES:
            new = OVERRIDES[s["key"]]
        elif is_auto(s.get("label")):
            new = humanise(s["label"])
        else:
            continue                      # hand-edited in the browser: leave it
        if new != s["label"]:
            if report:
                print("  %-46s -> %s" % (s["label"], new))
            s["label"] = new
            changed += 1
    for s in data["sounds"]:
        if is_auto(s.get("label")):
            still_auto += 1
    print("\nlabels rewritten : %d" % changed)
    print("still auto-looking: %d" % still_auto)
    if report:
        print("--report: nothing written.")
        return 0
    manifest.write_manifest(MANIFEST, data)
    print("wrote %s" % os.path.relpath(MANIFEST, ROOT))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
