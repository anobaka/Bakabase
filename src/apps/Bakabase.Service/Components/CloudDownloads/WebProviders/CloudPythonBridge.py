"""Private JSON-lines adapter for gdown 6.4.2 and gallery-dl 1.32.17.

Run only with the dependency's isolated Python. No user config, browser cookies,
postprocessors, shell commands, global package installation or URL fallback.
"""
import contextlib
import errno
import hashlib
import importlib.metadata
import json
import os
from pathlib import Path
import re
import stat
import sys
import tempfile
import time
import urllib.parse


PROTOCOL_OUTPUT = sys.stdout


class BridgeError(Exception):
    pass


def provider_failure(provider, error):
    """Stable, deliberately conservative diagnostics; never forward exception URLs/tokens.

    gdown's generic permission/many-accesses advice is not proof of a quota failure.
    Its inner FileURLRetrievalError retains Google's actual error text; only the
    explicit Google quota phrase maps to quota. gallery-dl exposes typed errors.
    """
    if isinstance(error, BridgeError):
        return str(error)
    current = error
    for _ in range(6):
        name = type(current).__name__
        module = type(current).__module__
        if provider == "googleDrive" and module.startswith("gdown") and name == "FileURLRetrievalError":
            text = " ".join(str(current)[:16384].lower().split())
            if "too many users have viewed or downloaded this file" in text:
                return "GOOGLE_DOWNLOAD_QUOTA_EXCEEDED: Google reports a download quota limit; wait or choose another confirmed source"
            return "GOOGLE_DRIVE_SHARE_UNAVAILABLE_OR_ACCESS_REQUIRED: the share could not be retrieved; verify availability and permission on its official page"
        if provider == "googleDrive" and module.startswith("gdown") and name == "DownloadError":
            # gdown 6.4.2's folder discovery embeds the HTTP status in DownloadError
            # without retaining a response/cause. Match only its exact prefix; its
            # generic 'many accesses' advice is not evidence of a quota failure.
            if re.match(r"Failed to retrieve folder contents for folder ID: [\w-]+ \(status code (?:401|403|404|410)\)\.", str(current)[:2048]):
                return "GOOGLE_DRIVE_SHARE_UNAVAILABLE_OR_ACCESS_REQUIRED: the folder could not be retrieved; verify availability and permission on its official page"
        if provider == "gofile" and module.startswith("gallery_dl"):
            if name == "NotFoundError":
                # gallery-dl 1.32.17's GoFile request_api raises this only for
                # the provider JSON status error-notFound. Its HTTP request path
                # uses notfound=None; generic HTTP404 remains a different code.
                return "GOFILE_CONTENT_NOT_FOUND: GoFile explicitly reported this content missing or no longer shared"
            if name == "AuthorizationError":
                return "GOFILE_ACCESS_REQUIRED: GoFile requires additional access information; verify the official share page"
        if module.startswith(("requests", "urllib3")) and name in ("Timeout", "ConnectTimeout", "ReadTimeout", "ConnectionError", "SSLError", "ProxyError", "NewConnectionError", "MaxRetryError", "ChunkedEncodingError", "ProtocolError"):
            return "CLOUD_NETWORK_ERROR: the source connection failed; check this source's network mode and try again"
        status = getattr(current, "status", None)
        response = getattr(current, "response", None)
        status = status or getattr(response, "status_code", None)
        if status == 429:
            return "CLOUD_RATE_LIMITED: the source rate limit was reached; wait before retrying"
        if status in (401, 403):
            return "CLOUD_ACCESS_REQUIRED: the source denied access; check permission on its official page"
        if provider == "gofile" and status in (404, 410):
            return "GOFILE_SHARE_NOT_FOUND: GoFile could not find this content; verify the share or provide another source"
        if provider == "googleDrive" and status in (404, 410):
            return "GOOGLE_DRIVE_SHARE_UNAVAILABLE_OR_ACCESS_REQUIRED: the share could not be retrieved; verify availability and permission on its official page"
        next_error = current.__cause__ or current.__context__
        if next_error is None or next_error is current:
            break
        current = next_error
    return "CLOUD_TOOL_FAILED: the tool did not complete; verify share access and this source's network mode"


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
    elif provider == "pixeldrain":
        # Official aliases share the same IDs; the adapter keeps using the canonical API.
        # https://pixeldrain.com/about, checked 2026-10-25.
        if parsed.hostname not in ("pixeldrain.com", "www.pixeldrain.com", "pixeldrain.net", "www.pixeldrain.net", "pixeldra.in", "www.pixeldra.in") or not re.fullmatch(r"/(?:[ul]/[A-Za-z0-9_-]+|d/[A-Za-z0-9_-]+(?:/[^/]+)*)/?", parsed.path):
            raise BridgeError("Unsupported PixelDrain share URL")
        if parsed.path.startswith("/d/"):
            relative_path(urllib.parse.unquote(parsed.path[3:].rstrip("/")))
        if parsed.fragment and (not parsed.path.startswith("/l/") or not re.fullmatch(r"item=\d+", parsed.fragment)):
            raise BridgeError("Unsupported PixelDrain share fragment")
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


def pixeldrain_failure(status=0, value=""):
    value = value.lower() if isinstance(value, str) else ""
    if "captcha" in value or "recaptcha" in value:
        return BridgeError("PIXELDRAIN_VERIFICATION_REQUIRED: complete verification on the official share page or use another source")
    if value == "transfer_limit_exceeded":
        return BridgeError("PIXELDRAIN_TRANSFER_QUOTA_EXCEEDED: the free Pixeldrain transfer quota was exceeded; wait for the quota to reset or use a supported account")
    if value == "download_limit_exceeded":
        return BridgeError("PIXELDRAIN_DOWNLOAD_QUOTA_EXCEEDED: the free Pixeldrain download quota was exceeded; wait for the quota to reset or use a supported account")
    if status == 429 or value == "max_concurrent_downloads":
        return BridgeError("PIXELDRAIN_RATE_LIMITED: the current download or transfer limit was reached; wait or use the official share page")
    if status in (404, 410, 451) or value in ("not_found", "path_not_found", "unavailable_for_legal_reasons"):
        return BridgeError("PIXELDRAIN_SHARE_UNAVAILABLE: this file or folder is no longer publicly available")
    if status in (401, 403) or value in ("authentication_required", "permission_denied", "hotlink_detected", "access_required",
                                         "password_required", "file_password_required", "invalid_password"):
        return BridgeError("PIXELDRAIN_ACCESS_REQUIRED: this share requires permission, a supported account or access through its official page")
    return BridgeError("PIXELDRAIN_NETWORK_ERROR: PixelDrain did not return a usable public response; check the source proxy and retry")


