# Shared download services

`Bakabase.Modules.Downloader` owns file transfer and depends only on .NET libraries, [Downloader 5.9.6](https://www.nuget.org/packages/Downloader/5.9.6) and [MonoTorrent 3.0.2](https://www.nuget.org/packages/MonoTorrent/3.0.2).
It does not reference the application host, resource models, acquisition, workflows, a database or a task queue.
Desktop and server hosts register the same services.

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

The cache directory factory is evaluated when a transfer starts. Hosts own its location under their writable application data directory. The Bakabase host keeps the historical `acquisition-torrent-cache` path for compatibility; this name does not limit who can use it. Torrent payloads remain in the caller's working directory under `torrent-data`.

Callers own scheduling, concurrency, retries, link selection, acquisition lead storage, workflow state, file placement and resource materialization. Opening a magnet in the OS default application remains an application handoff, since it cannot report a verified local result. Workflow download nodes and component installers are consumers of these services; no workflow is required for a transfer.

The standalone test project references only this module and exercises dependency injection, real HTTP transfer, named clients, parallel connections, retries, checkpoint restoration, timeout and cancellation. Transport and workflow adapter regressions also run in `Bakabase.Tests`.
