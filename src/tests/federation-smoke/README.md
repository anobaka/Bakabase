# Federation host smoke tests

`run.py` starts three independent production Service test hosts with separate
ports, databases, credentials and temporary directories. Build
`src/tests/Bakabase.Federation.TestHost` first. Run `python3 run.py --help` for
result-directory and deadline options. The default run cleans up its own hosts
and fixture data; `--keep` is for manual debugging only.
The pairing check first creates a pending approval and then completes the same
transaction with an invitation code. SQLite inspection connections are explicitly
closed before fixture cleanup, including on Windows where open files cannot be
removed.
The TestHost writes complete fixture remote-access settings atomically before
configuration watchers start. It intentionally warms empty resource/property
caches, then seeds through the production cache-aware ORM and checks resource
visibility before signalling readiness; background index timing cannot leave the
detail API reading an empty cache while SQL-based federation queries see rows.

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

## Real video and streaming failures

Build the production web app and `Bakabase.Federation.TestHost`, and install this
repository's pinned `federation-browser-smoke` Playwright/Chromium dependencies.
Generate a disposable synthetic video with FFmpeg (no user media):

```bash
ffmpeg -hide_banner -loglevel error -y \
  -f lavfi -i 'testsrc2=size=640x360:rate=24' -t 120 -an \
  -c:v libvpx -deadline realtime -cpu-used 8 -b:v 900k -g 48 \
  /absolute/temporary/path/fixture.webm
python3 src/tests/federation-smoke/media-stream.py \
  --dotnet /absolute/path/to/dotnet --node /absolute/path/to/node \
  --media /absolute/temporary/path/fixture.webm \
  --results-directory /absolute/path/to/media-results --timeout 240
```

Two independent production hosts pair through a test-only relay pinned to one
loopback destination. The relay preserves signed requests, forwards real media
bytes, limits transfer to 256 KiB/s, and injects incomplete responses and stalls.
It never stores authorization headers or media tickets. The optional
`BAKABASE_FEDERATION_TEST_MEDIA_FILE` is used only by the unpackaged TestHost;
ordinary smoke tests keep their original one-second WAV.

The browser must present an actual video frame (`requestVideoFrameCallback`),
pause its clock, refresh the query without replacing the preview element or media
session, resume, then present a frame at 90 seconds. Seeking must issue a
new Range beyond half of the source file. HTTP checks verify a continuous transfer
beyond the eight-second header deadline, reader cancellation releasing the
upstream, interrupted transfer detection, matching bytes after a new Range,
the product's 30-second idle cutoff, and cancellation of an active stream when
browsing is disabled. Source revocation must reject new Range requests; playback
must not update either library's history.

The overall deadline terminates owned hosts/browser processes, and cleanup removes
the temporary databases and raw host logs. Results retain only metrics, sanitized
exception types, and a screenshot of the synthetic video. The input fixture must
be 8–128 MiB and is left for its caller to remove. The Ubuntu browser CI job runs
this check and uploads its results, not the video. This is deterministic loopback
rate/failure injection; it does not claim physical-network latency, packet loss,
NAS throughput, or a native external-player matrix.

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

## Native desktop and production Docker boundary

`docker-boundary.py` runs the actual macOS portable application against a Linux
x64 Service image built with the repository's `docker/Dockerfile`. It does not
use TestHost or seed SQLite directly. Prepare the real production publish/image,
portable package, and the test-only Velopack hook described in
`../upgrade-tests/README.md` first:

```bash
python3 src/tests/federation-smoke/docker-boundary.py \
  --native-portable /absolute/path/to/Bakabase-federation-test-Portable.zip \
  --hook /absolute/path/to/VelopackIsolationHook.dll \
  --docker-image bakabase-federation-boundary:tested-commit \
  --native-host 192.168.1.10 \
  --provenance /absolute/path/to/provenance.json \
  --results-directory /tmp/bakabase-docker-boundary-new-run
```

Provenance must contain full `nativeSourceCommit` and `dockerSourceCommit` SHAs;
retain the build and package audit evidence alongside it. The runner records
package/assembly hashes, the immutable Docker index and selected amd64 manifest,
and the network addresses. It requires a locally cached `curlimages/curl:latest`
(or `--curl-image`) and never pulls images. Its short curl helpers share only
the owned Service container's network namespace, so Docker's local management
API is accessed through its own loopback interface.
Replace the example `--native-host` with this Mac's actual private LAN IPv4.
OrbStack's `host.docker.internal` translates the source to host loopback, so it
cannot validate non-loopback management denial. The explicit LAN route preserves
that distinction; the runner requires denial even with forged local Host/Origin
and forwarding headers. The Docker fixture sets `ASPNETCORE_HTTP_PORTS=34567`
explicitly because its production base image otherwise defaults to port 8080.

Each application creates 17 resources through the business API and materializes
one synthetic audio file. The check separately authorizes each direction,
traverses all 34 resources with cursor replay, reads remote detail/media ranges,
checks remote management denial, stops/restarts Docker, and verifies browsing
opt-out remains independent from sharing. Revocation must invalidate cached
pages and media. After both applications stop, both databases must pass integrity
checks without playback-history writes and the media files must be unchanged.

The runner refuses an existing native Bakabase process, requires a fresh `/tmp`
result directory, and enforces a 10-minute deadline and 5 GiB free-space floor.
It removes only its own labelled containers/network, native process and fixture
files, including on failure. It leaves existing user containers and images alone.
Only sanitized diagnostics are retained. Run its pure failure/cleanup checks with
`python3 src/tests/federation-smoke/test_docker_boundary.py`.

This crosses a real container network boundary on one Mac. OrbStack on ARM runs
the x64 image through emulation; this result does not establish physical LAN/NAS
performance, firewall behavior on other machines, or signed installer acceptance.

## Isolated Linux verification from a desktop

With a local .NET 9 SDK, cross-build on the desktop and run only the Linux x64
runtime in Docker. This avoids downloading a Linux SDK or compiling under CPU
emulation:

```bash
docker pull --platform linux/amd64 mcr.microsoft.com/dotnet/aspnet:9.0-noble
python3 src/tests/federation-smoke/run-linux-cross-container.py \
  --dotnet /absolute/path/to/dotnet --ref HEAD \
  --with-player --with-compatibility \
  --results-directory /absolute/path/to/new-linux-x64-results
```

The image must already exist and have an immutable repository digest. The runner
verifies its x64 architecture and ASP.NET 9 runtime, clones the selected committed
revision and exact submodule SHAs, and cross-builds `linux-x64`. The default check
is the three-host smoke; optional flags add Player and compatibility tests.
`--skip-smoke` reruns only selected optional suites, and the report records that
selection. Reusing previous result directories is rejected to prevent stale TRX
files from being counted.

Source/artifact mounts are read-only. Legacy test code that writes next to its
assembly runs from a disposable container-local copy. CPU/memory/process limits,
a 20-minute overall deadline and a 5 GiB free-space floor bound the run. Cleanup
addresses only owned processes, containers and source copies; failures and cleanup
errors remain in the result. An ARM Docker provider executes x64 through emulation,
which is recorded explicitly and does not replace native Linux CI. This focused
runner does not audit a shipping Service package or exercise a physical network.

The alternative below builds and checks the complete Linux role inside a Linux
SDK container, including the package audit. It needs more disk/download space.
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