def pixeldrain_manifest(request):
    """Use the pinned extractor's isolated HTTP session, with strict whole-list validation.

    gallery-dl's normal job skips unavailable files and changes filenames. A cloud operation
    instead keeps original names and rejects an incomplete list, rate limit or permission gap.
    Protocol checked against pixeldrain.com/api and gallery-dl v1.32.17, 2026-10-20.
    """
    from gallery_dl import config
    from gallery_dl.extractor.pixeldrain import PixeldrainFileExtractor
    if importlib.metadata.version("gallery-dl") != "1.32.17":
        raise BridgeError("Unsupported gallery-dl version")
    # Saved post metadata can include an archive password in the access-code field.
    # Always discover the selected files anonymously and honour their actual permissions.
    config.clear()
    config.set(("cache",), "file", ":memory:")
    config.set(("extractor",), "proxy-env", False)
    config.set(("extractor",), "timeout", 60)
    config.set(("extractor",), "retries", 2)
    extractor = PixeldrainFileExtractor.from_url("https://pixeldrain.com/u/fixture")
    extractor.initialize()
    extractor._init_options()
    parsed = urllib.parse.urlsplit(request["url"])
    parts = parsed.path.strip("/").split("/")
    entries = []
    visited = set()

    def get(path):
        try:
            data = extractor.request_json("https://pixeldrain.com/api/" + path, allow_redirects=False)
        except Exception as error:
            # gallery_dl.exception.HttpError carries response/status and embeds the signed URL
            # in str(error). Never relay that message or arbitrary upstream response text.
            status = getattr(error, "status", None)
            if status is None:
                raise
            response = getattr(error, "response", None)
            value = ""
            if response is not None:
                try:
                    body = response.json()
                    value = body.get("value", "") if isinstance(body, dict) else ""
                except (ValueError, TypeError):
                    pass
            raise pixeldrain_failure(status, value) from None
        if not isinstance(data, dict) or data.get("success") is False:
            raise pixeldrain_failure(value=data.get("value", "") if isinstance(data, dict) else "")
        return data

    def append(data, prefix, filesystem=False):
        if data.get("availability"):
            raise pixeldrain_failure(value=data["availability"])
        if data.get("abuse_type"):
            raise pixeldrain_failure(value="unavailable_for_legal_reasons")
        if data.get("can_download") is False:
            raise pixeldrain_failure(value="access_required")
        name = segment(data.get("name"))
        size = data.get("file_size" if filesystem else "size")
        if not isinstance(size, int) or isinstance(size, bool) or size < 0:
            raise BridgeError("PixelDrain original file size is missing")
        checksum = data.get("sha256_sum" if filesystem else "hash_sha256")
        if checksum is not None and not re.fullmatch(r"[a-fA-F0-9]{64}", checksum):
            raise BridgeError("Invalid PixelDrain original checksum")
        if filesystem:
            remote = remote_path(data.get("path"))
            identity = remote
            link = "https://pixeldrain.com/api/filesystem" + urllib.parse.quote(remote, safe="/") + "?attach"
        else:
            identity = data.get("id")
            if not isinstance(identity, str) or not re.fullmatch(r"[A-Za-z0-9_-]+", identity):
                raise BridgeError("PixelDrain original file ID is missing")
            link = "https://pixeldrain.com/api/file/" + identity + "?download"
        entries.append({"id": identity, "path": prefix + name, "size": size, "url": link,
                        "sha256": checksum})
        if len(entries) > 100000:
            raise BridgeError("PixelDrain share exceeds the file limit")

    def remote_path(path):
        if not isinstance(path, str) or not path.startswith("/"):
            raise BridgeError("PixelDrain returned an invalid filesystem path")
        return "/" if path == "/" else "/" + relative_path(path[1:])

    def visit(path, prefix, depth):
        if depth > 32 or path in visited or len(visited) >= 100000:
            raise BridgeError("PixelDrain folder cycle or recursion limit")
        visited.add(path)
        stat = get("filesystem" + urllib.parse.quote(path, safe="/") + "?stat")
        chain, index = stat.get("path"), stat.get("base_index")
        if not isinstance(chain, list) or not isinstance(index, int) or not 0 <= index < len(chain):
            raise BridgeError("PixelDrain filesystem metadata is incomplete")
        node = chain[index]
        if stat.get("permissions", {}).get("read") is False:
            raise pixeldrain_failure(value="access_required")
        if node.get("type") == "file":
            append(node, prefix, True)
            return
        children = stat.get("children")
        if node.get("type") != "dir" or not isinstance(children, list):
            raise BridgeError("PixelDrain folder listing is incomplete")
        parent = remote_path(node.get("path"))
        seen = set()
        for child in children:
            name = segment(child.get("name"))
            child_path = remote_path(child.get("path"))
            if child_path != parent.rstrip("/") + "/" + name or child_path in seen:
                raise BridgeError("PixelDrain returned duplicate or non-child filesystem entries")
            seen.add(child_path)
            if child.get("type") == "dir":
                visit(child_path, prefix + name + "/", depth + 1)
            elif child.get("type") == "file":
                append(child, prefix, True)
            else:
                raise BridgeError("PixelDrain returned an unsupported filesystem entry")

    if parts[0] == "u":
        file = get("file/" + parts[1] + "/info")
        if file.get("id") != parts[1]:
            raise BridgeError("PixelDrain returned a different original file")
        append(file, "")
    elif parts[0] == "l":
        album = get("list/" + parts[1])
        files, count = album.get("files"), album.get("file_count")
        if not isinstance(files, list) or not isinstance(count, int) or isinstance(count, bool) or count != len(files) or count > 100000:
            raise BridgeError("PixelDrain album listing is incomplete")
        if parsed.fragment:
            index = int(parsed.fragment[5:])
            if index >= len(files):
                raise BridgeError("PixelDrain album item is unavailable")
            files = files[index:index + 1]
        seen = set()
        for file in files:
            if file.get("id") in seen:
                raise BridgeError("PixelDrain album returned duplicate entries")
            seen.add(file.get("id"))
            append(file, "")
    else:
        visit("/" + relative_path(urllib.parse.unquote("/".join(parts[1:]))), "", 0)
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


