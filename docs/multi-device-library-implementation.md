# 多设备联合媒体库：实施与验证记录

执行日期：2026-09-20 至 2026-09-21。开发分支：`codex/multi-device-library`。

开始执行前已 fetch `origin/main`，以 `f1fa1469f32f794f17895f5ba886cc14ddd42883` 新建独立 worktree。执行期间主线若继续推进，不改变这次记录的起始基线。

子模块固定为父仓库版本：

- Bakabase.Infrastructures：`a3715f40d7c470ec6610ab1a1fdfb8ce21231893`
- LazyMortal：`f35a158a8395c09216451f77d04e6fea33b9e77b`

原工作区不切分支、不覆盖已有修改。新增模块不修改业务数据库 schema，不互换桌面应用的包名、AppData、单实例标识或更新源。

## 当前可用流程

统一版保留原本机资源管理页，新增“联合媒体库”和设备页。浏览器入口使用应用原有 HashRouter：`/#/federation`、`/#/federation/devices`。

1. 在来源设备明确开启只读分享；远程访问关闭时，界面要求同时启用原有配对保护。
2. 在另一设备输入来源地址与一次性邀请码，或发起请求后由来源批准，再领取授权。
3. 每个方向单独授权；A 能读取 B 并不代表 B 能读取 A，也不代表 A 能读取 B 的其他设备。
4. 选择本机、全部已启用来源或指定设备搜索。卡片、详情、缓存和深链使用完整的 `nodeId + libraryEpoch + resourceId`。
5. 详情只读；图片、音频和视频通过本机受控媒体会话预览。系统播放器由当前设备发现和启动；远端不启动播放器，不修改播放历史。
6. 来源离线会明确显示查询未覆盖该设备。翻页中途失败会中断当前会话；不会悄悄删掉参与节点后继续返回看似完整的结果。

本机旧页面仍承担编辑、删除、任务和高级搜索。联合视图不复制远端资源表、配置、词典、管理员权限或 SignalR 状态。

## 实现位置与边界

| 位置 | 行为 |
| --- | --- |
| `src/modules/Bakabase.Modules.Federation/Identity`、`Peers` | 节点身份、代际、定向配对、撤销、分享和路径映射 |
| `Security`、`Transport` | 独立 Node 签名、重放保护、固定对端会话、取消、已认证时钟 |
| `Contracts`、`Queries` | 严格查询、不可变节点快照、固定参与者、全量有界归并分页 |
| `Media` | 绑定资源与授权的资产票据、路径边界、固定媒体类型 |
| `src/apps/Bakabase.Service/Components/Federation` | 实际数据库投影、宿主适配、鉴权管线、媒体代理 |
| `src/web/src/features/federation` | 独立只读视图、设备管理、迁移提示与连接信息导入 |
| `src/tests/Bakabase.Federation.TestHost` | 使用正式 Service 启动流程的独立进程夹具 |
| `src/tests/federation-smoke/run.py` | 三个独立进程之间的真实网络验收 |

共享发现与本机播放器定位已下沉到 RemoteAccess/Player 模块；Service 不引用 Client.Remoting。旧客户端保留原协议和转发器。新增 Node 协议不需要搬动旧签名、JSON 兼容或活动服务器逻辑。

新增数据统一存于 AppData 下 `federation/state.json`。身份、授权、配对状态使用一次原子替换，避免多文件部分提交；Unix 文件权限为 0600。普通配置导入不覆盖节点身份。损坏状态正常启动时拒绝使用；显式重置可恢复。还原本库使用新 LibraryEpoch，克隆成另一节点同时换 NodeId。直接从外部完整替换 AppData 无法自动识别，仍需按此操作重置。

`/federation/local/*` 只接受真实回环调用，并检查 Host/Origin。`/federation/v1/export/*` 每次独立验证 Node 授权、来源和分享状态；旧 Unrestricted、回环或管理员配对均不能豁免。新 Node 凭据不能进入旧管理接口。控制 API 使用独立的原始 JSON DTO、UTC ISO 8601 日期和真实 HTTP 错误码；SDK 从程序集生成。

