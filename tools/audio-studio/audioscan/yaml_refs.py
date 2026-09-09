"""Read object references out of Unity's YAML, without a YAML library.

Unity's dialect is not standard YAML (the `--- !u!114 &123` document headers
are its own), PyYAML is not available (stdlib only), and the target scene is
72 MB, so a real parse would be both wrong and slow. Line scanning it is.

The important half of this module is `scan_overrides`. On disk,
NetworkPlayer.prefab says the player's footstep clips are empty; the clips
that actually play are assigned by the SCENE, as prefab-instance overrides.
Anything that reads prefabs alone concludes the game has no footstep audio.
"""
import re

DOC_RE = re.compile(r"^--- !u!(\d+) &(\d+)")
SCRIPT_RE = re.compile(r"^\s*m_Script:\s*\{fileID:\s*\d+,\s*guid:\s*([0-9a-f]{32})")
# A serialized object reference: either empty ({fileID: 0}) or a real GUID.
REF_RE = re.compile(
    r"^  ([A-Za-z_]\w*):\s*\{fileID:\s*(\d+)(?:,\s*guid:\s*([0-9a-f]{32}))?"
)

TARGET_RE = re.compile(
    r"^\s*-\s*target:\s*\{fileID:\s*(\d+),\s*guid:\s*([0-9a-f]{32})"
)
PROPERTY_RE = re.compile(r"^\s*propertyPath:\s*(.+?)\s*$")
OBJREF_RE = re.compile(
    r"^\s*objectReference:\s*\{fileID:\s*(\d+)(?:,\s*guid:\s*([0-9a-f]{32}))?"
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
    """Every Unity YAML document in `text`, in order."""
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

    Empty slots are returned too (clip_guid None) - a slot nobody filled is
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
