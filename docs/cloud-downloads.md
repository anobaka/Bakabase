# Cloud downloads and post workflows

Cloud adapters add Baidu Netdisk, Google Drive, GoFile, Pixeldrain, PikPak, MEGA, TeraBox and public
SharePoint folders to the existing download center. Cloud services have their own site identities; optional command
tools remain managed components. Post parsing produces reusable source data,
including access codes and archive instructions. A workflow decides which source
to use and how to obtain the files.

## Use

1. Open System → Configuration → Dependency management and install the optional components
   for the providers you need. Opening settings does not install them. For Google
   Drive, GoFile or Pixeldrain public file/list shares, the shared Base Python prerequisite is installed together
   with the selected Python tool. rclone is a separate optional managed component.
2. For Baidu, open third-party configuration → Baidu Netdisk. Add an account using
   its BDUSS and Netdisk STOKEN, or the corresponding cookie import. Login checks
   the account online. Accounts can be verified, made default, logged out or
   deleted. Logout removes this installation's saved credentials; other Baidu
   device sessions remain active.
3. Use **Create download task** in the download center and choose a site to enter a share link, extraction
   code and output directory. Choose a Baidu account for Baidu sources. An existing
   absolute Baidu remote path can be downloaded directly without transferring a
   share first. PikPak, OneDrive/SharePoint and Pixeldrain also offer an explicit
   **account path** source, downloaded through rclone after configuring that account.
   A path is relative to the selected account's configured drive/root; `/` selects
   that root. It is not a share URL or a local filesystem path.
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
| Google Drive | gdown | Public shared files and recursive folders. Native Google Docs, Sheets and Slides export is not enabled by this integration. Private-account OAuth and browser sessions are outside this integration. |
| GoFile | gallery-dl | Public shares and recursive directory content, with access information passed to the adapter. The pinned upstream requests only the first page of 1000 entries; the Bakabase adapter explicitly fetches subsequent pages and validates the complete directory count. Missing or changing counts, repeated entries and incomplete pages fail without claiming completion. |
| HTTP / BT | Existing native download managers | Explicit direct HTTP sources and magnet sources reuse their existing queues and workflow ownership. An unrecognized web page remains unresolved until a supported source or explicit direct download is chosen. |
| Pixeldrain | rclone Filesystem; gallery-dl public shares | Public `/d/` Filesystem directories use rclone without an account. Personal Filesystem paths use an API key and require the upstream Pro/Prepaid plan. Public `/u/` files and `/l/` lists use the isolated gallery-dl based adapter; rclone's backend does not support that separate sharing product. |
| PikPak | Official PikPak CLI public shares; rclone account paths | Public `/s/` shares use device authorization, save the selected share subtree to the authorized account, then download original files. Paginated manifests, account bindings and durable save receipts prevent repeating a confirmed or uncertain transfer. Existing account paths remain a separate rclone method; its credentials do not authorize the official CLI. |
| OneDrive / SharePoint | rclone OAuth account paths; public-folder resolver and native HTTP | Native rclone authorization selects a personal/business drive or SharePoint library, then downloads account paths. Public SharePoint folder shares remain a separate anonymous method. A successful login does not grant access to arbitrary external share links or automatically map them into a configured drive. |
| Feemoo | Recognized final share locator | Reviewed short links can resolve to this provider. No reviewed automatic adapter is available; keep the final share and provenance for manual handling. |
| MEGA | MEGAcmd public shares | Direct anonymous file/folder downloads with the complete link and decryption key; no account import. Automated execution currently requires Bakabase's managed Linux runtime. Windows/macOS dependency discovery does not yet enable isolated automatic execution. Quota, network, decryption and uncertain share availability are separate errors. |
| TeraBox | TeraBox CLI public shares | Authorized original-file downloads from reviewed `/s/` links on `terabox.com` and `1024terabox.com`. The adapter enumerates directory pages and downloads each file ID; it never invokes `share-save`. The account's actual download permissions still apply. Unknown or partial results require review rather than being reported as an expired link or a completed download. |
| Unknown hosts | Recognized where possible | Recognition preserves information for routing; it does not imply an implemented adapter or automatic success. |

Archive passwords, extraction plans and groups remain in the workset. These nodes
obtain files; they do not automatically execute an archive extraction plan.

## MEGA and PikPak official CLI preparation

Both tools are optional managed dependencies, downloaded from their official hosts
and checked against reviewed SHA-256 hashes. Install them in dependency management
or from the download preparation panel. The new engines use the same per-tool and
per-site network settings as other download methods.

For **MEGA**, retain the full public link, including the fragment containing its
decryption key. The adapter invokes the official MEGAcmd downloader and decryption
implementation without logging in or importing the share. On Linux, Bakabase owns
the background server, profile and socket; it refuses to adopt an existing user
profile. It never changes `HOME`. Cancelling the client alone is insufficient, so
the adapter cancels owned transfers and shuts down its owned server. Completed
receipts are checked against local files before reuse. Partial or uncertain results
wait for reconciliation instead of calling `get` again and producing numbered
duplicate files. MEGAcmd proxy support is limited to compatible HTTP/HTTPS proxies.

