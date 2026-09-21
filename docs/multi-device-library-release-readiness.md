# 多设备媒体库：发布准备与迁移验收

记录日期：2026-09-21。对应开发分支 `codex/multi-device-library`；初始实现基线为 `f1fa1469`，本轮验收从 `b80ddfbe` 继续，包含其后的工作区修改。没有发布、推送、切换更新 feed 或收敛官方下载入口。

当前设计以[多设备联合媒体库执行计划](multi-device-library-execution-plan.md)为准，功能、预算和已有测试记录见[实施记录](multi-device-library-implementation.md)。[旧拆分设计](pc-client-design.html)与[旧拆分执行计划](pc-client-execution-plan.html)保留历史内容，并增加了当前状态入口。

代码和自动化已提供可执行的发布检查。本机通过不等于所有平台已通过；Windows、Linux、macOS Intel 的新矩阵需在 CI 实际运行，最终安装包、跨物理设备、实际播放器和升级验收仍是扩大试用前的门禁。

## 1. 产品身份与包内容

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

身份检查同时读取源代码、AppData profile、plist、打包与上传 workflow。它不是对生产 CDN 清单的在线审计，也不会把期望值跟着一次身份改名自动更新。

`src/tests/upgrade-tests/check-release-contract.py` 检查实际 publish 目录内的 DLL 和 `.deps.json`，包括传递依赖：

- Service/Docker 必须有 Service/Federation，不得有 Client、Shell、Avalonia、YARP。
- 统一桌面必须有主程序、Shell、Service/Federation，不得有 Client、YARP。
- 旧客户端必须有 Client、Client.Remoting、Shell，不得有 Service、Federation 宿主或业务 migrations；不得捎带前端目录。
- `--require-web` 不接受只有占位 `index.html` 的发布目录。

`.github/workflows/_build.yml` 在生成正式安装产物前执行上述内容检查。它保留两个产品的打包、artifact 与部署身份，没有把旧客户端 feed 指向统一版。

## 2. 已接入的 CI 矩阵

`.github/workflows/ci.yml` 增加独立、失败不互相取消的四个平台作业。每个作业上限 40 分钟；先执行自动测试，再检查实际 publish 输出。所有平台保留 JSON、日志和 TRX，artifact 名称为 `federation-<rid>-evidence`，保留 14 天。

| Runner 标签 | RID | 三独立 Service 进程 | 实际发布目录检查 |
| --- | --- | --- | --- |
| `windows-latest` | `win-x64` | 是 | Service、统一桌面、旧客户端 |
| `ubuntu-24.04` | `linux-x64` | 是 | Service/Docker 角色 |
| `macos-15-intel` | `osx-x64` | 是 | Service、统一桌面、旧客户端 |
| `macos-15` | `osx-arm64` | 是 | Service、统一桌面、旧客户端 |

macOS 标签依据 [GitHub 托管 runner 官方清单](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)：Intel 与 ARM 必须分开指定；不再用 `macos-15` 冒充 Intel。Linux 此处验证无 GUI 的 Service 角色，没有新增一个未交付的 Linux 桌面安装包。

每个平台实际执行：

1. 产品身份检查和 6 个发布 guard 反例测试。
2. `run-compatibility.py` 调用 8 个真实测试类，覆盖 AppData profile、环境变量、路径解析/迁移/失败恢复、旧安装发现、更新源隔离、旧客户端真实 HTTP 白名单导出。
3. Federation 模块测试、Player 模块测试，均调用实际 MSTest 可执行 runner 并要求至少执行一个测试。
4. 三进程 HTTP smoke：各有独立 AppData、SQLite、端口、身份和密钥，每库 257 条，合计 771 条；包括配对方向、无信任传递、完整归并与 cursor 重试、资源来源、媒体 HEAD/Range/416、撤销、代际变化、离线覆盖、无远端播放历史写入。
5. 新安装浏览默认关闭；显式启用后才查询；关闭浏览释放旧本机会话/媒体，但已授权对端仍能读取本机分享。重新启用不复活旧会话。
6. 用前端作业生成的实际生产构建检查各角色 publish 内容。

Smoke 请求/启动总期限默认 300 秒，单次 HTTP 最多 15 秒，启动最多 90 秒；清理时终止并等待自己创建的进程，再删除自己的数据库目录。`--keep` 只用于手动调试。主后端大套件按 class 使用独立进程和临时目录，退出后清理，避免旧 fixture 留存 SQLite 填满磁盘；失败和日志仍保留。

复现命令（仓库根目录）：

