#!/usr/bin/env python3
"""Builds the SideStore/AltStore source JSON for Bakabase Mobile.

Regenerates the whole source from the repository's GitHub releases every time
(stateless): every non-draft release tagged `mobile-v*` that carries an
`*-ios-unsigned.ipa` asset becomes one entry in `apps[0].versions`, newest
first. The result is served from the `sidestore` branch, so users add

    https://raw.githubusercontent.com/anobaka/Bakabase/sidestore/source.json

to SideStore once and receive every later release automatically.

Stdlib only — runs on a bare GitHub Actions runner. Auth comes from the
GITHUB_TOKEN env var (optional locally; unauthenticated works at low rate).
"""

import argparse
import json
import os
import sys
import urllib.request

TAG_PREFIX = "mobile-v"
IPA_SUFFIX = "-ios-unsigned.ipa"

SOURCE_TEMPLATE = {
    "name": "Bakabase Mobile",
    "identifier": "com.bakabase.mobile-source",
    "apps": [],
}

APP_TEMPLATE = {
    "name": "Bakabase Mobile",
    "bundleIdentifier": "com.bakabase.bakabaseMobile",
    "developerName": "anobaka",
    "subtitle": "Browse and play your Bakabase library",
    "localizedDescription": (
        "Thin companion app for a Bakabase server on your local network: "
        "auto-discovers the server, browses your libraries, and plays media "
        "with on-device decoding.\n\n"
        "Requires a running Bakabase server with remote access enabled."
    ),
    "iconURL": "https://raw.githubusercontent.com/anobaka/Bakabase/main/src/web/src/assets/logo/bakabase.png",
    "tintColor": "#0E7C6B",
    "screenshotURLs": [],
    "versions": [],
}


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


def build_versions(releases: list) -> list:
    versions = []
    for release in releases:
        tag = release.get("tag_name") or ""
        if release.get("draft") or not tag.startswith(TAG_PREFIX):
            continue

        ipa = next(
            (a for a in release.get("assets", []) if a.get("name", "").endswith(IPA_SUFFIX)),
            None,
        )
        if ipa is None:
            print(f"skipping {tag}: no {IPA_SUFFIX} asset", file=sys.stderr)
            continue

        versions.append(
            {
                "version": tag[len(TAG_PREFIX):],
                "date": release.get("published_at") or release.get("created_at"),
                "size": ipa.get("size", 0),
                "downloadURL": ipa["browser_download_url"],
                "minOSVersion": "15.0",
                "localizedDescription": (release.get("body") or "").strip()
                    or f"Bakabase Mobile {tag[len(TAG_PREFIX):]}",
            }
        )

    # GitHub returns releases newest-first already, but do not rely on it:
    # SideStore treats versions[0] as the latest.
    versions.sort(key=lambda v: v["date"] or "", reverse=True)
    return versions


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--owner", default="anobaka")
    parser.add_argument("--repo", default="Bakabase")
    parser.add_argument("--out", default="source.json")
    args = parser.parse_args()

    versions = build_versions(fetch_releases(args.owner, args.repo))
    if not versions:
        print("no mobile releases with an unsigned IPA found; refusing to publish an empty source",
              file=sys.stderr)
        return 1

    source = dict(SOURCE_TEMPLATE)
    app = dict(APP_TEMPLATE)
    app["versions"] = versions
    source["apps"] = [app]

    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(source, f, ensure_ascii=False, indent=2)
        f.write("\n")

    print(f"wrote {args.out} with {len(versions)} version(s); latest {versions[0]['version']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
