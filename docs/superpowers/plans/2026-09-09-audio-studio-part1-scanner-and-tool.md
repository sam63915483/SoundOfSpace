🟢 ACTIVE — written 2026-09-09. Part 1 of 2. Spec: `docs/superpowers/specs/2026-09-09-audio-studio-design.md`

# Audio Studio, Part 1 — Scanner + Browser Tool

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give Sam a browser tool at `localhost:8766` that lists every action in scene `1.6.7.7.7` that makes a sound, plays it at an adjustable level, and lets him change its volume, category and clip file — writing all of it to a JSON manifest.

**Architecture:** A read-only Python scanner reads the C# sources, the `.meta` GUID index, and the Unity scene/prefab YAML to discover every sound and which clip is actually wired to it. It emits a draft manifest, which is merged into a human-curated `sounds.json` without ever clobbering Sam's edits. A stdlib HTTP server serves a single-page UI over that manifest and streams the real audio files to the browser.

**Tech Stack:** Python 3 standard library only (no pip — matches Dialogue Studio). Vanilla HTML/CSS/JS, no framework, no CDN. `unittest` for tests.

**Scope boundary:** This plan ships a working tool and stops. It changes **nothing** the game runs — no C#, no assets, no scene, no prefabs. Part 2 (`GameAudio`, the clip move into `Resources`, and the migration waves) is written *after* Sam has mixed, because the manifest contents determine that work.

---

## Why this plan is safe to run start-to-finish

Every file created here is new and lives under `tools/audio-studio/`. It touches nothing the parallel Claude session owns, needs no Unity Editor, needs no scene save, and cannot make the game behave differently. Per the cross-session protocol in the spec (§7), **no announce-and-ack is required for any task in this plan.** Part 2 is where that changes.

---

## Ground truth established before writing this plan

These were verified against the live project on 2026-09-09. The scanner must reproduce them; they double as acceptance fixtures.

| Fact | Value | Consequence for the code |
|---|---|---|
| Audio file types in `Assets/` (excl. third-party packs) | 268 `.mp3`, 96 `.wav` | Both play natively in every browser. **No transcoding needed.** |
| `AudioSource`s in the 72 MB scene | **1** (and **0** `AudioListener`) | Audio is a code concern; the scene is not the place to look for sources. |
| `AudioSource`s created in code | 100 `AddComponent<AudioSource>` | The scanner's primary unit is the *serialized clip field*, not the source. |
| Serialized `AudioClip` fields | 164 | The inventory's backbone. |
| `PlayerController` in the scene | **absent** — it lives on `Assets/5 - External Imports/Prefabs/NetworkPlayer.prefab` | Prefab scanning **must not** skip `5 - External Imports/`. |
| `footstepWalkClipA/B` **on that prefab** | `{fileID: 0}` — empty | Reading prefabs alone reports the player as silent. **This is the trap.** |
| `footstepWalkClipA/B` **in the scene** | overridden via `m_Modifications` → `walk.mp3`, `walkk.mp3` | **Scene prefab-instance overrides are where the real answers live.** Task 5 exists for this. |
| Resolved footstep clips | `Assets/transfer/audio/walk.mp3`, `.../walkk.mp3` | GUID index must cover `Assets/transfer/`, not just `Assets/Audio/`. |
| Stale override present | `propertyPath: footstepWalkClip` (no such field on the class) | Scanner must report unmatched property paths rather than crash. |
| `jumpClip` | serialized but deliberately never read (see `PlayerController.cs:1124`) | Manifest needs a `dead: true` state so Sam isn't asked to mix a sound that cannot play. |
| Python | `py -3` → 3.14.4. **`python` is NOT on PATH.** | Every command in this plan uses `py -3`. |

---

## File structure

```
tools/audio-studio/
  Audio Studio.bat            double-click launcher (mirrors Dialogue Studio's)
  README.md                   the manual Sam reads
  serve.py                    stdlib HTTP server + JSON API, port 8766
  scan.py                     CLI: rebuild the draft, merge into sounds.json
  audioscan/
    __init__.py
    meta_index.py             GUID <-> asset path, from .meta files
    cs_parser.py              parse .cs for clip fields / Resources.Load / dead fields
    yaml_refs.py              parse Unity YAML: MonoBehaviour blocks + m_Modifications
    manifest.py               scope rules, key generation, build + non-destructive merge
  tests/
    __init__.py
    fixtures/                 tiny hand-written .cs / .prefab / .unity / .meta samples
    test_meta_index.py
    test_cs_parser.py
    test_yaml_refs.py
    test_manifest.py
  index.html  app.js  styles.css
  backups/                    timestamped manifest backups, 30 kept
  incoming/                   drop folder for replacement sounds
```

**Responsibilities, one per module.** `meta_index` knows only about GUIDs. `cs_parser` knows only about C# text. `yaml_refs` knows only about Unity YAML. `manifest` is the only module that knows what a "sound" is. `scan.py` and `serve.py` are thin wiring over those four. No module imports Unity, opens a socket, or writes to `Assets/` except through `manifest`.

**Output lives at** `Assets/StreamingAssets/Audio/sounds.json` (the game reads it in Part 2) with the raw scan cached alongside at `tools/audio-studio/sounds.scan.json` (never hand-edited, safe to delete).

---

## Task 1: Scaffold the package and prove the test runner works

**Files:**
- Create: `tools/audio-studio/audioscan/__init__.py`
- Create: `tools/audio-studio/tests/__init__.py`
- Create: `tools/audio-studio/tests/test_smoke.py`

- [ ] **Step 1: Write the failing test**

Create `tools/audio-studio/tests/test_smoke.py`:

```python
import unittest

from audioscan import VERSION


class SmokeTest(unittest.TestCase):
    def test_package_imports_and_has_a_version(self):
        self.assertEqual(VERSION, 1)


if __name__ == "__main__":
    unittest.main()
```

- [ ] **Step 2: Run it and watch it fail**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1/tools/audio-studio" && py -3 -m unittest discover -s tests -t . -v
```

Expected: `ModuleNotFoundError: No module named 'audioscan'`.

- [ ] **Step 3: Create the package**

Create `tools/audio-studio/audioscan/__init__.py`:

```python
"""Read-only scanners that turn a Unity project into an audio manifest.

No module in this package imports Unity, opens a network socket, or writes
anywhere under Assets/ except through `manifest.write_manifest`.
"""

VERSION = 1
```

Create an empty `tools/audio-studio/tests/__init__.py` (zero bytes).

- [ ] **Step 4: Run it and watch it pass**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1/tools/audio-studio" && py -3 -m unittest discover -s tests -t . -v
```

Expected: `Ran 1 test ... OK`.

- [ ] **Step 5: Commit**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1"
git add "tools/audio-studio/audioscan/__init__.py" "tools/audio-studio/tests/__init__.py" "tools/audio-studio/tests/test_smoke.py"
git commit -m "feat(audio-studio): scaffold the scanner package and test runner"
```

---

## Task 2: `meta_index` — resolve a Unity GUID to a file path

Every reference in Unity YAML is a GUID. The only way back to a filename is the sidecar `.meta` files. This module builds that map once and answers lookups.

**Files:**
- Create: `tools/audio-studio/audioscan/meta_index.py`
- Create: `tools/audio-studio/tests/test_meta_index.py`
- Create: `tools/audio-studio/tests/fixtures/meta/` (fixture tree)

- [ ] **Step 1: Write the fixtures**

Create `tools/audio-studio/tests/fixtures/meta/Audio/walk.mp3.meta`:

```
fileFormatVersion: 2
guid: 6c9d80c6033e14f469c4646a523c6bbb
AudioImporter:
  externalObjects: {}
```

Create `tools/audio-studio/tests/fixtures/meta/Scripts/PlayerController.cs.meta`:

```
fileFormatVersion: 2
guid: 0aa13226b151d40ae952589d37a6dd9e
MonoImporter:
  externalObjects: {}
```

Create the two files they describe so the index can confirm they exist —
`tools/audio-studio/tests/fixtures/meta/Audio/walk.mp3` containing the single
line `not really audio`, and
`tools/audio-studio/tests/fixtures/meta/Scripts/PlayerController.cs` containing
the single line `// not really C#`.

- [ ] **Step 2: Write the failing test**

Create `tools/audio-studio/tests/test_meta_index.py`:

```python
import os
import unittest

from audioscan import meta_index

HERE = os.path.dirname(os.path.abspath(__file__))
FIXTURE = os.path.join(HERE, "fixtures", "meta")


class MetaIndexTest(unittest.TestCase):
    def setUp(self):
        self.index = meta_index.build_index(FIXTURE)

    def test_resolves_an_audio_guid_to_its_path(self):
        self.assertEqual(
            self.index.path_for("6c9d80c6033e14f469c4646a523c6bbb"),
            "Audio/walk.mp3",
        )

    def test_resolves_a_script_guid_to_its_path(self):
        self.assertEqual(
            self.index.path_for("0aa13226b151d40ae952589d37a6dd9e"),
            "Scripts/PlayerController.cs",
        )

    def test_unknown_guid_returns_none_rather_than_raising(self):
        self.assertIsNone(self.index.path_for("deadbeef" * 4))

    def test_guid_lookup_is_reversible(self):
        self.assertEqual(
            self.index.guid_for("Audio/walk.mp3"),
            "6c9d80c6033e14f469c4646a523c6bbb",
        )

    def test_script_guids_maps_only_cs_files(self):
        self.assertEqual(
            self.index.script_guids(),
            {"0aa13226b151d40ae952589d37a6dd9e": "Scripts/PlayerController.cs"},
        )

    def test_audio_guids_maps_only_audio_files(self):
        self.assertEqual(
            self.index.audio_guids(),
            {"6c9d80c6033e14f469c4646a523c6bbb": "Audio/walk.mp3"},
        )

    def test_meta_without_its_asset_is_ignored(self):
        orphan = os.path.join(FIXTURE, "Audio", "ghost.mp3.meta")
        with open(orphan, "w", encoding="utf-8") as f:
            f.write("fileFormatVersion: 2\nguid: " + ("ab" * 16) + "\n")
        try:
            index = meta_index.build_index(FIXTURE)
            self.assertIsNone(index.path_for("ab" * 16))
        finally:
            os.remove(orphan)


if __name__ == "__main__":
    unittest.main()
```

