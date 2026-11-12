# Download-tool proxies

Reviewed against current official documentation and source on **2026-10-20**.

The network settings contain a `downloadToolProxies` dictionary using stable tool
IDs: `baidu-pcs-go`, `gdown`, and `gallery-dl`. Each value uses the same proxy
selection model as the existing global, source and BT settings.

Routing precedence is an explicit tool selection, then the calling source's
selection, then the global selection. Removing a tool entry restores inheritance.
`DoNotUse` explicitly connects directly; it never falls through to a lower
selection. System selections honor the system proxy's bypass rules. An unavailable
custom proxy or unsupported protocol fails the tool request rather than silently
connecting directly. These tool settings do not change independent BT routing or
dependency installation: dependency source archives, Go modules and pip installation keep
their existing global network configuration.

PATCH `options/network` accepts `downloadToolProxies`: omitted/null leaves the saved
map unchanged, `{}` clears it. Unknown tool IDs, invalid modes and missing custom
proxy references are rejected before saving. Removing a shared custom proxy clears
its tool/source overrides (which resume inheritance), and resets global/BT
selections referring to it to direct. Other overrides remain unchanged.

The build-time `DownloadToolProxyTargets` declaration is shared by backend routing
and save validation and exported by the existing constants generator. The UI does
not maintain another protocol table or fetch static tool metadata at runtime.

| Tool | Protocols enabled by this adapter | Official evidence |
| --- | --- | --- |
| BaiduPCS-Go v4.0.2 | HTTP, HTTPS, SOCKS5, SOCKS5H | [Upstream requester transport](https://github.com/qjfoidnh/BaiduPCS-Go/blob/v4.0.2/requester/http_client.go), [proxy selection](https://github.com/qjfoidnh/BaiduPCS-Go/blob/v4.0.2/requester/dial.go), [Go transport protocol support](https://pkg.go.dev/net/http#Transport) |
| gdown 6.4.2 | HTTP, HTTPS, SOCKS4, SOCKS5, SOCKS5H | [Current proxy usage documentation](https://github.com/wkentaro/gdown#resume-speed-limit-proxy), [Requests dependency with SOCKS support](https://github.com/wkentaro/gdown/blob/main/pyproject.toml), [Requests proxy documentation](https://requests.readthedocs.io/en/latest/user/advanced/#proxies) |
| gallery-dl 1.32.17 | HTTP, HTTPS, SOCKS4, SOCKS5, SOCKS5H | [Extractor proxy configuration](https://gdl-org.github.io/docs/configuration.html#extractor-proxy), [file-download proxy configuration](https://gdl-org.github.io/docs/configuration.html#downloader-proxy), [upstream optional PySocks dependency](https://github.com/mikf/gallery-dl#dependencies), [urllib3 SOCKS transport](https://github.com/urllib3/urllib3/blob/main/src/urllib3/contrib/socks.py) |

The Python dependency installer already installs `requests[socks]` for both tools.
Requests distinguishes SOCKS5 client DNS from SOCKS5H proxy DNS; Go treats both
SOCKS5 spellings as proxy DNS. Baidu's Go transport does not support SOCKS4.
This change does not upgrade the pinned download-tool versions.

Baidu's isolated bridge receives its resolved proxy only in child-process
environment variables, with ambient proxy/bypass variables removed. The Python
adapter receives the credential-free endpoint and separate credentials over
private stdin. It disables Requests' environment/.netrc lookup and gallery-dl's
`proxy-env`; proxy credentials are excluded from process arguments and diagnostic
strings. Third-party stderr remains discarded, and unsafe third-party exceptions
are reduced to safe messages. No proxy credentials are added to operation receipts.

Regression coverage includes precedence, explicit direct and bypass behavior,
live configuration changes, unknown references, protocol compatibility, deletion
cleanup, separate BT/global routing, and credential escaping/private transport.
These tests use local fixtures; they do not establish live connectivity to an
authenticated proxy or cloud account.