def sync_directory(path):
    if os.name == "nt":
        return
    fd = os.open(path, os.O_RDONLY | os.O_DIRECTORY | getattr(os, "O_NOFOLLOW", 0))
    try:
        try:
            os.fsync(fd)
        except OSError as error:
            if error.errno not in (errno.EINVAL, errno.ENOTSUP):
                raise
    finally:
        os.close(fd)


def present(path):
    try:
        return path.lstat()
    except FileNotFoundError:
        return None


def file_identity(info):
    return [info.st_dev, info.st_ino, info.st_size, info.st_mtime_ns]


def content_identity(info):
    return [*file_identity(info), info.st_ctime_ns]


def download_identity(request, output):
    fields = [request["provider"], request["url"], request.get("accessCode"), str(output.resolve())]
    return hashlib.sha256(json.dumps(fields).encode()).hexdigest()


def reuse_error():
    return BridgeError("DOWNLOAD_DESTINATION_REUSE_SOURCE_CHANGED: The retained download no longer matches its verified placement")


def reuse_directory(value):
    path = Path(value)
    if not path.is_absolute() or any(p.is_symlink() for p in (path, *path.parents)):
        raise reuse_error()
    return path


def reuse_file_identity(path):
    info = present(path)
    if info is None:
        return None
    if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1:
        raise reuse_error()
    return content_identity(info)


def read_reuse_record(path, limit=64 * 1024 * 1024):
    identity = reuse_file_identity(path)
    if identity is None:
        return None
    if not 0 < identity[2] <= limit:
        raise reuse_error()
    flags = os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_NONBLOCK", 0)
    with os.fdopen(os.open(path, flags), "rb") as stream:
        if content_identity(os.fstat(stream.fileno())) != identity:
            raise reuse_error()
        try:
            value = json.loads(stream.read(limit + 1))
        except (UnicodeError, ValueError):
            raise reuse_error() from None
        if content_identity(os.fstat(stream.fileno())) != identity or reuse_file_identity(path) != identity:
            raise reuse_error()
        return value


def pixeldrain_frozen_manifest(request, output, state):
    recorded = read_reuse_record(state / "manifest.json")
    identity = download_identity(request, output)
    if (not isinstance(recorded, dict) or recorded.get("identity") != identity or
            not isinstance(recorded.get("files"), list) or not 0 < len(recorded["files"]) <= 100000):
        raise reuse_error()
    names = set()
    for entry in recorded["files"]:
        if not isinstance(entry, dict) or set(entry) != {"id", "path", "size", "md5", "sha256"}:
            raise reuse_error()
        name, size, checksum = entry["path"], entry["size"], entry["sha256"]
        if (not isinstance(name, str) or relative_path(name) != name or name.casefold() in names or
                type(size) is not int or size < 0 or not isinstance(checksum, str) or
                not re.fullmatch("[a-fA-F0-9]{64}", checksum)):
            raise reuse_error()
        names.add(name.casefold())
    return identity, recorded["files"]


