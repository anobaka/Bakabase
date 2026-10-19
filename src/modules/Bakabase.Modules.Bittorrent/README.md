# BitTorrent downloads

This module owns durable transfer jobs, scheduling, progress and stop/resume/retry.
It depends on the shared `ITorrentDownloader` transport and has no dependency on
workflow, ExHentai, the application's database, or HTTP controllers.

The application exposes `/bt-downloads` as an independent management page. Both
ExHentai result orchestration and workflow activities submit jobs through the same
`IBtDownloadManager`. An idempotency key preserves the job and partial files across
retries. Workflow owns its wait and continuation; the transfer owns its status.
The application projects those statuses into download task summaries without
replacing the platform downloader's discovery progress.

Job records and private torrent metadata live under AppData. Files downloaded to
the requested output directory are never deleted by removing a job record. Active
jobs with overlapping output directories run sequentially. User stops are durable;
restarting the server rechecks pieces for eligible interrupted jobs. An application
startup policy reconciles source ownership before scheduling: a workflow's transfer
waits for its owning workflow to resume, while independent downloads can recover
directly. Existing source tasks retain their old save-torrent behavior until users
select BT explicitly.

BT network settings are separate from HTTP downloader settings. The default is
explicit direct access, bypassing system proxies. A saved SOCKS5 proxy can cover
metadata, HTTP(S) trackers, webseeds and peer TCP connections; proxy mode disables
DHT and UDP trackers and never falls back to direct access. Settings changes apply
to new or resumed transfers.
