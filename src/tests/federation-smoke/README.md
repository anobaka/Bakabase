# Multi-device host smoke tests

`run.py` starts three independent production Service test hosts with separate ports,
databases, credentials and temporary directories. Build
`src/tests/Bakabase.Federation.TestHost` first, then run:

```bash
python3 src/tests/federation-smoke/run.py --dotnet /absolute/path/to/dotnet \
  --timeout 300 --results-directory /absolute/path/to/results
```

The first stage verifies actual administrator pairing A → B and B → C, without a
reverse or transitive management relationship. It waits for A's owner-scoped metadata
search to contain B's 257 resources; B's mapping tree still contains only B's owned
resource directory, even while B manages C. Mapping writes use actual path prefixes
and compare-and-swap, and a stale save must leave the current mappings intact.

The same paired connection streams the fixture WAV with GET, HEAD, valid byte ranges
and an unsatisfiable range. Removed read-only sharing routes are refused. Cross-site,
invalid Host and node-signature requests still cannot enter the management surface.
After B stops, A can search its local metadata replica; reads did not change B's play
history. The retired read-only media-stream fixture is removed; common stream failure
handling remains covered by the management media and stream unit tests.

Only generated test data is used. Every host disables analytics before it starts,
and the runner verifies `/app/analytics-info`. By default, processes and databases are
removed; `--keep` retains only these disposable fixtures for debugging. SQLite handles
are closed before cleanup, including on Windows. Logs and JSON evidence remain in the
results directory.

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
   first sync is empty; C's first pull creates everything.
3. B links two-way with C: its first sync creates 94 and asks about 6 name
   matches, which B links under Needs you before C pulls. B's extra options
   reach C, then A; all three end equal.
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

Every TestHost offers other devices (an invitation's, a two-way request's
read-back address) the loopback address it listens on, rather than this
machine's interfaces, where a fixture never listens. One TestHost variable
exists for this stage: `BAKABASE_DATASYNC_TEST_FUTURE_SCHEMA=<property name>` makes the host serve that
custom property one schema version ahead. Each step writes its evidence (overview, links, requests, readers, inbox,
history of every host) to `<results>/datasync/`, with the hosts' logs and the
CLI transcript.
