#!/usr/bin/env python3
"""Prepare Velopack's literal custom plist without changing product identity."""
import argparse
from pathlib import Path
import plistlib
import re


def prepare(template, output, version):
    # Apple display/build versions use numeric components. Velopack keeps the
    # complete SemVer (including its release channel) in its own manifest.
    match = re.fullmatch(r"(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?", version)
    if match is None:
        raise ValueError("A three-component semantic release version is required")
    with template.open("rb") as stream:
        info = plistlib.load(stream)
    if info.get("CFBundleExecutable") != "Bakabase":
        raise ValueError("The template must identify the app's executable")
    numeric_version = ".".join(match.groups())
    info["CFBundleVersion"] = numeric_version
    info["CFBundleShortVersionString"] = numeric_version
    info["CFBundleGetInfoString"] = f'{info["CFBundleDisplayName"]} {version}'
    with output.open("wb") as stream:
        plistlib.dump(info, stream, sort_keys=False)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--template", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--version", required=True)
    args = parser.parse_args()
    prepare(args.template, args.output, args.version)
