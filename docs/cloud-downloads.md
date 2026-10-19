# Cloud downloads and post workflows

This first version adds Baidu Netdisk, Google Drive and GoFile to the existing
download center. Cloud services have their own site identities; optional command
tools remain managed components. Post parsing produces reusable source data,
including access codes and archive instructions. A workflow decides which source
to use and how to obtain the files.

## Use

1. Open system configuration → dependencies and install the optional components
   for the providers you need. Opening settings does not install them. For Google
   Drive or GoFile, the managed Cloud Python prerequisite is installed together
   with the selected tool.
2. For Baidu, open third-party configuration → Baidu Netdisk. Add an account using
   its BDUSS and Netdisk STOKEN, or the corresponding cookie import. Login checks
   the account online. Accounts can be verified, made default, logged out or
   deleted. Logout removes this installation's saved credentials; other Baidu
   device sessions remain active.
3. Use **Cloud download** in the download center to enter a share link, extraction
   code and output directory. Choose a Baidu account for Baidu sources. An existing
   absolute Baidu remote path can be downloaded directly without transferring a
   share first.
4. A completed saved post result has a download action. Select the required
   content and one mirror per content group, choose an output directory and start
   the workflow. Legacy ungrouped links require explicit selection. Parsing a
   post by itself does not start a transfer or download.
5. Watch transfer and routing in workflow details and file downloads in the
   download center. Missing tools, accounts, access information or unsupported
   providers suspend the workflow with the original sources intact. Resume from
   the workflow after resolving the problem.

The default source workflow selects sources, transfers applicable Baidu shares,
routes supported downloads and waits for completion. The workflow editor also
offers separate **Transfer Baidu share** and **Download Baidu files** nodes. A
transfer-only workflow is valid; downloading owned files does not implicitly
transfer a share.

## Provider boundary

| Provider | First-version adapter | Boundary |
|---|---|---|
| Baidu Netdisk | BaiduPCS-Go JSON bridge | Multiple isolated accounts; credential login and validation; share transfer; owned file/directory download; recursive manifests and continuation state. Four-character extraction codes are required by this upstream transfer implementation. |
| Google Drive | gdown | Public shared files and recursive folders. Google Docs, Sheets and Slides use gdown's default exports (DOCX, XLSX and PPTX). Private-account OAuth and browser sessions are outside this integration. |
| GoFile | gallery-dl | Public shares and recursive directory content, with access information passed to the adapter. The pinned upstream requests only the first page of 1000 entries; the Bakabase adapter explicitly fetches subsequent pages and validates the complete directory count. Missing or changing counts, repeated entries and incomplete pages fail without claiming completion. |
| HTTP / BT | Existing native download managers | Explicit direct HTTP sources and magnet sources reuse their existing queues and workflow ownership. An unrecognized web page remains unresolved until a supported source or explicit direct download is chosen. |
| PikPak, OneDrive, Pixeldrain, Mega and unknown hosts | Recognized where possible | Recognition preserves information for routing; it does not imply an implemented adapter or automatic success. |

Archive passwords, extraction plans and groups remain in the workset. These nodes
obtain files; they do not automatically execute an archive extraction plan.

## Reviewed upstream versions

Official releases, documentation and the relevant implementation were checked on
**2026-10-19**. Versions are fixed until their adapters are reviewed again.