def pixeldrain_reuse_inventory(request):
    if request["provider"] != "pixeldrain":
        raise reuse_error()
    validate_url(request["provider"], request["url"])
    output, state = reuse_directory(request["output"]), reuse_directory(request["state"])
    staging = reuse_directory(output / (".bakabase-cloud-" + state.name))
    identity, manifest = pixeldrain_frozen_manifest(request, output, state)
    if any(p.name.endswith((".repair.json", ".rejected.json", ".new")) for p in state.iterdir()):
        raise reuse_error()
    receipts = read_reuse_record(state / "completed.json")
    if receipts is None:
        receipts = {}
    if not isinstance(receipts, dict):
        raise reuse_error()
    candidates = {}
    for entry in manifest:
        name, size, checksum = entry["path"], entry["size"], entry["sha256"]
        key = hashlib.sha256(name.encode()).hexdigest()
        final = beneath(output, name)
        part = staging / (key + ".part")
        complete, partial = reuse_file_identity(final), reuse_file_identity(part)
        if complete is not None:
            if partial is not None or complete[2] != size or receipts.get(name) != {"size": size, "sha256": checksum.lower()}:
                raise reuse_error()
            candidates[name] = ("complete", complete, final)
        elif partial is not None:
            if not 0 <= partial[2] <= size:
                raise reuse_error()
            # The destination’s ordinary Range and whole-file checksum checks still
            # run before any copied prefix counts as a completed download.
            if partial[2]:
                candidates[name] = ("partial", partial, part)
        elif name in receipts:
            raise reuse_error()
    if not candidates:
        raise reuse_error()
    evidence = [identity, manifest, [(k, v[0], v[1]) for k, v in sorted(candidates.items())]]
    fingerprint = hashlib.sha256(json.dumps(evidence, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    estimate = {"fileCount": len(manifest), "totalBytes": sum(e["size"] for e in manifest), "candidateFileCount": len(candidates),
                "candidateBytes": sum(v[1][2] for v in candidates.values()), "inventoryFingerprint": fingerprint}
    return manifest, candidates, estimate


def pixeldrain_reuse_marker(request, identity):
    fingerprint = request["reuseSource"]["inventoryFingerprint"]
    if request["provider"] != "pixeldrain" or not re.fullmatch("[0-9a-f]{64}", fingerprint):
        raise reuse_error()
    return {"identity": identity, "inventoryFingerprint": fingerprint}


def pixeldrain_reuse_budget(request):
    validate_url(request["provider"], request["url"])
    output, state = reuse_directory(request["output"]), reuse_directory(request["state"])
    prepared = read_reuse_record(state / "reuse-prepared.json", 4096)
    if prepared is None:
        return None
    identity, manifest = pixeldrain_frozen_manifest(request, output, state)
    expected = pixeldrain_reuse_marker(request, identity)
    if prepared != expected or read_reuse_record(state / "reuse-intent.json", 4096) != expected:
        raise reuse_error()
    receipts = read_reuse_record(state / "completed.json")
    if receipts is None:
        receipts = {}
    if not isinstance(receipts, dict):
        raise reuse_error()
    # Count every incomplete member at full length. An explicit checksum retry
    # can retain the failed partial while downloading a replacement on this volume.
    needed = 0
    for entry in manifest:
        name, size = entry["path"], entry["size"]
        installed = reuse_file_identity(beneath(output, name))
        if (installed is None or installed[2] != size or
                receipts.get(name) != {"size": size, "sha256": entry["sha256"].lower()}):
            needed += size
    return needed


def copy_reuse_part(source, identity, part, checksum):
    read_flags = os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_NONBLOCK", 0)
    write_flags = os.O_RDWR | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_NONBLOCK", 0)
    flags = write_flags if reuse_file_identity(part) is not None else write_flags | os.O_CREAT | os.O_EXCL
    with os.fdopen(os.open(source, read_flags), "rb") as old:
        if content_identity(os.fstat(old.fileno())) != identity:
            raise reuse_error()
        with os.fdopen(os.open(part, flags, 0o600), "r+b") as new:
            info = os.fstat(new.fileno())
            if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or info.st_size > identity[2]:
                raise reuse_error()
            original_length = info.st_size
            digest = hashlib.sha256() if checksum else None
            remaining = identity[2]
            reported = time.monotonic()
            while remaining:
                chunk = old.read(min(remaining, 1024 * 1024))
                if not chunk:
                    raise reuse_error()
                if digest:
                    digest.update(chunk)
                # A prior process may have stopped at any byte. Verify the already
                # copied prefix before appending and preserve it on mismatch.
                already_copied = min(max(original_length - new.tell(), 0), len(chunk))
                if new.read(already_copied) != chunk[:already_copied]:
                    raise reuse_error()
                if already_copied < len(chunk):
                    new.write(chunk[already_copied:])
                remaining -= len(chunk)
                now = time.monotonic()
                if now - reported >= 2:
                    emit({"type": "progress", "stage": "reusing"})
                    reported = now
            new.flush()
            if (content_identity(os.fstat(old.fileno())) != identity or reuse_file_identity(source) != identity or
                    reuse_file_identity(part) != content_identity(os.fstat(new.fileno())) or
                    digest is not None and digest.hexdigest() != checksum.lower()):
                raise reuse_error()
            os.fsync(new.fileno())


def seed_pixeldrain_reuse(request, output, state, staging, manifest, identity):
    source = request.get("reuseSource")
    if source is None:
        return
    expected = pixeldrain_reuse_marker(request, identity)
    marker = state / "reuse-prepared.json"
    intent = state / "reuse-intent.json"
    prepared = read_reuse_record(marker, 4096)
    previous = read_reuse_record(intent, 4096)
    if prepared is not None:
        if prepared != expected or previous != expected:
            raise reuse_error()
        return
    if previous not in (None, expected) or source.get("disableSourceRead"):
        raise reuse_error()
    old_output, old_state = reuse_directory(source["output"]), reuse_directory(source["state"])
    if any(a.resolve().is_relative_to(b.resolve()) or b.resolve().is_relative_to(a.resolve())
           for a, b in ((old_output, output), (old_state, state))):
        raise reuse_error()
    old_manifest, candidates, estimate = pixeldrain_reuse_inventory(dict(request, output=str(old_output), state=str(old_state)))
    if estimate["inventoryFingerprint"] != expected["inventoryFingerprint"] or old_manifest != manifest:
        raise reuse_error()
    if previous is None:
        if any(staging.iterdir()) or any(beneath(output, e["path"]).exists() for e in manifest):
            raise reuse_error()
        atomic_json(intent, expected)
        sync_directory(state)
    members = {e["path"]: e for e in manifest}
    for name, (kind, stamp, path) in candidates.items():
        key = hashlib.sha256(name.encode()).hexdigest()
        copy_reuse_part(path, stamp, staging / (key + ".part"), members[name]["sha256"] if kind == "complete" else None)
        emit({"type": "progress", "stage": "reusing"})
    sync_directory(staging)
    atomic_json(marker, expected)
    sync_directory(state)


def rename_noreplace(source, destination):
    import ctypes

    rename = getattr(ctypes.CDLL(None, use_errno=True), 'renameat2', None)
    if rename is None:
        raise OSError(errno.ENOSYS, 'renameat2 is unavailable')
    rename.argtypes = [ctypes.c_int, ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_uint]
    rename.restype = ctypes.c_int
    at_fdcwd = -100
    rename_noreplace_flag = 1
    if rename(at_fdcwd, os.fsencode(source), at_fdcwd, os.fsencode(destination), rename_noreplace_flag) != 0:
        code = ctypes.get_errno()
        raise OSError(code, os.strerror(code))


def publish_verified_part(part, destination, verified, state):
    try:
        os.link(part, destination)
        return False
    except OSError as error:
        if sys.platform != 'linux' or error.errno not in (errno.ENOTSUP, errno.ENOSYS):
            raise

    # A move consumes the staging name. Persist the already written completion
    # receipt and staged bytes first so replay can verify the destination alone.
    sync_directory(state)
    # Network filesystems can reject a flush on a read-only handle.
    with os.fdopen(os.open(part, os.O_RDWR | os.O_NOFOLLOW | os.O_NONBLOCK), 'r+b') as source:
        info = os.fstat(source.fileno())
        if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or file_identity(info) != file_identity(verified):
            raise BridgeError('The verified partial changed before publication')
        os.fsync(source.fileno())
        if file_identity(part.lstat()) != file_identity(verified):
            raise BridgeError('The verified partial changed before publication')
        rename_noreplace(part, destination)
    sync_directory(destination.parent)
    sync_directory(part.parent)
    return True


def rejected_partial_error():
    return BridgeError("CLOUD_PARTIAL_IDENTITY_UNVERIFIED: The rejected partial does not match this operation")


def checksum_retry_key(value):
    return value if isinstance(value, str) and re.fullmatch("[0-9]{1,19}", value) else None


def rejected_partial_state(state, part, identity, entry):
    record = state / (part.stem + ".rejected.json")
    rejected = part.with_suffix(".rejected")
    expected = {"version": 2, "operation": identity, "member": part.stem,
                "size": entry["size"], "sha256": entry.get("sha256")}
    info = present(record)
    if info is None:
        if present(rejected) is not None:
            raise rejected_partial_error()
        return record, rejected, expected, None, None
    try:
        if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or not 0 < info.st_size <= 4096:
            raise ValueError()
        with os.fdopen(os.open(record, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0)), encoding="utf-8") as source:
            if file_identity(os.fstat(source.fileno())) != file_identity(info):
                raise ValueError()
            saved = json.loads(source.read(4096))
        original = saved.pop("original")
        key = checksum_retry_key(saved.pop("key"))
        version = saved["version"]
        if type(version) is not int or version not in (1, 2):
            raise ValueError()
        expected["version"] = version
        if key is None or saved != expected or not isinstance(original, list) or len(original) != 4 or any(
                type(value) is not int for value in original) or not 0 < original[2] <= entry["size"]:
            raise ValueError()
    except (TypeError, AttributeError, KeyError, ValueError):
        raise rejected_partial_error() from None
    return record, rejected, expected, original, key


def save_rejected_partial(record, expected, original, key):
    # The provider holds its operation lock. A private temporary file keeps a
    # process interruption before publishing intent from leaving a partial record.
    descriptor, name = tempfile.mkstemp(dir=record.parent, prefix=".rejected-")
    temporary = Path(name)
    try:
        with os.fdopen(descriptor, "w", encoding="utf-8") as target:
            json.dump(dict(expected, original=original, key=key), target)
            target.flush()
            os.fsync(target.fileno())
        if present(record) is not None:
            raise rejected_partial_error()
        os.replace(temporary, record)
        sync_directory(record.parent)
    finally:
        temporary.unlink(missing_ok=True)


def rejected_original(rejected, version):
    if version == 1:
        return rejected
    info = present(rejected)
    if info is not None and (not stat.S_ISDIR(info.st_mode) or any(child.name != "original" for child in rejected.iterdir())):
        raise rejected_partial_error()
    return rejected / "original"


def isolate_rejected_partial(state, part, identity, entry, repair_record, retry_key):
    record, rejected, expected, original, _ = rejected_partial_state(state, part, identity, entry)
    current = present(part)
    if original is None:
        if (retry_key is None or current is None or current.st_size == 0 or present(repair_record) is not None or
                present(repair_record.with_suffix(".new")) is not None):
            return
        checksum = entry.get("sha256")
        if (not isinstance(checksum, str) or not re.fullmatch("[a-fA-F0-9]{64}", checksum) or
                not stat.S_ISREG(current.st_mode) or current.st_nlink != 1 or not 0 < current.st_size <= entry["size"]):
            raise rejected_partial_error()
        original = file_identity(current)
        if current.st_size == entry["size"] and sha256(part) == checksum.lower():
            if file_identity(part.lstat()) != original:
                raise rejected_partial_error()
            return
        save_rejected_partial(record, expected, original, retry_key)

    version = expected["version"]
    original_path = rejected_original(rejected, version)
    old = present(original_path)
    if old is None:
        if current is None or not stat.S_ISREG(current.st_mode) or current.st_nlink != 1 or file_identity(current) != original:
            raise rejected_partial_error()
        if version == 1:
            os.link(part, original_path, follow_symlinks=False)
        else:
            if present(rejected) is None:
                rejected.mkdir(mode=0o700)
            if present(rejected_original(rejected, version)) is not None:
                raise rejected_partial_error()
            # A rename into our exclusive private directory does not ask the
            # share to unlink a second name for the original file.
            os.rename(part, original_path)
            sync_directory(rejected)
        sync_directory(part.parent)
        old = original_path.lstat()
    if not stat.S_ISREG(old.st_mode) or file_identity(old)[:3] != original[:3]:
        raise rejected_partial_error()
    current = present(part)
    if current is not None and file_identity(current)[:2] == original[:2]:
        # Only the legacy method could leave two names for the original inode.
        if (version != 1 or not stat.S_ISREG(current.st_mode) or file_identity(current) != original or
                file_identity(old) != original or current.st_nlink != 2 or old.st_nlink != 2):
            raise rejected_partial_error()
        part.unlink()
        sync_directory(part.parent)


def release_rejected_partial(state, part, identity, entry, destination, verified):
    _, rejected, expected, original, _ = rejected_partial_state(state, part, identity, entry)
    if original is None:
        return False
    with os.fdopen(os.open(destination, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0)), "rb") as output:
        if file_identity(os.fstat(output.fileno())) != verified or file_identity(destination.lstat()) != verified:
            raise rejected_partial_error()
        if os.name != "nt":
            os.fsync(output.fileno())
    sync_directory(destination.parent)
    pending = present(part)
    if pending is not None:
        if not stat.S_ISREG(pending.st_mode) or file_identity(pending) != verified or pending.st_nlink != 2:
            return True
        part.unlink()
        sync_directory(part.parent)
    # A share's recycle bin can retain a name and round timestamps after unlink.
    # The final file has its own content proof; ambiguous rejected bytes can stay.
    version = expected["version"]
    try:
        original_path = rejected_original(rejected, version)
    except BridgeError:
        return True
    old = present(original_path)
    if old is not None:
        if not stat.S_ISREG(old.st_mode) or old.st_nlink != 1 or file_identity(old) != original:
            return True
        original_path.unlink()
    if version == 2 and present(rejected) is not None:
        rejected.rmdir()
    sync_directory(part.parent)
    # Keep the generation's intent after this member publishes. A process
    # restart can still carry the previous failure while another member waits.
    return True


