"""Strict own-loopback package feed for native installed-updater acceptance."""
import hashlib
import http.server
import json
from pathlib import Path
import re
import threading
import time
import urllib.parse

ROLES = {"unified": "Bakabase", "client": "Bakabase.Client"}
MAX_PACKAGE = 1024 ** 3


def require(value, message):
    if not value:
        raise ValueError(message)


def validate_manifest(manifest, rid, old_version):
    require(manifest.get("passed") is True, "Update preparation did not pass")
    require(manifest.get("rid") == rid and manifest.get("oldVersion") == old_version,
            "Update manifest does not match this installed fixture")
    require(re.fullmatch(r"0\.0\.1-acceptance\.[1-9]\d*\.[1-9]\d*", old_version), "Invalid old fixture version")
    require(re.fullmatch(r"0\.0\.2-updater\.[1-9]\d*\.[1-9]\d*", manifest.get("newVersion", "")),
            "Invalid target fixture version")
    require(re.fullmatch(r"[0-9a-f]{40}", manifest.get("sourceSHA", "")), "Missing package source identity")
    require(set(manifest.get("roles", {})) == set(ROLES), "Expected two distinct update roles")
    require(rid in ("win-x64", "osx-arm64", "osx-x64"), "Unsupported native update architecture")
    channel = "win" if rid == "win-x64" else "osx"
    for role, identity in ROLES.items():
        entry = manifest["roles"][role]
        require(entry.get("role") == role and entry.get("payloadUnchanged") is True,
                "Unverified or mismatched product payload")
        require(entry.get("channel") == channel, "Unexpected updater channel")
        for kind in ("old", "new"):
            metadata = entry[kind + "Manifest"]
            require(metadata.get("id") == identity and metadata.get("rid") == rid and
                    metadata.get("version") == manifest[kind + "Version"], "Update package identity mismatch")
        marker = entry.get("marker", {})
        require(marker.get("role") == role and marker.get("sourceSHA") == manifest["sourceSHA"] and
                marker.get("oldVersion") == old_version and marker.get("newVersion") == manifest["newVersion"] and
                isinstance(marker.get("nonce"), str) and len(marker["nonce"]) >= 16,
                "Update marker does not match the prepared product")


def validate_package(entry, kind):
    path = Path(entry[kind + "FullPackage"])
    expected = entry["packageChecksums"][kind + "Full"]
    require(path.is_absolute() and path.is_file() and not path.is_symlink(), "Expected an absolute regular package")
    require(re.fullmatch(r"[A-Za-z0-9_.-]+-full\.nupkg", path.name), "Unsafe package filename")
    require(expected.get("fileName") == path.name and Path(expected.get("path", "")).resolve() == path.resolve(),
            "Package checksum path mismatch")
    size = path.stat().st_size
    require(type(expected.get("sizeBytes")) is int and size == expected["sizeBytes"] and 0 < size <= MAX_PACKAGE,
            "Package size mismatch or budget exceeded")
    hashes = {name: hashlib.new(name) for name in ("sha1", "sha256")}
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            for digest in hashes.values():
                digest.update(block)
    require(all(expected.get(name) == digest.hexdigest() for name, digest in hashes.items()), "Package hash mismatch")
    return path, {"PackageId": entry[kind + "Manifest"]["id"], "Version": entry[kind + "Manifest"]["version"],
                  # Velopack 1.2.0 compares the feed digest with uppercase hex.
                  "Type": "Full", "FileName": path.name, "SHA1": expected["sha1"].upper(),
                  "SHA256": expected["sha256"].upper(), "Size": size}


class Feed:
    """Start with each original version; expose a new version only on activate()."""

    def __init__(self, manifest_path, rid, old_version):
        self.manifest = json.loads(Path(manifest_path).read_text())
        validate_manifest(self.manifest, rid, old_version)
        self.deliveries, self.requests = [], []
        self._lock = threading.Lock()
        self._active = {role: "old" for role in ROLES}
        self._packages = {(role, kind): validate_package(entry, kind)
                          for role, entry in self.manifest["roles"].items() for kind in ("old", "new")}
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
                    content = json.dumps({"Assets": [owner._packages[role, kind][1]]}).encode()
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
