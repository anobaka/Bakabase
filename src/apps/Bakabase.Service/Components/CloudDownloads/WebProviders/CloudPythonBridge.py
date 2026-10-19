"""Private JSON-lines adapter for gdown 6.4.2 and gallery-dl 1.32.17.

Run only with the dependency's isolated Python. No user config, browser cookies,
postprocessors, shell commands, global package installation or URL fallback.
"""
import contextlib
import hashlib
import importlib.metadata
import json
import os
from pathlib import Path
import re
import sys
import time
import urllib.parse


PROTOCOL_OUTPUT = sys.stdout


class BridgeError(Exception):
    pass


def emit(value):
    print(json.dumps(value, ensure_ascii=True), file=PROTOCOL_OUTPUT, flush=True)


def segment(value):
    if not isinstance(value, str) or not value or value in (".", ".."):
        raise BridgeError("Invalid remote filename")
    if any(c in value for c in '/\\\x00<>:"|?*') or any(ord(c) < 32 for c in value):
        raise BridgeError("Unsafe remote filename")
    if value.endswith((".", " ")) or value.split(".")[0].upper() in {
        "CON", "PRN", "AUX", "NUL", *[f"COM{i}" for i in range(1, 10)], *[f"LPT{i}" for i in range(1, 10)]
    }:
        raise BridgeError("Unsafe remote filename")
    return value


def relative_path(value):
    # Google returns platform-relative paths. Reject both styles of traversal.
    return "/".join(segment(s) for s in value.replace("\\", "/").split("/"))


def beneath(root, relative):
    root = root.resolve()
    candidate = root.joinpath(*relative.split("/"))
    for item in [root, *candidate.parents, candidate]:
        if item.is_symlink():
            raise BridgeError("Symbolic links are not supported in download destinations")
    if not candidate.resolve().is_relative_to(root):
        raise BridgeError("Download path escapes destination")
    return candidate


def validate_url(provider, url):
    parsed = urllib.parse.urlsplit(url)
    if parsed.scheme != "https" or parsed.username or parsed.password or parsed.port not in (None, 443):
        raise BridgeError("Only HTTPS share URLs are supported")
    if provider == "googleDrive":
        if parsed.hostname == "drive.google.com":
            valid = re.fullmatch(r"/(?:drive/(?:u/\d+/)?folders/[\w-]+|file/d/[\w-]+(?:/(?:view|edit|preview))?|(?:open|uc))/?", parsed.path)
            if not valid:
                raise BridgeError("Unsupported Google Drive share URL")
            if parsed.path in ("/open", "/uc") and not re.fullmatch(r"[\w-]+", urllib.parse.parse_qs(parsed.query).get("id", [""])[0]):
                raise BridgeError("Google Drive file ID is missing")
        elif parsed.hostname == "docs.google.com" and re.fullmatch(r"/(?:document|spreadsheets|presentation)/d/[\w-]+(?:/(?:edit|view|preview))?/?", parsed.path):
            pass
        else:
            raise BridgeError("Unsupported Google Drive share URL")
    elif provider == "gofile":
        if parsed.hostname not in ("gofile.io", "www.gofile.io") or not re.fullmatch(r"/d/[A-Za-z0-9_-]+/?", parsed.path):
            raise BridgeError("Unsupported GoFile share URL")
    else:
        raise BridgeError("Unsupported provider")


def configure_session(session, settings):
    # trust_env=False disables .netrc and environment proxies; only app-resolved settings apply.
    session.trust_env = False
    if settings is None:
        return
    parsed = urllib.parse.urlsplit(settings["url"])
    if parsed.scheme not in ("http", "https", "socks4", "socks5", "socks5h") or not parsed.hostname or parsed.username or parsed.password:
        raise BridgeError("Unsupported app proxy configuration")
    netloc = parsed.netloc
    if settings.get("username") is not None:
        username = urllib.parse.quote(settings["username"], safe="")
        password = urllib.parse.quote(settings.get("password") or "", safe="")
        netloc = username + ":" + password + "@" + netloc
    url = urllib.parse.urlunsplit(parsed._replace(netloc=netloc))
    session.proxies.update({"http": url, "https": url})


def gofile_content(extractor, content_id, password):
    """The pinned extractor only requests page one. Validate and merge every page."""
    children = {}
    first = None
    expected = None
    for page in range(1, 10002):
        params = {"contentFilter": "", "page": str(page), "pageSize": "1000",
                  "sortField": "name", "sortDirection": "1",
                  "password": None if password is None else hashlib.sha256(password.encode()).hexdigest()}
        headers = {"Authorization": "Bearer " + extractor.api_token,
                   "X-Website-Token": extractor._generate_website_token("en-US"), "X-BL": "en-US"}
        data = extractor.request_api("/contents/" + content_id, params, headers)
        if data.get("type") == "file":
            if page != 1:
                raise BridgeError("GoFile content changed during discovery")
            return data
        if "children" not in data:
            raise BridgeError("GoFile password or access permission is required")
        if first is None:
            first = dict(data)
            expected = data.get("childrenCount")
            if not isinstance(expected, int) or expected < 0 or expected > 100000:
                raise BridgeError("GoFile did not provide a verifiable directory count")
        if data.get("childrenCount") != expected:
            raise BridgeError("GoFile directory changed during discovery")
        batch = data["children"]
        if not isinstance(batch, dict) or any(k in children for k in batch):
            raise BridgeError("GoFile pagination returned duplicate entries")
        children.update(batch)
        if len(children) == expected:
            first["children"] = children
            return first
        if not batch or len(children) > expected:
            raise BridgeError("GoFile directory listing is incomplete")
    raise BridgeError("GoFile pagination limit exceeded")


