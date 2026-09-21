# 多设备媒体库：发布准备与迁移验收

记录日期：2026-09-21。对应开发分支 `codex/multi-device-library`；初始实现基线为 `f1fa1469`，后续验收在 `b80ddfbe`、`cf787d13` 的基础上继续，包含其后的工作区修改。没有发布、推送、切换更新 feed 或收敛官方下载入口。

当前设计以[多设备联合媒体库执行计划](multi-device-library-execution-plan.md)为准，功能、预算和已有测试记录见[实施记录](multi-device-library-implementation.md)。[旧拆分设计](pc-client-design.html)与[旧拆分执行计划](pc-client-execution-plan.html)保留历史内容，并增加了当前状态入口。

代码和自动化已提供可执行的发布检查。本机通过不等于所有平台已通过；Windows、Linux x64、macOS Intel 的新矩阵需在 CI 实际运行，签名安装包、跨物理设备、播放器矩阵和升级验收仍是扩大试用前的门禁。

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

实际 Velopack 1.2.0 打包暴露了两个原有 plist 缺陷：两个产品均缺少 `CFBundleExecutable`，且自定义 plist 的版本固定为 `1.0.0`。现已补齐主程序名称；打包前由 `prepare-macos-plist.py` 写入发布版本，保留两个产品原 bundle ID。打包后另检查实际 `*-Portable.zip` 中的 plist、版本、Mach-O 主程序及执行权限，避免只审计源文件却交付无法正常识别的 `.app`。

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

1. 产品身份检查和 14 个发布 guard / plist 测试。
2. `run-compatibility.py` 调用 8 个真实测试类，覆盖 AppData profile、环境变量、路径解析/迁移/失败恢复、旧安装发现、更新源隔离、旧客户端真实 HTTP 白名单导出。
3. Federation 模块、Player 模块，以及 `Bakabase.Tests.Federation` 的完整命名空间（包含子命名空间）测试，均调用实际 MSTest 可执行 runner 并要求至少执行一个测试。命名空间筛选不存在时失败，不允许空跑成功；原 Ubuntu 全量作业本已覆盖 Service 测试，本轮补齐其四平台执行。
4. 三进程 HTTP smoke：各有独立 AppData、SQLite、端口、身份和密钥，每库 257 条，合计 771 条；包括配对方向、无信任传递、完整归并与 cursor 重试、资源来源、媒体 HEAD/Range/416、撤销、代际变化、离线覆盖、无远端播放历史写入。
5. 新安装浏览默认关闭；显式启用后才查询；关闭浏览释放旧本机会话/媒体，但已授权对端仍能读取本机分享。重新启用不复活旧会话。
6. 用前端作业生成的实际生产构建检查各角色 publish 内容。

另有 Ubuntu Chromium 作业 `federation-browser`，使用相同生产前端和三个独立进程（两个 Service、一个真实 ClientHost/ClientStartup），执行旧端连接与下载提示文件、统一版导入/刷新/重复导入、来源界面重新批准只读访问、媒体和跨窗口关闭浏览。旧客户端使用真实 Client AppData profile，但 GUI adapter 为空；此作业不代替 Avalonia 或安装器验证。Playwright 在测试目录独立锁定，未加入产品依赖。

