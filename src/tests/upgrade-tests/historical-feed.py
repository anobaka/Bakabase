"""Bounded own-loopback feed for actual published historical installations.

Inactive means an empty feed: GitHub never published an old full nupkg for this
release. Only verified candidate nupkg files can be downloaded after activation.
"""
import http.server
import importlib.util
import json
from pathlib import Path
import threading
import time
import urllib.parse

SPEC = importlib.util.spec_from_file_location("historical_release_feed", Path(__file__).with_name("historical-release.py"))
release = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(release)
shared = release.sibling("installed-update-feed")
require, validate_package, ROLES = shared.require, shared.validate_package, shared.ROLES


class Feed:
    """No old full archive is fabricated: inactive feeds contain no assets."""

    def __init__(self, manifest_path, rid, old_version):
        require(old_version == release.OLD_VERSION, "Wrong historical installed version")
        self.manifest = release.read_manifest(Path(manifest_path), rid)
        self.deliveries, self.requests = [], []
        self._lock = threading.Lock()
        self._active = {role: "old" for role in ROLES}
        self._packages = {(role, kind): validate_package(entry, kind)
                          for role, entry in self.manifest["roles"].items() for kind in ("new",)}
        self._bytes = 0
        self._budget = sum(asset["Size"] for _, asset in self._packages.values()) * 3
        self._expires = time.monotonic() + 20 * 60
        owner = self
        channel = "win" if rid == "win-x64" else "osx"
        feeds = {f"/{role}/{rid}/releases.{channel}.json": role for role in ROLES}
        packages = {f"/{role}/{rid}/{path.name}": (role, kind) for (role, kind), (path, _) in self._packages.items()}

        class Handler(http.server.BaseHTTPRequestHandler):
            def do_GET(self):
                self.connection.settimeout(15)
                # Velopack may append a cache-busting query. The path is still exact.
                parsed = urllib.parse.urlsplit(self.path)
                path = parsed.path
                if parsed.scheme or parsed.netloc or len(self.path) > 2048 or time.monotonic() > owner._expires:
                    self.send_error(400)
                    return
                with owner._lock:
                    if len(owner.requests) >= 1000:
                        self.send_error(429)
                        return
                    owner.requests.append({"path": path, "at": time.time()})
                if path in feeds:
                    role = feeds[path]
                    with owner._lock:
                        kind = owner._active[role]
                    assets = [] if kind == "old" else [owner._packages[role, kind][1]]
                    content = json.dumps({"Assets": assets}).encode()
                    self.send_response(200)
                    self.send_header("Content-Type", "application/json")
                    self.send_header("Content-Length", str(len(content)))
                    self.send_header("Cache-Control", "no-store")
                    self.end_headers()
                    self.wfile.write(content)
                    return
                pair = packages.get(path)
                if pair is None:
                    self.send_error(404)
                    return
                role, kind = pair
                with owner._lock:
                    if kind != owner._active[role]:
                        self.send_error(404)
                        return
                    file, asset = owner._packages[pair]
                    if owner._bytes + asset["Size"] > owner._budget:
                        self.send_error(429)
                        return
                    owner._bytes += asset["Size"]
                delivery = {"role": role, "kind": kind, "file": file.name, "bytes": 0, "completed": False}
                with owner._lock:
                    owner.deliveries.append(delivery)
                try:
                    self.send_response(200)
                    self.send_header("Content-Type", "application/octet-stream")
                    self.send_header("Content-Length", str(asset["Size"]))
                    self.end_headers()
                    with file.open("rb") as stream:
                        for block in iter(lambda: stream.read(1024 * 1024), b""):
                            require(time.monotonic() < owner._expires, "Package feed deadline exceeded")
                            self.wfile.write(block)
                            delivery["bytes"] += len(block)
                    self.wfile.flush()
                    delivery["completed"] = delivery["bytes"] == asset["Size"]
                except (OSError, ValueError):
                    pass

            def log_message(self, *_):
                pass

        self._server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self._server.daemon_threads = True
        self._thread = threading.Thread(target=self._server.serve_forever, daemon=True)
        self._thread.start()
        self.environment = {("BAKABASE_UPDATE_URL" if role == "unified" else "BAKABASE_CLIENT_UPDATE_URL"):
                            f"http://127.0.0.1:{self._server.server_port}/{role}" for role in ROLES}

    def activate(self, role):
        require(role in ROLES, "Unknown product role")
        with self._lock:
            require(self._active[role] == "old", "Target feed is already activated")
            self._active[role] = "new"

    def deactivate(self, role):
        require(role in ROLES, "Unknown product role")
        with self._lock:
            require(self._active[role] == "new", "Target feed is not activated")
            self._active[role] = "old"

    def close(self):
        self._server.shutdown()
        self._server.server_close()
        self._thread.join(timeout=5)
