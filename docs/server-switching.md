# 服务器切换：在一体版中管理其他设备

一体版是所有 PC 的唯一桌面产品：它始终运行本机服务端，同时可以把主窗口切换到已配对的其他服务端（NAS，或另一台 PC 上的一体版），并像原纯客户端一样完全控制它。无界面的 NAS/Docker 镜像只会被管理，不会管理其他设备。纯客户端已移除，旧安装留下的配对由一体版直接导入。

与[联合媒体库](multi-device-library-implementation.md)的区别：联合视图把多台设备的资源合并在一起只读浏览；服务器切换则是整窗切到某一台设备自己的界面，在那里做任何管理操作。两者使用不同的凭据，互不替代。

## 用户流程

1. **允许被管理**（在被管理的设备上）：设备页“允许其他设备管理本机”——开启远程访问并要求配对。该设备若处于“无限制”模式（NAS 默认），界面只提示“局域网内任何人无需配对即可完全控制”，不会自动修改。
2. **添加可管理设备**（在一体版上）：设备页“可管理的设备”中输入地址或从附近设备中选择。“附近设备”通过远程访问的局域网广播（UDP 探测与 mDNS，与原纯客户端相同）查找，不要求对方开启媒体库共享，也不会列出本机。可填写对方显示的配对码直接完成；不填则向对方发起请求，对方在通知中心或设备页批准后，本机在后台自动领取，无需停留在页面；某次领取暂时失败（网络中断、对方限流）不会结束等待。
3. **切换**：左侧菜单顶部的下拉框列出“本机”和所有可管理的设备，选择即把主窗口切到那台设备自己的界面；下拉框标明当前正在管理哪台设备。托盘菜单也提供同样的切换，用于对方版本较旧、界面里没有切换器的情况。
4. **本机动作留在本机**：在被管理设备的界面里播放、打开文件夹、登录第三方站点等动作，都在当前这台电脑上执行，使用本机的播放器设置，并按每台设备各自的路径映射把对方路径换成本机路径。
5. **停止管理**：设备页“停止管理”会尽力让对方撤销本机的配对，然后删除本机保存的密钥并关闭中继。对方也可以随时在自己的设备页撤销。
6. **导入旧纯客户端的配对**：纯客户端已移除（测试版期间直接移除，不提供停止维护提示）。一体版首次启动时自动读取本机旧纯客户端留下的配对（含密钥与路径映射）并导入，已存在的设备不覆盖；设备页可再次手动导入。只读取，从不修改旧安装的文件。

## 设计

- **显示对方自己的界面**：窗口加载的是目标设备自带的前端，界面与接口永远和对方版本一致。本机前端无法驱动另一台服务端：前端按同源构建，请求签名也不能在页面里完成。
- **每台受管设备一个本机中继**：`Bakabase.Remoting` 为每台设备启动一个精简的 `WebApplication`，独立容器，只监听 `127.0.0.1`，端口按设备固定（浏览器存储以源区分）。中继用该设备的密钥签名转发所有请求，并拦截必须在本机执行的动作。
- **中继不进入本机服务端**：中继与进程内服务端并列，不在其容器或管线中——否则本机动作拦截会截走本机服务端自己的播放、打开路由。服务端只通过 `IManagedServerService` 接口访问中继；无界面版本不注册该接口，相关接口返回“不可用”。
- **切换就是导航**：前端请求一个地址（本机为 `/federation/local/servers/{id}/open`，中继内为 `/client/switcher/{id}/open`），然后 `location.assign`。
- **中继内的切换器标明各设备的状态**：`GET /client/switcher` 返回 `{currentId, targets: [{id, name, isLocal, isCurrent, state}]}`。`state` 为 `ManagedServerState` 的数值：`0` 未知、`1` 在线、`2` 离线、`3` 已撤销；**本机条目没有 `state`**（本机就是应用自身）。当前中继所对应的设备取自最近一次经由该中继转发的请求的结果（`UpstreamStanding`）：对方网关未拒绝的任何应答都算在线（即使是 404/500，签名已被接受）；`DeviceRevoked`/`Unauthenticated` 为已撤销；`Disabled`/`SignatureExpired`，或由对方或网络导致的转发失败为离线；中继自己的应答、浏览器主动断开不影响状态。尚未转发任何请求前，退回为管理器最近一次探测的结果（配对也会更新），其他受管设备同样使用该结果。只读内存，不等待网络或磁盘。
- **完全控制**：管理使用旧的已配对设备权限（`Bakabase-Device` 签名，`/remote-access/pair/*`），与只读的联合授权无关。
- **请求的等待状态**：`/federation/local/servers` 列出的待批准请求中，`outcome` 是最近一次领取的结果，`active` 表示本机是否仍在等待；`Unreachable`/`TooManyAttempts` 只是暂时失败，`active` 仍为真。批准、拒绝、过期或取消后 `active` 为假（批准后请求从列表中移除，设备出现在可管理列表里）。
- **查找可管理设备**：`GET /federation/local/servers/discover` 使用远程访问信标（约 3 秒，仅在用户请求时发出），排除本机，标出已管理的设备；联合媒体库的设备发现只列出开启了共享的设备，不适用于管理。

