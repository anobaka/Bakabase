# 多设备联合媒体库：实施与验证记录

执行日期：2026-09-20 至 2026-09-21。开发分支：`codex/multi-device-library`。

开始执行前已 fetch `origin/main`，以 `f1fa1469f32f794f17895f5ba886cc14ddd42883` 新建独立 worktree。执行期间主线若继续推进，不改变这次记录的起始基线。

子模块固定为父仓库版本：

- Bakabase.Infrastructures：`a3715f40d7c470ec6610ab1a1fdfb8ce21231893`
- LazyMortal：`f35a158a8395c09216451f77d04e6fea33b9e77b`

原工作区不切分支、不覆盖已有修改。新增模块不修改业务数据库 schema，不互换桌面应用的包名、AppData、单实例标识或更新源。

## 当前可用流程

统一版保留原本机资源管理页，新增“联合媒体库”和设备页。浏览器入口使用应用原有 HashRouter：`/#/federation`、`/#/federation/devices`。

1. 在当前设备主动启用联合浏览，在来源设备明确开启只读分享；两者默认关闭、分别控制。远程访问关闭时，分享界面要求同时启用原有配对保护。
2. 在另一设备输入来源地址与一次性邀请码，或发起请求后由来源批准，再领取授权。
3. 每个方向单独授权；A 能读取 B 并不代表 B 能读取 A，也不代表 A 能读取 B 的其他设备。
4. 选择本机、全部已启用来源或指定设备搜索。卡片、详情、缓存和深链使用完整的 `nodeId + libraryEpoch + resourceId`。
5. 详情只读；图片、音频和视频通过本机受控媒体会话预览。播放器由当前设备发现和启动；远端不启动播放器，不修改播放历史。目录在当前设备打开：本机资源可直接打开，远端资源须先配置有效映射。
6. 来源离线会明确显示查询未覆盖该设备。翻页中途失败会中断当前会话；不会悄悄删掉参与节点后继续返回看似完整的结果。

本机旧页面仍承担编辑、删除、任务和高级搜索。联合视图不复制远端资源表、配置、词典、管理员权限或 SignalR 状态。

关闭联合浏览会取消正在进行的本机查询和媒体请求、释放查询快照、清空媒体票据；重新开启不会恢复旧会话。设备页、配对、路径映射和对外分享独立保留。节点/库代际重置后需要重新开启浏览。多个窗口通过本机状态事件和恢复焦点时的重新读取同步开关。

## 实现位置与边界

| 位置 | 行为 |
| --- | --- |
| `src/modules/Bakabase.Modules.Federation/Identity`、`Peers` | 节点身份、代际、定向配对、撤销、分享和路径映射 |
| `Security`、`Transport` | 独立 Node 签名、重放保护、固定对端会话、取消、已认证时钟 |
| `Contracts`、`Queries` | 严格查询、不可变节点快照、固定参与者、全量有界归并分页 |
| `Media` | 绑定资源与授权的资产票据、路径边界、固定媒体类型 |
| `src/apps/Bakabase.Service/Components/Federation` | 实际数据库投影、宿主适配、鉴权管线、媒体代理 |
| `src/web/src/features/federation` | 独立只读视图、设备管理、迁移提示与连接信息导入 |
| `Client.Remoting/Components/Forwarding/ClientApiEndpoints.cs`、`Shell/Components/AvaloniaGuiAdapter.FileSave.cs` | 旧客户端连接提示白名单、原生保存对话框；共用可选接口位于父仓库 `Bakabase.Abstractions/Components/Gui/ILocalFileSaveDialog.cs` |
| `src/tests/Bakabase.Federation.TestHost` | 使用正式 Service 启动流程的独立进程夹具 |
| `src/tests/federation-smoke/run.py` | 三个独立进程之间的真实网络验收 |

共享发现与本机播放器定位已下沉到 RemoteAccess/Player 模块；Service 不引用 Client.Remoting。旧客户端保留原协议和转发器。新增 Node 协议不需要搬动旧签名、JSON 兼容或活动服务器逻辑。