For **PikPak**, open third-party configuration → PikPak and choose **Connect PikPak
account**. Open the official authorization page, check the displayed verification
code, and personally review the requested `account` and `files:write` permissions.
The official `account` group covers profile and account settings; the current CLI
requires it for its post-login profile check, and Bakabase uses it to identify the
account. File read/write access enables saving shares and downloading original
files. Additional `manage` and permanent deletion (`purge`) permissions are not
required. Bakabase polls the
official CLI and promotes the isolated account profile only after authorization is
confirmed. The browser can be on another device: neither a browser nor a CLI on the
Docker host is needed. Device credentials and access tokens stay on the server.
Disconnecting clears local authorization without deleting cloud or downloaded
files; revoke the connected app separately in PikPak's account/security settings.

Share visibility is separate from original-file download permission. An anonymous
share listing can display files while providing no original URL. The authorized
CLI method therefore saves the selected share contents into the user's account,
records the destination and receipt, and downloads the originals. Account changes,
changed remote manifests, uncertain save responses and partial downloads require
reconciliation rather than a blind retry. Free accounts can download, subject to
available cloud storage and the account's connected-app transfer limits; those
limits are not evidence that a share expired. No fixed quota number is used to
decide whether an operation is allowed.

PikPak transfer attempts for an account are serialized. Before creating a remote
folder and again before saving a share, the adapter compares the selected files
with the account's reported capacity. CLI display values are rounded, so only a
definite shortage blocks the attempt; unavailable capacity metadata does not
invent a limit. A blocked attempt keeps its operation identity. After the user
clears space, **Diagnose and continue** can resume it without repeating a saved
transfer. The application does not delete cloud files. A previous submission
whose outcome is unknown still needs reconciliation, even if its destination
currently looks empty. Cloud capacity is a separate attention tag from expired
links and transfer-traffic limits.

For a single-part acquisition stopped at an empty failed download, **Change
download source** can select a confirmed mirror from its saved plan or the exact
origin revision. The preview includes the link, access information and that
mirror's processing plan. Applying it appends a plan revision and retires the old
run; starting the new plan remains a separate action. Existing payloads, shared
operations and uncertain remote writes block this action. Old runs, download
records and files remain available in history.
The reviewed download identity also includes the method, account configuration
generation and output directory. If these change before starting, execution is
blocked. Reconfirming an already saved, unstarted source revision is not yet
supported; the old run and its files remain retained.

After preparing dependencies or authorization, use **Diagnose and continue** on
existing acquisition tasks. Its preview is read-only. A never-executed prepared
plan may start after current preflight checks; a failed original download may
resume only when its provider and retained request satisfy the recovery checks.
Existing files, saved operations and history are preserved. Tool installation,
anonymous metadata checks and fixture tests do not by themselves establish live
account acceptance; account-backed end-to-end validation requires the user's
authorization and a verified final file.

## rclone accounts and Microsoft authorization

Third-party configuration reuses the same account panel as download preparation.
Accounts have separate private rclone profiles, a default per provider, an explicit
connection check, and delete/logout that removes authorization without deleting
downloaded files. Listing accounts and download preflight do not log in, install
tools, transfer shares, or test the network. Configured and explicitly verified
accounts are displayed separately.

For OneDrive/SharePoint, create an account and start authorization. Bakabase drives
rclone's native noninteractive configuration questions with read-only scopes. The
default manual callback authorization uses the server-managed rclone: open Microsoft
sign-in from the dialog, complete consent, then immediately paste the complete
localhost callback address back into the secret input. The browser may show a
connection error at that callback; it refers to the browser computer, not Docker.
No browser-side rclone installation or extra published port is needed. The
advanced section retains the official `rclone authorize` command and token-result
input. Continue the native drive/site questions in the dialog. Tokens
remain private and are never returned by account/session APIs. Cancelled or expired
authorization staging is cleared; an existing working account remains configured
until a new authorization completes.

rclone normally uses its built-in Microsoft application; registering an application
is not ordinarily required. Tenant consent restrictions can still require an
administrator's approval. Third-party configuration also offers an optional
automatic HTTPS callback mode for your own Microsoft application: configure its
client ID, write-only secret Value, tenant and pre-registered fixed HTTPS callback
URL. This URL routes directly to the server's `/oauth/callback/microsoftGraph`
endpoint, independently of the address used to open Bakabase. Complete Microsoft
sign-in in the new tab, then return to the original Bakabase tab to continue the
native site and document-library questions. Configuration alone does not authorize
or replace an account. The same application's credentials remain with each
completed rclone profile so rclone can refresh its tokens.
In Docker, the browser machine's `localhost:53682` is not the container, so merely
opening that callback from the server would not complete authorization. The
browser bridge validates the pasted address against the same live native process
and forwards it only to its fixed loopback listener. Token exchange and refresh
stay in rclone for this default manual mode. Automatic mode uses a shared OAuth
broker with PKCE and one-use state to redeem the code, then continues the same
native rclone account setup and refresh lifecycle. Neither mode publishes the
native OAuth/RC listener on the network. See [authorization details](rclone-accounts.md)
for Microsoft application setup, callback routing and lifecycle constraints.

Method and account selections are preserved in download tasks and workflow
continuations. Only the selected method contributes blocking requirements; a
missing tool for an alternative method does not block a ready choice. Account
configuration generations are part of artifact identity and are checked while the
engine holds the profile lock. Reconfiguring an account requires a new run instead
of reusing the previous account's artifacts. rclone operations freeze their exact
remote manifests before transfer, verify them before/after copying, and publish
only verified files without overwriting unrelated output.