- [ ] **Step 3: Run it and watch it fail**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1/tools/audio-studio" && py -3 -m unittest tests.test_meta_index -v
```

Expected: `ModuleNotFoundError: No module named 'audioscan.meta_index'`.

- [ ] **Step 4: Implement**

Create `tools/audio-studio/audioscan/meta_index.py`:

```python
"""Unity GUID <-> asset path, built from the .meta files beside every asset.

Unity YAML never names a file; it cites a 32-hex GUID that lives in the
asset's sidecar `.meta`. This module is the only place that knows that.
"""
import os
import re

GUID_RE = re.compile(r"^guid:\s*([0-9a-f]{32})\s*$", re.MULTILINE)

AUDIO_EXTS = (".mp3", ".wav")
SCRIPT_EXTS = (".cs",)

# Directories that never contain anything we care about. Note that
# "5 - External Imports" is deliberately NOT skipped: NetworkPlayer.prefab,
# which carries PlayerController, lives there.
SKIP_DIRS = {"Library", "Temp", "obj", "Logs", "UserSettings", ".git"}


class MetaIndex:
    """Immutable two-way map between GUIDs and project-relative asset paths."""

    def __init__(self, by_guid):
        self._by_guid = by_guid
        self._by_path = {p: g for g, p in by_guid.items()}

    def path_for(self, guid):
        return self._by_guid.get(guid)

    def guid_for(self, path):
        return self._by_path.get(path.replace("\\", "/"))

    def _filtered(self, exts):
        return {
            g: p for g, p in self._by_guid.items() if p.lower().endswith(exts)
        }

    def script_guids(self):
        return self._filtered(SCRIPT_EXTS)

    def audio_guids(self):
        return self._filtered(AUDIO_EXTS)

    def __len__(self):
        return len(self._by_guid)


def read_guid(meta_path):
    """The GUID declared by a .meta file, or None if it has none."""
    try:
        with open(meta_path, encoding="utf-8", errors="replace") as f:
            head = f.read(4096)
    except OSError:
        return None
    m = GUID_RE.search(head)
    return m.group(1) if m else None


def build_index(assets_root):
    """Walk `assets_root` and map every GUID to its asset's relative path.

    A .meta whose asset is missing is skipped: Unity leaves those behind and
    they would otherwise resolve to files that do not exist.
    """
    by_guid = {}
    for dirpath, dirnames, filenames in os.walk(assets_root):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for name in filenames:
            if not name.endswith(".meta"):
                continue
            meta = os.path.join(dirpath, name)
            asset = meta[: -len(".meta")]
            if not os.path.exists(asset):
                continue
            guid = read_guid(meta)
            if not guid:
                continue
            rel = os.path.relpath(asset, assets_root).replace("\\", "/")
            by_guid[guid] = rel
    return MetaIndex(by_guid)
```

- [ ] **Step 5: Run it and watch it pass**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1/tools/audio-studio" && py -3 -m unittest tests.test_meta_index -v
```

Expected: `Ran 6 tests ... OK`.

- [ ] **Step 6: Sanity-check it against the real project**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1/tools/audio-studio" && py -3 -c "from audioscan import meta_index; i = meta_index.build_index(r'C:/2.0 SOUND OF SPACE/1aughhh1/Assets'); print('guids:', len(i)); print('walk.mp3 ->', i.path_for('6c9d80c6033e14f469c4646a523c6bbb')); print('PlayerController ->', i.path_for('0aa13226b151d40ae952589d37a6dd9e'))"
```

Expected, exactly:
```
walk.mp3 -> transfer/audio/walk.mp3
PlayerController -> 3 - Scripts/Scripts/Game/Controllers/PlayerController.cs
```
If either prints `None`, the walk is skipping a directory it should not — check `SKIP_DIRS`.

- [ ] **Step 7: Commit**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1"
git add "tools/audio-studio/audioscan/meta_index.py" "tools/audio-studio/tests/test_meta_index.py" "tools/audio-studio/tests/fixtures/meta"
git commit -m "feat(audio-studio): resolve Unity GUIDs to asset paths"
```

---

## Task 3: `cs_parser` — find the sound slots in C#

**Files:**
- Create: `tools/audio-studio/audioscan/cs_parser.py`
- Create: `tools/audio-studio/tests/test_cs_parser.py`

- [ ] **Step 1: Write the failing test**

Create `tools/audio-studio/tests/test_cs_parser.py`:

```python
import unittest

from audioscan import cs_parser

SAMPLE = """
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [SerializeField] AudioClip footstepWalkClipA;
    [SerializeField] AudioClip footstepWalkClipB;
    public AudioClip landClip;
    // The serialized `jumpClip` is deliberately NO LONGER read: the scene had it
    [SerializeField] AudioClip jumpClip;
    [SerializeField] float footstepVolume = 0.35f;
    AudioSource sfxSource;

    void Start()
    {
        sfxSource = gameObject.AddComponent<AudioSource>();
        var hum = Resources.Load<AudioClip>("DomeFX/dome_hum");
        sfxSource.PlayOneShot(landClip, landVolume);
    }
}
"""


class ClipFieldTest(unittest.TestCase):
    def setUp(self):
        self.fields = {f.name: f for f in cs_parser.find_clip_fields(SAMPLE)}

    def test_finds_every_audioclip_field(self):
        self.assertEqual(
            sorted(self.fields),
            ["footstepWalkClipA", "footstepWalkClipB", "jumpClip", "landClip"],
        )

    def test_does_not_mistake_a_float_for_a_clip(self):
        self.assertNotIn("footstepVolume", self.fields)

    def test_marks_a_field_dead_when_a_comment_says_it_is_not_read(self):
        self.assertTrue(self.fields["jumpClip"].dead)

    def test_a_normal_field_is_not_dead(self):
        self.assertFalse(self.fields["landClip"].dead)


class OtherSignalsTest(unittest.TestCase):
    def test_finds_resources_load_paths(self):
        self.assertEqual(
            cs_parser.find_resource_loads(SAMPLE), ["DomeFX/dome_hum"]
        )

    def test_finds_the_class_name(self):
        self.assertEqual(cs_parser.class_name(SAMPLE), "PlayerController")

    def test_counts_sources_created_in_code(self):
        self.assertEqual(cs_parser.count_code_sources(SAMPLE), 1)

    def test_a_file_with_no_audio_yields_nothing(self):
        self.assertEqual(cs_parser.find_clip_fields("class Empty {}"), [])


if __name__ == "__main__":
    unittest.main()
```

- [ ] **Step 2: Run it and watch it fail**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1/tools/audio-studio" && py -3 -m unittest tests.test_cs_parser -v
```

Expected: `ModuleNotFoundError: No module named 'audioscan.cs_parser'`.

- [ ] **Step 3: Implement**

Create `tools/audio-studio/audioscan/cs_parser.py`:

```python
"""Pull the audio-shaped facts out of a C# source file.

Regex, not a parser: the only constructs that matter are field declarations
and a handful of call shapes, and this has to run over ~600 files quickly.
"""
import re

CLIP_FIELD_RE = re.compile(
    r"^\s*(?:\[[^\]]*\]\s*)*"          # attributes, e.g. [SerializeField]
    r"(?:public|private|protected|internal)?\s*"
    r"(?:static\s+)?(?:readonly\s+)?"
    r"AudioClip\s+([A-Za-z_]\w*)\s*[;=]",
    re.MULTILINE,
)

RESOURCE_LOAD_RE = re.compile(r"Resources\.Load<AudioClip>\(\s*\"([^\"]+)\"")
CLASS_RE = re.compile(r"\bclass\s+([A-Za-z_]\w*)")
ADD_SOURCE_RE = re.compile(r"AddComponent<AudioSource>")

# A field the code no longer reads. Previous sessions leave a note rather than
# deleting the field, because deleting a serialized field mid-class corrupts
# existing scene/prefab serialization (CLAUDE.md).
DEAD_HINTS = ("no longer read", "is dead", "deliberately not read", "never read")


class ClipField:
    """One `AudioClip` field declared in a script."""

    __slots__ = ("name", "dead")

    def __init__(self, name, dead=False):
        self.name = name
        self.dead = dead

    def __repr__(self):
        return "ClipField(%r, dead=%r)" % (self.name, self.dead)

    def __eq__(self, other):
        return (
            isinstance(other, ClipField)
            and (self.name, self.dead) == (other.name, other.dead)
        )


def _is_dead(text, field_name):
    """True when a comment within 6 lines above the field says it is not read."""
    lines = text.splitlines()
    for i, line in enumerate(lines):
        if not re.search(r"AudioClip\s+%s\s*[;=]" % re.escape(field_name), line):
            continue
        window = " ".join(lines[max(0, i - 6):i]).lower()
        return any(h in window for h in DEAD_HINTS)
    return False


def find_clip_fields(text):
    return [
        ClipField(name, dead=_is_dead(text, name))
        for name in CLIP_FIELD_RE.findall(text)
    ]


def find_resource_loads(text):
    return RESOURCE_LOAD_RE.findall(text)


def class_name(text):
    m = CLASS_RE.search(text)
    return m.group(1) if m else ""


def count_code_sources(text):
    return len(ADD_SOURCE_RE.findall(text))
```

- [ ] **Step 4: Run it and watch it pass**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1/tools/audio-studio" && py -3 -m unittest tests.test_cs_parser -v
```

Expected: `Ran 8 tests ... OK`.