def used_checksum_retry_keys(state, identity, members):
    keys = set()
    for record in state.glob("*.rejected.json"):
        member = members.get(record.name.removesuffix(".rejected.json"))
        if member is None:
            raise rejected_partial_error()
        entry, part = member
        _, _, _, original, key = rejected_partial_state(state, part, identity, entry)
        if original is None or key in keys:
            raise rejected_partial_error()
        keys.add(key)
    return keys


# Sparse partials left by interrupted storage writes sometimes contain long zero runs.
# These are only candidates: an official bounded range and the whole SHA must both agree.
MIN_ZERO_RUN = 64 * 1024
ZERO_RUN = re.compile(b'\x00{16,}')
MAX_REPAIR_INTERVALS = 16
MAX_REPAIR_BYTES = 32 * 1024 * 1024
MAX_PREFIX_SCAN_BYTES = 16 * 1024 ** 3


def zero_ranges(file, length):
    if length < MIN_ZERO_RUN:
        return []
    ranges = []
    total = 0
    def record(start, count):
        nonlocal total
        if count >= MIN_ZERO_RUN:
            total += count
            ranges.append((start, count))
        return total <= MAX_REPAIR_BYTES and len(ranges) <= MAX_REPAIR_INTERVALS

    file.seek(0)
    offset = trailing = 0
    limit = min(length, MAX_PREFIX_SCAN_BYTES)
    while offset < limit:
        data = file.read(min(1024 * 1024, limit - offset))
        if not data:
            raise BridgeError('CLOUD_PARTIAL_IDENTITY_UNVERIFIED: The partial file changed while being read')
        leading = len(data) - len(data.lstrip(b'\x00'))
        if leading == len(data):
            trailing += leading
            if total + trailing > MAX_REPAIR_BYTES:
                return []
        else:
            if not record(offset - trailing, trailing + leading):
                return []
            trailing = len(data) - len(data.rstrip(b'\x00'))
            for match in ZERO_RUN.finditer(data, leading, len(data) - trailing):
                if not record(offset + match.start(), match.end() - match.start()):
                    return []
        offset += len(data)
    return ranges if record(offset - trailing, trailing) else []


