# Shared download services

`Bakabase.Modules.Downloader` owns file transfer and depends only on .NET libraries, [Downloader 5.9.6](https://www.nuget.org/packages/Downloader/5.9.6) and [MonoTorrent 3.0.2](https://www.nuget.org/packages/MonoTorrent/3.0.2).
It does not reference the application host, resource models, acquisition, workflows, a database or a task queue.
Desktop and server hosts register the same services.

## Download task identity

The module also defines the neutral `Models.Tasks.DownloadTask` snapshot and
`IDownloadTaskRegistry` contract. The host persists `ManagedDownloadTaskDbModel`
in its own database and exposes one `/download-center/tasks` command surface.
`site:<id>`, `bt:<id>` and `http:<id>` identify the same task in every list,
workflow link and parent expansion. Protocol URLs, credentials and provider
configuration are not copied into the common snapshot.

The categories are `site` and `general`; providers keep their own scheduling and
recovery. The legacy site downloader discovers works, while the BT and HTTP
managers perform transfers. A site-generated transfer points to its source task
through `ParentId`. A workflow is an owner or a subsequent processor, not a
download provider. Common task reconciliation registers existing identities
without starting them or changing their provider IDs.

The host aggregates content and workflow state for a source task: acquiring
torrent metadata does not complete a task whose files are still downloading.
Stopping a parent stops its dependent work. An explicit child stop is retained
as `UserStopped`, so continuing the parent does not undo that choice. An explicit
child start or an authorized workflow continuation clears that intent. Workflow
transfers must resume through their owner, which validates the workflow state
before resuming the same provider task.

- `IHttpDownloader`: HTTP(S) files using Downloader for parallel chunks, transient retries and speed limits; configurable target name and named HTTP client.
- `ITorrentDownloader`: magnets, torrent bytes and HTTP(S) torrent URLs; verified pieces, nested file trees and retry rehashing.
- `IAria2Downloader`: optional aria2 JSON-RPC transport; tracks metadata and payload jobs and cancels its owned download on failure or cancellation.
- `TorrentMetadata`: shared validation and bounded reading of untrusted torrent metadata.

All transfers accept cancellation and report timeout as `TimeoutException`. BitTorrent returns the verified files and their common directory and stops when complete. The aria2 daemon must write into a directory visible to the host.

HTTP defaults to at most four connections, three transient retries, no speed limit and a four-hour operation timeout. Callers can override `ParallelConnections`, `MaxRetries`, `MaximumBytesPerSecond` and `Timeout`. The HTTP workflow node exposes these settings; its limit is entered in KiB/s and converted to bytes/s when calling the shared service.

Downloader owns chunk scheduling, transport retries, throttling and package restoration. Bakabase supplies version and integrity checks around those operations. HTTP range responses must match the requested offsets. Resuming requires a stable strong ETag or Last-Modified value, unchanged length and a valid checkpoint of the saved pieces. Unverifiable or changed content starts over; servers that ignore range requests fall back to a whole-file download. Files in progress remain in the host's download cache, and only a completed, validated file is published to the caller's destination. BitTorrent separately rehashes its own pieces on retry.

## Register and call from any module

```csharp
services.AddDownloader(_ => Path.Combine(applicationDataDirectory, "download-cache"));

var downloader = serviceProvider.GetRequiredService<IHttpDownloader>();
var file = await downloader.DownloadAsync(
    new HttpDownloadRequest(url, destinationDirectory)
    {
        ParallelConnections = 4,
        MaxRetries = 3,
        MaximumBytesPerSecond = 0,
        Timeout = TimeSpan.FromHours(2)
    },
    (percentage, message) => ReportProgress(percentage, message),
    cancellationToken);
```

The cache directory factory is evaluated when a transfer starts. Hosts own its location under their writable application data directory. The Bakabase host keeps the historical `acquisition-torrent-cache` path for compatibility; this name does not limit who can use it. Legacy torrent calls write below `torrent-data`. Managed transfers use `DownloadAsync(TorrentDownloadRequest, ...)` with `UseWorkingDirectoryAsDestination = true` to write into the exact directory supplied by their owner. Structured progress includes the transfer stage, verified bytes, total bytes, percentage, speed and peer count.

BT routing is independent of HTTP download, source and workflow settings. `ITorrentNetworkOptionsProvider` lets the host resolve a saved proxy selection at transfer start. Missing providers or a null `ProxyUri` mean explicit direct access: system/environment proxies are never consulted. A configured SOCKS5 proxy carries metadata URL requests, HTTP(S) tracker requests, web seeds and peer TCP connections, including remote hostname resolution and optional username/password authentication. Proxy failures never fall back to direct access. UDP trackers and DHT are disabled in SOCKS5 mode; port forwarding, local peer discovery and incoming peer listeners are always disabled. Proxied magnet downloads therefore need reachable HTTP(S) trackers to discover peers. Changing a saved proxy affects subsequent starts/retries, not an active transfer's snapshot.

Callers own scheduling, concurrency, retries, link selection, acquisition lead storage, workflow state, file placement and resource materialization. Opening a magnet in the OS default application remains an application handoff, since it cannot report a verified local result. Workflow download nodes and component installers are consumers of these services; no workflow is required for a transfer.

The standalone test project references only this module and exercises dependency injection, real HTTP transfer, named clients, parallel connections, retries, checkpoint restoration, timeout and cancellation. Transport and workflow adapter regressions also run in `Bakabase.Tests`.
