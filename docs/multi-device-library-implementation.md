# 多设备联合媒体库：实施记录

当前实现、使用方式与剩余发布门槛。设计背景与原始决策见[执行计划](multi-device-library-execution-plan.md)。开发期间各轮 CI、安装包、升级与原生界面验收的原始证据（运行编号、证据包、诊断脚本）保留在标签 `acceptance-evidence-2026-09-23`，不再随主线维护。

新增数据只写入 AppData 下 `federation/state.json`，不修改业务数据库 schema，不互换两个桌面产品的包名、AppData、单实例标识或更新源。基础设施子模块另有一个修复提交（迁移完整性检查不再保留 SQLite 连接池中的文件句柄），需先于本仓库合并。

## 当前可用流程

统一版保留原本机资源管理页，新增“联合媒体库”和设备页。浏览器入口使用应用原有 HashRouter：`/#/federation`、`/#/federation/devices`。

1. 在来源设备开启只读分享（默认关闭）。远程访问关闭时，“开启分享”默认同时把远程访问设为“开启并要求配对”，一次操作即可被发现；已开放为更宽模式的设置不会被改动。分享期间设备页同时显示本机地址与一次性邀请码。
2. 在另一设备“发现附近设备”或输入地址，使用邀请码直接获得授权，或发起请求等待对方批准。来源会在通知中心收到请求提醒；请求方在后台自动领取授权，不需要停留在设备页。
3. 授权仍是单向的：A 能读取 B 不代表 B 能读取 A，也不代表 A 能读取 B 的其他设备。连接时勾选“也让对方读取我的资源库”（默认勾选）会附带一个只对该设备有效的一次性回授码：对方批准（或邀请码生效）后自动反向连接，一次批准即双向互通。
4. 获得读取授权后自动开启联合浏览。选择本机、全部已启用来源或指定设备搜索。卡片、详情、缓存和深链使用完整的 `nodeId + libraryEpoch + resourceId`。
5. 详情只读；图片、音频和视频通过本机受控媒体会话预览。播放器由当前设备发现和启动；远端不启动播放器，不修改播放历史。播放会话在使用期间持续有效（空闲 2 小时或最长 24 小时过期），来源的短期资产授权到期时自动经签名请求续期。目录在当前设备打开：本机资源可直接打开，远端资源须先配置有效映射；macOS 上的应用包只在 Finder 中定位，不会被打开。
6. 来源离线会明确显示查询未覆盖该设备。翻页中途失败会中断当前会话；不会悄悄删掉参与节点后继续返回看似完整的结果。已配对设备的地址（DHCP、端口）变化后，通过局域网发现找到同一 NodeId 的新地址，并在新地址用原授权完成签名握手后才采用。
7. 设备可设置显示名称（无界面宿主可用 `BAKABASE_NODE_NAME`），可以“移除设备”一次性撤销双向授权并删除记录。

NAS/Docker 等无界面宿主：`BAKABASE_FEDERATION_SHARING=true` 在启动时开启分享，`--federation-invite-on-start` 启动时打印邀请码；运行中可用 `docker exec <容器> dotnet Bakabase.Service.dll federation <status|share on|invite|approve|reject|revoke>` 管理，该命令只调用实例自身的回环接口。

产品范围：一体版只访问（浏览、搜索、查看、播放）其他设备的资源，不管理其他设备；编辑、删除、任务和高级搜索始终在资源所属设备本机进行。联合视图不复制远端资源表、配置、词典、管理员权限或 SignalR 状态。

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
| `Service/Components/Federation/FederationPairingFlow.cs` | 后台领取已批准的请求、批准后反向连接、获得授权后开启浏览 |
| `Service/Components/Federation/FederationCli.cs` | 无界面宿主的 `federation` 管理命令（只调用本机回环接口） |
| `src/tests/Bakabase.Federation.TestHost` | 使用正式 Service 启动流程的独立进程夹具 |
| `src/tests/federation-smoke/run.py` | 三个独立进程之间的真实网络验收 |