旧客户端的 `GET /client/migration-hints` 继续提供名称、规范化 origin 地址及路径映射提示。原生导出使用新增的 `POST /client/migration-hints/export`：服务端从本机连接存储生成同一份白名单 JSON，固定建议文件名为 `bakabase-connection-hints.json`，通过可选 `ILocalFileSaveDialog` 交给 Avalonia `StorageProvider.SaveFilePickerAsync`。用户选择目的文件后才写入；启用覆盖确认，同一进程同时只允许一个保存对话框。网页不能传入目的路径或任意内容，带请求体的调用返回 400；接口沿用客户端既有回环、Host/Origin 守卫，输出不含旧管理员密钥、设备身份、URL 凭据或活动连接设置。没有修改固定的 Infrastructure 子模块，也没有添加通用 HTTP 写文件接口或全局 WebView 下载处理。

前端只在返回 `saved` 时显示已保存；`cancelled` 显示取消且不再下载。没有原生保存能力的 `unavailable`，或旧客户端缺少该接口的 404，才回退到既有 GET 与浏览器 Blob 下载。其他 HTTP 错误或无效结果明确显示失败。每次操作先清除上次结果；成功、取消、失败均在 `finally` 解除按钮忙碌状态，Shell 也在 `finally` 释放对话框互斥。通过浏览器访问带可用 GUI 的旧客户端时，同样会打开该客户端所在设备的保存对话框；无 GUI 的测试宿主使用浏览器下载。

新增数据统一存于 AppData 下 `federation/state.json`。身份、授权、配对状态使用一次原子替换，避免多文件部分提交；Unix 文件权限为 0600。普通配置导入不覆盖节点身份。损坏状态正常启动时拒绝使用；显式重置可恢复。还原本库使用新 LibraryEpoch，克隆成另一节点同时换 NodeId。直接从外部完整替换 AppData 无法自动识别，仍需按此操作重置。

`/federation/local/*` 只接受真实回环调用，并检查 Host/Origin。`/federation/v1/export/*` 每次独立验证 Node 授权、来源和分享状态；旧 Unrestricted、回环或管理员配对均不能豁免。新 Node 凭据不能进入旧管理接口。控制 API 使用独立的原始 JSON DTO、UTC ISO 8601 日期和真实 HTTP 错误码；SDK 从程序集生成。

媒体使用固定的非活动图片/音频/视频 MIME 白名单、nosniff 与 sandbox CSP。不支持 HTML/SVG、任意 URL 或压缩包内条目播放。映射只接受来源声明的 root ID 与本机根目录，校验目录祖先符号链接；无效映射回落到授权流。取得媒体票据之后，实际读取仍会重新验证当前授权、资源归属和映射。

目录动作只接受完整 ResourceRef。来源只返回授权范围内的 root ID、相对位置和类型，不接收远程打开目录命令；当前设备重新验证来源后，从本机映射解析文件夹。前端不接收来源绝对路径。无 GUI、无文件、未映射和映射失效都有明确原因。覆盖路径映射采用 `expectedMappings` 原子比较，冲突返回 409，要求重新审阅。

播放器发现支持 macOS 系统与用户 Applications 下的 VLC/IINA，IINA 使用 `iina-cli --no-stdin`；Windows 保留注册表和目录发现，Unix 保留 PATH 并检查执行权限。本机没有支持的播放器时返回可操作错误，网页预览仍可使用。

启动流程不再主动探测或安装本机文件工具。ffmpeg、7-Zip、Lux、Locale Emulator 的实际调用负责首次发现；可选组件缺失仍使用现有安装提示，依赖管理页保留手动发现与安装。安装和发现串行执行，取消等待不影响其他调用，失败可重试。

## 预算和一致性