```bash
python3 src/tests/upgrade-tests/check-release-contract.py
python3 src/tests/upgrade-tests/test_release_contract.py
python3 src/tests/upgrade-tests/run-compatibility.py --dotnet /absolute/path/to/dotnet
/absolute/path/to/dotnet run --project src/tests/Bakabase.Modules.Federation.Tests -- --minimum-expected-tests 1
/absolute/path/to/dotnet run --project src/tests/Bakabase.Modules.Player.Tests -- --minimum-expected-tests 1
/absolute/path/to/dotnet build src/tests/Bakabase.Federation.TestHost/Bakabase.Federation.TestHost.csproj
python3 src/tests/federation-smoke/run.py --dotnet /absolute/path/to/dotnet --timeout 300 --results-directory /absolute/path/to/results
```

不要用裸 `dotnet test` 的成功退出码证明这些 MSTest.Sdk 项目执行了测试。`run-compatibility.py --no-build` 只适用于已经构建当前源码的情况。

## 3. 本轮本机证据与适用范围

执行机器为 macOS ARM64，.NET SDK 9.0.100 / runtime 9.0.0。以下证据均是实际执行结果；本机临时路径用于这次审计，CI 将生成自身 artifact，临时日志不是长期发布记录。

| 检查 | 结果 | 本机证据 |
| --- | --- | --- |
| 产品身份/源依赖 | 通过 | `/tmp/bakabase-release-readiness/*-contract.log` |
| 发布 guard 反例 | 6 / 6 通过 | `test_release_contract.py` 实际执行 |
| AppData、旧更新源与迁移导出 | 131 / 131 通过，0 skipped | `/tmp/bakabase-release-readiness/compatibility/summary.json` |
| 实际 Service、统一桌面、旧客户端 publish | 三角色通过；前两者带真实 web | `/tmp/bakabase-release-readiness/{server,unified,client}-package.json` |
| 三真实宿主 HTTP | 771 行遍历及上述故障/开关场景通过 | `/tmp/bakabase-release-readiness/smoke/result.json`、`smoke.log` |
| Player 模块 | 72 / 72 通过，0 skipped | `/tmp/bakabase-release-readiness/player.log`、`player/*.trx` |
| 前端当前源码 | 95 文件、913 测试通过；生产构建通过 | `/tmp/bakabase-frontend-cont-*.log`，详见实施记录 |
| 前端全量类型检查 | 310 个主线既有错误，与隔离 `f1fa1469` 的输出逐字节一致 | `/tmp/bakabase-frontend-cont-tsc.log` 与 `/tmp/bakabase-tsc-baseline.log` |

正式 macOS publish 曾启动，并在 `/tmp/bakabase-federation-native-check.log` 记录 `NativeWebViewHost` 导航至 `http://localhost:34567`。此次桌面自动化报告 Mac locked，无法解锁；只记录进程/导航启动，不能记为原生 GUI 操作通过。双实例浏览器流程是另行验证，详见实施记录。

上述 publish 是当前源码的 framework-dependent 输出，不是经过签名、公证、Velopack 安装/更新的最终安装包。自动化媒体验证使用真实 WAV HTTP 字节与 Range，不能代替第三方播放器首帧、拖动、暂停续播的真机记录。

