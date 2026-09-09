"""Pull the audio-shaped facts out of a C# source file.

Regex, not a parser: the only constructs that matter are field declarations
and a handful of call shapes, and this has to run over ~800 files quickly.
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
        return isinstance(other, ClipField) and (self.name, self.dead) == (
            other.name,
            other.dead,
        )


COMMENT_RE = re.compile(r"^\s*(?://|/\*|\*)")


def _is_dead(text, field_name):
    """True when any comment in the file names the field and says it is not read.

    Deliberately whole-file, not "a few lines above the declaration". In
    PlayerController the marker for `jumpClip` sits ~960 lines below it, in the
    method body that stopped using it — which is the natural place to write it,
    and where the previous author actually did.
    """
    needle = field_name.lower()
    for line in text.splitlines():
        if not COMMENT_RE.match(line):
            continue
        low = line.lower()
        if needle in low and any(h in low for h in DEAD_HINTS):
            return True
    return False


def _class_body_depth(text):
    """Brace depth at which this file's type members live.

    0 for a bare `class Foo`, 1 when it is wrapped in a namespace. Anything
    deeper than this is a method body, and an `AudioClip` declared there is a
    local variable, not a serialized slot.
    """
    depth = 0
    for line in text.splitlines():
        if re.search(r"\b(class|struct)\s+[A-Za-z_]\w*", line):
            return depth + 1
        depth += line.count("{") - line.count("}")
    return 1


def find_clip_fields(text):
    """Every serialized `AudioClip` slot on the type in `text`.

    Locals are excluded by brace depth: `AudioClip target = a : b;` inside a
    method would otherwise become a row in the mixer that maps to nothing the
    user can change.
    """
    want = _class_body_depth(text)
    out = []
    depth = 0
    for line in text.splitlines():
        if depth == want:
            m = CLIP_FIELD_RE.match(line)
            if m:
                out.append(ClipField(m.group(1), dead=_is_dead(text, m.group(1))))
        depth += line.count("{") - line.count("}")
    return out


def find_resource_loads(text):
    return RESOURCE_LOAD_RE.findall(text)


def class_name(text):
    m = CLASS_RE.search(text)
    return m.group(1) if m else ""


def count_code_sources(text):
    return len(ADD_SOURCE_RE.findall(text))