| 项目 | 当前默认值 |
| --- | --- |
| 单次来源数 / 页面条数 | 16 / 最多 200 |
| 节点分块 | 通常 128 条，最多 256 条；2 MiB 上限 |
| 输出页面 / 远端 JSON | 4 MiB / 解码后 8 MiB |
| 捕获、准备、翻页期限 | 各 8 秒 |
| 节点快照 / 联合会话 TTL | 10 分钟 / 5 分钟，绝对期限 |
| 单快照 / 总快照 | 64 MiB / 128 MiB |
| 单捕获 / 总捕获工作区 | 128 MiB / 256 MiB |
| 协调器保留内存 | 64 MiB |
| 资产与媒体会话 | 有数量、字段长度、TTL 和总字节配额 |
| 媒体控制请求、响应头 | 8 秒 |
| 媒体流读写空闲 | 30 秒；活跃长视频不受固定总时长限制 |
| 资源目录枚举 | 最多 4096 个条目、256 个媒体、16 层、2 秒 |
| 映射根扫描 | 最多 25 万行、32 MiB 输入、4096 个根、1 MiB 响应、4 秒 |

超限会明确失败，不截断后宣称完整。查询是 `frozen-observation-v1`：捕获一个时间窗口中的必要字段并冻结，不声称跨表 MVCC 或跨设备共同事务时点。翻页不会重新扫描整库；每页仍复核参与节点，包括已耗尽的来源。

## 可复现的本机验证

要求：仓库固定子模块、.NET 9 SDK、Node/Corepack、Python 3。所有下列服务夹具只使用自己新建的临时数据目录，不连接用户媒体库。夹具显式移除依赖自动下载服务；首次使用依赖的生命周期另由正式组件测试验证。

```bash
dotnet build src/apps/Bakabase.App/Bakabase.App.csproj
dotnet build src/apps/Bakabase.Client.App/Bakabase.Client.App.csproj
dotnet run --project src/tests/Bakabase.Modules.Federation.Tests -- --minimum-expected-tests 1
python3 src/tests/run-backend-tests.py --project src/tests/Bakabase.Tests/Bakabase.Tests.csproj
dotnet build src/tests/Bakabase.Federation.TestHost/Bakabase.Federation.TestHost.csproj
python3 src/tests/federation-smoke/run.py --dotnet /absolute/path/to/dotnet
```

三实例脚本会建立三个独立 SQLite/AppData、NodeId、密钥与 HTTP 端口，每个实例 257 条同 ID 测试资源。验收包括定向授权、不传递信任、771 条完整分页、重复 cursor、严格条件拒绝、只读详情、真实 WAV 的 HEAD/206/416、恶意 Host/Origin、旧 Unrestricted、撤销、代际重置、离线覆盖与远端 PlayedAt 不变。脚本结束会关闭自己创建的进程；`--keep` 只用于开发者手动保留。

CI 使用实际 MSTest 可执行 runner，并要求至少执行一个测试。主测试工程按 class 使用独立临时目录和进程，结束后只清理该 class 的临时目录，保留日志/TRX。这样避免原测试辅助类每次留下一份 SQLite 数据库导致磁盘耗尽；不会跳过断言或将空跑当作成功。

前端：

```bash
cd src/web
corepack yarn install --immutable
corepack yarn vitest run
corepack yarn build
```

开发者可用以下方式分别启动两个带静态前端的夹具，端口和绝对数据目录必须不同。先等待终端出现 `FEDERATION_TEST_READY`，再打开 `http://127.0.0.1:35181/#/federation`。

```bash
BAKABASE_FEDERATION_TEST_WEB_ROOT="$PWD/src/web/dist" \
dotnet src/tests/Bakabase.Federation.TestHost/bin/Debug/net9.0/Bakabase.Federation.TestHost.dll \
  35181 /absolute/temporary/federation-a 257
```

夹具主动分享测试资源，但联合浏览仍默认关闭，须在界面启用。正式应用默认不分享。无人值守 Service 的一次性 Node 邀请入口是显式参数 `--federation-invite-on-start`，须先启用分享及远程访问；它不复用旧 Device 管理邀请码。

NAS 首次启用可在宿主或容器内部向实际端口的回环地址发送以下请求，再生成本机邀请码；不要把这两个管理接口当作可从其他机器直接调用的端点。

```bash
curl -X PUT http://127.0.0.1:PORT/federation/local/peers/sharing \
  -H 'Content-Type: application/json' \
  -d '{"enabled":true,"enablePairedRemoteAccess":true}'
curl -X POST http://127.0.0.1:PORT/federation/local/peers/invite
```

启用请求会把旧远程访问设为 Enabled 并要求配对；它不提供 Unrestricted 管理入口。邀请码只用于向指定设备授予只读 Node 权限。

