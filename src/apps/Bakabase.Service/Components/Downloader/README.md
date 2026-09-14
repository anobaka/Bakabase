# Download result workflows

Source downloaders record what they obtained for each work. The application layer
decides what happens next through workflows; the shared download module only
transfers files. ExHentai authentication, request pacing and gallery fetching stay
in the existing platform downloader.

## Entry points

```text
Independent ExHentai task
  → per-work result
  → configured result workflow
  → torrent contents
  → [optional] prepare resource → place files → associate resource

Acquisition of an ExHentai resource
  → original acquisition workflow
  → owned platform task → per-work result
  → continue the same run → download torrent contents when needed
  → place files → associate the original resource
```

In ExHentai settings, **Result workflow** chooses the default for newly created
independent tasks. It is initially unset: saving a torrent does not imply fetching
its contents. The task snapshots this choice, so changing settings does not change
existing tasks. The built-in **Download torrent contents** workflow accepts torrent
metadata results and downloads their files without importing them. Users can add
the resource preparation, placement and association nodes to import as well.
Image results can be handled by a workflow whose result filter includes local files.

Acquisition-owned tasks explicitly disable independent result workflows. The
ExHentai acquisition recipe handles metadata and image results and keeps the
original acquisition, workflow run and resource identities. An unavailable swarm,
platform authentication failure or filesystem error remains visible in that run;
its original task is the retry entry point.

## Durable contracts

- `DownloadResultDbModel` is the producer record: canonical platform identity,
  result kind, exact files or managed torrent metadata, fingerprint, and the
  task's workflow choice. Downloading a `.torrent` is a metadata result, not
  completed resource contents. Validated metadata is copied into application data
  before recording, so removing the original download does not break continuation.
- `DownloadResultOwnerDbModel` is saved with a disabled child task in one
  transaction, before that task may start. Ownership is permanent, including on
  cancellation; the independent dispatcher must never claim such a result.
- `DownloadResultProcessingDbModel` binds an independent result to one workflow
  run and records the current content location separately from resource
  association. Dispatch commits the run and binding before enqueueing. The periodic
  dispatcher repairs lost queue entries; explicit retries reuse the bound run.
  Invalid workflows have a retry delay and cannot block later results.
- A producer deduplication key combines task, platform, source identity, kind and
  content fingerprint. Re-recording the same result does not create another run.
  This is durable dispatch deduplication, not a promise that arbitrary workflow
  node side effects execute exactly once.

Automatic platform tasks use a per-work directory. Import preparation copies only
the recorded files from older shared output directories; it never adopts sibling
works. Content observers update the result after download and placement, so a
later association failure still leaves a valid location to inspect. Gallery cache
invalidation follows successful database updates.

The result API exposes stages, workflow errors and retry actions. It accepts no
client-supplied paths. Built-in workflow names, node names and descriptions have
Chinese and English translations.

## Validation

`ExHentaiDownloadResultTests` exercises the real producer with a local HTTP
fixture. `ExHentaiAcquisitionTests` covers owned task creation, retry, cancellation
and same-resource continuation. `DownloadResultWorkflowTests` uses migrated SQLite
and the real workflow runner to cover durable dispatch, cached metadata, BT retry
and optional import. `DownloadResultContentStageTests` covers ready states before
resource association, placement conflicts, explicit file ownership and API output.
Transfer tests use a controlled torrent downloader; public
swarm availability is outside these orchestration tests.
