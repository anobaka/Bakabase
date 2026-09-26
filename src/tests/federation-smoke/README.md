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
No host reports to analytics: the TestHost blanks every `Analytics:*` key and
turns anonymous tracking off itself, whatever the caller's environment (see
`FixtureAnalytics.cs`), and `run.py` checks each host's `/app/analytics-info`
before anything else.
The TestHost writes complete fixture remote-access settings atomically before
configuration watchers start. It intentionally warms empty resource/property
caches, then seeds through the production cache-aware ORM and checks resource
visibility before signalling readiness; background index timing cannot leave the
detail API reading an empty cache while SQL-based federation queries see rows.

## Data sync (`datasync.py`)

After its own checks, `run.py` runs data sync (definitions kept in step between
devices) across three more hosts, within the same deadline; `--skip-datasync`
leaves it out, and `python3 datasync.py --dotnet …` runs it alone. A and B are
desktop apps (`BAKABASE_FEDERATION_TEST_DESKTOP_WINDOW`), C is a headless server
started with `BAKABASE_DATASYNC_SHARING=true`; `BAKABASE_NODE_NAME` names each.
Every step goes through each host's own loopback API (`/data-sync/*`, and
`/federation/local/*` for the identity reset), and C is managed through its
headless CLI — `dotnet Bakabase.Service.dll federation datasync …` from the
TestHost's output, in C's own environment, as `docker exec` runs it:

0. C's sharing is on with nobody at a window; `share off` lasts until its next
   start.
1. A gets 100 custom properties (one with 2,000 tags, a multilevel one) and 4
   extension groups; B gets 6 of the same names, each with an extra option.
2. A asks C for a two-way link; C approves on its CLI and reads A back; A's
   review is empty; C's first pull creates everything.
3. B links two-way with C: 94 created, 6 linked as exact matches. B's extra
   options reach C, then A; all three end equal.
4. A and B rename one property differently while paused (C through its CLI).
   C meets the conflict — and creates no notification; A sees it waiting on C;
   B keeps its own name, which closes C's item as decided elsewhere and
   reaches A.
5. A deletes an option a resource on B uses: C, where nothing used it, drops
   it; B holds it and asks.
6. A resets its identity (same node, new epoch): C pauses its link
   (`PeerReset`) and applies nothing A changed afterwards.
7. B restarts serving one property as a newer schema: C holds that record and
   applies the rest of the pull.
8. C announced nothing under any data sync notification source; the desktops
   did; the CLI's `status` shows what waits on each peer.

Two TestHost variables exist for it. `BAKABASE_FEDERATION_TEST_ADDRESSES=loopback`
makes the address a host offers other devices (an invitation's, a two-way
request's read-back address) the loopback one it listens on, rather than this
machine's interfaces, where a fixture never listens.
`BAKABASE_DATASYNC_TEST_FUTURE_SCHEMA=<property name>` makes the host serve that
custom property one schema version ahead, with a member this build does not
know. Each step writes its evidence (overview, links, requests, readers, inbox,
history of every host) to `<results>/datasync/`, with the hosts' logs and the
CLI transcript.

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
be 8–128 MiB and is left for its caller to remove. The `CI` workflow runs
this check on Linux when dispatched with `suite=platforms` and uploads its results, not the video. This is deterministic loopback
rate/failure injection; it does not claim physical-network latency, packet loss,
NAS throughput, or a native external-player matrix.