## 已执行的验证结果

| 检查 | 结果 |
| --- | --- |
| Bakabase.App / Bakabase.Client.App | 两个入口均编译通过；保留原有编译与传递依赖警告 |
| 13 个后端测试工程 | 2,481 通过、0 失败、57 跳过；跳过项沿用现有测试设定 |
| 后续资产配额测试 | 联邦模块完整重跑 47/47 通过，含全套回归之后新增的 2 项 |
| 映射根 / 媒体安全 | 6/6、2/2 通过，包含执行中取消和流空闲期限 |
| 最后迁移/分享回归 | 3/3 通过：导出剔除 URL 凭据，分享初始化保留旧身份及转码选项 |
| 三个真实宿主 HTTP | 最新代码 771 条完整遍历及安全、媒体、撤销、离线场景全部通过 |
| 前端全量单测（续轮） | 95 个文件、913/913 测试通过 |
| 前端全量 ESLint | 0 errors，保留 901 个现有 warnings；新增联合模块 0 warnings |
| 前端生产构建 | Vite build 通过 |
| 全量 TypeScript 检查 | 基线和当前均 310 个既有错误；相同依赖与命令的完整日志逐字节一致，新增 0 |
| Chromium 真实双节点 | 514 条、2/2 来源、50→100 翻页、远端同 ID 详情、音频 metadata、空搜索、连接错误全部通过 |
| 浏览器重复刷新 | 连续 3 次刷新产生 4 个会话；3 个旧会话均已释放并返回 410，没有 JS pageerror |
| 分批后端 runner | 构建/发现/执行/清理链通过；不存在的 class 返回失败，空跑不能冒充成功 |
| 续轮联邦回归 | 模块 47/47、最终 Service 联邦测试 28/28 通过，含目录响应返回后禁用/忘记设备时禁止打开目录 |
| 依赖生命周期 | 新增 12/12、已有本机功能回归 36/36 通过；14 项压缩集成因本机没有 7-Zip 跳过 |
| 播放器模块 | 72/72 通过，含 macOS app bundle、执行权限、IINA 单 URL/批量参数 |
| 升级兼容契约 | AppData、迁移、旧协议和更新源 131/131；包审计故障用例 6/6 |
| 实际 publish 产物 | macOS ARM 的统一版、旧客户端及无界面 Service 内容/角色隔离检查通过 |
| 续轮 Chromium 双节点 | 默认关闭、显式启用、跨窗口关闭清媒体、迁移刷新恢复/幂等、映射明确确认全部通过；0 pageErrors |
| localhost 原生壳来源地址 | Chromium 验证媒体 URL 同源、真实音频 metadata 成功；修复原固定 127.0.0.1 的地址不一致 |
| 第三轮前端完整回归 | 97 文件、922/922；定向 lint 0 errors / 0 warnings、格式及生产构建通过 |
| 第三轮类型检查 | 310 条既有诊断；4 条 AppInfo 诊断仅行列移动，去除行列后与基线全文一致 |
| 第三轮身份恢复 | 11/11；真实配对后恢复保留访问授权/映射，克隆清空，两者关闭分享和浏览 |
| 迁移浏览器脚本诊断 | 2/2；构建失败不会遗留 running 状态，清理后仍保留不含密钥的宿主诊断 |
| 原生迁移导出接口回归 | `ClientPipelineTests` 34/34；错误 Origin/Host、任意请求体均不能弹保存框；取消/不可用不写文件，保存失败不报成功，保存内容与 GET 使用相同白名单 |
| 原生迁移导出前端回归 | 全量 98 文件、932/932；导出定向 12/12，定向 lint 0 errors / 0 warnings，Shell 与生产前端构建通过 |
| 原生迁移导出类型检查 | 仍为 310 条既有诊断；去除既有行列移动后与基线全文一致，无新增 |
| 原生保存不可用时的浏览器回退 | 最新三宿主迁移全流程通过，114 条/2 来源、恢复/克隆及音频仍通过，0 pageErrors |
| macOS 实际 SavePicker | self-contained portable `0.0.2-federation.4` 实测通过：实际弹框、取消不写文件、选择测试目录保存白名单 JSON；原连接文件字节不变、无业务数据库。统一版原生文件选择导入后显示地址草稿；证据 `/tmp/bakabase-native-validation/native-export-result.json` |

