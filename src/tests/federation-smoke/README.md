# Federation host smoke tests

`run.py` starts three independent production Service test hosts with separate
ports, databases, credentials and temporary directories. Build
`src/tests/Bakabase.Federation.TestHost` first. Run `python3 run.py --help` for
result-directory and deadline options. The default run cleans up its own hosts
and fixture data; `--keep` is for manual debugging only.

## Two-library HTTP measurements

After building the same test host, run:

```bash
python3 src/tests/federation-smoke/benchmark.py \
  --dotnet /absolute/path/to/dotnet --counts 10000 100000 \
  --results-directory /absolute/path/to/benchmark-results
```

This traverses both full libraries in fresh and warm processes, measures page
latency, HTTP payloads and sampled RSS, then interrupts an active page and checks
that both session slots can be reused. Its loopback counting proxy adds overhead;
it does not flush filesystem caches or represent physical LAN/NAS performance.
Use `--concurrent-load` to record other workloads. Production budgets remain
unchanged, and owned processes and databases are removed even on failure.

## Actual VLC loopback playback

```bash
python3 src/tests/federation-smoke/player-proxy.py \
  --vlc /Applications/VLC.app/Contents/MacOS/VLC \
  --results-directory /absolute/path/to/player-results
```

Requires an existing official VLC build with HTTP, RC and dummy audio modules
(AVIO is used only for the inherited-proxy control). Separate headless processes
must actually pause their playback clock, resume advancing it, then seek via a
Range request beyond all bytes already received. The synthetic one-hour WAV has
a virtual size of 345,600,044 bytes, a 4 MiB/s rate limit and a 32 MiB total transfer
budget; no large media file is created. Exit code or a reported `paused` state
alone cannot pass the test.

The product uses native HTTP for VLC only when macOS CFNetwork settings establish
that no HTTP proxy is configured. A configured or unknown proxy state selects an
installed mpv/IINA instead, or returns `PlayerProxyUnsupported` before launching
VLC. Windows/Linux proxy detection is not yet supported, so unmapped VLC streams
also use this fallback; local files and mapped files retain ordinary VLC playback.
This script's native HTTP check requires a suitable proxy-free test environment;
it does not override system or player proxy settings. It uses only synthetic
tickets and never controls an existing player. Other players and GUI playback
require separate verification.

`--diagnose-avio` runs the rejected AVIO workaround and is expected to fail pause
validation on VLC 3.0.23. It is a regression diagnostic, not a supported playback
strategy; `:clock-synchro=1` is also unsuitable because it enables unbounded
timeshift buffering. Results and cleanup status are written even on failure.

## Isolated Linux verification from a desktop

Start your existing Docker service first, then run from the repository root:

```bash
python3 src/tests/federation-smoke/run-linux-container.py \
  --ref HEAD --platform linux/amd64 \
  --results-directory /absolute/path/to/linux-results
```

The default SDK image is `mcr.microsoft.com/dotnet/sdk:9.0.100-noble`. An ARM host
runs `linux/amd64` through its Docker provider's emulation; results record both
requested and provider architecture. `--platform linux/arm64` selects native ARM
Linux and is **not** evidence for the CI's Linux x64 row. Image downloads require
network access. Supply `--image` to use another already available SDK image; its
SDK must satisfy `global.json` and its digest is recorded.

Prerequisites: Python 3, Git, Docker, initialized local submodules, and an actual
production frontend in `src/web/dist` (or `--web-dist`). The runner clones the
specified **committed** parent tree and archives its exact pinned submodule
commits. It does not include uncommitted source edits; build the frontend from
the intended source before running. It never mounts the working checkout
writable. The host NuGet package cache and frontend are read-only mounts; newly
needed packages and apt-installed Python live only in the owned container.

Default limits: 4 CPUs, 4 GiB container memory, 512 processes, 30 minutes overall,
and a 5 GiB host disk free-space floor. Only a uniquely named, labelled container
and owned temporary clone are removed. It does not start/stop the Docker daemon,
prune images, or change existing user containers. Downloaded SDK images remain
cached. Allow roughly 2–5 GiB extra disk for a cold run; stop/failure logs survive.

Checks run independently so one failed build does not erase other useful results:
product identities, guard failure cases, compatibility test classes, Federation
and Player tests, real three-host HTTP smoke, and actual headless publish package
contents. A dependent smoke/package check runs only if its build succeeded. Any
failed check makes the overall result fail. Evidence is `result.json`,
`platform.txt`, per-stage logs, TRX reports, smoke logs and the publish audit.
No package is installed on the host and no updater feed is accessed or published.

`--disable-hardware-intrinsics` is an explicit diagnostic option for virtual CPU
runtime faults. It is off by default and marked in the result; a diagnostic run
must not be reported as the unmodified native CI environment.
