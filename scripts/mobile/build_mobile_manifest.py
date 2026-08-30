#!/usr/bin/env python3
"""Builds the mobile download manifest the Bakabase server reads.

The server's app-download page cannot know CI-produced download URLs by
itself, so every mobile-publishing pipeline regenerates this manifest and
uploads it to a fixed OSS path:

    https://cdn-public.anobaka.com/app/bakabase-mobile/manifest.json

Content: the newest release carrying mobile packages (by publish date,
regardless of whether it was a mobile-v* release or a desktop release that
attached a changed app), with a GitHub URL and an Aliyun CDN URL per file.

Stdlib only — runs on a bare GitHub Actions runner.
"""

import argparse
import json
import sys

from mobile_releases import (
    CDN_BASE,
    SIDESTORE_SOURCE_URL,
    fetch_releases,
    mobile_assets_of,
    platform_of,
)


def build_manifest(releases: list) -> dict | None:
    candidates = []
    for release in releases:
        if release.get("draft"):
            continue
        assets = mobile_assets_of(release)
        if assets:
            candidates.append((release.get("published_at") or release.get("created_at") or "", release, assets))

    if not candidates:
        return None

    candidates.sort(key=lambda c: c[0], reverse=True)
    published_at, release, assets = candidates[0]
    version = assets[0][0]

    return {
        "version": version,
        "publishedAt": published_at,
        "releaseUrl": release.get("html_url"),
        "sidestoreSourceUrl": SIDESTORE_SOURCE_URL,
        "files": [
            {
                "name": asset["name"],
                "platform": platform_of(asset["name"]),
                "size": asset.get("size", 0),
                "githubUrl": asset["browser_download_url"],
                "cdnUrl": f"{CDN_BASE}/archives/{ver}/{asset['name']}",
            }
            for ver, asset in assets
        ],
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--owner", default="anobaka")
    parser.add_argument("--repo", default="Bakabase")
    parser.add_argument("--out", default="manifest.json")
    args = parser.parse_args()

    manifest = build_manifest(fetch_releases(args.owner, args.repo))
    if manifest is None:
        print("no releases carry mobile packages; refusing to publish an empty manifest", file=sys.stderr)
        return 1

    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=2)
        f.write("\n")

    print(f"wrote {args.out}: version {manifest['version']} with {len(manifest['files'])} file(s)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