全套后端曾因原测试辅助类留下 321 个临时数据库、累计约 13 GB 耗尽磁盘而中断。仅清理本轮生成的目录后，已明确完成的前 498 项保留记录，余下 125 个 class 在独立进程和临时目录中全部补跑；合并计数与 discovery 的 1,579 个主工程 case 一致。没有改断言，也没有漏掉中断时失败的初始化用例。之后增加的局部回归单独运行，不重复计入全套统计。

本机原始日志位于 `/tmp/bakabase-backend-regression/`、`/tmp/bakabase-browser-tests/`，三实例终验日志为 `/tmp/bakabase-federation-smoke-final.log`。上述测试使用本轮创建的数据，未启动或改变用户原媒体库。

TypeScript 基线核验使用 `git archive f1fa1469 src/web` 导出的临时源码和相同 node_modules，运行 `corepack yarn exec tsc --noEmit --pretty false`，没有通过回退当前源码或改 SDK 来消除错误。记录为 `/tmp/bakabase-tsc-baseline-report.md`。

续轮结果位于 `/tmp/bakabase-federation-continuation-*.log`、`/tmp/bakabase-dependency-*.log`、`/tmp/bakabase-release-readiness/`。最后目录撤销修复后的 Service、三节点及重新生成的三角色产物证据统一位于 `/tmp/bakabase-federation-continuation-final/`。浏览器结构化证据为 `/tmp/bakabase-browser-tests/continuation-result.json` 和 `localhost-result.json`。三节点续轮检查增加默认关闭、关闭后拒绝旧查询/媒体、分享继续可读和重启后旧票据不复活。

上一轮以独立测试 AppData 启动 macOS ARM framework-dependent publish，因 Mac 锁屏只确认导航，没有计作 GUI 通过。此后已在实际 Velopack portable `.app` 完成原生配对、257 条联合查询、详情、音频播放/暂停/seek/恢复、Finder 映射目录、离线覆盖提示、本机设置/日志和来源重启恢复，详见[发布验收记录](multi-device-library-release-readiness.md)。新证据不代替 Windows/Intel、签名安装器、真实升级或多物理设备矩阵。

第三轮补充了可复现的真实旧客户端迁移浏览器脚本：生产 ClientHost/ClientStartup 与两个 Service 使用独立数据目录和端口，旧连接页实际配对/导出，统一版导入、刷新恢复、重复导入，再由来源设备界面批准新的只读授权。114 条联合资源、localhost 音频、跨窗口关闭浏览均通过，旧连接文件未变、旧 key 未迁入、0 pageErrors。脚本和独立 Playwright lockfile 在 `src/tests/federation-browser-smoke/`，已接入 CI。下载清单加载中或失败时，现在仍保留旧客户端迁移入口。

身份恢复补丁完成后，再次执行上述浏览器链路，并通过实际 UI/POST 验证恢复同一节点和克隆新节点两条路径，结果位于 `/tmp/bakabase-browser-migration-recovery-final/result.json`。当轮前端全量结果、构建、lint 和类型基线比较见 `/tmp/bakabase-identity-recovery-*.log` 与 `-tsc-comparison.json`。备份说明现在明确要求覆盖后首次启动保持网络隔离，身份重置不会自动处理旧版管理协议的授权。

随后实际旧客户端 macOS WebView 暴露了 Blob 导出缺口：GET 返回 200，但没有保存对话框或文件。Shell 原先只有上传用的 OpenPanel，没有下载保存实现。上述窄导出接口修复已完成编译及自动化回归，证据为 `/tmp/bakabase-native-migration-pipeline-tests/summary.json`、`/tmp/bakabase-native-migration-{frontend-tests,all-tests,lint,shell-build,web-build,tsc}.log`。最新 Chromium 回退链路结果为 `/tmp/bakabase-browser-native-export-fallback/result.json`；它使用没有原生保存能力的真实 ClientHost，只证明浏览器回退。另已用实际 macOS self-contained 包完成保存/取消、文件白名单检查、原生导入和重启恢复草稿，结果见[发布验收记录](multi-device-library-release-readiness.md)。