@contextlib.contextmanager
def original_response(session, entry, provider, start, end, ranged, open_ended=False):
    headers = {'Accept-Encoding': 'identity'}
    if ranged:
        headers['Range'] = 'bytes=%d-%s' % (start, '' if open_ended else end)
    with session.get(entry['url'], headers=headers, stream=True, timeout=(20, 60), allow_redirects=False) as response:
        status = getattr(response, 'status_code', 200)
        if ranged and status == 416:
            raise BridgeError('CLOUD_RANGE_UNCONFIRMED: The source rejected the requested byte range. The existing partial file was retained.')
        if provider == 'pixeldrain' and status >= 400:
            try:
                body = response.json()
                value = body.get('value', '') if isinstance(body, dict) else ''
            except (ValueError, TypeError):
                value = ''
            raise pixeldrain_failure(status, value)
        response.raise_for_status()
        metadata = getattr(response, 'headers', {})
        span = metadata.get('Content-Range')
        if ranged:
            match = re.fullmatch(r'bytes ([0-9]{1,20})-([0-9]{1,20})/([0-9]{1,20})', span or '', re.I)
            if status != 206 or not match or tuple(map(int, match.groups())) != (start, end, entry['size']):
                raise BridgeError('CLOUD_RANGE_UNCONFIRMED: The source did not confirm the requested byte range. The existing partial file was retained.')
        elif status != 200 or span:
            raise BridgeError('Unexpected public-share response to the original file request')
        if metadata.get('Content-Encoding', 'identity').lower() != 'identity':
            raise BridgeError('CLOUD_RANGE_UNCONFIRMED: The source returned encoded bytes instead of the original representation')
        length = metadata.get('Content-Length')
        if length is not None and (not re.fullmatch(r'[0-9]{1,20}', length) or int(length) != end - start + 1):
            raise BridgeError('CLOUD_RANGE_UNCONFIRMED: The original response length does not match the file manifest')
        yield response


def repair_ranges(file, prefix, entry, record):
    if not record.exists() and not record.is_symlink():
        return zero_ranges(file, prefix), False
    message = 'CLOUD_PARTIAL_IDENTITY_UNVERIFIED: The prior range repair does not match this original file'
    if record.is_symlink() or not record.is_file() or record.stat().st_size > 8192:
        raise BridgeError(message)
    try:
        data = json.loads(record.read_text())
        ranges = data['ranges']
        if data['size'] != entry['size'] or data['sha256'] != entry['sha256'] or prefix != entry['size'] or not 0 < len(ranges) <= MAX_REPAIR_INTERVALS:
            raise ValueError()
        end = total = 0
        for start, length in ranges:
            if type(start) is not int or type(length) is not int or length <= 0 or start < end or start + length > prefix:
                raise ValueError()
            total += length
            end = start + length
        if total > MAX_REPAIR_BYTES:
            raise ValueError()
    except (ValueError, TypeError, KeyError):
        raise BridgeError(message) from None
    return ranges, True