媒体使用固定的非活动图片/音频/视频 MIME 白名单、nosniff 与 sandbox CSP。不支持 HTML/SVG、任意 URL 或压缩包内条目播放。映射只接受来源声明的 root ID 与本机根目录，校验目录祖先符号链接；无效映射回落到授权流。取得媒体票据之后，实际读取仍会重新验证当前授权、资源归属和映射。

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

要求：仓库固定子模块、.NET 9 SDK、Node/Corepack、Python 3。所有下列服务夹具只使用自己新建的临时数据目录，不连接用户媒体库。夹具显式移除依赖自动下载服务，不能用它证明正式桌面启动没有下载。

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

夹具主动分享测试资源，正式应用默认不分享。无人值守 Service 的一次性 Node 邀请入口是显式参数 `--federation-invite-on-start`，须先启用分享及远程访问；它不复用旧 Device 管理邀请码。

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
| 前端全量单测 | 94 个文件、887/887 测试通过 |
| 前端全量 ESLint | 0 errors，保留 901 个现有 warnings；新增联合模块 0 warnings |
| 前端生产构建 | Vite build 通过 |
| 全量 TypeScript 检查 | 基线和当前均 310 个既有错误；相同依赖与命令的完整日志逐字节一致，新增 0 |
| Chromium 真实双节点 | 514 条、2/2 来源、50→100 翻页、远端同 ID 详情、音频 metadata、空搜索、连接错误全部通过 |
| 浏览器重复刷新 | 连续 3 次刷新产生 4 个会话；3 个旧会话均已释放并返回 410，没有 JS pageerror |
| 分批后端 runner | 构建/发现/执行/清理链通过；不存在的 class 返回失败，空跑不能冒充成功 |

全套后端曾因原测试辅助类留下 321 个临时数据库、累计约 13 GB 耗尽磁盘而中断。仅清理本轮生成的目录后，已明确完成的前 498 项保留记录，余下 125 个 class 在独立进程和临时目录中全部补跑；合并计数与 discovery 的 1,579 个主工程 case 一致。没有改断言，也没有漏掉中断时失败的初始化用例。之后增加的局部回归单独运行，不重复计入全套统计。

本机原始日志位于 `/tmp/bakabase-backend-regression/`、`/tmp/bakabase-browser-tests/`，三实例终验日志为 `/tmp/bakabase-federation-smoke-final.log`。上述测试使用本轮创建的数据，未启动或改变用户原媒体库。

TypeScript 基线核验使用 `git archive f1fa1469 src/web` 导出的临时源码和相同 node_modules，运行 `corepack yarn exec tsc --noEmit --pretty false`，没有通过回退当前源码或改 SDK 来消除错误。记录为 `/tmp/bakabase-tsc-baseline-report.md`。

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

## 与原计划的差异及发布门槛

代码层已实现身份/配对/权限、搜索分页、只读详情和媒体、独立 UI、迁移信息白名单。原计划 P00–P09 中需要跨 OS、真实桌面、安装包才能判定的验收项不会因代码完成而自动勾选。

- 状态存储采用单个原子文件，接口使用 raw DTO，媒体通过 ASP.NET 的范围文件结果与受控代理实现，未修改旧 FileController。
- 原 BakabaseHost 的依赖发现和必需依赖下载本来就在后台 Task.Run 中；本轮保留该行为。没有完成按本机用途延后下载的独立优化，也没有用测试夹具代替这项正式启动成本验收。
- 当前能力覆盖文件系统中的支持媒体；压缩包条目、格式转码、目录打开等更广泛媒体操作不在这次可用闭环内。
- 旧客户端提供统一版提示和连接信息导出；统一版只导入名称、地址和映射提示。每个来源必须重新授权，旧管理员密钥不会迁移。
- 未删除旧客户端产物、切换更新 feed、改动公开下载入口或发布安装包。等真实升级/迁移矩阵通过后，再收敛新用户下载入口。
- 还需在最终 Avalonia 安装包上验证 Windows/macOS 各目标、系统播放器启动、Windows 路径映射、休眠恢复、真实 NAS/Docker 和多机网络条件，以及仅旧客户端/仅统一版/两者同机的升级路径。

发布前继续按原计划 P10–P12 的验收清单推进。当前证据支持开发分支内试用和进一步评审，不等于跨平台发布验收已经完成。