最终三宿主重跑结果在 `/tmp/bakabase-federation-third-round-final-2/result.json`：771 条及全部撤销/媒体/覆盖场景通过。脚本增加恢复后两个开关均关闭、不能生成邀请码、显式重新开启分享后旧引用仍被拒绝的断言。首次重跑暴露脚本仍假设恢复后可立即邀请；现已按新的实际行为更新并完整重跑，没有恢复旧的自动分享行为。

原生 VLC 验证发现 macOS 系统 HTTP 代理会接收 localhost 媒体票据，造成 503。最初 AVIO/libavformat 策略通过直连及 seek 测试，但本轮实际 GUI 揭示暂停失败；其普通 HTTP 输入不支持暂停，RC 的 paused 状态不足以证明时钟停止，因此移除该策略。服务端在发送票据前检查本机代理配置，受影响或未知时跳过 VLC，自动选择支持显式直连的 mpv/IINA；无候选则提示预览、映射或安装支持的播放器。VLC 保留正常原生 HTTP 输入；本机文件和路径映射不受该策略影响，不修改系统代理或播放器偏好。入口继续拒绝任意目标、userinfo、query、fragment、转义和畸形票据。

本轮策略/参数 14 / 14、Player 74 / 74、旧播放处理器 20 / 20 通过。实际 self-contained 统一版 `0.0.2-federation.4` 在 VLC-only 代理环境显示中文提示，加入仅测试 PATH 的官方 IINA 1.4.4 后自动选择并播放；暂停时钟保持 `00:08`、恢复/拖动到 `07:00`、继续到 `07:08` 后再次暂停均通过。原生迁移草稿也在关闭旧包、启动新包后保留。Windows/Linux 尚无可信 VLC 代理检测，零映射 VLC 流使用同样替代路径；独立 mpv、视频和物理网络矩阵未记作通过。测试进程及临时发现链接均已清理，完整边界见发布验收记录。

最初 `player-proxy.py` 用官方 VLC 3.0.23 和独立 WAV/诱饵代理验证直连与 Range，记录在 `/tmp/bakabase-player-proxy-results-20260921/result.json`；该轮未验证真实暂停时钟，不能证明原 AVIO 策略可用。脚本现已补强暂停和大文件实际 Range 断言，`--diagnose-avio` 正确以非零退出捕获旧方案暂停失败；原生 IINA 已按上述步骤实际验证，独立 mpv 仍待验证。

真实 macOS 打包发现两个产品的 plist 缺少 `CFBundleExecutable`，且自定义版本固定为 1.0.0；现已修复并在 Velopack 前生成版本、后审计实际 Portable.zip，14 个 guard/plist 测试通过。Ubuntu 24.04 ARM64 容器中实际执行 Federation 47、Player 72、兼容 131 通过，Service 包审计通过；原生编译及三宿主启动遇到 SIGILL，不能记为完整 Linux 门禁通过。Windows 路径和播放器发现测试夹具另修复了平台依赖，27 项定向回归通过。

继续诊断已在无业务依赖的最小程序中捕获 .NET 9 CoreCLR PAL 执行 `rdvl` 导致的 SIGILL，与 SME-only Linux 信号上下文的官方已知缺陷一致；相同 DLL 在官方 .NET 10.0.12 的三次对照均正常。完整复现和平台范围见 [Linux ARM64 诊断](linux-arm64-runtime-diagnosis.md)。本轮不升级产品框架；Linux x64 发布目标仍须执行其自身 CI，不能用 ARM 失败或 .NET 10 最小程序成功替代。

本轮基于 `51601e31` 在 macOS SDK 交叉构建 Linux x64，再使用官方 ASP.NET 9.0.20 x64 镜像和 OrbStack 仿真执行，三宿主 771 条全流程通过，另补 Player 74 / 74、兼容性/迁移 135 / 135（0 跳过）。`run-linux-cross-container.py` 固定提交、子模块和镜像 digest，保持宿主挂载只读，执行输出使用容器内临时副本，结果保留真实 TRX 与平台信息。这不等于 GitHub 原生 runner 或物理 Linux 设备；没有修改产品目标框架或容器后台服务。

