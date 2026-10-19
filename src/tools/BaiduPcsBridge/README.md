# BaiduPCS-Go JSON bridge

Reviewed against the latest upstream release **v4.0.2** on 2026-10-19:

- [Release](https://github.com/qjfoidnh/BaiduPCS-Go/releases/tag/v4.0.2)
- [Account, transfer and download documentation](https://github.com/qjfoidnh/BaiduPCS-Go/blob/v4.0.2/README.md)
- [Pinned source](https://github.com/qjfoidnh/BaiduPCS-Go/tree/225bdd3b6cb298601c4d5ef7104c3e08cd1d692d)

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
Cross builds use the normal Go `GOOS` and `GOARCH` variables; run tests natively
before setting them for the final `go build`.

## Protocol and behavior

`--version` reports `4.0.2+bakabase.1`. Otherwise one JSON request is read from
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
- `transfer`: `operationId`, `url`, `accessCode`, `remoteDirectory`; stores the
  share in its own deterministic subdirectory and returns an exact recursive
  manifest. Top-level share items and owned directories are paginated. A four
  character access code is required by this upstream version. Both `pan.baidu.com`
  and `yun.baidu.com` standard shares and `/share/init?surl=...` links are accepted.
- `download`: `remotePath` and absolute `outputDirectory`; downloads existing
  owned files/directories without implicitly transferring a share. Enumerates a
  complete manifest before downloading and checks every upstream task's outcome
  and final size. Empty directories are preserved. Unsafe paths and symlinks are
  rejected. Progress is approximate bytes written; final success uses task status
  and verification. Killing the bridge leaves upstream continuation sidecars.

Per-account access is serialized in the host. Transfer journals record intent
before the remote mutation and persist a completed receipt after verifying the
target. Repeating a completed operation returns its receipt. If interrupted after
the remote request begins, the operation requires confirmation rather than
blindly transferring again. The host can recover completed receipts after its
own persistence boundary. Logout clears local credentials and retains operation
journals; it does not revoke sessions on other devices.

Tests do not require credentials. Live login, remote transfer and download need
a user-provided Baidu session and test share and are separate acceptance checks.
