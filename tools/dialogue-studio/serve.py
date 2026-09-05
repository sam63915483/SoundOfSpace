#!/usr/bin/env python3
"""
Dialogue Studio — local server.

    py -3 tools/dialogue-studio/serve.py          (or double-click "Dialogue Studio.bat")
    → http://localhost:8765

Serves the studio's static files and a tiny JSON API over the game's real
dialogue folder, Assets/StreamingAssets/Story. Every save writes a timestamped
backup to tools/dialogue-studio/backups/ first (30 kept per file) and creates
the Unity .meta if the file is new. Standard library only — no pip installs.
"""
import http.server
import json
import os
import re
import socketserver
import sys
import time
import uuid
import webbrowser
from urllib.parse import urlparse, unquote

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
STORY_DIR = os.path.join(ROOT, "Assets", "StreamingAssets", "Story")
BACKUP_DIR = os.path.join(HERE, "backups")
PORT = int(os.environ.get("DIALOGUE_STUDIO_PORT", "8765"))
KEEP_BACKUPS = 30
NAME_RE = re.compile(r"^(npc|conv)_[a-z0-9_]+\.json$")

META_TEMPLATE = (
    "fileFormatVersion: 2\n"
    "guid: {guid}\n"
    "DefaultImporter:\n"
    "  externalObjects: {{}}\n"
    "  userData: \n"
    "  assetBundleName: \n"
    "  assetBundleVariant: \n"
)