def google_manifest(request):
    import gdown
    if importlib.metadata.version("gdown") != "6.4.2":
        raise BridgeError("Unsupported gdown version")
    options = dict(quiet=True, use_cookies=False, timeout=(20, 60), skip_download=True)
    with contextlib.redirect_stdout(sys.stderr):
        if "/folders/" in urllib.parse.urlsplit(request["url"]).path:
            entries = gdown.download_folder(url=request["url"], output=request["output"], **options)
        else:
            entries = [gdown.download(url=request["url"], **options)]
    return [{"id": e.id, "path": relative_path(e.path), "size": None} for e in entries], None


def gofile_manifest(request):
    from gallery_dl import config
    from gallery_dl.extractor.gofile import GofileFolderExtractor
    if importlib.metadata.version("gallery-dl") != "1.32.17":
        raise BridgeError("Unsupported gallery-dl version")
    # Never load config.load(), which could enable browser cookies or executable postprocessors.
    config.clear()
    config.set(("cache",), "file", ":memory:")
    config.set(("extractor",), "proxy-env", False)
    config.set(("extractor",), "timeout", 60)
    config.set(("extractor",), "retries", 2)
    extractor = GofileFolderExtractor.from_url(request["url"])
    if extractor is None:
        raise BridgeError("Unsupported GoFile share URL")
    extractor.initialize()
    extractor._init_options()
    extractor.api_token = extractor._create_account()
    extractor.cookies.set("accountToken", extractor.api_token, domain=".gofile.io")
    entries = []
    visited = set()

    def visit(content_id, prefix, depth):
        if content_id in visited or depth > 32:
            raise BridgeError("GoFile folder recursion limit or cycle")
        visited.add(content_id)
        data = gofile_content(extractor, content_id, request.get("accessCode"))
        if data.get("type") == "file":
            append(data, prefix)
            return
        folder_path = prefix + segment(data["name"]) + "/"
        for child in data["children"].values():
            if child.get("type") == "folder":
                visit(child["id"], folder_path, depth + 1)
            elif child.get("type") == "file":
                append(child, folder_path)
            else:
                raise BridgeError("Unknown GoFile entry type")

    def append(data, prefix):
        size = data.get("size")
        if not isinstance(size, int) or size < 0:
            raise BridgeError("GoFile file size is missing")
        link = data["link"]
        parsed = urllib.parse.urlsplit(link)
        if parsed.scheme != "https" or not parsed.hostname or not parsed.hostname.endswith(".gofile.io") or parsed.username or parsed.password:
            raise BridgeError("Unexpected GoFile download server")
        checksum = data.get("md5")
        if checksum and not re.fullmatch("[a-fA-F0-9]{32}", checksum):
            raise BridgeError("Invalid GoFile checksum")
        entries.append({"id": data["id"], "path": prefix + segment(data["name"]),
                        "size": size, "url": link, "md5": checksum})
        if len(entries) > 100000:
            raise BridgeError("Share has too many files")

    visit(extractor.groups[0], "", 0)
    return entries, extractor.session


def file_hash(path, algorithm):
    digest = hashlib.new(algorithm)
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def sha256(path):
    return file_hash(path, "sha256")


def atomic_json(path, value):
    temp = path.with_suffix(".new")
    with temp.open("w", encoding="utf-8") as target:
        json.dump(value, target)
        target.flush()
        os.fsync(target.fileno())
    os.replace(temp, path)
    if os.name != "nt":
        os.chmod(path, 0o600)


