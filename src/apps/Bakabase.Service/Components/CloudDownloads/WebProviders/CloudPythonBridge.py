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
import stat
import sys
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
    if status == 429 or value in ("max_concurrent_downloads", "transfer_limit_exceeded", "download_limit_exceeded"):
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


def http_original(session, entry, part, provider, progress):
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
        if not offset or offset < expected_size:
            headers = {"Accept-Encoding": "identity"}
            if offset:
                headers["Range"] = f"bytes={offset}-"
            with session.get(entry["url"], headers=headers, stream=True, timeout=(20, 60), allow_redirects=False) as response:
                status = getattr(response, "status_code", 200)
                if offset and status == 416:
                    raise BridgeError("CLOUD_RANGE_UNCONFIRMED: The source rejected the requested byte range. The existing partial file was retained.")
                if provider == "pixeldrain" and status >= 400:
                    try:
                        body = response.json()
                        value = body.get("value", "") if isinstance(body, dict) else ""
                    except (ValueError, TypeError):
                        value = ""
                    raise pixeldrain_failure(status, value)
                response.raise_for_status()
                response_headers = getattr(response, "headers", {})
                content_range = response_headers.get("Content-Range")
                if offset:
                    match = re.fullmatch(r"bytes ([0-9]{1,20})-([0-9]{1,20})/([0-9]{1,20})", content_range or "", re.I)
                    if status != 206 or not match or tuple(map(int, match.groups())) != (offset, expected_size - 1, expected_size):
                        raise BridgeError("CLOUD_RANGE_UNCONFIRMED: The source did not confirm the requested byte range. The existing partial file was retained.")
                elif status != 200 or content_range:
                    raise BridgeError("Unexpected public-share response to the original file request")
                if response_headers.get("Content-Encoding", "identity").lower() != "identity":
                    raise BridgeError("CLOUD_RANGE_UNCONFIRMED: The source returned encoded bytes instead of the original representation")
                length = response_headers.get("Content-Length")
                if length is not None and (not re.fullmatch(r"[0-9]{1,20}", length) or int(length) != expected_size - offset):
                    raise BridgeError("CLOUD_RANGE_UNCONFIRMED: The original response length does not match the file manifest")
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
        actual_hash = sha256(part)
        if checksum and actual_hash.lower() != checksum.lower():
            target.truncate(offset)
            raise BridgeError("CLOUD_DOWNLOAD_CHECKSUM_MISMATCH: Original file SHA256 mismatch")
        if entry.get("md5") and file_hash(part, "md5").lower() != entry["md5"].lower():
            target.truncate(offset)
            raise BridgeError("CLOUD_DOWNLOAD_CHECKSUM_MISMATCH: Original file checksum mismatch")
    return actual_hash


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
    identity = hashlib.sha256(json.dumps([request["provider"], request["url"], request.get("accessCode"), str(output.resolve())]).encode()).hexdigest()
    persisted = {"identity": identity, "files": manifest}
    staging = output / staging_name
    if staging.is_symlink():
        raise BridgeError("Symbolic links are not supported")
    upgrade_manifest = False
    if manifest_path.exists():
        recorded = json.loads(manifest_path.read_text())
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
    completed = []
    completed_bytes = 0
    total_bytes = sum(e["size"] for e in entries) if all(
        isinstance(e.get("size"), int) and e["size"] >= 0 for e in entries) else None
    last_progress = 0
    for index, entry in enumerate(entries):
        destination = beneath(output, entry["path"])
        receipt = receipts.get(entry["path"])
        if destination.exists():
            if (not destination.is_file() or not receipt or destination.stat().st_size != receipt["size"] or
                    (entry.get("sha256") and receipt["sha256"].lower() != entry["sha256"].lower()) or
                    sha256(destination) != receipt["sha256"]):
                raise BridgeError("Destination already exists and is not a verified result of this operation")
            completed.append(str(destination))
            completed_bytes += destination.stat().st_size
            # Reset the network-speed baseline; these bytes were verified locally, not received.
            emit({"type": "progress", "stage": "reusing", "percentage": 100 * (index + 1) / len(entries)})
            continue
        destination.parent.mkdir(parents=True, exist_ok=True)
        # Partial files stay in a private operation directory; filenames never come from the remote server.
        part = staging / (hashlib.sha256(entry["path"].encode()).hexdigest() + ".part")
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
            checksum = http_original(session, entry, part, request["provider"], progress)
        receipts[entry["path"]] = {"size": part.stat().st_size, "sha256": checksum}
        atomic_json(receipt_path, receipts)
        # Hard-link publication fails if anything already exists; never overwrite the user's file.
        beneath(output, entry["path"])
        try:
            os.link(part, destination)
        except OSError:
            raise BridgeError("Could not publish file atomically without overwriting the destination")
        part.unlink()
        completed.append(str(destination))
        completed_bytes += receipts[entry["path"]]["size"]
        emit({"type": "progress", "stage": "downloading", "percentage": 100 * (index + 1) / len(entries), "completedBytes": completed_bytes, "totalBytes": total_bytes})
    if upgrade_manifest:
        atomic_json(manifest_path, persisted)
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
