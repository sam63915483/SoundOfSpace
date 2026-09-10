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
# The Sound Lab's audition sheet. Deliberately NOT in StreamingAssets: these are
# candidate clips that no game object references yet, so the sheet is a tool file,
# not shipped data. Winners get copied into the manifest by hand once picked.
CANDIDATES = os.path.join(HERE, "candidates.json")
BACKUP_DIR = os.path.join(HERE, "backups")
INCOMING = os.path.join(HERE, "incoming")
PORT = int(os.environ.get("AUDIO_STUDIO_PORT", "8766"))
KEEP_BACKUPS = 30
MAX_UPLOAD = 50 * 1024 * 1024

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
        p
        for p in os.listdir(BACKUP_DIR)
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


def audio_library():
    """Every playable audio file in the project, for the swap picker."""
    out = []
    for dirpath, dirnames, filenames in os.walk(ASSETS):
        dirnames[:] = [
            d
            for d in dirnames
            if d not in {"Library", "Temp", "obj", "Logs", ".git"}
        ]
        for name in filenames:
            if os.path.splitext(name)[1].lower() in AUDIO_TYPES:
                full = os.path.join(dirpath, name)
                out.append(os.path.relpath(full, ASSETS).replace(os.sep, "/"))
    return sorted(out)


def read_candidates():
    """The audition sheet, with each take stamped with whether its file is
    actually on disk yet. Generation happens outside this tool, so a take can
    sit here fully described and ungenerated for as long as it takes."""
    if not os.path.isfile(CANDIDATES):
        return {"version": 1, "sets": []}
    with io.open(CANDIDATES, encoding="utf-8") as f:
        data = json.load(f)
    made = 0
    total = 0
    for st in data.get("sets") or []:
        for tk in st.get("takes") or []:
            total += 1
            full = safe_asset_path(tk.get("file") or "")
            tk["exists"] = bool(full)
            tk["bytes"] = os.path.getsize(full) if full else 0
            if full:
                made += 1
    data["generated"] = made
    data["total"] = total
    return data


def write_candidates(data):
    with io.open(CANDIDATES, "w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(data, indent=2, ensure_ascii=False) + "\n")


class Handler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *a, **kw):
        super().__init__(*a, directory=HERE, **kw)

    def log_message(self, fmt, *args):
        # args[0] is the request line for a normal access log, but log_error
        # passes an HTTPStatus first (a favicon 404 is enough to hit it), and
        # `"/api/" in <HTTPStatus>` raises inside the request thread. Stringify
        # first, and swallow a bad format rather than printing a traceback.
        first = str(args[0]) if args else ""
        if "/api/" not in first:
            return
        try:
            line = fmt % args
        except Exception:
            line = "%s %r" % (fmt, args)
        sys.stdout.write("%s %s" % (time.strftime("%H:%M:%S"), line) + chr(10))

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
        if u.path == "/api/library":
            return self.send_json({"files": audio_library()})
        if u.path == "/api/candidates":
            return self.send_json(read_candidates())
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
            self.end_headers()
            with open(full, "rb") as f:
                shutil.copyfileobj(f, self.wfile)
            return
        if u.path.startswith("/api/"):
            return self.send_json({"error": "unknown api"}, 404)
        if u.path == "/":
            self.path = "/index.html"
        return super().do_GET()

    def do_PUT(self):
        path = urlparse(self.path).path
        if path == "/api/candidates":
            n = int(self.headers.get("Content-Length") or 0)
            try:
                data = json.loads(self.rfile.read(n).decode("utf-8"))
            except Exception as e:  # noqa: BLE001
                return self.send_json({"error": "not valid JSON: %s" % e}, 400)
            if not isinstance(data, dict) or not isinstance(data.get("sets"), list):
                return self.send_json({"error": "candidates need a sets list"}, 400)
            # `exists` is computed fresh on every read, so it must never be
            # written back - a stale true would show a play button for a file
            # that is not there.
            for st in data["sets"]:
                for tk in st.get("takes") or []:
                    tk.pop("exists", None)
                    tk.pop("bytes", None)
            write_candidates(data)
            return self.send_json({"ok": True})
        if path != "/api/manifest":
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
                return self.send_json({"error": "bad volume on %s" % s["key"]}, 400)
        bak = backup()
        manifest_mod.write_manifest(MANIFEST, data)
        return self.send_json({"ok": True, "backup": bak})

    def do_POST(self):
        if urlparse(self.path).path != "/api/upload":
            return self.send_json({"error": "unknown api"}, 404)
        name = os.path.basename(unquote(self.headers.get("X-Filename") or ""))
        if os.path.splitext(name)[1].lower() not in AUDIO_TYPES:
            return self.send_json({"error": "only .mp3 and .wav"}, 400)
        group = (
            re.sub(r"[^A-Za-z0-9_-]+", "", self.headers.get("X-Group") or "Other")
            or "Other"
        )
        n = int(self.headers.get("Content-Length") or 0)
        if n <= 0 or n > MAX_UPLOAD:
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
            {"ok": True, "clip": os.path.relpath(dest, ASSETS).replace(os.sep, "/")}
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