播放器发现现在检查 macOS `/Applications` 和 `~/Applications` 下 VLC/IINA 的已知 bundle 可执行文件，以及 PATH。IINA 使用 `iina-cli` 和 `--no-stdin`，参照 [IINA 官方 CLI 源码与帮助](https://github.com/iina/iina/blob/develop/iina-cli/main.swift)。Unix 检查当前进程执行权限，Windows 检查可执行二进制；不因同名文件存在就选用。自动测试不依赖真实安装，没有下载或安装播放器。

计划 P00–P10 的行为覆盖、10k/100k 查询基线与全后端回归见实施记录。这里新增的是 P11 平台门禁、发布目录检查与 P12 兼容/迁移验收。10k/100k 的本机时间和分配量不能推导局域网 p95、进程内存高水位、首帧或取消释放性能；这些保留在下一节的真实设备要求中。

## 4. 发布前尚需执行的门禁

| 门禁 | 必须记录的操作与证据 | 当前状态 |
| --- | --- | --- |
| 新 CI 四平台作业 | 同一最终 commit SHA 的四平台 artifact；不得用工作区本机结果替代远端结果 | 已接线，尚未触发远端 CI |
| 最终桌面包 | Windows x64、macOS ARM/Intel 的实际安装包，记录 SHA、版本、架构、签名/公证和启动结果 | 尚未完成整套安装包矩阵 |
| 三种安装来源 | 全新安装；已有一体版原位升级；只有旧客户端时并装统一版；额外验证两者原本同机安装 | 自动身份/路径测试已过，真实安装升级仍待执行 |
| 更新与数据隔离 | stable/beta 各按原 feed 更新，重启后有效 AppData 不变，独立单实例/端口/进程并存，原库 SQLite 完整性可复核 | 不访问生产 feed，真实升级待执行 |
| 完整 GUI 主路径 | 最终桌面壳首启、空库设置/日志、开启浏览、配对、联合查询、详情、播放、离线、恢复、关闭窗口；确认远端不启动播放器 | 自动流程已有；最终包完整验收待执行 |
| 物理设备和 NAS | Windows ↔ macOS ARM/Intel；至少一组桌面 ↔ Docker/NAS；各方向单独授权、撤销、断网和重启 | 三进程 HTTP 通过不等于跨物理设备通过 |
| 媒体和映射 | 真正安装的 VLC/IINA 等零映射流播放，seek、暂停续播、长流；Windows/macOS 映射与打开目录；来源离线与失效映射 | 参数/HTTP/边界自动验证；真人播放器矩阵待执行 |
| 性能验收 | 10k/100k 与两大库联合，冷/热状态、网络条件、准备/首屏/翻页 p50/p95、请求数/字节、内存高水位、取消后释放、媒体首帧 | 本机投影基线已有；全网络/真机指标待补 |
| 发布与迁移实际演练 | 从旧客户端导出、统一版刷新恢复草稿、重配对/重绑映射、重复导入、冲突保留/替换；原程序和源文件可继续使用 | 自动化已补，最终安装版本之间仍待执行 |
| 下载入口清单 | 发布后逐一验证 GitHub/CDN 目标确实存在且架构正确；先保留旧下载/feed，再更新推荐入口 | 未发布；不提前改入口或宣布旧端停止维护 |

验收记录须包含最终提交、协议版本、OS/架构、数据规模、操作、结果与日志。失败修复后重跑受影响场景，不通过降低数据量、跳过断言或放大预算制造成功。

`src/tests/upgrade-tests/run-*.sh` / `run-windows.ps1` 旧脚本只把当前代码以两个合成版本发布到临时目录、替换目录并比较样本哈希。它们不启动 GUI、不执行 updater、不验证真实旧版本升级，SQLite 命名样本也不是有效数据库。详见[准确适用范围](../src/tests/upgrade-tests/README.md)。这类结果不能关闭本节真实升级门禁。

## 5. 给旧安装用户的迁移步骤

### 已有一体版

在原 `Bakabase` 身份和更新源上升级，保留原 AppData 与本机库。联合浏览和向其他设备分享是两个独立开关，默认不开启。只在需要时主动启用浏览、为每个来源申请只读授权；本机资源编辑、任务和日志仍在本机。

### 已有纯客户端

1. 保留 `Bakabase.Client` 安装并启动它，从连接迁移入口导出提示文件。导出读取旧客户端当前解析的数据目录，因此支持 `BAKABASE_CLIENT_DATA_DIR` 和已有重定向，不猜固定磁盘路径，也不移动源文件。
2. 安装/打开统一版 `Bakabase`，进入设备页选择提示文件并预览。导入只有 `format/version/servers` 下的名称、规范化 origin 地址与旧路径映射提示；不含旧 key、管理权限、ServerId、cookies、options、数据库或更新配置。
3. 合法提示草稿保存在本机浏览器 localStorage 的专用白名单 schema，刷新后恢复；按规范化地址和路径对幂等合并。不是复制整个旧 localStorage/IndexedDB，也不是把 peer/grant 写入服务器。
4. 点候选仅填写连接地址。逐个来源重新申请 Node 只读授权，由新握手确定实际 NodeId/LibraryEpoch。旧管理凭据不能直接换成新授权，也不会获得朋友设备的权限。
5. 旧映射仅供对照。授权后选择来源当前声明的 `sourceRootId` 与当前设备的实际路径再保存；需要改变/删除既有映射时，显式选保留或替换。提交携带预览时的 `expectedMappings`；并发变化返回 409，刷新现状并重新审阅，不覆盖另一窗口的新设置。
6. 验证浏览、播放、离线恢复后可继续保留旧安装。迁移不会自动卸载客户端、删除源数据或互换更新源。

提示文件仍包含设备地址和路径，用户可自行删除下载的副本和页内草稿；它不携带授权密钥。直接整份复制 AppData 不是该迁移流程：恢复原节点库要显式轮换 LibraryEpoch，克隆为新节点还要换 NodeId，再重新授权。不要让两台运行设备复用同一节点身份。

### 回退与发布顺序

停止使用联合能力时，关闭本机浏览和不再需要的分享/授权，原本机库继续可用；旧客户端独立保留。新 Federation 状态是加法文件，业务数据库未为本功能改 schema，但回退前仍应保留备份。

先完成本节门禁并发布经过验证的统一版和旧客户端迁移提示版本，再收敛新用户下载推荐。旧 feed 保持原产物且可读；维护结束日期另行公布，不在这一轮实现中预设。
