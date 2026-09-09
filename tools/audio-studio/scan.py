#!/usr/bin/env python3
"""Rebuild the audio manifest from the project.

    py -3 tools/audio-studio/scan.py            rescan and merge
    py -3 tools/audio-studio/scan.py --report   rescan, print findings, write nothing

Read-only with respect to the game: the only file it writes is
Assets/StreamingAssets/Audio/sounds.json (plus a cached raw scan beside this
script). It never touches a scene, a prefab, or a .cs file.
"""
import io
import json
import os
import sys

from audioscan import cs_parser, manifest, meta_index, yaml_refs

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
ASSETS = os.path.join(ROOT, "Assets")
SCENE_NAME = "1.6.7.7.7.unity"
SCENE = os.path.join(ASSETS, SCENE_NAME)
MANIFEST = os.path.join(ASSETS, "StreamingAssets", "Audio", "sounds.json")
RAW = os.path.join(HERE, "sounds.scan.json")


def read(path):
    with io.open(path, encoding="utf-8", errors="replace") as f:
        return f.read()


def rel(path):
    return os.path.relpath(path, ASSETS).replace(os.sep, "/")


def collect_scripts(index):
    """{script guid -> (relative path, class name, {field name -> ClipField})}

    Only SERIALIZED fields are kept. A plain private `AudioClip _cachedHum` is
    a runtime cache the code fills itself -- it is not a slot anyone can point
    at a different file, so it must not become a row in the mixer.
    """
    out = {}
    for guid, path in index.script_guids().items():
        full = os.path.join(ASSETS, path)
        if not os.path.isfile(full):
            continue
        text = read(full)
        if "AudioClip" not in text:            # cheap reject before the regexes
            continue
        fields = [f for f in cs_parser.find_clip_fields(text) if f.serialized]
        if not fields:
            continue
        out[guid] = (path, cs_parser.class_name(text), {f.name: f for f in fields})
    return out


def collect_resource_sounds(index):
    """Sounds loaded by `Resources.Load<AudioClip>("path")`.

    These have no serialized slot at all -- the path is baked into the code --
    but they are real sounds the player hears (the dome hum, the dome/airlock
    whoosh), so they belong in the mixer. Part 2 gives them manifest keys and
    the hardcoded path goes away.
    """
    rows = {}
    audio = index.audio_guids()
    by_path = {p: g for g, p in audio.items()}
    for guid, path in index.script_guids().items():
        full = os.path.join(ASSETS, path)
        if not os.path.isfile(full):
            continue
        text = read(full)
        if "Resources.Load<AudioClip>" not in text:
            continue
        if not manifest.in_scope(path):
            continue
        klass = cs_parser.class_name(text)
        for res in cs_parser.find_resource_loads(text):
            clip = ""
            for ext in (".mp3", ".wav"):
                candidate = "Resources/%s%s" % (res, ext)
                if candidate in by_path:
                    clip = candidate
                    break
            key = "%s.res.%s" % (
                manifest.snake(klass),
                res.lower().replace("/", "_").replace("-", "_"),
            )
            # One row per resource path even if several scripts load it.
            if key in rows:
                continue
            row = manifest.blank(
                key,
                "%s -> %s" % (klass, res),
                manifest.group_for(path),
                clip,
            )
            row["script"] = path
            row["source"] = "Resources.Load"
            rows[key] = row
    return list(rows.values())


def collect_prefabs(index):
    """One walk: prefab component maps, and every prefab's own assignments."""
    maps = {}
    assignments = []
    for dirpath, dirnames, filenames in os.walk(ASSETS):
        dirnames[:] = [d for d in dirnames if d not in meta_index.SKIP_DIRS]
        for name in filenames:
            if not name.endswith(".prefab"):
                continue
            full = os.path.join(dirpath, name)
            text = read(full)
            source = rel(full)
            guid = index.guid_for(source)
            if guid:
                maps[guid] = yaml_refs.component_script_map(text)
            assignments += yaml_refs.scan_components(text, source)
    return maps, assignments


def build():
    index = meta_index.build_index(ASSETS)
    scripts = collect_scripts(index)
    prefab_maps, assignments = collect_prefabs(index)

    scene_text = read(SCENE)
    assignments += yaml_refs.scan_components(scene_text, SCENE_NAME)
    assignments += yaml_refs.scan_overrides(scene_text, SCENE_NAME, prefab_maps)

    # Precedence, strongest first:
    #   1. a scene override with a real clip   (what the running game uses)
    #   2. a prefab/component with a real clip
    #   3. an empty slot                       (so the gap is still visible)
    def rank(a):
        has_clip = a.clip_guid is not None
        from_scene = a.source.endswith(".unity")
        return (2 if has_clip and from_scene else 1 if has_clip else 0)

    best = {}
    for a in assignments:
        entry = scripts.get(a.script_guid)
        if not entry:
            continue
        path, klass, fields = entry
        # A propertyPath with no matching field is a stale override left behind
        # when the field was renamed -- e.g. `footstepWalkClip` on
        # PlayerController, which has only ...ClipA and ...ClipB.
        if a.field not in fields or not manifest.in_scope(path):
            continue
        key = manifest.make_key(klass, a.field)
        if key not in best or rank(a) > rank(best[key][0]):
            best[key] = (a, path, klass, fields[a.field])

    draft = []
    for key, (a, path, klass, field) in best.items():
        clip = index.path_for(a.clip_guid) if a.clip_guid else ""
        row = manifest.blank(
            key,
            "%s.%s" % (klass, field.name),
            manifest.group_for(path),
            clip or "",
            dead=field.dead,
        )
        row["script"] = path
        row["source"] = a.source
        draft.append(row)

    draft += collect_resource_sounds(index)
    return draft, scripts


def main():
    report_only = "--report" in sys.argv
    draft, scripts = build()
    empty = [d for d in draft if not d["clip"]]
    dead = [d for d in draft if d["dead"]]
    from_scene = [d for d in draft if d["source"].endswith(".unity")]
    from_res = [d for d in draft if d["source"] == "Resources.Load"]

    print("scripts with serialized clip slots : %d" % len(scripts))
    print("sounds found (in scope)            : %d" % len(draft))
    print("  assigned in the scene            : %d" % len(from_scene))
    print("  loaded by Resources.Load         : %d" % len(from_res))
    print("  with NO clip assigned            : %d" % len(empty))
    print("  dead (code never reads)          : %d" % len(dead))

    groups = {}
    for d in draft:
        groups[d["group"]] = groups.get(d["group"], 0) + 1
    print("\nby group:")
    for g in sorted(groups):
        print("  %-10s %d" % (g, groups[g]))

    if report_only:
        print("\n--report: nothing written.")
        return 0

    with io.open(RAW, "w", encoding="utf-8", newline="\n") as f:
        json.dump(draft, f, ensure_ascii=False, indent=2)
    merged = manifest.merge(draft, manifest.read_manifest(MANIFEST))
    manifest.write_manifest(MANIFEST, merged)
    print("\nwrote %s (%d sounds)" % (rel(MANIFEST), len(merged["sounds"])))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
