# Explicit ZIP password encoding

This adapter is used when an extraction step explicitly specifies
`ZipPasswordCodePage`, or an already-authorized extraction candidate has a ZIP
structure proven beyond the normal archive tool's bounded prefix opening range.
Ordinary discovery and default extraction remain on the normal archive engine.
The setting encodes a **known password** into bytes; it never guesses passwords
or encodings and is independent of entry-name decoding.

The managed optional `pyzipper` dependency uses the selected base Python source
(managed, external, or Conda) and an isolated package environment. The validated
version is **0.4.0**, checked against the upstream release and source on
2026-11-04. Installation or repair belongs to normal dependency management.

## Supported scope

- Ordinary single-volume ZIP, with its first local header at byte zero, for an
  explicit password encoding.
- A structurally verified media-wrapped single-disk ZIP or ZIP64 through an
  internal read-only seek window, excluding its prefix and bounded trailing data.
- WinZip AES AE-1/AE-2, 128/192/256-bit encryption; stored and deflate members.
- Traditional ZipCrypto; stored and deflate members, with complete EOF CRC and
  size validation. The same explicit encoding setting supplies known password
  bytes; no alternate encoding or password is tried automatically.
- Unencrypted companion entries and empty directories in the same archive.
- Explicit CP437, CP932, GBK/CP936, Big5/CP950, or UTF-8 password bytes.
- UTF-8 names and verified Info-ZIP Unicode Path extras take precedence over the
  explicitly selected legacy entry-name encoding.

Split input volumes, parser mode, other compression/encryption methods, more than
10,000 entries, a central directory above 8 MiB, or declared expanded contents
above 64 GiB are rejected. Plain password-encoding mode still rejects ZIP64 and
prefixes. Rejection retains the input and requires a different processing plan;
it is never interpreted as a bad password.

The verified media-wrapper path is narrower: at most 128 entries, 64 KiB of
central-directory metadata, 32 KiB of trailing data, four end-record candidates
and 384 KiB of metadata reads per validation. ZIP64 must use one fixed end record
and consistent single-disk offsets and sizes. The C# probe emits an internal
proof containing source length, boundaries and metadata hashes; Python verifies
it independently before giving a bounded seek window to pyzipper. No large prefix
scan, stripped-archive copy, inferred password or ordinary-media search is used.

## Integrity and isolation

pyzipper owns cryptography and decompression. Full EOF reads verify its AES HMAC
and applicable CRC, or ZipCrypto CRC, before creating an output directory, then verify again while
writing. The adapter bounds framing and expansion, checks central/local header
agreement, counts decoder output before pyzipper can clip it to a declared size,
and rejects overlapping members, symlinks/reparse points, traversal, device
names, duplicate/case-colliding paths, or a nonempty output directory. Resolved
paths are limited to 32 components and 1,024 UTF-8 bytes, bounding the parent-path
collision index even when a Unicode Path extra expands a legacy filename.

The workflow supplies a private staging directory and publishes only after the
adapter succeeds. A failed or canceled extraction can leave uncommitted staging
files, but cannot overwrite existing outputs or remove the source. Cancellation
terminates the child process tree. Passwords travel through stdin, not command
arguments, and the subprocess returns only closed error codes and progress.

The package does not provide a network service and receives no download URLs.

## Verification

`ZipPasswordCodePageTests` generates small synthetic encrypted ZIPs, authenticates
and extracts all AES strength/method variants, and checks corruption, unsafe
paths, encoding identity, missing dependencies, and cancellation. Set
`BAKABASE_TEST_PYZIPPER_PYTHON` to an isolated Python environment containing
`pyzipper==0.4.0`; no production archive is used by these tests.
`StructuredZipCompatibilityTests` covers sparse multi-GiB wrappers, ZIP64,
bounded trailing data, changed metadata, corrupt payloads, companion files and
dependency suspension, using tiny synthetic payloads and no production files.
`ZipCryptoCompatibilityTests` uses inert Info-ZIP fixtures with GBK password
bytes to cover stored/deflate members, data descriptors, incorrect encodings,
CRC and header corruption, strong encryption, and split-volume rejection.

## Upstream references

- [pyzipper repository and release history](https://github.com/danifus/pyzipper)
- [AES reader implementation](https://github.com/danifus/pyzipper/blob/master/pyzipper/zipfile_aes.py)
- [PyPI release](https://pypi.org/project/pyzipper/0.4.0/)
- [WinZip AES format](https://www.winzip.com/en/support/aes-encryption/)
- [PKWARE ZIP specification](https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT)

The 0.4.0 release includes the upstream CRC policy fix for CVE-2026-44722;
dependency updates require revalidating this adapter and its integrity tests.