- [ ] **Step 5: Sanity-check against the real `PlayerController.cs`**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1/tools/audio-studio" && py -3 -c "import io; from audioscan import cs_parser; t=io.open(r'C:/2.0 SOUND OF SPACE/1aughhh1/Assets/3 - Scripts/Scripts/Game/Controllers/PlayerController.cs',encoding='utf-8',errors='replace').read(); fs=cs_parser.find_clip_fields(t); print('class:', cs_parser.class_name(t)); print('fields:', len(fs)); [print(' ', f.name, '(DEAD)' if f.dead else '') for f in fs]"
```

Expected: `class: PlayerController`, 14 fields, and **`jumpClip` flagged `(DEAD)`** — that is the field whose comment says it is no longer read. If `jumpClip` is not flagged, widen the `_is_dead` window or add the comment's wording to `DEAD_HINTS`.

- [ ] **Step 6: Commit**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1"
git add "tools/audio-studio/audioscan/cs_parser.py" "tools/audio-studio/tests/test_cs_parser.py"
git commit -m "feat(audio-studio): find clip fields, dead fields and Resources loads in C#"
```

---

## Task 4: `yaml_refs` — read clip assignments out of plain component blocks

Unity YAML is a stream of documents. A `MonoBehaviour` document names its script
via `m_Script` and lists its serialized fields at two-space indent. This task
handles that shape; Task 5 handles the harder prefab-override shape.

**Files:**
- Create: `tools/audio-studio/audioscan/yaml_refs.py`
- Create: `tools/audio-studio/tests/test_yaml_refs.py`

- [ ] **Step 1: Write the failing test**

Create `tools/audio-studio/tests/test_yaml_refs.py`:

```python
import unittest

from audioscan import yaml_refs

PREFAB = """%YAML 1.1
--- !u!1 &6759884
GameObject:
  m_Name: Player
--- !u!114 &6759885
MonoBehaviour:
  m_GameObject: {fileID: 6759884}
  m_Script: {fileID: 11500000, guid: 0aa13226b151d40ae952589d37a6dd9e, type: 3}
  m_Name:
  footstepWalkClipA: {fileID: 0}
  footstepWalkClipB: {fileID: 0}
  landClip: {fileID: 8300000, guid: 6c9d80c6033e14f469c4646a523c6bbb, type: 3}
  footstepVolume: 0.35
--- !u!114 &6759886
MonoBehaviour:
  m_Script: {fileID: 11500000, guid: ffffffffffffffffffffffffffffffff, type: 3}
  someClip: {fileID: 8300000, guid: 0d5b074c61d987a47a93936925947666, type: 3}
"""


class DocumentSplitTest(unittest.TestCase):
    def test_splits_on_unity_document_markers(self):
        docs = yaml_refs.split_documents(PREFAB)
        self.assertEqual([d.file_id for d in docs], [6759884, 6759885, 6759886])

    def test_captures_the_script_guid_of_a_monobehaviour(self):
        docs = {d.file_id: d for d in yaml_refs.split_documents(PREFAB)}
        self.assertEqual(
            docs[6759885].script_guid, "0aa13226b151d40ae952589d37a6dd9e"
        )

    def test_a_gameobject_has_no_script_guid(self):
        docs = {d.file_id: d for d in yaml_refs.split_documents(PREFAB)}
        self.assertIsNone(docs[6759884].script_guid)


class AssignmentTest(unittest.TestCase):
    def setUp(self):
        self.found = yaml_refs.scan_components(PREFAB, "NetworkPlayer.prefab")
        self.by_field = {a.field: a for a in self.found}

    def test_reads_an_assigned_clip(self):
        a = self.by_field["landClip"]
        self.assertEqual(a.clip_guid, "6c9d80c6033e14f469c4646a523c6bbb")
        self.assertEqual(a.script_guid, "0aa13226b151d40ae952589d37a6dd9e")

    def test_an_empty_slot_is_reported_with_no_clip(self):
        self.assertIsNone(self.by_field["footstepWalkClipA"].clip_guid)

    def test_empty_slots_are_still_reported_so_gaps_are_visible(self):
        self.assertIn("footstepWalkClipB", self.by_field)

    def test_ignores_non_reference_fields(self):
        self.assertNotIn("footstepVolume", self.by_field)

    def test_ignores_unity_internal_fields(self):
        self.assertNotIn("m_Script", self.by_field)
        self.assertNotIn("m_GameObject", self.by_field)

    def test_records_where_it_was_found(self):
        self.assertEqual(self.by_field["landClip"].source, "NetworkPlayer.prefab")


if __name__ == "__main__":
    unittest.main()
```

- [ ] **Step 2: Run it and watch it fail**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1/tools/audio-studio" && py -3 -m unittest tests.test_yaml_refs -v
```

Expected: `ModuleNotFoundError: No module named 'audioscan.yaml_refs'`.

- [ ] **Step 3: Implement**

Create `tools/audio-studio/audioscan/yaml_refs.py`:

```python
"""Read object references out of Unity's YAML, without a YAML library.

Unity's dialect is not standard YAML (the `--- !u!114 &123` document headers
are its own), PyYAML is not available (stdlib only), and the target scene is
72 MB, so a real parse would be both wrong and slow. Line scanning it is.
"""
import re

DOC_RE = re.compile(r"^--- !u!(\d+) &(\d+)")
SCRIPT_RE = re.compile(r"^\s*m_Script:\s*\{fileID:\s*\d+,\s*guid:\s*([0-9a-f]{32})")
# A serialized object reference: either empty ({fileID: 0}) or a real GUID.
REF_RE = re.compile(
    r"^  ([A-Za-z_]\w*):\s*\{fileID:\s*(\d+)(?:,\s*guid:\s*([0-9a-f]{32}))?"
)

INTERNAL_PREFIXES = ("m_",)


class Document:
    """One `--- !u!<class> &<fileID>` block of a Unity YAML file."""

    __slots__ = ("class_id", "file_id", "lines")

    def __init__(self, class_id, file_id):
        self.class_id = class_id
        self.file_id = file_id
        self.lines = []

    @property
    def script_guid(self):
        for line in self.lines:
            m = SCRIPT_RE.match(line)
            if m:
                return m.group(1)
        return None

    @property
    def text(self):
        return "\n".join(self.lines)


class Assignment:
    """One serialized field on one component, and the clip wired into it."""

    __slots__ = ("script_guid", "field", "clip_guid", "source", "file_id")

    def __init__(self, script_guid, field, clip_guid, source, file_id=0):
        self.script_guid = script_guid
        self.field = field
        self.clip_guid = clip_guid
        self.source = source
        self.file_id = file_id

    def __repr__(self):
        return "Assignment(%s.%s -> %s @ %s)" % (
            self.script_guid[:8] if self.script_guid else "?",
            self.field,
            (self.clip_guid or "EMPTY")[:8],
            self.source,
        )


def split_documents(text):
    """Yield every Unity YAML document in `text`, in order."""
    docs = []
    current = None
    for line in text.splitlines():
        m = DOC_RE.match(line)
        if m:
            current = Document(int(m.group(1)), int(m.group(2)))
            docs.append(current)
            continue
        if current is not None:
            current.lines.append(line)
    return docs


def scan_components(text, source):
    """Clip assignments from plain MonoBehaviour blocks.

    Empty slots are returned too (clip_guid None) — a slot nobody filled is
    exactly the kind of gap the tool exists to make visible.
    """
    out = []
    for doc in split_documents(text):
        guid = doc.script_guid
        if not guid:
            continue
        for line in doc.lines:
            m = REF_RE.match(line)
            if not m:
                continue
            field, file_id, clip_guid = m.group(1), m.group(2), m.group(3)
            if field.startswith(INTERNAL_PREFIXES):
                continue
            if file_id == "0" and clip_guid is None:
                out.append(Assignment(guid, field, None, source, doc.file_id))
            elif clip_guid:
                out.append(Assignment(guid, field, clip_guid, source, doc.file_id))
    return out
```

- [ ] **Step 4: Run it and watch it pass**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1/tools/audio-studio" && py -3 -m unittest tests.test_yaml_refs -v
```

Expected: `Ran 9 tests ... OK`.

- [ ] **Step 5: Commit**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1"
git add "tools/audio-studio/audioscan/yaml_refs.py" "tools/audio-studio/tests/test_yaml_refs.py"
git commit -m "feat(audio-studio): read clip assignments from Unity component YAML"
```

---

## Task 5: `yaml_refs` — read clip assignments out of scene prefab overrides

**This is the task that makes the tool correct rather than misleading.** On the
prefab, `footstepWalkClipA` is `{fileID: 0}`. The real clip is assigned in the
*scene*, inside a `PrefabInstance`'s `m_Modifications` list, which cites the
target component by `fileID` **within the prefab file** — so resolving it needs
a second lookup back into that prefab.

**Files:**
- Modify: `tools/audio-studio/audioscan/yaml_refs.py`
- Modify: `tools/audio-studio/tests/test_yaml_refs.py`

- [ ] **Step 1: Write the failing test**

Append to `tools/audio-studio/tests/test_yaml_refs.py`:

```python
SCENE = """%YAML 1.1
--- !u!1001 &111222333
PrefabInstance:
  m_Modification:
    m_Modifications:
    - target: {fileID: 6759885, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 3}
      propertyPath: footstepWalkClipA
      value:
      objectReference: {fileID: 8300000, guid: 6c9d80c6033e14f469c4646a523c6bbb, type: 3}
    - target: {fileID: 6759885, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 3}
      propertyPath: footstepWalkClipB
      value:
      objectReference: {fileID: 8300000, guid: 0d5b074c61d987a47a93936925947666, type: 3}
    - target: {fileID: 6759885, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 3}
      propertyPath: m_Name
      value: Player
      objectReference: {fileID: 0}
    m_SourcePrefab: {fileID: 100100000, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 3}
"""

