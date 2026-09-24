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