def run(request):
    validate_url(request["provider"], request["url"])
    output = Path(request["output"])
    state = Path(request["state"])
    if not output.is_absolute() or not state.is_absolute():
        raise BridgeError("Download paths must be absolute")
    if any(p.is_symlink() for p in [output, *output.parents, state, *state.parents]):
        raise BridgeError("Symbolic links are not supported in download paths")
    output.mkdir(parents=True, exist_ok=True)
    state.mkdir(parents=True, exist_ok=True)
    if output.is_symlink() or state.is_symlink():
        raise BridgeError("Symbolic links are not supported")
    emit({"type": "progress", "stage": "discovering"})
    # Disable .netrc and ambient proxy credentials, including inside gdown.
    import requests
    original_init = requests.Session.__init__
    def isolated_session(self, *args, **kwargs):
        original_init(self, *args, **kwargs)
        configure_session(self, request.get("proxy"))
    requests.Session.__init__ = isolated_session
    entries, session = google_manifest(request) if request["provider"] == "googleDrive" else gofile_manifest(request)
    if not entries:
        raise BridgeError("The share contains no downloadable files; empty directories are not supported")
    paths = [relative_path(e["path"]) for e in entries]
    if len(entries) > 100000 or len({p.casefold() for p in paths}) != len(paths):
        raise BridgeError("Share contains duplicate or excessive file paths")
    folded_paths = {p.casefold() for p in paths}
    staging_name = ".bakabase-cloud-" + state.name
    for path in paths:
        components = path.split("/")
        if components[0] == staging_name or any("/".join(components[:i]).casefold() in folded_paths for i in range(1, len(components))):
            raise BridgeError("Share contains conflicting file and directory paths")
        beneath(output, path)
    # URLs can expire; stable identity excludes the transport URL and credentials.
    manifest = [{k: e.get(k) for k in ("id", "path", "size", "md5")} for e in entries]
    manifest_path = state / "manifest.json"
    identity = hashlib.sha256(json.dumps([request["provider"], request["url"], request.get("accessCode"), str(output.resolve())]).encode()).hexdigest()
    persisted = {"identity": identity, "files": manifest}
    if manifest_path.exists():
        if json.loads(manifest_path.read_text()) != persisted:
            raise BridgeError("Share contents or destination changed; create a new download operation")
    else:
        atomic_json(manifest_path, persisted)
    receipt_path = state / "completed.json"
    receipts = json.loads(receipt_path.read_text()) if receipt_path.exists() else {}
    staging = output / (".bakabase-cloud-" + state.name)
    if staging.is_symlink():
        raise BridgeError("Symbolic links are not supported")
    staging.mkdir(mode=0o700, exist_ok=True)
    completed = []
    last_progress = 0
    for index, entry in enumerate(entries):
        destination = beneath(output, entry["path"])
        receipt = receipts.get(entry["path"])
        if destination.exists():
            if not destination.is_file() or not receipt or destination.stat().st_size != receipt["size"] or sha256(destination) != receipt["sha256"]:
                raise BridgeError("Destination already exists and is not a verified result of this operation")
            completed.append(str(destination))
            continue
        destination.parent.mkdir(parents=True, exist_ok=True)
        # Partial files stay in a private operation directory; filenames never come from the remote server.
        part = staging / (hashlib.sha256(entry["path"].encode()).hexdigest() + ".part")
        def progress(done, total):
            nonlocal last_progress
            now = time.monotonic()
            if now - last_progress >= .25 or (total is not None and done == total):
                emit({"type": "progress", "stage": "downloading", "percentage": 100 * (index + (done / total if total else 0)) / len(entries), "completedBytes": done, "totalBytes": total})
                last_progress = now
        if request["provider"] == "googleDrive":
            import gdown
            with contextlib.redirect_stdout(sys.stderr):
                result = gdown.download(id=entry["id"], output=str(part), quiet=True, use_cookies=False,
                                        resume=True, timeout=(20, 60), retries=2, progress=progress)
            if result != str(part) or not part.is_file():
                raise BridgeError("gdown did not produce the expected file")
        else:
            # Restart the current file; completed files resume from receipts. Requests checks transfer truncation too.
            with session.get(entry["url"], stream=True, timeout=(20, 60), allow_redirects=False) as response:
                response.raise_for_status()
                if response.is_redirect:
                    raise BridgeError("Unexpected GoFile download redirect")
                written = 0
                with part.open("wb") as target:
                    for chunk in response.iter_content(512 * 1024):
                        target.write(chunk)
                        written += len(chunk)
                        if written > entry["size"]:
                            raise BridgeError("GoFile file exceeds manifest size")
                        progress(written, entry["size"])
            if part.stat().st_size != entry["size"]:
                raise BridgeError("GoFile file is incomplete")
            if entry.get("md5"):
                if file_hash(part, "md5").lower() != entry["md5"].lower():
                    raise BridgeError("GoFile checksum mismatch")
        receipts[entry["path"]] = {"size": part.stat().st_size, "sha256": sha256(part)}
        atomic_json(receipt_path, receipts)
        # Hard-link publication fails if anything already exists; never overwrite the user's file.
        beneath(output, entry["path"])
        try:
            os.link(part, destination)
        except OSError:
            raise BridgeError("Could not publish file atomically without overwriting the destination")
        part.unlink()
        completed.append(str(destination))
        emit({"type": "progress", "stage": "downloading", "percentage": 100 * (index + 1) / len(entries)})
    try:
        staging.rmdir()
    except OSError:
        pass
    emit({"type": "result", "files": completed})


if __name__ == "__main__":
    try:
        run(json.load(sys.stdin))
    except BaseException as error:
        # Third-party exceptions often embed temporary signed URLs or access tokens.
        emit({"type": "error", "message": str(error) if isinstance(error, BridgeError) else "Cloud tool failed; check share access and network, then retry", "kind": type(error).__name__})
        sys.exit(1)