浏览器作业还运行容器/升级测试脚本的故障与清理守卫（不启动 Docker 或原生应用），并生成一次性 120 秒 WebM，经过两个真实配对宿主及限速/故障转发器，验证真实呈现帧、暂停/恢复、90 秒跳转、超过响应头期限的持续传输、断流后的 Range 恢复、30 秒空闲超时和流中关闭浏览。源视频不上传，结果和截图随既有 artifact 保留。它是受控 loopback 速率/故障注入，不代替物理网络测试。

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
| 发布 guard / plist | 14 / 14 通过 | `test_release_contract.py` 实际执行 |
| AppData、旧更新源与迁移导出 | 131 / 131 通过，0 skipped | `/tmp/bakabase-release-readiness/compatibility/summary.json` |
| 实际 Service、统一桌面、旧客户端 publish | 三角色通过；前两者带真实 web | `/tmp/bakabase-release-readiness/{server,unified,client}-package.json` |
| 三真实宿主 HTTP | 771 行遍历及上述故障/开关场景通过 | `/tmp/bakabase-release-readiness/smoke/result.json`、`smoke.log` |
| Player 模块及本轮策略 | 模块 74 / 74、策略/参数 14 / 14、旧播放处理器 20 / 20 通过 | `/tmp/bakabase-player-policy-results.log`；本轮对应 TRX |
| 前端当前源码 | 98 文件、932 测试通过；生产构建、定向 lint/格式检查通过 | `/tmp/bakabase-native-migration-{all-tests,web-build}.log` |
| 前端全量类型检查 | 310 个主线既有诊断，无新增/消失；AppInfo 新增链接使其中 4 条诊断行列移动，去掉行列后全文一致 | `/tmp/bakabase-identity-recovery-tsc-comparison.json` 与 `/tmp/bakabase-tsc-baseline.log` |

较早一轮原生操作曾因 Mac 锁屏受阻；本轮已可操作桌面，使用独立 AppData 和实际 Velopack portable `.app` 验证：空库首启、主动开启浏览、通过设备页配对、257 条 / 2 个来源、完整只读详情、原生音频预览播放/暂停/拖动到 63 秒/恢复、映射后由 Finder 打开真实测试目录、来源离线时明确显示覆盖不完整、本机设置及日志仍可用、来源重启后无需重新配对恢复结果。没有操作用户原媒体库。

前两轮三角色 publish 是 framework-dependent 输出；后续统一版以正式 workflow 的 `--self-contained -r osx-arm64 -p:RuntimeMode=MACOS` 生成 portable，无 SDK 环境变量启动成功，重启保留身份/授权，257 条查询和真实音频 Range 206 通过（`/tmp/bakabase-native-validation/final-package-http.json`）。本轮统一版和旧客户端均生成 self-contained Velopack portable `0.0.2-federation.4`，包身份/版本/主程序审计通过，实际并行启动与正常退出通过；SHA256、隔离配置和操作结果见 `native-result.json`。这些合成测试包未签名或公证，未安装 `.pkg`；该轮原生界面操作未执行 updater，后续独立的真实升级证据见下文。音频 WAV 也不能代替视频解码、长流和弱网体验。

解锁后已从 self-contained 统一版界面启动官方 VLC，并验证播放与拖动；真实暂停失败，暴露了此前 headless 测试只确认 Range/seek 的不足。AVIO 不能暂停普通 HTTP 流，RC 的 paused 状态也不代表播放时钟停止；因此移除该策略，测试改为比较暂停前后时钟。原生预览和本机映射不受此限制。较早包与本轮统一版、旧客户端均已通过“关闭 → 退出”正常终止进程；最终验收汇总在 `/tmp/bakabase-native-validation/native-result.json`。

