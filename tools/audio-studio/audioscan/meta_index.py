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
        return {g: p for g, p in self._by_guid.items() if p.lower().endswith(exts)}

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