def read_repair_patches(session, entry, file, ranges, continuing_commit):
    patches = []
    for start, length in ranges:
        with original_response(session, entry, 'pixeldrain', start, start + length - 1, True) as response:
            remote = bytearray()
            for block in response.iter_content(512 * 1024):
                if len(remote) + len(block) > length:
                    raise BridgeError('CLOUD_RANGE_UNCONFIRMED: Repair response exceeded the requested byte range')
                remote.extend(block)
            if len(remote) != length:
                raise BridgeError('CLOUD_RANGE_UNCONFIRMED: Repair response did not cover the requested byte range')
        file.seek(start)
        original = file.read(length)
        if original == remote:
            continue
        # A stopped commit can contain a mix of the original zeros and verified bytes.
        if len(original) != length or (original.strip(b'\x00') and
                (not continuing_commit or any(a and a != b for a, b in zip(original, remote)))):
            raise BridgeError('CLOUD_PARTIAL_IDENTITY_UNVERIFIED: The candidate repair range changed locally')
        patches.append((start, remote))
    return patches


def overlay_sha256(file, patches, length):
    digest = hashlib.sha256()
    file.seek(0)
    for start, replacement in [*patches, (length, b'')]:
        while file.tell() < start:
            block = file.read(min(start - file.tell(), 1024 * 1024))
            if not block:
                raise BridgeError('CLOUD_PARTIAL_IDENTITY_UNVERIFIED: The partial file changed during verification')
            digest.update(block)
        digest.update(replacement)
        file.seek(start + len(replacement))
    return digest.hexdigest()


def http_original(session, entry, part, provider, progress, repair_record=None):
    # A resumed body is publishable only after the provider's checksum validates the entire file.
    # operation/URL/destination/manifest have already been matched by run().
    expected_size = entry["size"]
    if part.is_symlink():
        raise BridgeError("Symbolic links are not supported in partial files")
    flags = os.O_RDWR | os.O_CREAT | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_NONBLOCK", 0)
    with os.fdopen(os.open(part, flags, 0o600), "r+b") as target:
        info = os.fstat(target.fileno())
        if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1:
            raise BridgeError("The partial file is not exclusive to this download")
        # GoFile has no strong content digest. Its existing full-file retry remains separate.
        offset = info.st_size if provider == "pixeldrain" else 0
        checksum = entry.get("sha256")
        if offset and (offset > expected_size or not checksum):
            raise BridgeError("CLOUD_PARTIAL_IDENTITY_UNVERIFIED: Cannot verify the existing partial file against the original. Its bytes were retained.")
        patches = []
        record = repair_record or part.with_suffix('.repair.json')
        already_valid = offset and offset == expected_size and sha256(part) == checksum.lower()
        journal_present = provider == 'pixeldrain' and (record.exists() or record.is_symlink())
        if journal_present and not offset:
            raise BridgeError('CLOUD_PARTIAL_IDENTITY_UNVERIFIED: The previously verified repair has no original partial file')
        if offset and (not already_valid or journal_present):
            ranges, continuing_commit = repair_ranges(target, offset, entry, record)
            if not already_valid:
                patches = read_repair_patches(session, entry, target, ranges, continuing_commit)
        if not offset or offset < expected_size:
            with original_response(session, entry, provider, offset, expected_size - 1, bool(offset), open_ended=True) as response:
                target.seek(offset)
                if not offset:
                    target.truncate(0)
                if offset:
                    progress(offset, expected_size)
                for chunk in response.iter_content(512 * 1024):
                    if target.tell() + len(chunk) > expected_size:
                        target.truncate(offset)
                        raise BridgeError("CLOUD_RANGE_UNCONFIRMED: Original file exceeds manifest size")
                    target.write(chunk)
                    progress(target.tell(), expected_size)
                if target.tell() != expected_size:
                    raise BridgeError("Original file is incomplete")
        target.flush()
        if already_valid:
            actual_hash = checksum.lower()
        elif patches:
            actual_hash = overlay_sha256(target, patches, expected_size)
        else:
            actual_hash = sha256(part)
        if checksum and actual_hash.lower() != checksum.lower():
            target.truncate(offset)
            raise BridgeError("CLOUD_DOWNLOAD_CHECKSUM_MISMATCH: Original file SHA256 mismatch")
        if patches:
            # All proposed changes are authorized together by the original whole-file digest.
            # Persist only the intervals before the first in-place write, so a terminated
            # process can finish the same commit without retrieving the suffix again.
            os.fsync(target.fileno())
            temporary = record.with_suffix('.new')
            if temporary.is_symlink() or (temporary.exists() and (not temporary.is_file() or temporary.stat().st_nlink != 1)):
                raise BridgeError('CLOUD_PARTIAL_IDENTITY_UNVERIFIED: The range repair journal is not exclusive')
            atomic_json(record, {'size': expected_size, 'sha256': checksum, 'ranges': [[p, len(data)] for p, data in patches]})
            for start, data in patches:
                target.seek(start)
                target.write(data)
            target.flush()
            os.fsync(target.fileno())
            if sha256(part) != actual_hash:
                raise BridgeError('CLOUD_DOWNLOAD_CHECKSUM_MISMATCH: The repaired file changed while committing; its recovery record was retained')
        if provider == 'pixeldrain' and record.exists():
            record.unlink()
        if entry.get("md5") and file_hash(part, "md5").lower() != entry["md5"].lower():
            target.truncate(offset)
            raise BridgeError("CLOUD_DOWNLOAD_CHECKSUM_MISMATCH: Original file checksum mismatch")
    return actual_hash