# fileID 6759885 inside the prefab is the PlayerController MonoBehaviour.
PREFAB_COMPONENT_SCRIPTS = {
    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa": {6759885: "0aa13226b151d40ae952589d37a6dd9e"}
}


class OverrideTest(unittest.TestCase):
    def setUp(self):
        self.found = yaml_refs.scan_overrides(
            SCENE, "1.6.7.7.7.unity", PREFAB_COMPONENT_SCRIPTS
        )
        self.by_field = {a.field: a for a in self.found}

    def test_finds_the_clip_the_scene_overrides_in(self):
        a = self.by_field["footstepWalkClipA"]
        self.assertEqual(a.clip_guid, "6c9d80c6033e14f469c4646a523c6bbb")

    def test_attributes_the_override_to_the_right_script(self):
        self.assertEqual(
            self.by_field["footstepWalkClipA"].script_guid,
            "0aa13226b151d40ae952589d37a6dd9e",
        )

    def test_finds_every_overridden_clip(self):
        self.assertEqual(
            sorted(self.by_field), ["footstepWalkClipA", "footstepWalkClipB"]
        )

    def test_ignores_overrides_that_are_not_object_references(self):
        self.assertNotIn("m_Name", self.by_field)

    def test_records_the_scene_as_the_source(self):
        self.assertEqual(self.by_field["footstepWalkClipB"].source, "1.6.7.7.7.unity")

    def test_unknown_prefab_is_skipped_rather_than_crashing(self):
        self.assertEqual(yaml_refs.scan_overrides(SCENE, "s.unity", {}), [])


class ComponentScriptMapTest(unittest.TestCase):
    def test_maps_component_file_ids_to_their_script_guids(self):
        self.assertEqual(
            yaml_refs.component_script_map(PREFAB),
            {6759885: "0aa13226b151d40ae952589d37a6dd9e",
             6759886: "ffffffffffffffffffffffffffffffff"},
        )
```

- [ ] **Step 2: Run it and watch it fail**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1/tools/audio-studio" && py -3 -m unittest tests.test_yaml_refs -v
```

Expected: `AttributeError: module 'audioscan.yaml_refs' has no attribute 'scan_overrides'`.

- [ ] **Step 3: Implement**

Append to `tools/audio-studio/audioscan/yaml_refs.py`:

```python
TARGET_RE = re.compile(
    r"^\s*-\s*target:\s*\{fileID:\s*(\d+),\s*guid:\s*([0-9a-f]{32})"
)
PROPERTY_RE = re.compile(r"^\s*propertyPath:\s*(.+?)\s*$")
OBJREF_RE = re.compile(
    r"^\s*objectReference:\s*\{fileID:\s*(\d+)(?:,\s*guid:\s*([0-9a-f]{32}))?"
)


def component_script_map(text):
    """{component fileID -> its script GUID} for one prefab file.

    Needed because a scene override cites the component by its fileID *inside
    the prefab*, which on its own says nothing about which script it is.
    """
    out = {}
    for doc in split_documents(text):
        guid = doc.script_guid
        if guid:
            out[doc.file_id] = guid
    return out


def scan_overrides(text, source, prefab_component_scripts):
    """Clip assignments made by scene/prefab-instance overrides.

    `prefab_component_scripts` maps prefab GUID -> {component fileID -> script
    GUID}, built with `component_script_map` over every prefab in the project.

    Only overrides carrying a real objectReference GUID are returned: a
    propertyPath with an empty objectReference is a value override (a name, a
    float), not a clip.
    """
    out = []
    target_file_id = None
    target_prefab = None
    prop = None
    for line in text.splitlines():
        m = TARGET_RE.match(line)
        if m:
            target_file_id = int(m.group(1))
            target_prefab = m.group(2)
            prop = None
            continue
        m = PROPERTY_RE.match(line)
        if m:
            prop = m.group(1)
            continue
        m = OBJREF_RE.match(line)
        if not m or prop is None or target_prefab is None:
            continue
        clip_guid = m.group(2)
        if clip_guid is None:
            prop = None
            continue
        script_guid = (prefab_component_scripts.get(target_prefab) or {}).get(
            target_file_id
        )
        if script_guid:
            out.append(
                Assignment(script_guid, prop, clip_guid, source, target_file_id)
            )
        prop = None
    return out
```

- [ ] **Step 4: Run it and watch it pass**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1/tools/audio-studio" && py -3 -m unittest tests.test_yaml_refs -v
```

Expected: `Ran 16 tests ... OK`.

- [ ] **Step 5: Prove it on the real scene — the acceptance test for this task**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1/tools/audio-studio" && py -3 -c "
import io, os
from audioscan import meta_index, yaml_refs
R = r'C:/2.0 SOUND OF SPACE/1aughhh1/Assets'
idx = meta_index.build_index(R)
maps = {}
for dp, dn, fn in os.walk(R):
    dn[:] = [d for d in dn if d not in meta_index.SKIP_DIRS]
    for n in fn:
        if n.endswith('.prefab'):
            p = os.path.join(dp, n)
            g = idx.guid_for(os.path.relpath(p, R).replace(chr(92), '/'))
            if g:
                maps[g] = yaml_refs.component_script_map(io.open(p, encoding='utf-8', errors='replace').read())
scene = io.open(os.path.join(R, '1.6.7.7.7.unity'), encoding='utf-8', errors='replace').read()
for a in yaml_refs.scan_overrides(scene, '1.6.7.7.7.unity', maps):
    if 'footstep' in a.field.lower():
        print(a.field, '->', idx.path_for(a.clip_guid), '| script:', idx.path_for(a.script_guid))
"
```

Expected, exactly:
```
footstepWalkClipA -> transfer/audio/walk.mp3 | script: 3 - Scripts/Scripts/Game/Controllers/PlayerController.cs
footstepWalkClipB -> transfer/audio/walkk.mp3 | script: 3 - Scripts/Scripts/Game/Controllers/PlayerController.cs
```
(The stale `footstepWalkClip` override has no matching field and is expected to
appear here too; Task 6 is what filters it out.) **If this prints nothing, stop
— the whole tool would under-report, and every later task inherits the error.**

- [ ] **Step 6: Commit**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1"
git add "tools/audio-studio/audioscan/yaml_refs.py" "tools/audio-studio/tests/test_yaml_refs.py"
git commit -m "feat(audio-studio): resolve clip assignments made by scene prefab overrides"
```

---

## Task 6: `manifest` — scope, keys, and a merge that never eats Sam's work

**Files:**
- Create: `tools/audio-studio/audioscan/manifest.py`
- Create: `tools/audio-studio/tests/test_manifest.py`

- [ ] **Step 1: Write the failing test**

Create `tools/audio-studio/tests/test_manifest.py`:

```python
import unittest

from audioscan import manifest


class ScopeTest(unittest.TestCase):
    def test_gameplay_scripts_are_in_scope(self):
        self.assertTrue(manifest.in_scope("3 - Scripts/Survival/OxygenManager.cs"))

    def test_separate_scenes_are_out_of_scope(self):
        for path in (
            "3 - Scripts/Dimensions/OrchardController.cs",
            "3 - Scripts/Poolrooms/DrowningController.cs",
            "3 - Scripts/Cutscenes/DeathCutsceneAudio.cs",
        ):
            self.assertFalse(manifest.in_scope(path), path)

    def test_third_party_packs_are_out_of_scope(self):
        self.assertFalse(manifest.in_scope("5 - External Imports/Foo/Bar.cs"))


class GroupTest(unittest.TestCase):
    def test_player_controller_is_movement(self):
        self.assertEqual(
            manifest.group_for(
                "3 - Scripts/Scripts/Game/Controllers/PlayerController.cs"
            ),
            "Movement",
        )

    def test_ship_is_ship(self):
        self.assertEqual(
            manifest.group_for("3 - Scripts/Ship/ShipReactor.cs"), "Ship"
        )

    def test_smuggling_radio_lines_are_voice(self):
        self.assertEqual(
            manifest.group_for("3 - Scripts/Story/TevSmugglingMission.cs"), "Voice"
        )

    def test_an_unmapped_folder_falls_back_to_other(self):
        self.assertEqual(manifest.group_for("3 - Scripts/Weird/Thing.cs"), "Other")


class KeyTest(unittest.TestCase):
    def test_key_is_snake_cased_class_and_field(self):
        self.assertEqual(
            manifest.make_key("PlayerController", "footstepWalkClipA"),
            "player_controller.footstep_walk_clip_a",
        )

    def test_key_does_not_contain_the_group(self):
        # groups are editable; keys must be stable forever
        self.assertNotIn("movement", manifest.make_key("PlayerController", "x"))


