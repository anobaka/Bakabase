#!/usr/bin/env python3
"""Wait for one artifact from this workflow run before downloading it normally.

Callers include github.run_attempt in the artifact name to keep uploads
immutable. For reruns of only failed jobs, --allow-previous-attempts can reuse
a successful earlier frontend build of the same run and immutable input ref.
This only gates the download; the caller must still require every build job
to succeed before publishing a release.
"""

import argparse
import json
import math
import os
from pathlib import Path
import re
import sys
import time
from urllib.error import HTTPError, URLError
from urllib.parse import quote, urlencode, urlsplit
from urllib.request import Request, urlopen


TRANSIENT_HTTP_STATUSES = {408, 429, 500, 502, 503, 504}


def positive_seconds(value):
    seconds = float(value)
    if not math.isfinite(seconds) or seconds <= 0:
        raise argparse.ArgumentTypeError("must be a positive, finite number of seconds")
    return seconds


def find_artifact(url, headers, name, previous_attempts, deadline):
    prefix = latest_attempt = None
    if previous_attempts:
        match = re.fullmatch(r"(.+)-([1-9][0-9]*)", name)
        if not match:
            raise ValueError("Previous-attempt lookup requires an artifact name ending in -<run_attempt>")
        prefix, latest_attempt = match.group(1), int(match.group(2))

    best = None
    best_attempt = 0
    page = 1
    while True:
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            return None
        parameters = {"per_page": 100, "page": page}
        if not previous_attempts:
            parameters["name"] = name
        request = Request(url + "?" + urlencode(parameters), headers=headers)
        with urlopen(request, timeout=min(30, remaining)) as response:
            payload = json.load(response)
        if time.monotonic() >= deadline:
            return None
        artifacts = payload.get("artifacts") if isinstance(payload, dict) else None
        if not isinstance(artifacts, list) or not all(isinstance(artifact, dict) for artifact in artifacts):
            raise ValueError("GitHub returned an invalid artifact list")
        for artifact in artifacts:
            if artifact.get("expired") is not False:
                continue
            if previous_attempts:
                match = re.fullmatch(re.escape(prefix) + r"-([1-9][0-9]*)", str(artifact.get("name", "")))
                if not match or int(match.group(1)) > latest_attempt:
                    continue
                attempt = int(match.group(1))
            else:
                if artifact.get("name") != name:
                    continue
                attempt = 1
            artifact_id = artifact.get("id")
            if type(artifact_id) is not int or artifact_id <= 0:
                raise ValueError("GitHub returned an invalid artifact ID")
            if best is not None and attempt == best_attempt:
                raise ValueError(f"More than one active artifact is named {artifact['name']!r}")
            if attempt > best_attempt:
                best, best_attempt = artifact_id, attempt
        if len(artifacts) < 100:
            return best
        page += 1


def wait_for_artifact(name, *, timeout=600, interval=10, previous_attempts=False):
    if not name or "\n" in name or "\r" in name:
        raise ValueError("An artifact name without line breaks is required")
    if not math.isfinite(timeout) or timeout <= 0 or not math.isfinite(interval) or interval <= 0:
        raise ValueError("timeout and interval must be positive and finite")

    token = os.environ["GITHUB_TOKEN"]
    repository = os.environ["GITHUB_REPOSITORY"]
    run_id = os.environ["GITHUB_RUN_ID"]
    api_url = os.environ.get("GITHUB_API_URL", "https://api.github.com").rstrip("/")
    if not token or len(repository.split("/")) != 2 or not all(repository.split("/")) or not run_id.isdecimal():
        raise ValueError("GITHUB_TOKEN, GITHUB_REPOSITORY and GITHUB_RUN_ID must identify the current run")
    parsed_api_url = urlsplit(api_url)
    if parsed_api_url.scheme != "https" or not parsed_api_url.netloc or parsed_api_url.query or parsed_api_url.fragment:
        raise ValueError("GITHUB_API_URL must be an HTTPS API base URL")

    # Scope every lookup to this run. Earlier attempts remain safe only when
    # every attempt uses the same immutable inputs.ref, as our build caller does.
    url = f"{api_url}/repos/{quote(repository, safe='/')}/actions/runs/{run_id}/artifacts"
    headers = {
        "Authorization": f"Bearer {token}",
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
        "User-Agent": "Bakabase-artifact-wait",
    }
    deadline = time.monotonic() + timeout
    while True:
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise TimeoutError(f"Artifact {name!r} was not available in run {run_id} within {timeout:g} seconds")
        delay = interval
        try:
            # A stalled API request cannot consume an unbounded part of the wait.
            artifact_id = find_artifact(url, headers, name, previous_attempts, deadline)
            if artifact_id is not None:
                print(f"Artifact for {name!r} is ready in run {run_id} (ID {artifact_id})", flush=True)
                return artifact_id
            print(f"Waiting for artifact {name!r} in run {run_id}", flush=True)
        except HTTPError as error:
            if error.code not in TRANSIENT_HTTP_STATUSES:
                # In particular, do not turn denied access into ten minutes of retries.
                raise RuntimeError(f"GitHub artifact lookup failed with HTTP {error.code}; check actions: read permission") from error
            retry_after = error.headers.get("Retry-After", "") if error.headers else ""
            if retry_after.isdecimal():
                delay = max(delay, int(retry_after))
            print(f"GitHub artifact lookup returned HTTP {error.code}; retrying", flush=True)
        except (URLError, TimeoutError, ConnectionError):
            print("GitHub artifact lookup had a network error; retrying", flush=True)

        remaining = deadline - time.monotonic()
        if remaining > 0:
            time.sleep(min(delay, remaining))


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--name", required=True)
    parser.add_argument("--timeout", type=positive_seconds, default=600)
    parser.add_argument("--interval", type=positive_seconds, default=10)
    parser.add_argument("--allow-previous-attempts", action="store_true",
                        help="Allow prefix-1..prefix-N from this run when --name is prefix-N")
    args = parser.parse_args(argv)
    artifact_id = wait_for_artifact(args.name, timeout=args.timeout, interval=args.interval,
                                    previous_attempts=args.allow_previous_attempts)
    with Path(os.environ["GITHUB_OUTPUT"]).open("a", encoding="utf-8") as output:
        output.write(f"artifact-id={artifact_id}\n")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError, KeyError, RuntimeError) as error:
        print(f"::error::Unable to wait for artifact: {error}", file=sys.stderr)
        sys.exit(1)