def run(request):
    if request.get("mode") == "inspectReuseSource":
        _, _, estimate = pixeldrain_reuse_inventory(request)
        emit({"type": "reuseInventory", **estimate})
        return
    if request.get("mode") == "inspectReuseDestination":
        emit({"type": "reuseWriteBudget", "maximumAdditionalBytes": pixeldrain_reuse_budget(request)})
        return
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
    resolver = {"googleDrive": google_manifest, "gofile": gofile_manifest, "pixeldrain": pixeldrain_manifest}[request["provider"]]
    entries, session = resolver(request)
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
    fields = ("id", "path", "size", "md5", "sha256") if request["provider"] == "pixeldrain" else ("id", "path", "size", "md5")
    manifest = [{k: e.get(k) for k in fields} for e in entries]
    manifest_path = state / "manifest.json"
    identity = download_identity(request, output)
    persisted = {"identity": identity, "files": manifest}
    staging = output / staging_name
    if staging.is_symlink():
        raise BridgeError("Symbolic links are not supported")
    upgrade_manifest = False
    frozen_manifest = False
    if manifest_path.exists():
        recorded = json.loads(manifest_path.read_text())
        frozen_manifest = recorded == persisted
        if recorded != persisted:
            # Older file descriptors could omit SHA256. Only add the provider's checksum
            # when the operation and every previously frozen descriptor still match.
            old_files = recorded.get("files") if isinstance(recorded, dict) else None
            if not (request["provider"] == "pixeldrain" and isinstance(recorded, dict) and recorded.get("identity") == identity and
                    isinstance(old_files, list) and len(old_files) == len(manifest) and
                    all(isinstance(old, dict) and current["sha256"] and
                        dict(old, sha256=current["sha256"] if old.get("sha256") is None else old["sha256"]) == current
                        for old, current in zip(old_files, manifest))):
                raise BridgeError("CLOUD_PARTIAL_IDENTITY_UNVERIFIED: Share contents or destination changed; create a new download operation")
            upgrade_manifest = True
    else:
        if staging.exists() and any(staging.iterdir()):
            raise BridgeError("CLOUD_PARTIAL_IDENTITY_UNVERIFIED: An existing partial download is missing its original operation manifest")
        atomic_json(manifest_path, persisted)
    receipt_path = state / "completed.json"
    receipts = json.loads(receipt_path.read_text()) if receipt_path.exists() else {}
    staging.mkdir(mode=0o700, exist_ok=True)
    seed_pixeldrain_reuse(request, output, state, staging, manifest, identity)
    completed = []
    completed_bytes = 0
    total_bytes = sum(e["size"] for e in entries) if all(
        isinstance(e.get("size"), int) and e["size"] >= 0 for e in entries) else None
    pixeldrain = request["provider"] == "pixeldrain"
    members = {}
    for entry in entries:
        key = hashlib.sha256(entry["path"].encode()).hexdigest()
        members[key] = (entry, staging / (key + ".part"))
    retry_key = checksum_retry_key(request.get("checksumRetryKey")) if pixeldrain and frozen_manifest else None
    if pixeldrain and retry_key in used_checksum_retry_keys(state, identity, members):
        retry_key = None
    completed_recoveries = []
    last_progress = 0
    for index, (entry, part) in enumerate(members.values()):
        destination = beneath(output, entry["path"])
        receipt = receipts.get(entry["path"])
        # Partial and recovery names are private, derived from the immutable member path.
        repair_record = state / (part.stem + ".repair.json")
        if destination.exists():
            existing = destination.stat()
            if (not stat.S_ISREG(existing.st_mode) or not receipt or existing.st_size != receipt["size"] or
                    (entry.get("sha256") and receipt["sha256"].lower() != entry["sha256"].lower()) or
                    sha256(destination) != receipt["sha256"]):
                raise BridgeError("Destination already exists and is not a verified result of this operation")
            if pixeldrain and release_rejected_partial(state, part, identity, entry, destination, file_identity(existing)):
                completed_recoveries.append((entry, part))
            completed.append(str(destination))
            completed_bytes += destination.stat().st_size
            # Reset the network-speed baseline; these bytes were verified locally, not received.
            emit({"type": "progress", "stage": "reusing", "percentage": 100 * (index + 1) / len(entries)})
            continue
        destination.parent.mkdir(parents=True, exist_ok=True)
        if pixeldrain:
            isolate_rejected_partial(state, part, identity, entry, repair_record,
                                     retry_key if entry["path"] not in receipts else None)
            retry_key = None
        def progress(done, total):
            nonlocal last_progress
            now = time.monotonic()
            if now - last_progress >= .25 or (total is not None and done == total):
                emit({"type": "progress", "stage": "downloading", "percentage": 100 * (index + (done / total if total else 0)) / len(entries), "completedBytes": completed_bytes + done, "totalBytes": total_bytes})
                last_progress = now
        if request["provider"] == "googleDrive":
            import gdown
            with contextlib.redirect_stdout(sys.stderr):
                result = gdown.download(id=entry["id"], output=str(part), quiet=True, use_cookies=False,
                                        resume=True, timeout=(20, 60), retries=2, progress=progress)
            if result != str(part) or not part.is_file():
                raise BridgeError("gdown did not produce the expected file")
            checksum = sha256(part)
        else:
            checksum = http_original(session, entry, part, request["provider"], progress, repair_record)
        verified = part.stat()
        receipts[entry["path"]] = {"size": verified.st_size, "sha256": checksum}
        atomic_json(receipt_path, receipts)
        # Both publication primitives fail if anything already exists.
        beneath(output, entry["path"])
        try:
            part_moved = publish_verified_part(part, destination, verified, state)
        except OSError:
            raise BridgeError("Could not publish file atomically without overwriting the destination") from None
        if pixeldrain and release_rejected_partial(state, part, identity, entry, destination, file_identity(verified)):
            completed_recoveries.append((entry, part))
        elif not part_moved:
            part.unlink()
        completed.append(str(destination))
        completed_bytes += receipts[entry["path"]]["size"]
        emit({"type": "progress", "stage": "downloading", "percentage": 100 * (index + 1) / len(entries), "completedBytes": completed_bytes, "totalBytes": total_bytes})
    if upgrade_manifest:
        atomic_json(manifest_path, persisted)
    for entry, part in completed_recoveries:
        record, rejected, _, original, _ = rejected_partial_state(state, part, identity, entry)
        if original is not None and present(rejected) is None and present(part) is None:
            record.unlink()
    if completed_recoveries:
        sync_directory(state)
    try:
        staging.rmdir()
    except OSError:
        pass
    emit({"type": "result", "files": completed})


if __name__ == "__main__":
    request = None
    try:
        request = json.load(sys.stdin)
        run(request)
    except BaseException as error:
        # Third-party exceptions often embed temporary signed URLs or access tokens.
        emit({"type": "error", "message": provider_failure(request.get("provider") if isinstance(request, dict) else None, error), "kind": type(error).__name__})
        sys.exit(1)
