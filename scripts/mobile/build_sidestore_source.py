#!/usr/bin/env python3
"""Builds the SideStore/AltStore source JSON for Bakabase Mobile.

Regenerates the whole source from the repository's GitHub releases every time
(stateless): every unsigned IPA asset — whether it sits on a dedicated
`mobile-v*` release or on a desktop release that carried a changed app —
becomes one entry in `apps[0].versions`, newest first, deduplicated by
version. Users add

    https://raw.githubusercontent.com/anobaka/Bakabase/sidestore/source.json

to SideStore once and receive every later release automatically.

Stdlib only — runs on a bare GitHub Actions runner.
"""

import argparse
import json
import sys

from mobile_releases import fetch_releases, mobile_assets_of

SOURCE_TEMPLATE = {
    "name": "Bakabase Mobile",
    "identifier": "com.bakabase.mobile-source",
    "apps": [],
}

APP_TEMPLATE = {
    "name": "Bakabase Mobile",
    "bundleIdentifier": "com.bakabase.mobile",
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


def build_versions(releases: list) -> list:
    by_version = {}
    for release in releases:
        if release.get("draft"):
            continue
        tag = release.get("tag_name") or ""
        for version, asset in mobile_assets_of(release):
            if not asset["name"].endswith(".ipa"):
                continue
            date = release.get("published_at") or release.get("created_at") or ""
            existing = by_version.get(version)
            if existing and existing["date"] >= date:
                continue
            # Desktop release bodies describe the desktop app; only dedicated
            # mobile releases carry notes about this build.
            notes = (release.get("body") or "").strip() if tag.startswith("mobile-v") else ""
            by_version[version] = {
                "version": version,
                "date": date,
                "size": asset.get("size", 0),
                "downloadURL": asset["browser_download_url"],
                "minOSVersion": "15.0",
                "localizedDescription": notes or f"Bakabase Mobile {version}",
            }

    # SideStore treats versions[0] as the latest.
    return sorted(by_version.values(), key=lambda v: v["date"], reverse=True)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--owner", default="anobaka")
    parser.add_argument("--repo", default="Bakabase")
    parser.add_argument("--out", default="source.json")
    args = parser.parse_args()

    versions = build_versions(fetch_releases(args.owner, args.repo))
    if not versions:
        print("no releases with an unsigned mobile IPA found; refusing to publish an empty source",
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