四平台 CI 现另运行整个 `Bakabase.Tests.Federation` 命名空间，包括子命名空间与未来新增类。本机实际发现 9 类、42 / 42 通过；选择器 8 项回归及不存在 namespace 的失败验收通过。原 Ubuntu 全量作业原已覆盖这些 Service 测试，本轮补的是 Windows/macOS/Linux 专项矩阵中的遗漏。

本轮还完成 macOS ARM 的真实旧版更新链路：用基线 `f1fa1469` 及锁定子模块构建旧前后端，合成 portable 版本 `0.0.1-upgrade.1`；旧应用通过真实 API 创建两条资源，自身 updater 从隔离 loopback feed 下载新版，真实 `UpdateMac` 将应用替换为 `51601e31` / `0.0.2-federation.4`。新程序启动后 DLL SHA 与包一致、AppData 相同，两条资源响应逐字段一致，两个 SQLite 快照完整性均通过，数据文件 SHA 保持不变。证据为 `/private/tmp/bakabase-real-upgrade-51601e31/run5/report.json`。

`run-velopack-macos.py` 与不随产品发布的 startup hook 将 Velopack cache/log 定位到临时目录，并禁用 apply 的自动重启；脚本在替换完成后以相同显式 AppData 启动新二进制。该结果不是旧目录复制模拟，也不代表历史签名安装包、默认缓存、LaunchServices 自动重启或生产更新通道已通过；WebKit/SavedState 的系统缓存不在隔离范围。所有 owned 应用/updater 进程退出后清理测试工作目录，保留报告、日志和一致性 SQLite 快照。

## 性能观测

本机 macOS arm64、10 逻辑处理器、.NET 9.0.0。以下是测试夹具观测值，不是生产保证。

| 实际 SQLite 读取夹具 | 1 万条 | 10 万条 |
| --- | ---: | ---: |
| 捕获耗时 | 53.77 ms | 569.81 ms |
| 保留工作区预算计费 | 11.89 MB | 118.86 MB |
| 快照估算 | 5.22 MB | 52.20 MB |
| 累计托管分配 | 32.90 MB | 325.58 MB |

夹具包含两个 Name scope、来源、偏好和 profile。累计分配包含短期 EF 对象，不能解释为同时占用内存。10 万条结果的快照估算促使单快照预算从建议的 32 MiB 调整到 64 MiB，总预算仍有限制。

纯内存投影加协调器：10 万条首屏约 159 ms、完整遍历约 278 ms、后续页 p95 约 0.275 ms。这组数字不含数据库或网络。跨设备网络首屏、弱网首帧、打包桌面 CPU/内存高水位仍须发布验收。

### 双生产宿主 HTTP 基线（2026-09-21）

`src/tests/federation-smoke/benchmark.py` 启动两个独立真实 Service/TestHost 和 SQLite，通过透明 loopback 计数代理记录节点流量，完整遍历而非只取首屏。机器为 Apple M4、10 核、16 GiB、macOS 26.1 ARM64、SDK 9.0.100/runtime 9.0.0。Debug 构建，基于 `cf787d13`；同时有原生 GUI、4 CPU/4 GiB Linux 测试和镜像下载负载，不是空闲机器性能门槛。

| 每节点规模 / 状态 | 联合首屏 | 完整遍历 | 后续页 p50 / p95 | 节点 HTTP 请求 / 响应载荷 |
| --- | ---: | ---: | ---: | ---: |
| 10k × 2，进程冷启动 | 540 ms | 3.055 s / 100 页 | 22.2 / 48.9 ms | 182 / 3.41 MB |
| 10k × 2，热进程 | 133 ms | 4.390 s / 100 页 | 33.2 / 104.1 ms | 180 / 3.41 MB |
| 100k × 2，进程冷启动 | 2,947 ms | 22.794 s / 1,000 页 | 17.0 / 34.7 ms | 1,785 / 34.22 MB |
| 100k × 2，热进程 | 1,084 ms | 15.772 s / 1,000 页 | 12.9 / 24.1 ms | 1,783 / 34.22 MB |