## 安全边界

- 密钥只保存在 `AppData/remote-access/managed/connection.json`（Unix 权限 0600），不进入配置、接口返回、日志或 `/client` 响应。
- 中继只服务本机窗口：校验 `Host`；来自其他站点的浏览器请求（`Sec-Fetch-Site` 为 `same-site`/`cross-site`）一律拒绝，唯一例外是携带该中继一次性导航票据的顶层导航：票据用后即失效，中继返回一个小页面，用 `location.replace` 跳转到去掉票据的同一地址，并追加 `location.hash` 保留 `#` 后的前端路由（不用 302：浏览器按整条重定向链计算 `Sec-Fetch-Site`，重定向后的请求仍是跨站，会被拒绝）。没有 Fetch Metadata 的请求（本机播放器）保持原行为（WebSocket 握手除外，见下）。`Cookie`、`Authorization` 不会转发给对方。
- WebSocket 握手按 `Origin` 判断（中继与本机服务端都是如此）：Chromium（因而 WebView2）在 WebSocket 握手中**不发送任何 Fetch Metadata**，只有 `Host`、`Upgrade` 和 `Origin`；握手又是 GET，`Sec-Fetch-Site` 规则和“非安全方法”规则都看不到它，而建立后的通道不受任何 CORS 检查。中继拒绝 `Origin` 存在且不是自身页面（本端口上的 `127.0.0.1`、`localhost` 或 `[::1]`）的握手（`Upgrade: websocket`、`Sec-Fetch-Mode: websocket` 或 HTTP/2 扩展 `CONNECT`），判为 `ForeignOrigin`（400 `ForeignCaller`），在读取 Fetch Metadata 和票据之前完成，握手永远不会消耗票据；没有 `Origin` 的视为原生客户端，保持原行为。否则本机任何浏览器中的任何页面（本机窗口、其他中继页面、任意网站）都能经中继以本机密钥签名打开对方的 hub，读取其推送的全部配置。
- 中继把自身页面作为对方自己的界面转交：中继从自身源放行的请求，转发时 `Origin` 改为对方服务端自身的源。另一台机器上的服务端不读取 `Origin`；同一台机器上的服务端（另一个安装、host 网络的容器、SSH 隧道）把中继视为回环调用者，并同样按 `Origin` 判断握手，若不改写就会拒绝其自身界面经中继的 hub；此时它的 `/federation/local` 也会像对待自身窗口一样应答该页面。外来的 `Origin` 从不改写。
- 中继不向对方页面暴露本机诊断信息：已移除的纯客户端曾在 `/client/log*`、`/client/app/*` 提供自身的日志、应用目录路径和打开目录，受管设备的旧版界面仍可能请求；一体版中这些是本机整个应用的日志（含本机服务端打印的配对码、所有受管设备的地址）和保存所有密钥的数据目录，中继一律返回 404。纯客户端的连接与配对接口返回 409 `ManagedByHost`。
- 本机服务端不信任其他回环源：中继页面运行的是对方的脚本。本机服务端（`LoopbackCrossSiteGuard`，所有回环请求，403 `HostOnly`）拒绝以下请求，例外只有 CORS 白名单内的来源（本机 `ApiEndpoints`、油猴脚本站点；`yarn dev` 的 `http://localhost:3000` 仅在 `RuntimeMode.Dev` 构建中）和浏览器扩展（油猴脚本管理器通过扩展发送请求）：
  - `Origin` 存在、且既不是请求自身源（与请求所发往的协议加 `Host` 完全相同，名称不限：本机窗口用 `localhost`、`127.0.0.1`，或经 hosts 别名、本机反向代理/内网穿透以明文 HTTP 访问都可以——这类访问浏览器不发送 Fetch Metadata。这条规则区分的是“不同源”的页面，即所有中继页面和其他网站；它不防 DNS 重绑定，后者需要同时覆盖读请求的 `Host` 白名单，另行处理）也不受信任的 **WebSocket 握手**；`null` 视为外来；没有 `Origin` 的（原生客户端）保持原行为。之所以按 `Origin`，是因为 Chromium 在握手中不发送 Fetch Metadata：只看 `Sec-Fetch-Site` 时，中继页面可以打开 `ws://127.0.0.1:<端口>/hub/ui`，读到 `GetInitialData` 推送的全部配置（含第三方 Cookie 和 API Key）；
  - 浏览器标记为 `Sec-Fetch-Site: same-site|cross-site` 的写操作（GET/HEAD/OPTIONS 以外）和框架加载；
  - 未发送 `Sec-Fetch-Site` 的引擎发出的、`Origin` 为外来来源的写操作；以及路由后由上述两类不受信任页面访问的“在用户机器上执行”的操作（`LoopbackCrossSiteUserMachineFilter`）。

  本机服务端只有两个 WebSocket 端点，即两个 SignalR hub：`/hub/ui` 和 `/hub/progressor`。其余传输方式都需要先通过 `negotiate`（POST，外来页面会被拒绝，也读不到应答）取得连接 ID；每次发送也是 POST；长轮询和 SSE 的接收应答不会给外来页面 CORS 许可。服务端只监听明文 HTTP，浏览器不会在其上使用 HTTP/2，扩展 `CONNECT` 握手只由单元测试矩阵覆盖。