def read_json(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def list_story_files():
    out = []
    if not os.path.isdir(STORY_DIR):
        return out
    for name in sorted(os.listdir(STORY_DIR)):
        if not NAME_RE.match(name):
            continue
        path = os.path.join(STORY_DIR, name)
        entry = {"file": name, "id": "", "kind": "", "displayName": "", "nodes": 0, "error": ""}
        try:
            data = read_json(path)
            entry["id"] = data.get("id", "")
            entry["kind"] = data.get("kind") or ("npc" if name.startswith("npc_") else "phone")
            entry["displayName"] = data.get("displayName", "")
            entry["nodes"] = len(data.get("nodes") or [])
            entry["presets"] = len(data.get("testPresets") or [])
        except Exception as e:  # noqa: BLE001
            entry["error"] = str(e)
        out.append(entry)
    return out


def backup(name):
    src = os.path.join(STORY_DIR, name)
    if not os.path.isfile(src):
        return None
    os.makedirs(BACKUP_DIR, exist_ok=True)
    stamp = time.strftime("%Y%m%d-%H%M%S")
    dst = os.path.join(BACKUP_DIR, f"{name[:-5]}.{stamp}.json")
    with open(src, "rb") as f, open(dst, "wb") as g:
        g.write(f.read())
    # prune
    prefix = name[:-5] + "."
    olds = sorted(p for p in os.listdir(BACKUP_DIR) if p.startswith(prefix) and p.endswith(".json"))
    for p in olds[:-KEEP_BACKUPS]:
        try:
            os.remove(os.path.join(BACKUP_DIR, p))
        except OSError:
            pass
    return os.path.relpath(dst, ROOT)


def write_story_file(name, text):
    os.makedirs(STORY_DIR, exist_ok=True)
    path = os.path.join(STORY_DIR, name)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
        if not text.endswith("\n"):
            f.write("\n")
    meta = path + ".meta"
    if not os.path.isfile(meta):
        with open(meta, "w", encoding="utf-8", newline="\n") as f:
            f.write(META_TEMPLATE.format(guid=uuid.uuid4().hex))


class Handler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *a, **kw):
        super().__init__(*a, directory=HERE, **kw)

    def log_message(self, fmt, *args):  # quieter console
        if "/api/" in (args[0] if args else ""):
            sys.stdout.write("%s %s\n" % (time.strftime("%H:%M:%S"), fmt % args))

    # ---- helpers ----
    def send_json(self, obj, code=200):
        body = json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def read_body(self):
        n = int(self.headers.get("Content-Length") or 0)
        return self.rfile.read(n).decode("utf-8") if n else ""

    def story_name(self, path):
        m = re.match(r"^/api/file/([^/]+)$", path)
        if not m:
            return None
        name = unquote(m.group(1))
        return name if NAME_RE.match(name) else None

    # ---- routes ----
    def do_GET(self):
        p = urlparse(self.path).path
        if p == "/api/roster":
            roster = read_json(os.path.join(HERE, "roster.json"))
            return self.send_json({"storyDir": STORY_DIR, "files": list_story_files(), "roster": roster})
        if p == "/api/vocab":
            return self.send_json(read_json(os.path.join(HERE, "vocab.json")))
        if p.startswith("/api/file/"):
            name = self.story_name(p)
            if not name:
                return self.send_json({"error": "bad file name"}, 400)
            path = os.path.join(STORY_DIR, name)
            if not os.path.isfile(path):
                return self.send_json({"error": "not found"}, 404)
            with open(path, "rb") as f:
                body = f.read()
            self.send_response(200)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
            return
        if p.startswith("/api/"):
            return self.send_json({"error": "unknown api"}, 404)
        if p == "/" or p.startswith("/#"):
            self.path = "/index.html"
        return super().do_GET()

    def end_headers(self):
        # static files too: the browser must never cache app.js between edits
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def do_PUT(self):
        p = urlparse(self.path).path
        name = self.story_name(p)
        if not name:
            return self.send_json({"error": "bad file name (npc_*.json or conv_*.json, lower-case)"}, 400)
        text = self.read_body()
        try:
            data = json.loads(text)
        except Exception as e:  # noqa: BLE001
            return self.send_json({"error": "not valid JSON: %s" % e}, 400)
        if not isinstance(data, dict) or not data.get("id"):
            return self.send_json({"error": "graph needs an id"}, 400)
        expected_id = name[:-5]
        if data["id"] != expected_id:
            return self.send_json({"error": f"id '{data['id']}' must match the file name ('{expected_id}')"}, 400)
        bak = backup(name)
        write_story_file(name, json.dumps(data, ensure_ascii=False, indent=2))
        return self.send_json({"ok": True, "backup": bak, "path": os.path.relpath(os.path.join(STORY_DIR, name), ROOT)})

    def do_POST(self):
        p = urlparse(self.path).path
        if p == "/api/new":
            try:
                req = json.loads(self.read_body() or "{}")
            except Exception as e:  # noqa: BLE001
                return self.send_json({"error": "bad body: %s" % e}, 400)
            slug = re.sub(r"[^a-z0-9_]+", "_", (req.get("id") or "").strip().lower()).strip("_")
            if not slug:
                return self.send_json({"error": "need an id"}, 400)
            kind = "conv" if req.get("kind") == "phone" else "npc"
            name = f"{kind}_{slug}.json"
            path = os.path.join(STORY_DIR, name)
            if os.path.isfile(path):
                return self.send_json({"error": f"{name} already exists"}, 409)
            display = (req.get("displayName") or slug.replace("_", " ").title()).strip()
            graph = {
                "id": name[:-5],
                "kind": "phone" if kind == "conv" else "npc",
                "displayName": display,
                "testPresets": [],
                "nodes": [
                    {"id": "start", "speaker": display if kind == "npc" else "AI",
                     "lines": ["Hello there."], "responses": []}
                ],
            }
            write_story_file(name, json.dumps(graph, ensure_ascii=False, indent=2))
            return self.send_json({"ok": True, "file": name})
        return self.send_json({"error": "unknown api"}, 404)


class Server(socketserver.ThreadingMixIn, http.server.HTTPServer):
    allow_reuse_address = True
    daemon_threads = True


def main():
    if not os.path.isdir(STORY_DIR):
        print("!! Story folder not found:", STORY_DIR)
        print("   Run this from the repo (tools/dialogue-studio/serve.py).")
        sys.exit(1)
    url = f"http://localhost:{PORT}/"
    print("Dialogue Studio")
    print("  story folder :", STORY_DIR)
    print("  backups      :", BACKUP_DIR)
    print("  open         :", url)
    print("  (Ctrl+C to stop)")
    if "--no-browser" not in sys.argv:
        try:
            webbrowser.open(url)
        except Exception:  # noqa: BLE001
            pass
    with Server(("127.0.0.1", PORT), Handler) as httpd:
        try:
            httpd.serve_forever()
        except KeyboardInterrupt:
            print("\nbye")


if __name__ == "__main__":
    main()
