"""Refresh desktop release objects without expiring historical release caches."""

import argparse
import json
import re
import subprocess
import time


BASE_URL = "https://cdn-public.anobaka.com/app/bakabase"
PLATFORMS = ("win-x64", "osx-x64", "osx-arm64")
VERSION_PATTERN = re.compile(
    r"[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?"
)


def refresh_requests(version):
    if not VERSION_PATTERN.fullmatch(version):
        raise ValueError(f"Invalid release version: {version}")

    # Mirror _build.yml, including beta/rc matching anywhere in the version.
    prerelease_channel = "beta" if any(x in version.lower() for x in ("beta", "rc")) else None
    # vpk's NuGet SemanticVersion.ToNormalizedString() omits build metadata.
    core, separator, labels = version.split("+", 1)[0].partition("-")
    package_version = ".".join(str(int(x)) for x in core.split(".")) + separator + labels

    files = []
    for platform in PLATFORMS:
        os_channel = "win" if platform.startswith("win-") else "osx"
        channel = prerelease_channel or os_channel
        prefix = f"{BASE_URL}/releases/{platform}"
        # Exact vpk 1.2.0 contract: DefaultName.cs and CoreUtil.cs.
        # Setup/Portable always have a channel suffix. Only default Windows
        # nupkg names omit it; stable macOS packages retain -osx.
        # https://github.com/velopack/velopack/blob/1.2.0/src/vpk/Velopack.Core/DefaultName.cs
        package_suffix = "" if channel == "win" else f"-{channel}"
        legacy_feed = "RELEASES" if channel == "win" else f"RELEASES-{channel}"
        setup_extension = "exe" if os_channel == "win" else "pkg"
        # SimpleWebSource appends arch/os/rid/id/localVersion to feed URLs.
        # This domain ignores all query parameters in its cache key:
        # set_hashkey_args.disable=on, verified read-only on 2026-10-07.
        # keep_oss_args=on only controls parameter forwarding to the origin.
        # File refresh therefore also refreshes those updater query variants.
        # https://help.aliyun.com/en/cdn/developer-reference/parameters-for-configuring-features-for-domain-names
        names = (
            f"releases.{channel}.json",
            f"assets.{channel}.json",
            legacy_feed,
            f"Bakabase-{channel}-Setup.{setup_extension}",
            f"Bakabase-{channel}-Portable.zip",
            # Reruns can replace this version's packages. Refresh only those,
            # including a delta if present; keep every older package cached.
            f"Bakabase-{package_version}{package_suffix}-full.nupkg",
            f"Bakabase-{package_version}{package_suffix}-delta.nupkg",
        )
        files.extend(f"{prefix}/{name}" for name in names)

    files.extend((
        f"{BASE_URL}/releases/changelogs/index.json",
        f"{BASE_URL}/releases/changelogs/{version}/README.md",
        f"{BASE_URL}/scripts/bakabase.user.js",
    ))
    return (
        ("File", files),
        # Keep same-version archive recovery without touching old versions.
        ("Directory", [f"{BASE_URL}/archives/{version}/"]),
    )


def aliyun(cli_path, action, **parameters):
    command = [cli_path, "cdn", action]
    for name, value in parameters.items():
        command.extend((f"--{name}", str(value)))
    result = subprocess.run(command, check=True, capture_output=True, text=True, timeout=60)
    return json.loads(result.stdout)


def task_ids(response):
    raw_ids = response.get("RefreshTaskId")
    if not isinstance(raw_ids, str) or not re.fullmatch(r"\d+(?:\s*,\s*\d+)*", raw_ids.strip()):
        raise RuntimeError("Alibaba Cloud did not return valid refresh task IDs")
    return {task_id.strip() for task_id in raw_ids.split(",")}


def describe_tasks(call, task_id):
    # Keep the existing DescribeRefreshTasks permission. One group ID can
    # cover many URLs, so inspect every row and every page rather than [0].
    page = 1
    rows = []
    while True:
        response = call("DescribeRefreshTasks", TaskId=task_id, PageSize=100, PageNumber=page)
        batch = response.get("Tasks", {}).get("CDNTask", [])
        total = response.get("TotalCount", 0)
        if not isinstance(batch, list) or not isinstance(total, int) or total < 0:
            raise RuntimeError(f"Invalid CDN refresh task response for {task_id}")
        rows.extend(batch)
        if len(rows) >= total:
            return rows, True
        # Task visibility can be eventual; retry an incomplete snapshot in
        # the next poll instead of accepting it or looping through empty pages.
        if not batch:
            return rows, False
        page += 1


def refresh(version, call, timeout=360, interval=10):
    requests = refresh_requests(version)
    expected = {(object_type.lower(), path) for object_type, paths in requests for path in paths}
    ids = set()
    for object_type, paths in requests:
        response = call("RefreshObjectCaches", ObjectPath="\n".join(paths), ObjectType=object_type)
        ids.update(task_ids(response))
        print(f"Submitted {len(paths)} {object_type.lower()} refresh(es)")

    # IDs can repeat across submissions: same-domain requests within the same
    # second may be merged by Alibaba Cloud, even across refresh types.
    deadline = time.monotonic() + timeout
    while True:
        completed = set()
        pending = False
        for task_id in sorted(ids):
            rows, complete_snapshot = describe_tasks(call, task_id)
            pending |= not complete_snapshot
            for row in rows:
                key = (row.get("ObjectType", "").lower(), row.get("ObjectPath", ""))
                # Other branches or mobile distribution can share a merged ID.
                # Their refresh failures must not block this invocation's targets.
                if key not in expected:
                    continue
                status = row.get("Status", "")
                if status in ("Failed", "Timeout", "Canceled"):
                    raise RuntimeError(f"CDN refresh {task_id} {status}: {row}")
                if status != "Complete":
                    pending = True
                else:
                    completed.add(key)

        missing = expected - completed
        if not pending and not missing:
            print(f"CDN refresh completed for all {len(expected)} objects ({len(ids)} task ID(s))")
            return
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise RuntimeError(f"CDN refresh timed out after {timeout}s; {len(missing)} object(s) incomplete")
        print(f"Waiting for CDN refresh: {len(missing)} object(s) incomplete")
        time.sleep(min(interval, remaining))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", required=True)
    parser.add_argument("--cli", default="./aliyun")
    args = parser.parse_args()
    try:
        refresh(args.version, lambda action, **parameters: aliyun(args.cli, action, **parameters))
    except (ValueError, RuntimeError, subprocess.SubprocessError) as error:
        raise SystemExit(f"::error::{error}") from error


if __name__ == "__main__":
    main()
