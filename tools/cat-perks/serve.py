#!/usr/bin/env python3
"""
Cat Perks — local mockup server.

    py -3 tools/cat-perks/serve.py        (or double-click "Cat Perks.bat")
    -> http://localhost:8767

Static-only. This is a DESIGN MOCKUP of the space-cat fish-for-perks trade and
three different case-opening presentations, so Sam can feel them before any C#
is written. Nothing here touches the project. Standard library only.
"""
import http.server
import os
import socketserver
import webbrowser

HERE = os.path.dirname(os.path.abspath(__file__))
PORT = int(os.environ.get("CAT_PERKS_PORT", "8767"))


class Handler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *a, **kw):
        super().__init__(*a, directory=HERE, **kw)

    def end_headers(self):
        self.send_header("Cache-Control", "no-store, max-age=0")
        super().end_headers()

    def log_message(self, fmt, *args):
        pass


class Server(socketserver.TCPServer):
    allow_reuse_address = True


if __name__ == "__main__":
    url = "http://localhost:%d/" % PORT
    print("Cat Perks mockup  ->  " + url)
    print("Ctrl+C to stop.")
    try:
        webbrowser.open(url)
    except Exception:
        pass
    with Server(("127.0.0.1", PORT), Handler) as httpd:
        try:
            httpd.serve_forever()
        except KeyboardInterrupt:
            print("\nstopped.")