共享发现与本机播放器定位已下沉到 RemoteAccess/Player 模块；Service 不引用 Client.Remoting。旧客户端保留原协议和转发器。新增 Node 协议不需要搬动旧签名、JSON 兼容或活动服务器逻辑。

旧客户端的 `GET /client/migration-hints` 继续提供名称、规范化 origin 地址及路径映射提示。原生导出使用新增的 `POST /client/migration-hints/export`：服务端从本机连接存储生成同一份白名单 JSON，固定建议文件名为 `bakabase-connection-hints.json`，通过可选 `ILocalFileSaveDialog` 交给 Avalonia `StorageProvider.SaveFilePickerAsync`。用户选择目的文件后才写入；启用覆盖确认，同一进程同时只允许一个保存对话框。网页不能传入目的路径或任意内容，带请求体的调用返回 400；接口沿用客户端既有回环、Host/Origin 守卫，输出不含旧管理员密钥、设备身份、URL 凭据或活动连接设置。导出接口本身不依赖 Infrastructure 子模块修改，也没有添加通用 HTTP 写文件接口或全局 WebView 下载处理；后续 Windows 数据迁移的独立子模块修复见本文开头。

前端只在返回 `saved` 时显示已保存；`cancelled` 显示取消且不再下载。没有原生保存能力的 `unavailable`，或旧客户端缺少该接口的 404，才回退到既有 GET 与浏览器 Blob 下载。其他 HTTP 错误或无效结果明确显示失败。每次操作先清除上次结果；成功、取消、失败均在 `finally` 解除按钮忙碌状态，Shell 也在 `finally` 释放对话框互斥。通过浏览器访问带可用 GUI 的旧客户端时，同样会打开该客户端所在设备的保存对话框；无 GUI 的测试宿主使用浏览器下载。

新增数据统一存于 AppData 下 `federation/state.json`。身份、授权、配对状态使用一次原子替换，避免多文件部分提交；Unix 文件权限为 0600。普通配置导入不覆盖节点身份。损坏状态正常启动时拒绝使用；显式重置可恢复。还原本库使用新 LibraryEpoch，克隆成另一节点同时换 NodeId。直接从外部完整替换 AppData 无法自动识别，仍需按此操作重置。

`/federation/local/*` 只接受真实回环调用，并检查 Host/Origin。`/federation/v1/export/*` 每次独立验证 Node 授权、来源和分享状态；旧 Unrestricted、回环或管理员配对均不能豁免。新 Node 凭据不能进入旧管理接口。控制 API 使用独立的原始 JSON DTO、UTC ISO 8601 日期和真实 HTTP 错误码；SDK 从程序集生成。

媒体使用固定的非活动图片/音频/视频 MIME 白名单、nosniff 与 sandbox CSP。不支持 HTML/SVG、任意 URL 或压缩包内条目播放。映射只接受来源声明的 root ID 与本机根目录，校验目录祖先符号链接；无效映射回落到授权流。取得媒体票据之后，实际读取仍会重新验证当前授权、资源归属和映射。

目录动作只接受完整 ResourceRef。来源只返回授权范围内的 root ID、相对位置和类型，不接收远程打开目录命令；当前设备重新验证来源后，从本机映射解析文件夹。前端不接收来源绝对路径。无 GUI、无文件、未映射和映射失效都有明确原因。覆盖路径映射采用 `expectedMappings` 原子比较，冲突返回 409，要求重新审阅。

播放器发现支持 macOS 系统与用户 Applications 下的 VLC/IINA，IINA 使用 `iina-cli --no-stdin`；Windows 保留注册表和目录发现，Unix 保留 PATH 并检查执行权限。本机没有支持的播放器时返回可操作错误，网页预览仍可使用。

启动流程在后台只探测已安装的本机文件工具（界面据此显示真实状态），不再自动下载；空库启动浏览其他设备不会触发任何下载。ffmpeg、7-Zip、Lux、Locale Emulator 缺失时由实际调用给出现有安装提示；可选功能在工具安装中或刚探测为缺失（10 秒内）时直接跳过，不等待下载、不反复启动探测进程。安装失败或取消后重新探测磁盘，已有的可用版本不会被标记为未安装。

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