每页 200 条；分别完整验证 20,000 和 200,000 条。UI 响应载荷分别 6.79 MB、68.09 MB；请求数含每页权限验证与最终释放。冷启动只重启进程，不清 OS 文件缓存；字节不含 HTTP/TLS 头，计数代理和 RSS 采样本身也有成本。

100k × 2 普通遍历的采样 RSS 峰值 A/B 约 832/905 MiB，两会话额度复用测试中约 1,112/1,205 MiB。关闭浏览时，执行中的请求被中断，旧会话重启后为 410，两个额度立即可复用；两档关闭耗时约 77/31 ms，来源端 3 次创建对应 3 次 DELETE。大库关闭前后 RSS 并未立即回落（A 约 833→841 MiB，B 约 972 MiB），不能把额度释放等同于 OS 立即回收内存，也不能仅凭此推断泄漏。

结构化结果在 `/tmp/bakabase-federation-http-benchmark-final-20260921/result.json`。测试未放大默认预算，已经清理自身进程和数据库；这些是本机 HTTP 观测，物理 LAN/NAS、弱网和视频首帧仍需独立记录。

### 真实视频与流故障（2026-09-21）

`media-stream.py` 使用两份真实 Service/SQLite 和固定目标的 loopback 转发器，生产 UI/Chromium 实际解码 120 秒、640×360、13 MiB 的合成 WebM。256 KiB/s 限速下首个呈现帧约 772 ms；暂停时钟稳定、恢复成功，`requestVideoFrameCallback` 确认呈现 `mediaTime=90` 的帧，来源还收到文件后半段的新 Range。

持续传输 2.25 MiB 约 9.43 秒，超过响应头 8 秒期限仍正常；读端断开后上游连接释放。注入截断必须报不完整/RST，重开 Range 的实际字节与源文件一致；停滞约 30.13 秒触发产品 idle 取消；流中关闭浏览约 100 ms 终止请求并释放上游。来源撤销后新 Range 被拒绝，两边播放历史保持为空。

完整结果与实际视频截图在 `/tmp/bakabase-media-stream-presented-frames/`。短截止失败运行 `/tmp/bakabase-media-stream-deadline/result.json` 非零退出并清理自身进程。测试已接入 Ubuntu browser CI，源视频不上传；这些是单机速率/故障注入样本，不声称物理网络延迟、丢包、NAS 吞吐或 p95 性能。

## 与原计划的差异及发布门槛

代码层已实现身份/配对/权限、搜索分页、只读详情和媒体、独立 UI、迁移信息白名单。原计划 P00–P09 中需要跨 OS、真实桌面、安装包才能判定的验收项不会因代码完成而自动勾选。

- 状态存储采用单个原子文件，接口使用 raw DTO，媒体通过 ASP.NET 的范围文件结果与受控代理实现，未修改旧 FileController。
- 依赖初始化已延后至本机功能实际使用；最终桌面首启的 CPU、内存和弱网成本仍须真机观测。
- 当前能力覆盖文件系统中的支持媒体与目录打开；压缩包条目和格式转码不在这次可用闭环内。
- 旧客户端提供统一版提示和连接信息导出；统一版只导入名称、地址和映射提示。白名单草稿可刷新恢复、重复导入去重，映射替换明确确认且防止并发覆盖。每个来源必须重新授权，旧管理员密钥不会迁移。
- 未删除旧客户端产物、切换更新 feed、改动公开下载入口或发布安装包。等真实升级/迁移矩阵通过后，再收敛新用户下载入口。
- 还需在最终 Avalonia 安装包上验证 Windows/macOS 各目标、系统播放器启动、Windows 路径映射、休眠恢复、真实 NAS/Docker 和多机网络条件，以及仅旧客户端/仅统一版/两者同机的升级路径。

四平台 CI 已加入真实三节点链路、兼容契约和实际 publish 角色审计，本轮尚未在远端 CI 执行。详细命令和未完成真机门槛见 [发布准备与升级验收](multi-device-library-release-readiness.md)。当前证据支持开发分支内试用和进一步评审，不等于跨平台发布验收已经完成。
