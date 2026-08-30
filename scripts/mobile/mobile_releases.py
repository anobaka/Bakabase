"""Shared release-scanning for the mobile distribution generators.

Mobile packages are published from two pipelines — dedicated `mobile-v*`
releases, and desktop releases that attach packages when the app changed —
so generators identify them by ASSET NAME (`bakabase-mobile-{version}-...`),
never by tag.

Stdlib only. Auth via the optional GITHUB_TOKEN env var.
"""

import json
import os
import re
import urllib.request

ASSET_RE = re.compile(r"^bakabase-mobile-(?P<version>.+?)-(?P<rest>android-[a-z0-9_-]+\.apk|ios-unsigned\.ipa)$")

CDN_BASE = "https://cdn-public.anobaka.com/app/bakabase-mobile"
SIDESTORE_SOURCE_URL = "https://raw.githubusercontent.com/anobaka/Bakabase/sidestore/source.json"


def fetch_releases(owner: str, repo: str) -> list:
    releases = []
    page = 1
    while True:
        url = f"https://api.github.com/repos/{owner}/{repo}/releases?per_page=100&page={page}"
        request = urllib.request.Request(url)
        request.add_header("Accept", "application/vnd.github+json")
        token = os.environ.get("GITHUB_TOKEN")
        if token:
            request.add_header("Authorization", f"Bearer {token}")
        with urllib.request.urlopen(request) as response:
            batch = json.load(response)
        if not batch:
            return releases
        releases.extend(batch)
        page += 1


def mobile_assets_of(release: dict) -> list:
    """[(version, asset)] for every mobile package attached to a release."""
    found = []
    for asset in release.get("assets", []):
        match = ASSET_RE.match(asset.get("name", ""))
        if match:
            found.append((match.group("version"), asset))
    return found


def platform_of(asset_name: str) -> str:
    match = ASSET_RE.match(asset_name)
    if not match:
        return "unknown"
    rest = match.group("rest")
    return "ios" if rest == "ios-unsigned.ipa" else rest.rsplit(".", 1)[0]