class MergeTest(unittest.TestCase):
    def setUp(self):
        self.draft = [
            {
                "key": "player_controller.footstep_walk_clip_a",
                "label": "PlayerController.footstepWalkClipA",
                "group": "Movement",
                "clip": "transfer/audio/walk.mp3",
                "volume": 1.0,
                "bus": "SFX",
                "note": "",
                "dead": False,
            }
        ]
        self.existing = {
            "version": 1,
            "sounds": [
                {
                    "key": "player_controller.footstep_walk_clip_a",
                    "label": "Walking - step A",
                    "group": "Movement",
                    "clip": "transfer/audio/walk.mp3",
                    "volume": 0.35,
                    "bus": "SFX",
                    "note": "too loud",
                    "dead": False,
                }
            ],
        }

    def test_keeps_the_human_label(self):
        out = manifest.merge(self.draft, self.existing)
        self.assertEqual(out["sounds"][0]["label"], "Walking - step A")

    def test_keeps_the_tuned_volume(self):
        out = manifest.merge(self.draft, self.existing)
        self.assertEqual(out["sounds"][0]["volume"], 0.35)

    def test_keeps_the_note(self):
        out = manifest.merge(self.draft, self.existing)
        self.assertEqual(out["sounds"][0]["note"], "too loud")

    def test_takes_the_new_clip_when_the_scan_finds_a_different_one(self):
        self.draft[0]["clip"] = "Audio/Player/new_walk.wav"
        out = manifest.merge(self.draft, self.existing)
        self.assertEqual(out["sounds"][0]["clip"], "Audio/Player/new_walk.wav")

    def test_adds_a_newly_discovered_sound(self):
        self.draft.append(dict(self.draft[0], key="ship.thruster", label="x"))
        out = manifest.merge(self.draft, self.existing)
        self.assertEqual(len(out["sounds"]), 2)

    def test_marks_a_vanished_sound_missing_instead_of_deleting_it(self):
        out = manifest.merge([], self.existing)
        self.assertEqual(len(out["sounds"]), 1)
        self.assertTrue(out["sounds"][0]["missing"])

    def test_a_returning_sound_is_no_longer_missing(self):
        gone = manifest.merge([], self.existing)
        back = manifest.merge(self.draft, gone)
        self.assertFalse(back["sounds"][0]["missing"])

    def test_output_is_sorted_by_group_then_label(self):
        self.draft.append(
            dict(self.draft[0], key="a.b", group="Ship", label="Aaa")
        )
        out = manifest.merge(self.draft, {"version": 1, "sounds": []})
        self.assertEqual(
            [s["group"] for s in out["sounds"]], ["Movement", "Ship"]
        )


if __name__ == "__main__":
    unittest.main()
```

- [ ] **Step 2: Run it and watch it fail**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1/tools/audio-studio" && py -3 -m unittest tests.test_manifest -v
```

Expected: `ModuleNotFoundError: No module named 'audioscan.manifest'`.

- [ ] **Step 3: Implement**

Create `tools/audio-studio/audioscan/manifest.py`:

```python
"""The only module that knows what a "sound" is: scope, keys, and merging.

Merge rule, and the reason this module exists: a rescan must never overwrite a
decision Sam made in the browser. The scanner owns `clip`, `dead` and
existence; the human owns `label`, `group`, `volume`, `bus` and `note`.
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

GROUPS = (
    ("3 - Scripts/Scripts/Game/Controllers/PlayerController.cs", "Movement"),
    ("3 - Scripts/Audio/PlayerSuitAudio.cs", "Movement"),
    ("3 - Scripts/Story/TevSmugglingMission.cs", "Voice"),
    ("3 - Scripts/Ship/", "Ship"),
    ("3 - Scripts/Shuttle/", "Ship"),
    ("3 - Scripts/Scripts/Game/Controllers/Ship.cs", "Ship"),
    ("3 - Scripts/Combat/", "Combat"),
    ("3 - Scripts/Survival/", "Survival"),
    ("3 - Scripts/World/", "World"),
    ("3 - Scripts/Fishing/", "Fishing"),
    ("3 - Scripts/Pickups/", "Items"),
    ("3 - Scripts/NPC_Dialogue/", "Voice"),
    ("3 - Scripts/Vendor/", "Vendor"),
    ("3 - Scripts/UI/", "UI"),
    ("3 - Scripts/Audio/", "Ambience"),
    ("3 - Scripts/Tutorial/", "Tutorial"),
)

_CAMEL_1 = re.compile(r"(.)([A-Z][a-z]+)")
_CAMEL_2 = re.compile(r"([a-z0-9])([A-Z])")


def in_scope(script_path):
    p = script_path.replace("\\", "/")
    return not any(p.startswith(prefix) for prefix in OUT_OF_SCOPE)


def group_for(script_path):
    p = script_path.replace("\\", "/")
    for prefix, group in GROUPS:
        if p == prefix or p.startswith(prefix):
            return group
    return "Other"


def snake(name):
    return _CAMEL_2.sub(r"\1_\2", _CAMEL_1.sub(r"\1_\2", name)).lower()


def make_key(class_name, field_name):
    """Stable forever. Never includes the group, which Sam can rename."""
    return "%s.%s" % (snake(class_name), snake(field_name))


def blank(key, label, group, clip, dead=False):
    return {
        "key": key,
        "label": label,
        "group": group,
        "clip": clip,
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
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with io.open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
        f.write("\n")
```

- [ ] **Step 4: Run it and watch it pass**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1/tools/audio-studio" && py -3 -m unittest tests.test_manifest -v
```

Expected: `Ran 16 tests ... OK`.

- [ ] **Step 5: Commit**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1"
git add "tools/audio-studio/audioscan/manifest.py" "tools/audio-studio/tests/test_manifest.py"
git commit -m "feat(audio-studio): scope rules, stable keys, and a non-destructive merge"
```

---

## Task 7: `scan.py` — wire it together and produce the real manifest

**Files:**
- Create: `tools/audio-studio/scan.py`

- [ ] **Step 1: Implement the CLI**

Create `tools/audio-studio/scan.py`:

```python
#!/usr/bin/env python3
"""Rebuild the audio manifest from the project.

    py -3 tools/audio-studio/scan.py           rescan and merge
    py -3 tools/audio-studio/scan.py --report  rescan, print findings, write nothing

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
SCENE = os.path.join(ASSETS, "1.6.7.7.7.unity")
MANIFEST = os.path.join(ASSETS, "StreamingAssets", "Audio", "sounds.json")
RAW = os.path.join(HERE, "sounds.scan.json")


def read(path):
    with io.open(path, encoding="utf-8", errors="replace") as f:
        return f.read()


def rel(path):
    return os.path.relpath(path, ASSETS).replace("\\", "/")


def collect_scripts(index):
    """{script guid -> (relative path, {field name -> ClipField})}"""
    out = {}
    for guid, path in index.script_guids().items():
        full = os.path.join(ASSETS, path)
        if not os.path.isfile(full):
            continue
        text = read(full)
        fields = cs_parser.find_clip_fields(text)
        if not fields:
            continue
        out[guid] = (path, cs_parser.class_name(text),
                     {f.name: f for f in fields})
    return out


def collect_prefab_maps(index):
    """{prefab guid -> {component fileID -> script guid}}"""
    maps = {}
    for dirpath, dirnames, filenames in os.walk(ASSETS):
        dirnames[:] = [d for d in dirnames if d not in meta_index.SKIP_DIRS]
        for name in filenames:
            if not name.endswith(".prefab"):
                continue
            full = os.path.join(dirpath, name)
            guid = index.guid_for(rel(full))
            if guid:
                maps[guid] = yaml_refs.component_script_map(read(full))
    return maps, [
        os.path.join(dp, n)
        for dp, dn, fns in os.walk(ASSETS)
        for n in fns
        if n.endswith(".prefab")
    ]


def build():
    index = meta_index.build_index(ASSETS)
    scripts = collect_scripts(index)
    prefab_maps, prefab_files = collect_prefab_maps(index)

    assignments = []
    for path in prefab_files:
        assignments += yaml_refs.scan_components(read(path), rel(path))
    scene_text = read(SCENE)
    assignments += yaml_refs.scan_components(scene_text, "1.6.7.7.7.unity")
    assignments += yaml_refs.scan_overrides(
        scene_text, "1.6.7.7.7.unity", prefab_maps
    )

    # Scene overrides win: they are what the running game actually uses.
    best = {}
    for a in assignments:
        entry = scripts.get(a.script_guid)
        if not entry:
            continue
        path, klass, fields = entry
        if a.field not in fields or not manifest.in_scope(path):
            continue
        key = manifest.make_key(klass, a.field)
        from_scene = a.source.endswith(".unity")
        if key in best and not from_scene:
            continue
        if a.clip_guid is None and key in best:
            continue
        best[key] = (a, path, klass, fields[a.field])

    draft = []
    for key, (a, path, klass, field) in best.items():
        clip = index.path_for(a.clip_guid) if a.clip_guid else ""
        draft.append(
            dict(
                manifest.blank(
                    key,
                    "%s.%s" % (klass, field.name),
                    manifest.group_for(path),
                    clip,
                    dead=field.dead,
                ),
                script=path,
                source=a.source,
            )
        )
    return draft, index, scripts


def main():
    report_only = "--report" in sys.argv
    draft, index, scripts = build()
    empty = [d for d in draft if not d["clip"]]
    dead = [d for d in draft if d["dead"]]

    print("scripts with clip fields : %d" % len(scripts))
    print("sounds found (in scope)  : %d" % len(draft))
    print("  with no clip assigned  : %d" % len(empty))
    print("  dead (code never reads): %d" % len(dead))
    for d in sorted(draft, key=lambda x: (x["group"], x["label"]))[:10]:
        print("   %-10s %-46s %s" % (d["group"], d["label"], d["clip"] or "(empty)"))

    if report_only:
        return 0

    with io.open(RAW, "w", encoding="utf-8", newline="\n") as f:
        json.dump(draft, f, ensure_ascii=False, indent=2)
    merged = manifest.merge(draft, manifest.read_manifest(MANIFEST))
    manifest.write_manifest(MANIFEST, merged)
    print("\nwrote %s (%d sounds)" % (rel(MANIFEST), len(merged["sounds"])))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
```

- [ ] **Step 2: Run the report and read it**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1/tools/audio-studio" && py -3 scan.py --report
```

Expected: a non-zero count of sounds, `Movement` entries present, and the
footstep rows resolving to `transfer/audio/walk.mp3` / `walkk.mp3`.
**If `sounds found` is 0, or Movement is absent, stop and fix Task 5 first.**

- [ ] **Step 3: Confirm the footsteps specifically**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1/tools/audio-studio" && py -3 scan.py --report | grep -i footstep
```

Expected: two rows, clip column showing `transfer/audio/walk.mp3` and `transfer/audio/walkk.mp3`.