| Tool | Reviewed version | Primary documentation |
|---|---|---|
| BaiduPCS-Go | v4.0.2; commit `225bdd3b6cb298601c4d5ef7104c3e08cd1d692d` | [Release](https://github.com/qjfoidnh/BaiduPCS-Go/releases/tag/v4.0.2), [README](https://github.com/qjfoidnh/BaiduPCS-Go/blob/v4.0.2/README.md), [transfer implementation](https://github.com/qjfoidnh/BaiduPCS-Go/blob/v4.0.2/baidupcs/transfer.go) |
| Bakabase Baidu bridge | `4.0.2+bakabase.1` | [Source, protocol, build script and tests](../src/tools/BaiduPcsBridge/README.md) |
| gdown | 6.4.2 | [Release](https://github.com/wkentaro/gdown/releases/tag/v6.4.2), [README](https://github.com/wkentaro/gdown/blob/v6.4.2/README.md) |
| gallery-dl | 1.32.17 | [Release mirror](https://github.com/mikf/gallery-dl/releases/tag/v1.32.17), [current upstream release](https://codeberg.org/mikf/gallery-dl/releases/tag/v1.32.17), [supported sites](https://github.com/mikf/gallery-dl/blob/v1.32.17/docs/supportedsites.md) |
| Managed Python | CPython 3.13.16 / python-build-standalone 20261013 | [Official runtime build release](https://github.com/astral-sh/python-build-standalone/releases/tag/20261013) |

gallery-dl development has moved to Codeberg; use that upstream as well as the
GitHub release mirror when checking future versions. gdown's documented JSON
listing format is beta, which is another reason to review adapter behavior before
upgrading its package.

## Optional Baidu component installation

The upstream CLI does not expose a stable JSON protocol, and some failures are
printed without a failing process exit status. Bakabase therefore builds a small
bridge inside the pinned upstream Go module and uses its typed APIs and download
task outcomes. No upstream source patch or unpublished binary release URL is
required.

The first explicit component installation downloads the fixed upstream ZIP,
verifies its SHA-256, fetches a stable Go toolchain from the official
[download manifest](https://go.dev/dl/?mode=json), verifies its published SHA-256,
and builds with the upstream `go.sum` and `-mod=readonly`. This includes an
approximately 70 MB toolchain download and local compilation. The user does not
need Go on PATH. Build files and module caches stay inside AppData's component
temporary directory and are cleaned after installation.

Component downloads and Go module compilation use Bakabase's configured global
network proxy. Baidu transfer/download subprocesses honor the Baidu site proxy
override, falling back to the global setting. Resolved proxy credentials enter
only the child environment and are excluded from process diagnostics.

The Google Drive and GoFile subprocesses also use Bakabase's existing per-site
proxy resolver, falling back to the global configuration. The resolved proxy and
its credentials enter the private JSON stdin request. Requests sessions disable
ambient proxy and `.netrc` discovery, and no user browser or tool configuration is
loaded. Managed Python/package installation uses the configured global proxy;
packages install into application-owned virtual environments with private cache
and configuration handling. SOCKS support is installed alongside each Python tool.

The resulting component contains `bakabase-baidu-pcs[.exe]`, the upstream
`LICENSE.BaiduPCS-Go`, the exact upstream source ZIP, the bridge `main.go`, and
rebuild instructions under `source/`. The pinned upstream BaiduPCS-Go `LICENSE`
is **Apache-2.0**; the component retains the original license text. The main application starts
it as a separate process. The standalone build instructions identify the same
source revision and required module checksums.

Only a successfully compiled executable with the exact bridge protocol version
replaces the installed binary. A newer upstream version can be shown in dependency
metadata while the compatible installable bridge remains fixed.

## Persistence and recovery

`CloudSource` carries the original URL, provider, source kind, access code,
archive password, extraction plan and optional content group. `DownloadWorkset`
keeps all content targets in one workflow item, including selected mirrors,
account references, transfer receipts, download task identities and results.
This avoids losing sibling targets when the workflow suspends.

Each transfer/download has a stable operation identity and a frozen serialized
request in `CloudDownloadOperations`. The operation identity does not change
when someone changes a default account or output setting. Reusing an operation
with different parameters is rejected. Queue creation and its operation-to-task
binding are committed together; resumption reuses the same download task.

Baidu profiles live in private per-account directories under
`AppData/cloud-downloads/baidu/`. Credentials travel to the bridge over stdin;
they are absent from account API responses, workflow snapshots, task arguments
and process diagnostics. An account ID remains tied to the validated Baidu UID.
Work using a given account is serialized so config files cannot race.

The bridge records transfer intent before making the remote request. The remote
destination is specific to that operation. It records a completed receipt only
after checking the transferred roots and a complete recursive manifest. Replays
reuse completed receipts. If the process stops after the remote request begins
and no completed receipt proves the outcome, the operation remains **unknown**.
Inspect its remote operation directory before starting another transfer; retries
must not silently duplicate a potentially completed remote operation.

Python adapter downloads retain a private per-operation manifest and completion
hashes. They reuse only files whose saved size and SHA-256 still match. Partial
files live in a private staging directory on the destination volume, and completed
files are published without overwriting an existing file. The destination must
support hard links (for example, APFS, ext4 or NTFS); symbolic-link destinations
are rejected. Shares containing no downloadable files, including empty
directories, return an explicit unsupported/empty-content error. GoFile discovery
is bounded to 100,000 files and 32 nested folders; it must prove the complete
count for every directory before downloading any file.

Downloads enumerate the remote tree before writing files. Baidu receipt manifests
are compared again before download, each upstream task must succeed, and final
file sizes must match. The Python adapters similarly require a complete manifest
and validate local results. Unsafe output paths are rejected. Cancellation stops
the subprocess and retains partial download/continuation files for retry. Baidu
may repeat already-completed files while resuming a directory operation; it
retains partial-file continuation sidecars.

## Verification and upgrades

Automated checks cover workflow operation recovery and binding, path safety,
dependency discovery, adapter manifests and error handling. The Go bridge has
been built natively on macOS arm64 and cross-built for Linux/Windows amd64.
Cross compilation is not a native Windows acceptance run. No real Baidu account
login, share transfer or live Baidu directory download has been verified in this
development session; those require a supplied account and test share.

For an upgrade, read current official release notes, documentation and the
provider-specific source. Review login changes, pagination, partial failures,
continuation state, machine output and licenses. Update the fixed version/source
digest together with its adapter and tests, rebuild the bridge for supported
platforms, regenerate the API SDK if its contract changes, and perform real-share
acceptance. Never substitute the latest tool binary underneath a reviewed adapter
without these checks.
