# HTTP downloads

`IHttpDownloadManager` owns durable HTTP/HTTPS jobs, independent of the download
center and workflows. The host supplies its AppData store path, existing
`IHttpDownloader` transport, concurrency limit and optional startup policy.

The transport remains in `Bakabase.Modules.Downloader`: it owns HTTP client
selection, guarded byte ranges, parallel connections, retries, rate limits,
timeouts and hashed resume checkpoints. The manager owns identity, ownership,
queuing, public progress and safe publication. Default interface reporting keeps
existing transport adapters compatible.

Jobs expose no source URL or credentials. Their private atomic records retain
URLs and attempt options; outputs within AppData are stored relative to its root.
Interrupted manual jobs resume after restart, explicit stops remain stopped, and
workflow jobs require their owner to resume them. Idempotency verifies content,
output path and ownership; observing an existing stopped job never restarts it.

Verified transport outputs first land in the job's private staging directory.
Publication records a SHA256 digest, copies to the target volume and atomically
renames without overwriting an existing file. The digest recovers a crash between
that rename and saving completion. Explicit rechecking validates an existing
file or redownloads missing content; unrelated replacement files remain intact.
Conflicting outputs are serialized, including symlink aliases. Explicit distinct
file names can run concurrently; a server-selected name reserves its output
directory until publication.
Removing a terminal unlinked job removes its record and private staging files,
never its published output.