## 产品身份与发布契约

| 项目 | 统一版（已有一体版继续升级） | 旧纯客户端（继续保留） |
| --- | --- | --- |
| 安装包 ID / 主程序 / 单实例标识 | `Bakabase` | `Bakabase.Client` |
| macOS bundle ID | `com.anobaka.bakabase` | `com.anobaka.bakabase.client` |
| AppData 环境变量 | `BAKABASE_DATA_DIR` | `BAKABASE_CLIENT_DATA_DIR` |
| 非 Windows 数据目录名 | `Bakabase` | `Bakabase.Client` |
| Windows 数据目录名 | `Bakabase.AppData` | `Bakabase.Client.AppData` |
| 更新地址覆盖变量 | `BAKABASE_UPDATE_URL` | `BAKABASE_CLIENT_UPDATE_URL` |
| CDN 更新前缀 | `app/bakabase/releases/` | `app/bakabase-client/releases/` |
| 前端包 | 包含本机 `web/index.html` 与构建后的 JS | 不包含 `web` |

`src/scripts/check-release-contract.py` 在每次构建时检查源代码与实际 publish 目录（含传递依赖）：Service/Docker 不得带 Client、Shell、Avalonia、YARP；统一桌面不得带 Client、YARP；旧客户端不得带 Service、联合宿主、业务 migrations 或前端。macOS 自定义 plist 补齐了 `CFBundleExecutable`，打包前由 `prepare-macos-plist.py` 写入发布版本，并声明本地网络用途说明。

## 本机验证

```bash
dotnet build src/apps/Bakabase.App/Bakabase.App.csproj
dotnet build src/apps/Bakabase.Client.App/Bakabase.Client.App.csproj
dotnet test src/tests/Bakabase.Modules.Federation.Tests/Bakabase.Modules.Federation.Tests.csproj
dotnet test src/tests/Bakabase.Tests/Bakabase.Tests.csproj --filter "FullyQualifiedName~Federation"
dotnet build src/tests/Bakabase.Federation.TestHost/Bakabase.Federation.TestHost.csproj
python3 src/tests/federation-smoke/run.py
cd src/web && corepack yarn vitest run src/features/federation
```

三实例脚本为每个实例建立独立 SQLite/AppData、NodeId、密钥与端口（各 257 条同 ID 测试资源），覆盖定向授权与不传递信任、771 条完整分页与重复 cursor、严格条件、只读详情、媒体 HEAD/206/416、恶意 Host/Origin、旧 Unrestricted、撤销、代际重置、离线覆盖与远端 PlayedAt 不变。

开发者也可以直接运行两个无界面实例手动体验：发布 `Bakabase.Service`（`-p:RuntimeMode=DOCKER`），把 `src/web/dist` 复制为发布目录下的 `web`，分别以不同的 `BAKABASE_DATA_DIR` 和 `ASPNETCORE_HTTP_PORTS` 启动，再在各自的 `/#/federation/devices` 页面操作。

PR CI 在 Linux 上运行全部单元/集成测试、三实例冒烟、无界面发布内容检查和浏览器迁移冒烟；Windows/macOS 发布内容、macOS 原生 ABI 探针与视频流故障注入在发布前以 `CI` 手动触发 `suite=platforms` 运行，安装包并存验收为 `suite=packages`。

## 剩余发布门槛

- 真实网络：至少一次 Windows ↔ macOS 同一 Wi-Fi、桌面 ↔ 桥接模式 Docker NAS 的双向配对、搜索、播放、撤权、休眠唤醒与 DHCP 地址变化。
- 最终安装包上由人工完成一次“首启 → 发现 → 配对 → 搜索 → 详情 → 播放 → 离线 → 恢复”的完整桌面流程（Windows x64、macOS ARM/Intel）。
- 签名与公证、生产 stable/beta 更新通道演练；macOS 旧版纯客户端的自动更新路径不可用，需提供手动迁移说明。
- 规模：每次查询都会捕获整个本库的轻量投影（有捕获工作区与期限预算），十万级以上且元数据丰富的库可能触发预算；后续以索引预筛或复用捕获解决。
