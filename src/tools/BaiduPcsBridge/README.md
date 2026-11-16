# BaiduPCS-Go JSON bridge

Reviewed against the latest upstream release **v4.0.2** on 2026-10-29:

- [Release](https://github.com/qjfoidnh/BaiduPCS-Go/releases/tag/v4.0.2)
- [Account, transfer and download documentation](https://github.com/qjfoidnh/BaiduPCS-Go/blob/v4.0.2/README.md)
- [Pinned source](https://github.com/qjfoidnh/BaiduPCS-Go/tree/225bdd3b6cb298601c4d5ef7104c3e08cd1d692d)
- [Typed remote/local paths and checkpoint suffix](https://github.com/qjfoidnh/BaiduPCS-Go/blob/v4.0.2/internal/pcsfunctions/pcsdownload/download_task_unit.go)

The bridge is built inside the upstream module so it can use its typed internal
download task API. It requires no upstream patch and does not vendor upstream
source or modules. The upstream source is licensed under **Apache-2.0**.
The dependency installer preserves the upstream license and source references.

## Installation

Use the optional **BaiduPCS-Go** component in Bakabase's dependency settings.
The installer downloads the exact source ZIP (SHA-256 checked), reads the official
[Go download manifest](https://go.dev/dl/?mode=json), downloads and verifies its
stable runtime for this platform, then builds with the committed upstream
`go.sum` and `-mod=readonly`. Source, compiler, caches and temporary files live
under AppData/components, and are removed after installation. An unreviewed new
upstream release is displayed but is not automatically substituted for the tested
bridge source. No unpublished GitHub binary URL is assumed.

Alternatively, build with Go >= 1.23, Python 3 and curl:

```sh
./src/scripts/with-scratch.sh --root /Volumes/ssd1/Codex/tmp/bakabase -- \
  ./src/tools/BaiduPcsBridge/build.sh /absolute/output/bakabase-baidu-pcs
```

Choose an appropriate existing scratch root on other machines. Put the resulting
executable in AppData/components/baidu-pcs-go or on PATH and rediscover dependencies.
For cross builds, set `BAKABASE_BRIDGE_GOOS` and `BAKABASE_BRIDGE_GOARCH` for this
script. Tests still run natively; only the final `go build` uses those targets.

## Protocol and behavior

`--version` reports `4.0.2+bakabase.15`. Otherwise one JSON request is read from
stdin; JSON line events on stdout have `type: progress`, `result`, or `error`.
Set `BAIDUPCS_GO_CONFIG_DIR` to an absolute private per-account directory,
including when probing `--version` (upstream package initialization otherwise
uses its own default config lookup). Secrets are never command arguments.
Upstream stdout/stderr are discarded. Errors are stable safe codes.

- `login`: `cookies` or `bduss` plus `stoken`; online identity and quota validation.
  Username/password login is intentionally absent because upstream marks it
  unmaintained. The host preserves a stable account ID and refuses replacing an
  existing account with another Baidu UID.
- `validate`: verify the saved account online.
- `probe-share`: `url`, optional `accessCode`; anonymous read-only availability
  inspection. It returns before profile initialization and never takes account
  locks, transfers files or writes operation receipts. At most one first-page
  metadata listing is read, with bounded response sizes and TLS/redirect/host
  checks. Explicit cancelled/expired responses are distinct from ambiguous
  errors, network failures and missing access conditions. Checking metadata does
  not guarantee that an original file can ultimately be downloaded.
- `inspect-download`: read-only recovery inspection of the exact pinned remote manifest and retained local files/PCS range sidecars. Reads a stable existing profile with a fixed expected UID without `Config.Init`, account writes, downloads, transfers, directory creation, file hashing or checkpoint synthesis. An empty payload paired with an empty range sidecar is an unstarted upstream download. A nonempty partial file without a valid matching sidecar remains blocked. Complete-length files are only candidates; ordinary download checksum verification still decides reuse.
- `transfer`: `operationId`, `url`, `accessCode`, `remoteDirectory`; stores the
  share in its own deterministic subdirectory and returns an exact recursive
  manifest. Top-level share items and owned directories are paginated. A four
  character access code is required by this upstream version. Both `pan.baidu.com`
  and `yun.baidu.com` standard shares and `/share/init?surl=...` links are accepted.
- `download`: `remotePath` and absolute `outputDirectory`; downloads existing
  owned files/directories without implicitly transferring a share. Enumerates a
  complete manifest before downloading and checks every upstream task's outcome
  and final size. Empty directories are preserved. Unsafe paths and symlinks are
  rejected. Existing files are reused only after matching the remote size and a
  verified original `block_list`: either a single whole-file MD5 or every ordered
  block MD5 at upstream 4, 16 or 64 MiB boundaries, including the final partial
  block. Unknown block layouts are not reused. The provider's separate `md5` field
  can be encoded or incorrect for multipart uploads even after normalization,
  and is not sufficient proof. Missing listing
  checksums are fetched through upstream file metadata for matching-size local
  candidates, with the remote file identity checked again. Unsupported checksums
  retain the ordinary upstream download/continuation behavior.
  Preflight and final checksum reads emit throttled `verifying` activity events
  (at most once per second plus boundaries), including checked byte counts, so
  large files on slow mounts do not trip the host queue inactivity watchdog.
  Verification bytes are never counted as downloaded bytes or completion.
  Reads check cancellation between bounded chunks; the host terminates the
  process tree on cancellation as well.
  Full verification can also reuse an intact file with a stale continuation
  sidecar after an interrupted overwrite. Sidecars are preserved and excluded
  from the returned file manifest. Progress includes verified completed files
  plus valid upstream range checkpoints; an existing or sparse file's length is
  not counted as completed download data. Final success uses upstream task status
  and revalidates every supported original block checksum, including after range
  continuation. A checksum mismatch does not publish the file as a successful
  result, even when its size matches. Killing the bridge leaves upstream continuation sidecars.

Share access uses the upstream account HTTP client, including its cookie jar and
proxy. The bridge first validates the final HTTPS host. Baidu's modern
`/wap/error` and `/share/error` pages can contain valid account locals while the
share itself is unavailable. Desktop error pages can also keep the original
`/s/...` URL and publish `share_page_type: "error"` and `errortype` in their
structured `locals.mset(...)` or `window.locals=...` data. Both forms use the
same mapping, reviewed against Baidu's official error-page script:

| Official error type | Stable bridge code |
| --- | --- |
| 0 / 1 / 3 / 5 / 10 | `BAIDU_SHARE_DELETED` / `BAIDU_SHARE_CANCELLED` / `BAIDU_SHARE_BLOCKED` / `BAIDU_SHARE_NOT_FOUND` / `BAIDU_SHARE_EXPIRED` |
| 11, explicitly invalidated after too many visits | `BAIDU_SHARE_VISIT_LIMIT_INVALIDATED` |
| 2 / 12 / 15, temporarily unavailable | `BAIDU_SHARE_TEMPORARILY_UNAVAILABLE` |
| 17, restricted access scope | `BAIDU_SHARE_ACCESS_RESTRICTED` |
| 123, visitor limit reached | `BAIDU_SHARE_VISITOR_LIMIT_REACHED` |
| 124, frozen until the owner activates it | `BAIDU_SHARE_FROZEN` |
| Unknown, missing or malformed type | `BAIDU_SHARE_UNAVAILABLE` |

Titles, generic `errno` values and legacy CSS markers never establish a
specific invalidity reason. Structured error data is checked before legacy
unavailable-page markers, and no verification POST, metadata listing or transfer
follows an error page. Normal share locals retain the upstream token parsing and
request headers; missing share identity is `BAIDU_SHARE_PAGE_INVALID`.

Verification requires a bounded JSON response with an explicit integer `errno`
and, on success, a nonempty `randsk`. Error `-9` is
`BAIDU_SHARE_ACCESS_CODE_REJECTED`; `8001` is
`BAIDU_SHARE_VERIFICATION_REQUIRED`. Other errors, including ambiguous `-12`,
remain `BAIDU_SHARE_VERIFICATION_FAILED`. Malformed or oversized responses use
`BAIDU_SHARE_RESPONSE_INVALID`, while transport, HTTP status and body-read
failures use `BAIDU_SHARE_NETWORK_ERROR`. Response bodies, account tokens and
access codes never become error messages. These failures occur before any remote
file mutation; they do not imply that retrying an unavailable share can repair it.

The isolated availability probe only permits HTTPS requests to the compiled-in
`pan.baidu.com` and `yun.baidu.com` hosts, with the same allowlist on every
redirect. It keeps normal certificate/SNI verification and uses the configured
OS resolver and TUN routing for those hosts, including fake-IP DNS. This trusted
provider transport is never used for arbitrary user URLs or payload download
locations. General public-URL probes retain their public-address/DNS pinning
boundary. The probe does not change proxy settings or try another DNS resolver.
Account transfer/download operations keep their existing network behavior.

Account/profile mutations and remote transfers remain exclusive in the host;
downloads use shared read leases with a bounded per-account capacity. A queued
mutation blocks later readers, so continuous downloads cannot starve login/logout.
Overlapping local output roots are serialized even for different operations or
accounts. Transfer journals record intent
before the remote mutation and persist a completed receipt after verifying the
target. Repeating a completed operation returns its receipt. If interrupted after
the remote request begins, the operation requires confirmation rather than
blindly transferring again. The host can recover completed receipts after its
own persistence boundary. Logout clears local credentials and retains operation
journals; it does not revoke sessions on other devices.

Tests do not require credentials. Live login, remote transfer and download need
a user-provided Baidu session and test share and are separate acceptance checks.

Local filesystem failures include a safe structured `storage` kind and operation. Local disk/quota exhaustion differs from remote account quota. Legacy bare error codes never prove a full disk. Raw paths, credentials and upstream diagnostics are not emitted. `ENAMETOOLONG` / Windows error 206 uses `BAIDU_LOCAL_PATH_TOO_LONG`, not the non-regular-file code.

## Portable local names

Bridge `.11` keeps remote paths and manifests unchanged and assigns local `SavePath`
values separately. Every new mapped segment fits in 255 UTF-8 bytes; file names
reserve room for `.BaiduPCS-Go-downloading`. A readable prefix and deterministic
hash replace oversized/unsafe names. Archive families (`.part01.rar`, `.r00`,
`.zip`/`.z01`, `.7z.001`, etc.) share the same shortened stem and retain their
original part suffixes. Case collisions are disambiguated. Distinct non-ASCII
sibling families also receive separate hashes to remain unambiguous on volumes
with different Unicode normalization rules, without adding another upstream
dependency. Manifest ordering does not affect names.

Bridge `.15` also keeps a shared local stem when a publisher appends the same
short word to RAR volume extensions, such as `.part1.rar删除` and `.part2.rar删除`.
Each original extension remains visible. Different markers remain separate.
The saved path map pins its naming algorithm so interrupted downloads continue
using their original names. Both supported map versions are checked by
recreating the complete mapping from the bound remote manifest.

Before the first payload write, changed maps are atomically published without
replacement to the account's `operations/<operation-hash>.paths-v1.json`. The
versioned map is bound to UID, original operation ID, output root and exact remote
manifest. An existing map must match in full. No map is stored among payloads.
The read-only inspector computes the same prospective map without saving it.
An unmapped legacy destination containing files or checkpoints keeps its original
paths if the retained-file inspection can prove them safe; otherwise it is blocked.
New maps are only adopted for destinations without payloads. No existing files
are renamed, moved or overwritten to fit a new map. Unknown/mismatched maps and
ambiguous retained files require review. This permits an original failed task
with no output and an overlong remote name to continue using its existing transfer
receipt and download operation, without transferring again.

## Existing-file completion verification

Bridge `.12` adds `verify-existing`, separate from `download` and `inspect-download`.
The host binds it to one original failed download and completed transfer receipt.
The cheap inspection requires every expected local file at its original mapped
path and exact length; that only allows attempting verification, never completion.
With `verifyRemote: true`, it checks the pinned manifest, reads metadata for each
original fsid/path/size, verifies every ordered `block_list` MD5, and computes local
SHA256 from those same bytes. Metadata is read again after verification. Changed
identity, changed checksums, absent block lists, unknown block boundaries, partial
files and corrupt files fail closed. The host then independently captures SHA256
and atomically registers the original download's result and completion status.

This command never enters upstream account initialization, changes a profile,
creates or deletes local files/checkpoints, transfers a share, downloads payloads,
or falls back to the normal downloader. It retains stale valid sidecars. Upload
boundaries are the pinned upstream's 4/16/64 MiB constants; only a unique boundary
consistent with the entire ordered list is accepted. The upstream's aggregate
MD5 field is not accepted as a substitute for multipart checksums.

## Account capability and concurrent downloads

Bridge `.13` makes `download` read a stable existing profile bound to
`expectedUid`, without upstream `Config.Init`, `Close` or `Save`. Original
operation receipts, path maps and range checkpoints retain their identities and
locations. Files within a task remain sequential; `connectionsPerFile` controls
the upstream range downloader (1–20, default 1). The host independently limits
simultaneous tasks to at most five, including direct workflow calls. Existing
downloads are not cancelled when configured limits are lowered.

`membership` is a bounded, read-only request to the fixed official
`https://pan.baidu.com/rest/2.0/xpan/nas?method=uinfo` endpoint, using the current
profile's cookie jar. TLS validation is enabled and redirects are not followed.
Only a successful response with explicit integer `vip_type` and valid `uk` is
accepted. Types 0, 1 and 2 map to ordinary, VIP and SVIP; missing or new values,
authentication errors, malformed responses and timeouts remain unknown. Netdisk
UK is not treated as the profile's Baidu UID. Response bodies and credentials are
never emitted. This endpoint's published SDK uses OAuth; compatibility with an
existing cookie session is best-effort and cannot turn a failed probe into a
positive membership result.

Reviewed on 2026-11-05 against the current
[Baidu official user-info SDK](https://github.com/baidu-netdisk/mcp/blob/b3983d330fea79c7b72e6b7014803e1830148d2c/src/baidu-netdisk/openapi_client/api/userinfo_api.py),
[official response schema](https://github.com/baidu-netdisk/mcp/blob/b3983d330fea79c7b72e6b7014803e1830148d2c/src/baidu-netdisk/openapi_client/model/uinforesponse.py),
[OpenList's membership mapping](https://github.com/OpenListTeam/OpenList/blob/main/drivers/baidu_netdisk/driver.go),
and [upstream's concurrency guidance](https://github.com/qjfoidnh/BaiduPCS-Go/blob/main/README.md).
Upstream recommends single-task/single-connection operation for non-SVIP users.
Bakabase caches observations for six hours, coalesces refreshes per account and
backs off failed automatic probes for ten minutes. Login, validation, explicit
refresh and asynchronous download preflight can refresh membership; list/status
polling reads only cached metadata. A failed probe preserves the last successful
observation with a failure marker. Effective limits fall back to 1/1 unless
current SVIP evidence or explicit user confirmation permits parallelism. A fresh
ordinary/VIP observation always overrides the manual fallback.

`manifest` reads a bounded exact remote manifest with the same stable-profile
identity binding and no profile, local-file or remote-file writes. Before a
previously unpinned remote download starts, the host uses this manifest to create
one prepared request for both storage reservation and execution. The ordinary
download revalidates that exact manifest before writing. It never substitutes an
unknown size with zero or starts parallel unbudgeted payloads.