- 本机服务端拒绝跨站的框架加载，并发送 `frame-ancestors 'self'`：中继页面无法把本机界面嵌入框架中操纵。
- 信任逐对建立：管理 B 必须在 B 上批准或出示配对码，不会自动加入“集群”。
- 不与自己配对：握手返回本机 `ServerId` 的地址、本机服务端或中继端口都会被拒绝。

## 位置

| 位置 | 内容 |
| --- | --- |
| `src/apps/Bakabase.Remoting/Components/Relay` | 中继的公共组成 |
| `src/apps/Bakabase.Remoting/Components/Console` | 一体版的受管设备存储、配对、导入、每台设备的中继及其 `/client` 接口 |
| `src/apps/Bakabase.Remoting/Components/Forwarding` | 回环守卫、导航票据、转发 |
| `src/apps/Bakabase.App` | `UnifiedHost`：在进程内服务端旁组装中继 |
| `src/apps/Bakabase.Service/Controllers/FederationServerController.cs` | 本机界面使用的 `/federation/local/servers` |
| `src/apps/Bakabase.Shell` | 托盘“切换服务器”菜单 |
| `src/web/src/features/federation`、`layouts/BasicLayout/components/PageNav` | 设备页的管理部分、菜单顶部的切换器 |

## 剩余发布门槛

- 在 Windows（WebView2）和 macOS（WKWebView）实机确认：切换、视频拖动、SignalR、第三方登录窗口、托盘切换。
- 用真实 NAS 镜像验证“无限制”提示与要求配对后的批准流程。