Upstream references checked against the installed rclone 1.75.2 and current
[OneDrive/SharePoint](https://rclone.org/onedrive/),
[headless setup](https://rclone.org/remote_setup/),
[PikPak](https://rclone.org/pikpak/), and
[Pixeldrain](https://rclone.org/pixeldrain/) documentation.

Append **Process and extract downloaded files** (`download.processFiles`) after
**Wait for downloads** to execute those instructions. It processes each selected
content target's exact file manifest separately, carries its archive password,
and keeps the original download and completed sibling targets. Missing plans or
passwords suspend only file processing and can be answered from the run details;
resuming does not enqueue or download the files again. Processed output paths and
file counts are shown separately from the original download results. Private
processing state and outputs live under the current server's AppData
`file-processing/cloud/` directory. This node does not create library resources.

For a complete manual post workflow, use **Read post content → Unlock content →
Extract download information → Check links → Prepare download sources → Select
content and mirrors → Transfer Baidu share → Route selected downloads → Wait for
downloads → Process and extract downloaded files**. Set the output directory in
**Prepare download sources**. Keep source selection explicit when a post has
ungrouped or ambiguous mirrors. The Baidu step passes other providers through.
For already parsed posts, choose a `download.sources` workflow with the same
selection-through-processing suffix in the saved result's download dialog; this
reuses the saved revision without parsing or purchasing content again.

## Reviewed upstream versions

The original adapters were checked on **2026-10-20**; MEGAcmd and PikPak CLI were
reviewed on **2026-10-27**. Versions are fixed until their adapters are reviewed again.

| Tool | Reviewed version | Primary documentation |
|---|---|---|
| BaiduPCS-Go | v4.0.2; commit `225bdd3b6cb298601c4d5ef7104c3e08cd1d692d` | [Release](https://github.com/qjfoidnh/BaiduPCS-Go/releases/tag/v4.0.2), [README](https://github.com/qjfoidnh/BaiduPCS-Go/blob/v4.0.2/README.md), [transfer implementation](https://github.com/qjfoidnh/BaiduPCS-Go/blob/v4.0.2/baidupcs/transfer.go) |
| Bakabase Baidu bridge | `4.0.2+bakabase.5` | [Source, protocol, build script and tests](../src/tools/BaiduPcsBridge/README.md) |
| gdown | 6.4.2 | [Release](https://github.com/wkentaro/gdown/releases/tag/v6.4.2), [README](https://github.com/wkentaro/gdown/blob/v6.4.2/README.md) |
| gallery-dl | 1.32.17 | [Release mirror](https://github.com/mikf/gallery-dl/releases/tag/v1.32.17), [current upstream release](https://codeberg.org/mikf/gallery-dl/releases/tag/v1.32.17), [supported sites](https://github.com/mikf/gallery-dl/blob/v1.32.17/docs/supportedsites.md) |
| Managed Python | CPython 3.13.16 / python-build-standalone 20261013 | [Official runtime build release](https://github.com/astral-sh/python-build-standalone/releases/tag/20261013) |
| rclone | 1.75.2 | [Official release](https://github.com/rclone/rclone/releases/tag/v1.75.2), [PikPak backend](https://rclone.org/pikpak/), [download targets](https://rclone.org/downloads/) |
| MEGAcmd | Linux 2.6.0; Windows/macOS discovery 2.7.0 | [Official project](https://github.com/meganz/MEGAcmd), [public download command](https://github.com/meganz/MEGAcmd/blob/master/contrib/docs/commands/get.md), [transfer control](https://github.com/meganz/MEGAcmd/blob/master/contrib/docs/commands/transfers.md), [Debian 12 packages](https://mega.nz/linux/repo/Debian_12/) |
| PikPak official CLI | 0.6.0 | [Official CLI documentation](https://mypikpak.com/en-US/cli), [official release manifest](https://config.mypikpak.com/config/v1/command_line?client=global), [account and connected-app limits](https://mypikpak.com/en-US/faq) |
| TeraBox CLI | 1.1.0 | [Distribution script](https://github.com/terabox-storage/skills/blob/main/terabox-storage/scripts/install.sh), [authorization](https://github.com/terabox-storage/skills/blob/main/terabox-storage/reference/authentication.md), [commands](https://github.com/terabox-storage/skills/blob/main/terabox-storage/reference/terabox-commands.md), [beta limitations](https://github.com/terabox-storage/skills/blob/main/terabox-storage/reference/notes.md) |

gallery-dl development has moved to Codeberg; use that upstream as well as the
GitHub release mirror when checking future versions. gdown's documented JSON
listing format is beta, which is another reason to review adapter behavior before
upgrading its package.

### TeraBox authorization and download boundary

Reviewed on **2026-10-28**. Install the optional TeraBox CLI dependency, then open
third-party configuration → TeraBox. The official authorization page produces a
one-use code to paste back into Bakabase. This works from a remote browser without
a localhost callback or a host-installed CLI. The server sends the code to the
managed process on stdin; only an opaque account ID and configuration generation
are exposed to the rest of the application. Reconnecting changes that generation.
The pinned CLI reads its default profile despite accepting a `--config` flag, so
Bakabase also isolates its child `XDG_CONFIG_HOME`/`APPDATA` path. Existing user CLI
profiles are not adopted or modified.

This is a beta upstream binary, downloaded from `s5.teraboxcdn.com`. The skill
repository's MIT license is not a license assertion for the CLI binary, whose
source is not published there. Bakabase pins the reviewed archive bytes with SHA-256;
these are local verification hashes, not an upstream signature. Automatic CLI
updates are disabled. The rclone TeraBox pull request remains unmerged, so it is
not treated as released rclone support.

CLI 1.1.0 also writes diagnostic lines before some share-command JSON responses,
even with `--json`. The adapter discards only the reviewed, command-specific
diagnostic formats and requires one complete formal JSON object afterward.
The list diagnostic uses `root=1` at the share root and an empty `root=` for
subdirectories; both shapes are covered by recursive manifest tests.
Those diagnostics can contain signatures and share cookies; they are never
exposed in application logs or API errors. Unknown or truncated output is rejected.

`share-list` is paginated and `share-download` without a file ID does not guarantee
recursive content. Bakabase therefore prepares a complete bounded tree and invokes
the upstream download command for each leaf ID. Downloads and temporary payloads
stay on the chosen download volume. Local receipts bind the account generation,
source, manifest and destination; verified completed files can be reused, while
unverified partial files are retained for review. Metadata availability is separate
from download success. A missing account or ambiguous provider response cannot
authorize bulk disabling of the share.

Online authorization readiness uses the CLI's protected `quota` API; `whoami`
only reads its local profile and cannot establish that a token is accepted.
Quota exhaustion does not block direct public-share downloads. A successful
quota or share-list request still does not prove that the download server accepts
the same authorization.

The current official CLI remains 1.1.0. Its shared-file download appends singular
`access_token`, while the [official integration documentation](https://www.terabox.com/integrations/docs/chinese)
describes plural `access_tokens` for download links. Both forms returned HTTP 403
with upstream error 31045 (`user not exists`) in the reviewed acceptance sample;
changing the parameter alone is not a verified fix. The share remained browsable
and the protected quota API accepted the authorization. This does not establish
an expired share, nonexistent account, subscription requirement or desktop-only
download restriction.

The native CLI discards the response body on download errors. After its exact
HTTP-403 failure, and only when no staged payload exists, Bakabase can inspect the
same native-issued grant once with a one-byte range request. It binds the file ID,
profile and fixed HTTPS download host, disables redirects and limits error bodies
to 4 KiB. Only reviewed error codes reach the UI; tokens, signed URLs and raw
response bodies are never logged. A successful diagnostic response does not
complete or retry the download. A download authorization rejection remains
visible even when metadata is available, and metadata alone cannot authorize its
retry. Actual download compatibility for the 31045 sample remains unresolved.

Existing saved post sources can be recognized without reparsing. A task's reviewed
same-content mirror can be selected through the existing source revision workflow;
the old plan, run, transfer evidence and files remain intact. No automatic source
switch is performed for a running task or one with uncertain prior remote writes.

## Source resolution and execution engines

`DownloadMethodCatalog` declares the provider, supported input form, execution
method, dependency and account requirements. Site metadata used by the creation
dialog is generated from the same declarations. Adding a site does not require
adding another download entry point or workflow trigger.

The source resolver handles reviewed short-link hosts before acquisition identity
or durable download operations are created. Resolution is bounded, rejects loops
and unsafe destinations, and never treats arbitrary HTML as a direct file.
Preflight retains the original address and shows the effective source; confirmation
resolves the original address again. A workflow freezes the resolved locator and
retains `OriginalUrl` and `ResolutionChain` for provenance. Identity and verified
reuse depend on the effective provider locator, not the short-link spelling.

Native public-share traffic uses site/global proxy settings. Link resolution has
an independent **Source link resolver** tool proxy, falling back to the global
setting. Direct connections
resolve and pin public target addresses. An explicitly configured application
proxy is trusted to resolve allowlisted provider hosts; it does not enable
arbitrary target URLs. A fake-IP DNS environment may require configuring an
explicit proxy instead of the system/direct setting. Redirects cannot forward
authorization tokens to another origin, and signed URLs are excluded from HTTP
request logs.

rclone is an optional shared component, installed from reviewed official packages
with SHA-256 verification and advertised OS/architecture requirements. It does not
depend on Python. The controlled remote engine separates private account profiles,
recursive listing, exact-file copying and verified completion receipts from share
resolution. It exposes no arbitrary command or global `rclone.conf` editor. The
remote engine supports controlled PikPak, OneDrive/SharePoint and Pixeldrain
profiles configured through the shared account UI; public-share intake uses its
own resolver and remains a separate capability.
Completed files can be reused after validation; restarting a process does not
promise byte-range continuation of an incomplete individual file.

An upstream backend is not a public-share capability declaration. In particular,
rclone's [GoFile backend](https://rclone.org/gofile/) requires Premium plus an API
token, and its [Pixeldrain backend](https://rclone.org/pixeldrain/) targets the
Filesystem product. Existing public-share methods remain independently selectable
and do not acquire those account requirements merely because rclone is installed.

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

Managed Python is extracted into a fresh private attempt and checked for its exact
version plus SSL, venv and ensurepip support before publication. The optional
Linux `share/terminfo` database is omitted: it contains case-sensitive aliases
that collide on macOS Docker bind mounts, and the upstream runtime does not load
that bundled copy automatically. Bakabase's tools run noninteractively and do not
use it. See the [upstream runtime notes](https://github.com/astral-sh/python-build-standalone/blob/main/docs/quirks.rst#if-special-keys-do-not-work-in-the-python-repl)
and [APFS issue](https://github.com/astral-sh/python-build-standalone/issues/231).
Failed attempts do not poison the next extraction; healthy installed runtimes
and virtual environments remain in place when installation is requested again.

## Selecting a Python base

The shared **Base Python environment** keeps its existing component ID and
`components/cloud-python` location. Managed Python remains the default, including
existing installations. A server operator can instead select an absolute CPython
executable, or an explicit Conda executable and environment prefix. These paths
belong to the selected server; a Docker server needs paths inside its container.

Validation runs the selected interpreter without a shell, checks CPython 3.10+,
64-bit architecture, SSL, venv and ensurepip, and creates a disposable isolated
environment. Applying a source first prepares new private environments for tools
already installed, verifies their imports and fixed versions, and then persists
the selection. Failure or cancellation during preparation leaves the old source
selected. Missing optional tools are installed only when requested.

Conda launches use `conda run --no-capture-output --prefix ...` with explicit
argument boundaries; no global activation or package changes are performed.
Packages install only into Bakabase-owned venvs. Source fingerprints and atomic
environment pointers distinguish their permanent locations. Existing healthy
legacy tool environments are reused only for the default managed Python.
Downloads capture their executable and launcher before awaiting execution and
continue using that capture if another client changes the selected source.
Source changes retain old environments and never replace an active venv.

An external base remains necessary even with `venv --copies`. Moving/removing or
replacing it can invalidate its tool environments; revalidate and apply the
source to rebuild them. See the official [venv lifecycle documentation](https://docs.python.org/3.13/library/venv.html)
and [Conda execution documentation](https://docs.conda.io/projects/conda/en/latest/commands/run.html).
Configuration endpoints permit the host, paired devices and unrestricted clients;
nonpaired restricted callers cannot configure or execute a server interpreter.

Windows 7-Zip installation extracts the official installer's embedded payload into
Bakabase's component directory and verifies its executable before publication.
It does not run the installer or modify the registry.

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

Bridge `4.0.2+bakabase.3` distinguishes cancelled and expired Baidu shares from
access-code verification failures. It checks the final official error-page URL
before parsing share identity or sending verification, using the same account
HTTP client and proxy. Official error type `1` reports `BAIDU_SHARE_CANCELLED`,
`10` reports `BAIDU_SHARE_EXPIRED`; other error pages remain unavailable.
Incomplete page identity and malformed verification responses are rejected,
including nominal success without a nonempty verification proof. A cancelled or
expired share needs a new valid source link; changing its access code does not
restore it. Offline fixtures cover both normal page layouts and these failures
without real credentials. The upstream version and source pin are unchanged.

Bridge `4.0.2+bakabase.5` also verifies existing complete files before resuming a
download. The 2026-10-24 upstream review confirmed the same v4.0.2 source pin.
Reuse requires the expected size and either a full-file MD5 matching a single
original `block_list` hash, or every original block hash matching a supported
chunk layout that covers the entire file in order. Missing listing metadata is
fetched for the same remote file.
An encoded `md5` field, file size alone, or a leftover continuation sidecar is
not completion evidence. Unverifiable files keep the upstream download behavior,
and incomplete files keep their range checkpoints. Progress includes verified
complete files and valid checkpoint ranges. Verification does not delete files
or sidecars; upstream downloads retain their normal checkpoint lifecycle.
Available block checksums are checked again after downloading, so a successful
upstream continuation cannot publish a file with known corrupt completed ranges.

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

## Durable artifacts and repeat execution

File operations now use the shared workflow artifact store rather than a workflow-run cache. An operation has a semantic key, a renewable execution lease, an immutable exact SHA256 manifest, output lineage, and independent consumer references. File locations distinguish available, quarantined, missing and modified. Workflow consumers remain active while a run is pending, running or waiting; ordinary consumers use expiring leases. Completed workflow history can be pruned without deleting artifact receipts.

The downloader adapter identifies a source by provider locator, optional provider version, account scope and access scope. UI IDs, post IDs, revisions, groups and workflow run IDs do not identify content. Output placement is an explicit intent scope. Without a remote version, **reuse verified** means reuse previously acquired content after validating every local file; it does not promise the share has not changed remotely. Choose **force acquisition** to obtain current remote contents, or **force processing** to recompute file outputs. Neither policy overwrites previous output generations.

Upstream materializers can ask a downstream artifact-demand activity to satisfy its final requirement first. Only explicitly transparent activities may be traversed; unknown transforms, selection changes, placement/account overrides and force-acquisition steps are planning barriers. This allows a valid extracted result to satisfy a repeated request even when its original archive has been quarantined. This planning contract is in the workflow module and contains no post or provider routing rules.

Existing completed cloud downloads can be adopted on read only when the normalized source and destination scope match and their saved fingerprint still matches the exact files. No migration backfills historical outputs or scans unrelated directories. Known incomplete processing instructions can be resolved once by a user; their approved recipe is retained with the artifact. Changing a concrete recipe creates a different processing intent.

### File processing and recursion

`download.processFiles` is a downloader adapter to `FileArtifactProcessor`. `fs.processArtifacts` exposes the same processor for local/acquisition work items, including the existing manual `fs.processingPlan` trigger. Both use the source-independent `FileProcessingPlan` model; the old post extraction names remain serialization-compatible wrappers.

Optional recursive extraction discovers only archives in the exact supplied manifest and newly committed outputs. Each archive group has its own persistent checkpoint, parent link, input fingerprint and output manifest. Completed siblings survive missing-password waits, budget waits and interruption. Password answers bind to the pending archive ID. Explicit plans can first fix disguised names or extensions before recursion.

The default limits are 8 levels, 1,000 archive groups, 10,000 produced files, 20 GiB expanded data and 1 GiB minimum free space. Reaching a limit suspends the operation; it is never reported as complete. Limits can be increased when resuming. During extraction a periodic monitor and progress callbacks cancel an over-budget layer. This is a sampled guard, not a filesystem hard quota: a small amount can be written between checks. Documents and application containers such as DOCX, EPUB, JAR and APK are retained instead of being unpacked merely because they contain ZIP data.

### Recoverable cleanup

`download.cleanupArtifacts` is a separate, retryable node. Its default policy is `keep`. `quarantine` verifies the committed output and per-file coverage, fences new consumers and refuses active consumers, then moves only the managed input artifact's exact files to a same-volume `.bakabase-quarantine/<receipt>` directory. Explicit `delete` additionally requires verified final publication before retiring those exact source files. Neither policy authorizes deleting an entire directory. Original download files, processing copies and final outputs have distinct ownership.

### Per-file preservation and replacement

Cleanup permission and file disposition are separate. A caller may allow cleanup, but each input still needs a content-bound receipt showing where it was preserved or what successfully replaced it. Processing plans and LLM output cannot provide deletion receipts or mark arbitrary files disposable.

The executor carries untouched siblings forward. For `game.zip` plus `license.key`, extracting only the ZIP produces the extracted files plus the unchanged key. Only actual archive members consumed by successful extraction are marked replaced. Rename/move preserves content while changing its relative location. Empty or unproven replacements retain the input.

Execution receipts bind measured input/output manifests and record every required descendant. Dynamic workflow boundaries freeze their receipts; nested extraction composes them without dropping sibling files or replacement branches. If an archive produced two files, delivering only one cannot justify removing the archive. Final publication verifies bytes again and records preservation at the destination.

Before quarantine or deletion, coverage is checked for every registered source file through to the verified final results. Missing evidence, modified manifests, missing descendants or other consumers retain the original. Legacy records can prove preservation only with matching relative paths and content; missing transformation evidence is not reconstructed from the old plan and does not trigger another download or extraction. `FILE_CLEANUP_COVERAGE_INCOMPLETE` means the originals remain retained while final delivered files are unaffected.

Private intermediate reclamation follows the same rule: only recorded, verified files whose complete successors survive are eligible. Unknown files and checkpoints without sufficient execution evidence remain retained. File hashes and receipts demonstrate storage integrity and transformations, not that an application has been launched or is semantically usable; ambiguous processing instructions still require user review.

Cleanup receipts survive restart. The run result and history show the receipt and a restore action; restoration checks hashes, refuses overwrites and can continue an interrupted move. `GET /artifacts/cleanup` lists owner-only receipts; `POST /artifacts/cleanup/{receiptId}/restore` restores one. Cleanup failures suspend only cleanup: the committed processing artifact is reused on retry.

## Acquisition intake, provenance and delivery

The shared acquisition form consumes `DownloadCandidate` values through a source adapter. Its goals are files only, acquisition into the library, and saving leads. A saved post is one adapter: the server re-reads the selected result indices and revision instead of trusting client-provided addresses or credentials. Unknown web pages remain manual leads unless an explicit supported method is chosen. Method readiness is local setup validation, not a promise that a remote share is still accessible. Submission repeats readiness and revision checks before starting work.

The method registry describes prerequisites independently of execution: native HTTP/BitTorrent, optional cloud tools and explicitly mapped site downloaders retain their existing download-center identities. Missing tools, unsupported platforms and account requirements remain visible; immediate download is disabled until they are ready. Saving valid leads does not require an installed tool or a current login. Acquisition tasks persist prepared variables and source references so queueing and restarting do not discard the selected account or destination.

`WorkflowSourceRef(module, entityKind, entityId, revision, resultKey)` expresses provenance only. It is independent of source deduplication and authentication. Runs persist multiple source links, including repeated consumers of an existing operation; parsing history is not replaced by the latest download run. Historical typed payloads can be indexed at runtime, without data migration SQL. The source-history API batches projections from existing runs and download-center tasks rather than maintaining another execution state machine.

File processing defaults to `archivePolicy: instructions`. An unknown plan or a required plan with no steps waits for confirmation. `recursive.enabled` only permits deeper extraction after an initial extraction has been approved; it does not approve the initial files. An explicit `autoDetect` policy authorizes archive discovery, while `keep` preserves the supplied files. The revised intent version prevents pre-policy artifacts from silently satisfying a request under the new rules. A concrete answer to an incomplete plan is retained with that logical request and can be reused after manifest validation. Explicitly choosing a different archive policy creates a separate request intent.

`fs.publishArtifacts` is a separate delivery operation. It copies only the chosen artifact's exact manifest to a user destination outside AppData, verifies every file, never moves a shared input and never overwrites a conflicting destination. Its default cloud destination is a stable source-specific `result` directory beneath the requested output root. Conflicts suspend delivery for correction. Verified published artifacts participate in downstream demand planning, so available delivered files can satisfy a repeat request even if temporary processing files were retired. Unknown transforms remain planning barriers.

Restored quarantine receipts remain in AppData for recovery/history. A bounded startup/periodic cleanup removes only empty directories proven to belong to those receipts. Unknown files, including filesystem metadata, prevent removing their containing directory.

The post-local-file action uses a separate boundary adapter with the same trusted source-reference mechanism. It rechecks the saved revision and result index and starts the ordinary filesystem trigger with server-stamped references. The generic filesystem trigger does not parse post-specific binding strings, and the public manual-run endpoint cannot manufacture source associations through JSON. Missing instruction plans may enter a workflow and wait before making file changes.

## Download center and local processing boundaries

The download center embeds native HTTP/BT/torrent-file, site and cloud-provider forms in one creation dialog. Cloud providers use `POST /download-center/cloud/preflight` and `POST /download-center/cloud/tasks`, which accept only the files goal and reject custom workflow IDs. The fixed built-in recipe selects sources, performs provider-required transfer, enqueues downloads and waits for completion. Its node sequence and default configuration are checked before execution so an older recipe cannot override the validated destination or account. Library acquisition and lead intake remain separate orchestration entry points.

Local-file processing first binds each chosen parsed source to a server directory selected through the filesystem browser. It does not infer that an ungrouped link is a mirror or scan arbitrary folders to match filenames. One mirror is normally sufficient for a content group; independent content needs its own binding. The selection describes a directory, including the complete set of split archive parts when needed. It never starts another download.

This UI submits no individual file selection, so the filesystem trigger recursively includes the chosen directory's files. There is no required filename derived from a post title or download link. A processing step can explicitly select a filename, relative path or glob; if nothing matches, execution waits for the instructions to be corrected rather than guessing or marking processing complete.

The workflow page and related-workflow dialog share the same browser, labels and trigger filters. The local-processing preview reads the selected definition, configured activities and saved extraction instructions; unsupported or dynamic actions remain explicitly uncertain. The current default local recipe contains only `acquisition.unpack`: it preserves source files and does not automatically publish, import, or clean them. A custom recipe must explicitly add those capabilities.

## Rclone execution boundaries

The optional rclone dependency remains pinned to **v1.75.2**, checked against the
[official release](https://github.com/rclone/rclone/releases/tag/v1.75.2) on
2026-10-20. The controlled engine accepts only reviewed PikPak, OneDrive and
Pixeldrain profile fields. Profiles are isolated by provider and account beneath
AppData; OAuth tokens, passwords and API keys never appear in account responses,
process arguments, workflow state or upstream error text. Configurations are
private files; `rclone obscure` is reversible and is not a substitute for those
file permissions. A configuration generation pins enumeration and copying to the
same identity. Download routing uses the rclone tool override, then the actual
source site's proxy, then the global proxy.

The [Pixeldrain backend](https://rclone.org/pixeldrain/) implements its filesystem
product. Anonymous public **`/d/<directory-id>`** locators can be listed and copied;
free `/u` files and `/l` lists require their separate method. The pinned backend's
[source](https://github.com/rclone/rclone/blob/v1.75.2/backend/pixeldrain/pixeldrain.go)
uses **`root_folder_id`**, despite an older interactive documentation example
showing `directory_id`. Its personal `me` filesystem requires an API key and the
provider's applicable subscription. This integration does not expose arbitrary
backend URLs, configuration text or rclone command arguments.

[PikPak](https://rclone.org/pikpak/) account remotes request original files with
`no_media_link=true`; incoming public share resolution remains a separate
capability. [OneDrive](https://rclone.org/onedrive/) uses an OAuth token and an
explicit drive with read-only scopes. Neither account-remote support implies
that arbitrary share links can be imported or downloaded without a resolver.

Copying freezes an exact remote manifest, checks it again before and after the
copy, validates available remote hashes and local SHA-256, and publishes without
overwriting different files. Verified staged results can be reused. Cancellation
kills and drains the child process; unverified partial stages remain isolated,
and byte-range continuation is not promised. An official macOS arm64 binary has
been checked against its pinned SHA-256 and exercised against local directory and
single-file remotes. This local fixture is not live account or cross-platform
acceptance; user accounts must be configured and verified separately.

## Node output and recovery history

Workflow node executions are independent of the resumable item snapshot. Each entry
records a stable frozen node identity, attempt, lifecycle, counts and bounded output
metadata. Resuming appends an attempt and retains earlier node statistics. Existing
historical runs are not backfilled with invented outputs. Large file lists reference
the existing artifact ledger; the run-bound manifest endpoint paginates those records
without scanning arbitrary directories or rehashing files.

Items explicitly implement `IWorkflowOutputMetadata` for artifact, remote-location,
task-reference, scalar and warning blocks. Arbitrary execution objects are not reflected
into UI metadata. The legacy final-output field now accepts only explicit safe summaries;
credentials, plans containing passwords, signed URLs and checkpoint payloads are not a
display contract. Available ledger state describes the stored receipt, not a new integrity
check performed while viewing history.

## Processing preflight and bounded archive detection

Source intake checks file-instruction completeness only when the chosen workflow declares
`IFileProcessingPlanConsumer` with the `instructions` policy. Download-only and lead intake
remain usable without processing instructions. An explicit user edit affects this request;
the post adapter still reloads the saved addresses, credentials and revision and never
rewrites the saved parsing result. Complete syntax does not certify future filenames or
remote availability. Once the exact local manifest exists, a zero-match first selector
waits before making a working copy.

The existing analysis call records requirement, ordered operations and source evidence.
A resource being called an image pack is not evidence of an archive, and size alone does
not authorize extraction. Extension and short header checks stay lightweight. A bounded
7-Zip listing can detect an embedded archive behind an image prefix only in an explicitly
authorized extraction step whose input is a single file or an exact literal selection.
Automatic directory discovery and recursive image outputs do not invoke this deep probe
per file. File size may inform ordering but never proves a format or silently excludes a
small explicitly selected file.

One plan attempt may probe at most four candidates, with five seconds per probe, fifteen
seconds total and 64 KiB of process output per probe. Timeout, output overflow, exhausted
budget or ambiguous scope requires corrected instructions; these conditions are not
reported as successful detection of no archives. Probe subprocesses use no shell, receive
no interactive input and are cancelled and drained before returning. Normal extraction
still uses the existing password, path, volume and recursive-budget protections.

Renaming a split-archive member can carry unchanged members of that same recognized
family from the supplied manifest into its private operation directory. It does not scan
neighbouring files, include unrelated families, rename originals or change the ordinary
rename/move output scope. Completed old checkpoints remain immutable; revised concrete
instructions create their own processing state.


### Runtime storage reserve

Acquisition settings expose `minimumFreeSpaceBytes` (default 100 GiB). This is
an admission and runtime write guard, not part of an artifact key or frozen
processing plan: changing it also affects subsequent writes by existing tasks.
Each actual destination volume is checked independently, including processing,
download caches/staging and final publication. The generic writer receives an
owner context; the host resolves acquisition ownership and supplies the live
policy. Independent downloads retain their existing policy.

Falling below the reserve suspends incremental writes with
`LOCAL_STORAGE_RESERVE_LOW`. Running external writers are cancelled and awaited;
existing files, transfer receipts and download identities are kept for diagnosis
and continuation. Reads, cleanup and same-volume atomic moves do not consume the
reserve. Zero disables the additional acquisition reserve; it does not bypass
filesystem errors. The reserve is a lower-water mark, not a promise that an
archive's expanded contents or a complete download will fit.

Directory settings show a read-only capacity snapshot of the server-side volume:
available and total bytes plus the available percentage. Queries have a bounded
response time and native-probe concurrency; unreachable or unknown storage is
not reported as zero capacity. These figures do not reserve disk space or replace
the runtime write guard. Legacy library-placement fields are no longer exposed
by the acquisition settings/wizard; saving modern settings leaves their stored
values intact.


### Final archive review

New acquisition plans that explicitly extract archives include an independent
`fs.assessDelivery` node after their declared processing steps and before
publication. It examines only the recorded output manifest, with a maximum of
eight files and bounded header reads. A complete single archive group with a
matching file signature and no unrelated sibling files can be treated as outer
packaging. Every additional layer must pass the same check. Automatic continuation
is limited to three layers/groups, 10,000 generated files and 20 GiB of cumulative
expanded content, as well as the live storage reserve. The persisted operation
retains these counters across retries. Ambiguous groups, unknown companions and
exhausted budgets require review. This does not scan a game directory for internal
ZIPs, unwrap document/application formats or authorize deleting anything.

The user may explicitly retain these archives as the final result or supply
additional processing instructions. Continuation is bound to the current
artifact ID and fingerprint and uses only those retained files. Password and
storage interruptions resume that continuation; they cannot reacquire the
original source. Automatic outer-layer processing uses the archive's contents
as its output root and preserves the archive's internal directory structure;
author-created directories are not flattened by guessing from their names.
Existing running workflow snapshots and previously completed tasks are not
silently rewritten or re-executed by this check.

Completed tasks can explicitly create a new local-processing revision from a
verified processed or published artifact. The user can opt into the current
acquisition processing and delivery directory defaults for that new revision.
Its collision-protected delivery directory is separate from the old revision.
After successful publication, Bakabase may retire an older delivery used as its
input only after verifying that every old file is accounted for in the new
result and there are no other references or unregistered files. The old run,
manifest and retirement record remain available. To keep the superseded files
as well, enable **Keep superseded delivery files** in acquisition settings.
Existing historical directories are not swept when this setting is changed.
Borrowing a completed artifact cannot create a new download.

### Which output directories are kept?

The Baidu transfer node's local output layout controls the directory presented
to downstream nodes. By default it omits the task folder created for the
transfer; the alternative retains it. Both modes use the same isolated remote
staging area and preserve the shared files' original names and directories.
An unknown directory is never assumed to be a temporary transfer folder.

New extraction nodes place the archive contents at the archive's relative
location by default. To group them under a new folder named after the archive,
choose that layout on the node. An individual extraction step can override the
node setting; otherwise nested steps inherit it. Neither mode flattens folders
inside the archive. Conflicting output names require review instead of
overwriting a file. Changing a workflow does not rearrange existing deliveries.