- [ ] **Step 4: Write the manifest for real**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1/tools/audio-studio" && py -3 scan.py && py -3 -c "import json,io; d=json.load(io.open(r'C:/2.0 SOUND OF SPACE/1aughhh1/Assets/StreamingAssets/Audio/sounds.json',encoding='utf-8')); print('sounds:', len(d['sounds'])); print('groups:', sorted({s['group'] for s in d['sounds']}))"
```

- [ ] **Step 5: Prove the merge is non-destructive on real data**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1/tools/audio-studio" && py -3 -c "
import io, json
p = r'C:/2.0 SOUND OF SPACE/1aughhh1/Assets/StreamingAssets/Audio/sounds.json'
d = json.load(io.open(p, encoding='utf-8'))
d['sounds'][0]['label'] = 'DO NOT LOSE ME'; d['sounds'][0]['volume'] = 0.123
json.dump(d, io.open(p, 'w', encoding='utf-8', newline='\n'), ensure_ascii=False, indent=2)
print('seeded into', d['sounds'][0]['key'])
" && py -3 scan.py >/dev/null && py -3 -c "
import io, json
d = json.load(io.open(r'C:/2.0 SOUND OF SPACE/1aughhh1/Assets/StreamingAssets/Audio/sounds.json', encoding='utf-8'))
hit = [s for s in d['sounds'] if s['label'] == 'DO NOT LOSE ME']
assert hit and hit[0]['volume'] == 0.123, 'MERGE ATE THE EDIT'
print('merge preserved the edit:', hit[0]['key'])
"
```

Expected: `merge preserved the edit: ...`. **If this asserts, `merge` is wrong and
Sam would lose a mixing session — fix before continuing.**

- [ ] **Step 6: Commit**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1"
git add "tools/audio-studio/scan.py" "Assets/StreamingAssets/Audio/sounds.json"
git commit -m "feat(audio-studio): scan the project into a curated sound manifest"
```

Note: `sounds.scan.json` is a cache — add it to `.gitignore` in Task 10.

---

## Task 8: `serve.py` — the local server

**Files:**
- Create: `tools/audio-studio/serve.py`

- [ ] **Step 1: Implement**

Create `tools/audio-studio/serve.py`:

```python
#!/usr/bin/env python3
"""Audio Studio - local server.

    py -3 tools/audio-studio/serve.py     (or double-click "Audio Studio.bat")
    -> http://localhost:8766

Serves the studio's static files plus a small JSON API over the game's real
manifest, Assets/StreamingAssets/Audio/sounds.json. Every save writes a
timestamped backup first (30 kept). Standard library only - no pip installs.

Port 8766 deliberately: Dialogue Studio owns 8765 and both may run at once.
"""
import http.server
import io
import json
import os
import re
import shutil
import socketserver
import sys
import time
import uuid
import webbrowser
from urllib.parse import urlparse, parse_qs, unquote

from audioscan import manifest as manifest_mod

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
ASSETS = os.path.join(ROOT, "Assets")
MANIFEST = os.path.join(ASSETS, "StreamingAssets", "Audio", "sounds.json")
BACKUP_DIR = os.path.join(HERE, "backups")
INCOMING = os.path.join(HERE, "incoming")
PORT = int(os.environ.get("AUDIO_STUDIO_PORT", "8766"))
KEEP_BACKUPS = 30

AUDIO_TYPES = {".mp3": "audio/mpeg", ".wav": "audio/wav"}

META_TEMPLATE = (
    "fileFormatVersion: 2\n"
    "guid: {guid}\n"
    "AudioImporter:\n"
    "  externalObjects: {{}}\n"
    "  userData: \n"
    "  assetBundleName: \n"
    "  assetBundleVariant: \n"
)


def backup():
    if not os.path.isfile(MANIFEST):
        return None
    os.makedirs(BACKUP_DIR, exist_ok=True)
    stamp = time.strftime("%Y%m%d-%H%M%S")
    dst = os.path.join(BACKUP_DIR, "sounds.%s.json" % stamp)
    shutil.copyfile(MANIFEST, dst)
    olds = sorted(
        p for p in os.listdir(BACKUP_DIR)
        if p.startswith("sounds.") and p.endswith(".json")
    )
    for p in olds[:-KEEP_BACKUPS]:
        try:
            os.remove(os.path.join(BACKUP_DIR, p))
        except OSError:
            pass
    return os.path.relpath(dst, ROOT)


def safe_asset_path(rel):
    """Resolve a manifest `clip` to a real file, refusing anything outside Assets."""
    if not rel:
        return None
    full = os.path.normpath(os.path.join(ASSETS, rel.replace("\\", "/")))
    if not full.startswith(os.path.normpath(ASSETS) + os.sep):
        return None
    return full if os.path.isfile(full) else None


def write_meta_if_new(asset_path):
    meta = asset_path + ".meta"
    if not os.path.isfile(meta):
        with io.open(meta, "w", encoding="utf-8", newline="\n") as f:
            f.write(META_TEMPLATE.format(guid=uuid.uuid4().hex))


class Handler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *a, **kw):
        super().__init__(*a, directory=HERE, **kw)

    def log_message(self, fmt, *args):
        if "/api/" in (args[0] if args else ""):
            sys.stdout.write("%s %s\n" % (time.strftime("%H:%M:%S"), fmt % args))

    def send_json(self, obj, code=200):
        body = json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def end_headers(self):
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def do_GET(self):
        u = urlparse(self.path)
        if u.path == "/api/manifest":
            return self.send_json(manifest_mod.read_manifest(MANIFEST))
        if u.path == "/api/audio":
            rel = (parse_qs(u.query).get("path") or [""])[0]
            full = safe_asset_path(unquote(rel))
            if not full:
                return self.send_json({"error": "not found"}, 404)
            ctype = AUDIO_TYPES.get(os.path.splitext(full)[1].lower())
            if not ctype:
                return self.send_json({"error": "unsupported type"}, 415)
            size = os.path.getsize(full)
            self.send_response(200)
            self.send_header("Content-Type", ctype)
            self.send_header("Content-Length", str(size))
            self.send_header("Accept-Ranges", "none")
            self.end_headers()
            with open(full, "rb") as f:
                shutil.copyfileobj(f, self.wfile)
            return
        if u.path == "/api/library":
            return self.send_json({"files": self.library()})
        if u.path.startswith("/api/"):
            return self.send_json({"error": "unknown api"}, 404)
        if u.path == "/":
            self.path = "/index.html"
        return super().do_GET()

    def library(self):
        """Every playable audio file in the project, for the swap picker."""
        out = []
        for dirpath, dirnames, filenames in os.walk(ASSETS):
            dirnames[:] = [
                d for d in dirnames
                if d not in {"Library", "Temp", "obj", "Logs", ".git"}
            ]
            for name in filenames:
                if os.path.splitext(name)[1].lower() in AUDIO_TYPES:
                    full = os.path.join(dirpath, name)
                    out.append(
                        os.path.relpath(full, ASSETS).replace("\\", "/")
                    )
        return sorted(out)

    def do_PUT(self):
        if urlparse(self.path).path != "/api/manifest":
            return self.send_json({"error": "unknown api"}, 404)
        n = int(self.headers.get("Content-Length") or 0)
        try:
            data = json.loads(self.rfile.read(n).decode("utf-8"))
        except Exception as e:  # noqa: BLE001
            return self.send_json({"error": "not valid JSON: %s" % e}, 400)
        if not isinstance(data, dict) or not isinstance(data.get("sounds"), list):
            return self.send_json({"error": "manifest needs a sounds list"}, 400)
        for s in data["sounds"]:
            if not s.get("key"):
                return self.send_json({"error": "every sound needs a key"}, 400)
            try:
                s["volume"] = max(0.0, min(1.0, float(s.get("volume", 1.0))))
            except (TypeError, ValueError):
                return self.send_json(
                    {"error": "bad volume on %s" % s["key"]}, 400
                )
        bak = backup()
        manifest_mod.write_manifest(MANIFEST, data)
        return self.send_json({"ok": True, "backup": bak})

    def do_POST(self):
        if urlparse(self.path).path != "/api/upload":
            return self.send_json({"error": "unknown api"}, 404)
        name = os.path.basename(unquote(self.headers.get("X-Filename") or ""))
        if os.path.splitext(name)[1].lower() not in AUDIO_TYPES:
            return self.send_json({"error": "only .mp3 and .wav"}, 400)
        group = re.sub(
            r"[^A-Za-z0-9_-]+", "", self.headers.get("X-Group") or "Other"
        ) or "Other"
        n = int(self.headers.get("Content-Length") or 0)
        if n <= 0 or n > 50 * 1024 * 1024:
            return self.send_json({"error": "empty or over 50 MB"}, 400)
        dest_dir = os.path.join(ASSETS, "Audio", "Studio", group)
        os.makedirs(dest_dir, exist_ok=True)
        dest = os.path.join(dest_dir, name)
        stem, ext = os.path.splitext(name)
        i = 2
        while os.path.exists(dest):
            dest = os.path.join(dest_dir, "%s_%d%s" % (stem, i, ext))
            i += 1
        with open(dest, "wb") as f:
            f.write(self.rfile.read(n))
        write_meta_if_new(dest)
        return self.send_json(
            {"ok": True, "clip": os.path.relpath(dest, ASSETS).replace("\\", "/")}
        )