播放器发现检查 macOS `/Applications` 和 `~/Applications` 下 VLC/IINA 的已知 bundle 可执行文件，以及 PATH。IINA 使用 `iina-cli` 和 `--no-stdin`，参照 [IINA 官方 CLI 源码与帮助](https://github.com/iina/iina/blob/develop/iina-cli/main.swift)。Unix 检查当前进程执行权限，Windows 检查可执行二进制；不因同名文件存在就选用。单元测试不依赖真实安装；本轮另从 VideoLAN 官方源下载并校验 VLC 3.0.23 ARM64，在临时目录运行，并以测试专用 `~/Applications/VLC.app` 链接验证发现，不覆盖已有应用。

VLC 的原始 localhost 请求实际遇到系统代理 503；[Darwin 代理实现](https://github.com/videolan/vlc/blob/3.0.x/src/darwin/netconf.c)没有按目标地址绕过代理。新策略只读检查本机代理配置，受影响或无法确认时，在发送媒体票据前跳过 VLC，自动选择支持显式直连的 mpv/IINA；没有可用候选时提示使用预览、配置映射或安装支持的播放器。VLC 正常使用原生 HTTP 输入，不再使用 AVIO 或可能持续下载全文件的 timeshift 绕过。Windows/Linux 暂无可信的 VLC 代理检测，零映射流也使用上述替代路径；本机映射不受影响。mpv 使用[单文件选项作用域](https://mpv.io/manual/stable/#per-file-options)；IINA 的覆盖作用在 CLI 新启动的 PlayerCore 实例，后来在同一实例打开的文件也可能继承，未写入用户偏好（[稳定版启动实现](https://github.com/iina/iina/blob/v1.4.4/iina/AppDelegate.swift)）。

最终统一版原生界面实际验证两种安装状态：只有 VLC 时显示中文 `PlayerProxyUnsupported`，没有启动 VLC 子进程；仅向测试进程的临时 PATH 增加官方 IINA 1.4.4 后，不重启应用即自动发现并播放同一来源的 600 秒音频。暂停时钟在约 10 秒观察期间保持 `00:08`，恢复后拖动到 `07:00`，继续到 `07:08` 再暂停稳定。IINA 下载校验、Developer ID 签名和公证状态已核验；没有修改系统代理或全局 PATH。独立 mpv 仍未实际测试。本轮测试进程和发现链接已清理。

新增证据：

| 检查 | 结果与边界 | 本机证据 |
| --- | --- | --- |
| macOS 原生连接提示迁移 | self-contained 旧客户端实际保存/取消对话框、白名单文件检查、旧连接字节不变、无业务数据库、统一版原生文件选择与草稿预览通过 | `/tmp/bakabase-native-validation/native-export-result.json` |
| macOS 原生播放器策略 | VLC-only 明确拒绝；自动选择官方 IINA，播放、暂停时钟、恢复、拖动和再次暂停通过 | `/tmp/bakabase-native-validation/native-result.json` |
| 原生导出与浏览器回退回归 | ClientPipeline 34 / 34；真实 Chromium 回退下载/导入/新授权/恢复克隆通过，0 pageErrors | `/tmp/bakabase-native-migration-pipeline-tests/summary.json`；`/tmp/bakabase-browser-native-export-fallback/result.json` |
| 四平台 Service gate 本机执行 | 完整命名空间 9 类、42 / 42；选择器回归 8 / 8；不存在的命名空间实测非零退出 | `/tmp/bakabase-ci-service-federation-gate/summary.json`、`/tmp/bakabase-ci-service-empty-gate/summary.json` |
| 真实视频和流故障 | 13 MiB/120 秒 WebM，256 KiB/s 限速下约 772 ms 首帧；呈现 90 秒帧并请求后段 Range；暂停、断流恢复、30 秒 idle、流中关闭和历史不写入通过 | `/tmp/bakabase-media-stream-presented-frames/result.json` |
| Linux x64 三宿主 | `51601e31`、官方 ASP.NET 9.0.20 x64，771 条全流程约 18.5 秒通过；macOS ARM 交叉构建、OrbStack x64 仿真执行 | `/tmp/bakabase-linux-x64-51601e31-2/smoke/result.json` |
| Linux x64 补充回归 | Player 74 / 74、兼容性/迁移 135 / 135，0 失败/跳过；独立补测，未把旧 TRX 混入计数 | `/tmp/bakabase-linux-x64-51601e31-tests/result.json` |
| macOS ARM 真实旧版升级 | `f1fa1469` → `51601e31`；旧应用创建资源、自身 updater 下载、真实 UpdateMac 替换后新应用启动；2 条资源逐字段保留、两次 SQLite 完整性通过 | `/private/tmp/bakabase-real-upgrade-51601e31/run5/report.json`、`velopack-native.log` |
| 真实旧客户端迁移浏览器链路 | 旧连接保持可用且文件未变；白名单导出/草稿恢复/幂等/新授权/114 条联合资源/跨窗口关闭及两种身份恢复；0 pageErrors | `/tmp/bakabase-browser-migration-recovery-final/result.json` |
| 双大库 HTTP | 2×10k、2×100k 冷/热完整遍历、传输量与 RSS、执行中取消和额度复用通过；只含 loopback 网络 | `/tmp/bakabase-federation-http-benchmark-final-20260921/result.json` |
| Linux ARM64 实际执行 | Federation 47、Player 72、兼容 131 通过；实际 Service 包角色审计通过。由 macOS SDK 跨平台构建，在 Ubuntu 24.04 实际运行 | `/tmp/bakabase-linux-cross-results/summary.json` |
| Linux 未完成项 | ARM 容器原生编译及三宿主启动遇 SIGILL；后续无业务依赖最小程序捕获 .NET 9 PAL 的 `rdvl` 已知缺陷。x64 SDK 镜像下载超时，未算通过 | 同上；`/tmp/bakabase-sigill-d4e97c86/` |

Linux runner 使用独立容器、源码副本、资源和总时限，已清理本轮创建的容器和数据；未关闭用户容器。Windows 路径测试现采用当前平台绝对路径，播放器发现测试隔离真实 `%ProgramFiles%` 内容，本机 27 项回归通过；这不等于 Windows CI 已执行。

后续 [Linux ARM64 专项诊断](linux-arm64-runtime-diagnosis.md)在不包含 Bakabase 代码的同一 DLL 上得到 .NET 9.0.20 三次 SIGILL、官方 .NET 10.0.12 三次通过；捕获的指令及 SME/no-SVE 环境与官方 CoreCLR PAL 修复吻合。没有因此修改产品框架或把 ARM 诊断当成 Linux x64 CI 通过；当前发布目标仍是 Linux x64。

本轮使用 `run-linux-cross-container.py` 在 macOS 本机交叉构建后，以官方 ASP.NET 9 x64 运行时镜像执行，绕开的是不适用的 ARM 执行环境，没有修改产品运行时。镜像 manifest digest、平台、提交、选择的测试套件与 TRX 计数均保留；宿主挂载只读，测试需要写入的位置使用容器内临时副本。两个阶段各自清理自己的容器与源码副本，未修改用户容器。该 x64 仿真结果补足此前本机 Linux 执行证据，但仍不等于 GitHub 原生 Linux runner 结果。

真实升级使用基线 `f1fa1469` 及其锁定子模块构建旧后端和旧前端，再打包为合成版本 `0.0.1-upgrade.1`。旧应用通过 HTTP 创建两条资源，调用现有检查、下载、重启接口；实际 `UpdateManager` 从 loopback feed 下载校验新版，实际 `UpdateMac` 完成替换。新应用 `51601e31` 的 Service DLL SHA 与新版包一致，有效 AppData 路径不变，两条资源响应逐字段相同，升级前后 SQLite `integrity_check=ok`，外部数据文件 SHA 不变。最终运行的本地 feed 共 3 次请求、89,889,144 bytes（含新应用启动后的检查）；旧/新应用及 updater 进程均已退出，临时安装和数据目录已清理。资源检查另断言恰好返回所创建的两个唯一 ID、两份 SQLite 均有 2 行；空结果或缺项不能误报升级成功。

复现脚本为 `src/tests/upgrade-tests/run-velopack-macos.py`。测试专用 startup hook 将 Velopack cache/log 放进临时目录，并为真实 apply 加入 `--norestart --silent`；替换后由脚本以同一隔离环境启动新程序。没有复制新目录冒充 updater，也没有改产品代码。证据覆盖真实旧源码到新版的 portable 更新与数据保留；不覆盖历史官方签名包、默认用户缓存、LaunchServices 自动重启、生产 feed 或系统安装。macOS 管理的 WebKit/SavedState 缓存不在 AppData/Velopack 隔离范围内，未删除用户系统缓存。完整命令和边界见[升级测试说明](../src/tests/upgrade-tests/README.md)。

计划 P00–P10 的行为覆盖、查询基线与全后端回归见实施记录。双大库的新 HTTP 基线补充了本机进程内存、传输量和取消后的额度释放；视频首帧另有受控限速样本，仍不能推导物理局域网或弱网性能。

## 4. 发布前尚需执行的门禁

| 门禁 | 必须记录的操作与证据 | 当前状态 |
| --- | --- | --- |
| 新 CI 四平台作业 | 同一最终 commit SHA 的四平台 artifact；不得用工作区本机结果替代远端结果 | 已接线，尚未触发远端 CI |
| 最终桌面包 | Windows x64、macOS ARM/Intel 的实际安装包，记录 SHA、版本、架构、签名/公证和启动结果 | 尚未完成整套安装包矩阵 |
| 三种安装来源 | 全新安装；已有一体版原位升级；只有旧客户端时并装统一版；额外验证两者原本同机安装 | 自动身份/路径、macOS portable 并行运行及真实旧版 updater 替换已过；签名安装器与其他平台路径待执行 |
| 更新与数据隔离 | stable/beta 各按原 feed 更新，重启后有效 AppData 不变，独立单实例/端口/进程并存，原库 SQLite 完整性可复核 | macOS 隔离 feed 的真实下载/替换和原库保留已过；默认缓存、自动重启与生产通道仍待执行 |
| 完整 GUI 主路径 | 最终桌面壳首启、空库设置/日志、开启浏览、配对、联合查询、详情、播放、离线、恢复、关闭窗口；确认远端不启动播放器 | macOS ARM portable 主路径已实际操作；其他平台及签名安装包仍待执行 |
| 物理设备和 NAS | Windows ↔ macOS ARM/Intel；至少一组桌面 ↔ Docker/NAS；各方向单独授权、撤销、断网和重启 | 三进程 HTTP 通过不等于跨物理设备通过 |
| 媒体和映射 | 真正安装的 VLC/IINA 等零映射流播放，seek、暂停续播、长流；Windows/macOS 映射与打开目录；来源离线与失效映射 | 原生音频、Finder 映射、IINA 播放/暂停/seek，以及浏览器视频呈现/限速/断流/idle 已过；其他平台、外部播放器视频和物理弱网矩阵待执行 |
| 性能验收 | 10k/100k 与两大库联合，冷/热状态、网络条件、准备/首屏/翻页 p50/p95、请求数/字节、内存高水位、取消后释放、媒体首帧 | 本机 SQLite、双宿主 HTTP/RSS/取消基线及限速下视频首帧样本已有；物理网络与跨平台统计分布待补 |
| 发布与迁移实际演练 | 从旧客户端导出、统一版刷新恢复草稿、重配对/重绑映射、重复导入、冲突保留/替换；原程序和源文件可继续使用 | 真实 ClientStartup 浏览器链路与 macOS portable 原生导出/导入已过，签名安装版本之间仍待执行 |
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

### 全量备份恢复与克隆

外部覆盖 AppData 不会被自动识别，可能同时恢复旧分享开关、旧节点和旧版管理授权。先把备份准备到本机，在覆盖与首次重启前隔离网络；通过本机配置页的入口进入“设备与分享 → 克隆或恢复安装”：

- 同一设备恢复旧库：选择“恢复旧库后更换代际”。保留 NodeId、访问其他设备的授权和路径映射，轮换 LibraryEpoch，撤销旧入站授权并清除待处理入站请求与邀请。
- 复制到另一设备：选择“创建新的设备身份”。同时更换 NodeId/LibraryEpoch，清空节点连接、授权和映射。

两种操作都保留本机业务库，并在同一次状态写入中关闭联合浏览和节点分享；需手动重新启用。旧版管理授权属于另一套协议，仍须复核，再恢复网络和重新分享。仅搬迁同一份当前数据、不回滚或并行复制时，无需重置身份。真实配对后的恢复/重启/再次访问与克隆清空行为已通过 11 项定向测试；没有声称外部整份覆盖本身会自动撤权。

### 回退与发布顺序

停止使用联合能力时，关闭本机浏览和不再需要的分享/授权，原本机库继续可用；旧客户端独立保留。新 Federation 状态是加法文件，业务数据库未为本功能改 schema，但回退前仍应保留备份。

先完成本节门禁并发布经过验证的统一版和旧客户端迁移提示版本，再收敛新用户下载推荐。旧 feed 保持原产物且可读；维护结束日期另行公布，不在这一轮实现中预设。