class Server(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True


def main():
    os.makedirs(INCOMING, exist_ok=True)
    os.chdir(HERE)
    with Server(("127.0.0.1", PORT), Handler) as httpd:
        url = "http://localhost:%d" % PORT
        print("Audio Studio  ->  %s" % url)
        print("manifest      ->  %s" % MANIFEST)
        print("Ctrl+C to stop.")
        try:
            webbrowser.open(url)
        except Exception:  # noqa: BLE001
            pass
        try:
            httpd.serve_forever()
        except KeyboardInterrupt:
            print("\nbye")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
```

- [ ] **Step 2: Start it and check the API responds**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1/tools/audio-studio" && py -3 serve.py
```

In a second terminal:

```bash
curl -s http://localhost:8766/api/manifest | py -3 -c "import sys,json; d=json.load(sys.stdin); print('sounds:', len(d['sounds']))"
curl -s -o /dev/null -w "%{http_code} %{content_type}\n" "http://localhost:8766/api/audio?path=transfer/audio/walk.mp3"
curl -s -o /dev/null -w "traversal blocked: %{http_code}\n" "http://localhost:8766/api/audio?path=../../../../Windows/win.ini"
```

Expected: a sound count; `200 audio/mpeg`; and `traversal blocked: 404`.

- [ ] **Step 3: Commit**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1"
git add "tools/audio-studio/serve.py"
git commit -m "feat(audio-studio): local server with manifest, audio streaming and upload"
```

---

## Task 9: The browser UI

**Files:**
- Create: `tools/audio-studio/index.html`
- Create: `tools/audio-studio/styles.css`
- Create: `tools/audio-studio/app.js`

- [ ] **Step 1: Write `index.html`**

```html
<!doctype html>
<meta charset="utf-8">
<title>Audio Studio</title>
<link rel="stylesheet" href="styles.css">
<header>
  <h1>Audio Studio</h1>
  <input id="search" type="search" placeholder="Search sounds...">
  <span id="status"></span>
  <button id="save">Save</button>
</header>
<main>
  <nav id="groups"></nav>
  <section id="list"></section>
</main>
<script src="app.js"></script>
```

- [ ] **Step 2: Write `styles.css`**

```css
:root { --bg:#14161a; --panel:#1c1f26; --line:#2a2f3a; --fg:#e6e8ec;
        --dim:#8b93a3; --accent:#6ea8fe; --warn:#e0a04d; --bad:#e06c75; }
* { box-sizing: border-box; }
body { margin:0; background:var(--bg); color:var(--fg);
       font:14px/1.45 system-ui, "Segoe UI", sans-serif; }
header { display:flex; gap:12px; align-items:center; padding:10px 16px;
         background:var(--panel); border-bottom:1px solid var(--line);
         position:sticky; top:0; z-index:2; }
h1 { font-size:15px; margin:0 12px 0 0; letter-spacing:.02em; }
#search { flex:1; max-width:320px; background:var(--bg); color:var(--fg);
          border:1px solid var(--line); border-radius:6px; padding:6px 10px; }
#status { margin-left:auto; color:var(--dim); font-size:12px; }
button { background:var(--accent); color:#0b0d10; border:0; border-radius:6px;
         padding:6px 14px; font-weight:600; cursor:pointer; }
button.ghost { background:transparent; color:var(--fg);
               border:1px solid var(--line); font-weight:400; }
main { display:flex; align-items:flex-start; }
nav { width:180px; padding:12px; position:sticky; top:52px; }
nav a { display:block; padding:6px 10px; border-radius:6px; color:var(--dim);
        text-decoration:none; cursor:pointer; }
nav a.on { background:var(--panel); color:var(--fg); }
section { flex:1; padding:12px 16px 64px; }
.row { display:grid; grid-template-columns:34px 1fr 150px 92px 110px 34px;
       gap:10px; align-items:center; padding:8px 10px;
       border-bottom:1px solid var(--line); }
.row:hover { background:var(--panel); }
.name { min-width:0; }
.name b { font-weight:600; display:block; }
.name small { color:var(--dim); display:block; overflow:hidden;
              text-overflow:ellipsis; white-space:nowrap; }
.play { background:var(--panel); border:1px solid var(--line); color:var(--fg);
        border-radius:50%; width:30px; height:30px; cursor:pointer; }
.play.on { background:var(--accent); color:#0b0d10; }
input[type=range] { width:100%; }
.vol { width:56px; background:var(--bg); color:var(--fg); text-align:right;
       border:1px solid var(--line); border-radius:4px; padding:3px 6px; }
select, .swap { background:var(--bg); color:var(--fg); border:1px solid var(--line);
                border-radius:4px; padding:4px 6px; width:100%; cursor:pointer; }
.tag { font-size:11px; padding:1px 6px; border-radius:4px; margin-left:6px; }
.tag.empty { background:#3a2f1c; color:var(--warn); }
.tag.dead  { background:#3a1f22; color:var(--bad); }
.note { grid-column:2 / -1; }
.note input { width:100%; background:var(--bg); color:var(--dim);
              border:1px dashed var(--line); border-radius:4px; padding:4px 8px; }
h2 { font-size:12px; text-transform:uppercase; letter-spacing:.08em;
     color:var(--dim); margin:22px 0 6px; }
```

- [ ] **Step 3: Write `app.js`**

```js
// Audio Studio - the browser half.
//
// LOCKSTEP RULE (inherited from Dialogue Studio, and it has bitten before):
// BUSES and the manifest field names below must match GameAudio.cs in Part 2.
// Change one, change the other, or a sound previews here and does nothing
// in the game.
const BUSES = ["SFX", "Ambience", "UI", "Music"];

let manifest = { version: 1, sounds: [] };
let library = [];
let group = null;
let dirty = false;
let audio = new Audio();
let playingKey = null;

const $ = (s) => document.querySelector(s);
const status = (t) => ($("#status").textContent = t);

async function load() {
  manifest = await (await fetch("/api/manifest")).json();
  library = (await (await fetch("/api/library")).json()).files;
  render();
  status(`${manifest.sounds.length} sounds`);
}

function groups() {
  const seen = [];
  for (const s of manifest.sounds)
    if (!seen.includes(s.group)) seen.push(s.group);
  return seen.sort();
}

function visible() {
  const q = $("#search").value.trim().toLowerCase();
  return manifest.sounds.filter((s) => {
    if (group && s.group !== group) return false;
    if (!q) return true;
    return (
      (s.label || "").toLowerCase().includes(q) ||
      (s.key || "").toLowerCase().includes(q) ||
      (s.clip || "").toLowerCase().includes(q)
    );
  });
}

function play(sound, btn) {
  if (playingKey === sound.key) {
    audio.pause();
    playingKey = null;
    render();
    return;
  }
  if (!sound.clip) return;
  audio.pause();
  audio = new Audio("/api/audio?path=" + encodeURIComponent(sound.clip));
  audio.volume = Math.max(0, Math.min(1, Number(sound.volume) || 0));
  audio.onended = () => {
    playingKey = null;
    render();
  };
  audio.play().catch(() => status("could not play " + sound.clip));
  playingKey = sound.key;
  render();
}

function setField(sound, field, value) {
  sound[field] = value;
  dirty = true;
  status("unsaved changes");
  // A live volume change should be audible immediately.
  if (field === "volume" && playingKey === sound.key)
    audio.volume = Math.max(0, Math.min(1, Number(value) || 0));
}

function row(sound) {
  const el = document.createElement("div");
  el.className = "row";

  const play_ = document.createElement("button");
  play_.className = "play" + (playingKey === sound.key ? " on" : "");
  play_.textContent = playingKey === sound.key ? "\u25A0" : "\u25B6";
  play_.disabled = !sound.clip;
  play_.onclick = () => play(sound, play_);

  const name = document.createElement("div");
  name.className = "name";
  const b = document.createElement("b");
  b.textContent = sound.label || sound.key;
  if (!sound.clip) b.appendChild(tag("empty", "no clip"));
  if (sound.dead) b.appendChild(tag("dead", "never played"));
  if (sound.missing) b.appendChild(tag("dead", "gone from code"));
  const small = document.createElement("small");
  small.textContent = sound.clip || sound.key;
  name.append(b, small);

  const slider = document.createElement("input");
  slider.type = "range";
  slider.min = 0; slider.max = 1; slider.step = 0.01;
  slider.value = sound.volume;

  const num = document.createElement("input");
  num.className = "vol";
  num.type = "number";
  num.min = 0; num.max = 1; num.step = 0.01;
  num.value = Number(sound.volume).toFixed(2);

  slider.oninput = () => {
    num.value = Number(slider.value).toFixed(2);
    setField(sound, "volume", Number(slider.value));
  };
  num.oninput = () => {
    slider.value = num.value;
    setField(sound, "volume", Number(num.value));
  };

  const bus = document.createElement("select");
  for (const b2 of BUSES) {
    const o = document.createElement("option");
    o.value = o.textContent = b2;
    if (sound.bus === b2) o.selected = true;
    bus.appendChild(o);
  }
  bus.onchange = () => setField(sound, "bus", bus.value);

  const swap = document.createElement("button");
  swap.className = "swap";
  swap.textContent = "swap";
  swap.onclick = () => openSwap(sound);

  el.append(play_, name, slider, num, bus, swap);

  const note = document.createElement("div");
  note.className = "note";
  const ni = document.createElement("input");
  ni.placeholder = "note for Claude (e.g. too loud, find something drier)";
  ni.value = sound.note || "";
  ni.oninput = () => setField(sound, "note", ni.value);
  note.appendChild(ni);
  el.appendChild(note);
  return el;
}

function tag(cls, text) {
  const s = document.createElement("span");
  s.className = "tag " + cls;
  s.textContent = text;
  return s;
}

function openSwap(sound) {
  const picked = prompt(
    "Path of the replacement clip, relative to Assets/.\n" +
      "Leave blank and press OK to upload a file instead.\n\n" +
      "Current: " + (sound.clip || "(none)"),
    sound.clip || ""
  );
  if (picked === null) return;
  if (picked.trim() === "") return upload(sound);
  if (!library.includes(picked.trim()))
    return status("no such file in the project: " + picked.trim());
  setField(sound, "clip", picked.trim());
  render();
}

function upload(sound) {
  const input = document.createElement("input");
  input.type = "file";
  input.accept = ".mp3,.wav";
  input.onchange = async () => {
    const file = input.files[0];
    if (!file) return;
    status("uploading " + file.name + "...");
    const res = await fetch("/api/upload", {
      method: "POST",
      headers: { "X-Filename": file.name, "X-Group": sound.group || "Other" },
      body: await file.arrayBuffer(),
    });
    const out = await res.json();
    if (out.error) return status("upload failed: " + out.error);
    library.push(out.clip);
    setField(sound, "clip", out.clip);
    render();
    status("added " + out.clip);
  };
  input.click();
}

function render() {
  const nav = $("#groups");
  nav.innerHTML = "";
  const all = document.createElement("a");
  all.textContent = "All";
  all.className = group ? "" : "on";
  all.onclick = () => { group = null; render(); };
  nav.appendChild(all);
  for (const g of groups()) {
    const a = document.createElement("a");
    a.textContent = g;
    a.className = g === group ? "on" : "";
    a.onclick = () => { group = g; render(); };
    nav.appendChild(a);
  }

  const list = $("#list");
  list.innerHTML = "";
  let last = null;
  for (const s of visible()) {
    if (s.group !== last) {
      const h = document.createElement("h2");
      h.textContent = s.group;
      list.appendChild(h);
      last = s.group;
    }
    list.appendChild(row(s));
  }
}

async function save() {
  status("saving...");
  const res = await fetch("/api/manifest", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(manifest, null, 2),
  });
  const out = await res.json();
  if (out.error) return status("save failed: " + out.error);
  dirty = false;
  status("saved");
}

$("#save").onclick = save;
$("#search").oninput = render;
document.addEventListener("keydown", (e) => {
  if ((e.ctrlKey || e.metaKey) && e.key === "s") { e.preventDefault(); save(); }
});
window.addEventListener("beforeunload", (e) => {
  if (dirty) { e.preventDefault(); e.returnValue = ""; }
});
load();
```

- [ ] **Step 4: Manual acceptance — run through this list in the browser**

Start the server (`py -3 serve.py`) and confirm each:

1. The page lists sounds, grouped, with `Movement` present.
2. `PlayerController.footstepWalkClipA` shows `transfer/audio/walk.mp3`.
3. Pressing ▶ on it plays audio; pressing again stops it.
4. Dragging its volume to ~0.3 and pressing ▶ is audibly quieter than at 1.0.
5. Changing volume while it plays changes loudness live.
6. Sounds with no clip show an `no clip` tag and a disabled ▶.
7. `PlayerController.jumpClip` shows the `never played` tag.
8. Search filters the list.
9. Ctrl+S saves; `status` shows `saved`.
10. Reloading the page shows the saved volume, not the old one.
11. Closing the tab with unsaved changes warns.

- [ ] **Step 5: Confirm the save round-tripped to disk**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1" && py -3 -c "import io,json; d=json.load(io.open('Assets/StreamingAssets/Audio/sounds.json',encoding='utf-8')); s=[x for x in d['sounds'] if 'footstep_walk_clip_a' in x['key']][0]; print(s['key'], s['volume'], s['clip'])" && ls tools/audio-studio/backups/ | tail -3
```

Expected: the volume you set, and at least one backup file.

- [ ] **Step 6: Commit**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1"
git add "tools/audio-studio/index.html" "tools/audio-studio/styles.css" "tools/audio-studio/app.js"
git commit -m "feat(audio-studio): the browser UI - audition, level, recategorise, swap"
```

---

## Task 10: Launcher, ignore rules, and the manual

**Files:**
- Create: `tools/audio-studio/Audio Studio.bat`
- Create: `tools/audio-studio/README.md`
- Modify: `.gitignore`

- [ ] **Step 1: Write the launcher**

Create `tools/audio-studio/Audio Studio.bat` (mirrors Dialogue Studio's exactly):

```bat
@echo off
title Audio Studio
cd /d "%~dp0"
where py >nul 2>nul
if %errorlevel%==0 (
  py -3 serve.py
) else (
  python serve.py
)
pause
```

- [ ] **Step 2: Add ignore rules**

Append to `.gitignore`:

```
# Audio Studio: local caches, not source of truth
tools/audio-studio/sounds.scan.json
tools/audio-studio/backups/
tools/audio-studio/incoming/
```

- [ ] **Step 3: Write the manual**

Create `tools/audio-studio/README.md`:

```markdown
# Audio Studio

Mix the game's audio in a browser. No Unity, no recompiling.

**Start it:** double-click `Audio Studio.bat` -> http://localhost:8766
(Dialogue Studio is 8765; both can run at once.)

## What you're looking at

One row per sound the game can make in scene `1.6.7.7.7`, grouped by system.
Each row: play it, set its level, choose its category, swap its file, and leave
a note.

Tags you'll see:
- **no clip** - the slot exists in the code but nothing is wired to it.
- **never played** - the field exists but the code deliberately ignores it.
- **gone from code** - it was in the manifest, but the last scan couldn't find
  it any more. Nothing is ever deleted automatically.

## Swapping a sound

Press **swap**. Type a path relative to `Assets/`, or leave it blank and press
OK to upload an `.mp3`/`.wav` from anywhere on your machine. Uploads land in
`Assets/Audio/Studio/<Group>/` with their Unity `.meta` written for you.

## Saving

**Ctrl+S** or the Save button. Every save backs up the previous manifest to
`backups/` (30 kept). The manifest itself is
`Assets/StreamingAssets/Audio/sounds.json` - plain text, in git.

## Rescanning

After code changes add or remove sounds:

    py -3 tools/audio-studio/scan.py

**A rescan never overwrites your work.** The scanner owns which clip is wired
up and whether the sound still exists; you own the label, group, volume,
category and note.

## Tests

    cd tools/audio-studio && py -3 -m unittest discover -s tests -t . -v
```

- [ ] **Step 4: Run the whole test suite one final time**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1/tools/audio-studio" && py -3 -m unittest discover -s tests -t . -v
```

Expected: all tests pass, zero failures, zero errors.

- [ ] **Step 5: Commit**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1"
git add "tools/audio-studio/Audio Studio.bat" "tools/audio-studio/README.md" .gitignore
git commit -m "feat(audio-studio): launcher, manual and ignore rules"
```

---

## Task 11: Curate the labels

The scanner produces `PlayerController.footstepWalkClipA`. Sam should read
"Walking — step A". This is the last task because it needs the real scan output.

**Files:**
- Modify: `Assets/StreamingAssets/Audio/sounds.json`

- [ ] **Step 1: List what needs a label**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1" && py -3 -c "
import io, json
d = json.load(io.open('Assets/StreamingAssets/Audio/sounds.json', encoding='utf-8'))
for s in d['sounds']:
    print('%-10s %-46s %s' % (s['group'], s['label'], s['clip'] or '(empty)'))
"
```

- [ ] **Step 2: Rewrite every `label` in plain English**

Edit `Assets/StreamingAssets/Audio/sounds.json` directly. Rules:
- Say what the **player does or hears**, not what the code calls it.
  `PlayerController.footstepWalkClipA` → `Walking — step A`.
  `OxygenManager._alarmClip` → `Oxygen low — alarm`.
- Keep it under ~40 characters so the row does not wrap.
- Do **not** change any `key` — keys are permanent.
- Fix any obviously wrong `group` while you are in there.

- [ ] **Step 3: Verify the merge survives a rescan**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1/tools/audio-studio" && py -3 scan.py && cd .. && cd .. && py -3 -c "
import io, json
d = json.load(io.open('Assets/StreamingAssets/Audio/sounds.json', encoding='utf-8'))
raw = [s for s in d['sounds'] if '.' in s['label'] and s['label'][0].isupper() and 'Clip' in s['label']]
print('still-unlabelled:', len(raw))
for s in raw[:10]: print('  ', s['label'])
"
```

Expected: `still-unlabelled: 0`.

- [ ] **Step 4: Commit**

```bash
cd "C:/2.0 SOUND OF SPACE/1aughhh1"
git add "Assets/StreamingAssets/Audio/sounds.json"
git commit -m "feat(audio-studio): plain-English labels for every sound"
```

---

## Done — hand over to Sam

Tell Sam, in this shape:

> Audio Studio is ready. Double-click `tools/audio-studio/Audio Studio.bat`.
> It found **N** sounds. **X** of them have no clip wired up at all, and **Y**
> are dead fields the code never reads — worth a look, because some of those
> are probably things you thought were making noise.
> Go through and set levels, swap anything you dislike, and leave notes on the
> rows you want me to deal with. Nothing you do in there changes the game yet;
> that's Part 2, and I'll write that plan once you've mixed.

Then **stop**. Do not start Part 2 without Sam.

---

## Self-review notes

Checked against the spec on 2026-09-09:

- Spec §3.1 manifest schema — Task 6 (`blank`) and Task 9 (`BUSES`). ✅
- Spec §3.3 scanner (4 steps: C#, GUID index, YAML, curate) — Tasks 2–7, 11. ✅
- Spec §3.4 tool (port 8766, groups, ▶, volume, category, swap, notes, backups,
  upload, `incoming/`) — Tasks 8–10. ✅
- Spec §3.4 lockstep rule — carried as a comment at the top of `app.js`. ✅
- Spec §6 known limitation — reflected in the hand-over wording. ✅
- Spec §7 cross-session rules — the "safe to run start-to-finish" note. ✅
- Spec §9 success criteria — criteria 1 (inventory + audition + meaningful
  labels) is covered by Tasks 7/9/11; criteria 2–4 are **Part 2** by design;
  criterion 5 (zero warnings) is not applicable, no C# is written here;
  criterion 6 (no scene edit) holds — nothing in this plan opens the scene for
  writing.
- Naming consistency: `path_for`/`guid_for`/`script_guids`/`audio_guids`,
  `find_clip_fields`/`class_name`, `split_documents`/`scan_components`/
  `component_script_map`/`scan_overrides`, `in_scope`/`group_for`/`make_key`/
  `blank`/`merge`/`read_manifest`/`write_manifest` — each defined once and used
  with the same signature everywhere. ✅
- No placeholders: every step contains runnable code or a runnable command with
  its expected output. ✅
