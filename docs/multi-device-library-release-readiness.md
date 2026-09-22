# 多设备媒体库：发布准备与迁移验收

记录日期：2026-09-22。对应开发分支 `codex/multi-device-library`；初始实现基线为 `f1fa1469`，后续验收在 `b80ddfbe`、`cf787d13` 的基础上继续，包含其后的修改。继续验收时已合入最新 `origin/main`（`86d76392`）并推送开发分支，用于远端测试。没有发布、切换更新 feed 或收敛官方下载入口。

当前设计以[多设备联合媒体库执行计划](multi-device-library-execution-plan.md)为准，功能、预算和已有测试记录见[实施记录](multi-device-library-implementation.md)。[旧拆分设计](pc-client-design.html)与[旧拆分执行计划](pc-client-execution-plan.html)保留历史内容，并增加了当前状态入口。

后续真实播放器、四平台大库统计、原生 AX/UIA 界面和已发布历史包的结果，集中在[2026-09-22 扩展验收记录](multi-device-library-acceptance-20260922.md)。该记录区分产品源码与验收脚本提交，并保留未通过项；以下较早安装与更新证据继续归属于各自明确的产品 SHA。

Windows 数据迁移修复另在基础设施分支 `codex/multi-device-relocation-lock`（`dc6692a9522736969d7d574c980ca98e447570ee`），父仓库已固定引用且远端 CI 可拉取。后续合并顺序为先将基础设施修复合入其主线，再合入本仓库引用，保证子模块提交长期可达。没有改动用户原工作区的基础设施 checkout。

代码和自动化已提供可执行的发布检查。较早 `d77021ed` 修复 Intel 原生 WebView 崩溃，并通过六包安装、三平台双产品并存、原生更新/自动重启与数据保留（含 Mac 实际系统授权）。本轮另修复 mpv 网络后端兼容和查询准备调度，当前产品 `ed61e4cc` 的六种安装包、四平台大库统计，以及 Windows 真实已发布旧包升级至 core400 均通过。原生 GUI、播放器及完整回归的精确来源和结果见扩展验收记录。macOS 原始 v349 客户端经正常系统入口无法启动，历史升级因此未进入；Intel 完整原生界面、生产签名/公证、物理设备网络和生产通道继续保留独立门禁。

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

### 本次远端执行证据

该轮代码提交 `ca55d4733d7606807755868f5b04520d4ccdcf98` 的 [CI 35607879124](https://github.com/anobaka/Bakabase/actions/runs/35607879124) **7 个作业全部成功**：前端、全后端、浏览器以及 Windows x64、Linux x64、macOS Intel/ARM 四平台。各 artifact 的源码 SHA 已核对一致。该运行通过同一提交的临时 `codex/multi-device-library-ci-ca55d473` 分支触发，避免中断上一轮全后端；验证完成后临时分支已删除，代码保留在 `codex/multi-device-library`，没有触发部署。这些结果仅对应该提交，不能替代下述 `d77021ed` 产品修改后的复验。

全后端作业用时 25 分 59 秒。13 个项目共 2,617 项：通过 2,582、失败 0、跳过 35。主程序集实际发现并执行 184 类、1,641 项，selection、summary、184 份 TRX 的计数/结果以及日志逐类一致；其余 12 个模块每个只计一次真实 MTP 汇总。35 项跳过全部来自 ThirdParty 已显式 `[Ignore]` 的手动联网测试，其余项目没有跳过。artifact `10643378594` 为 718,387 bytes，下载 SHA256 与 GitHub digest 一致；完整索引在 `/tmp/bakabase-ci-ca55d473-backend/summary.json`。

最终四个平台的 artifact 均核对为该 SHA，每个平台仍为兼容性 136、Service Federation 48、Protocol 49、Player 74 项全部通过，零失败/跳过；Intel 的迟到快照释放回归实际通过。四个平台分别完成三节点 / 771 条 smoke，Linux 的 Service 角色以及 Windows、Intel、ARM 的三种发布角色检查均通过。四份小型平台 artifact 共 848,051 bytes，统计、TRX 和角色证据索引在 `/tmp/bakabase-ci-ca55d473-evidence/summary.json`。

最终前端为 98 文件 / 936 测试，lint 与生产构建通过。浏览器迁移为 114 条资源 / 2 个来源、0 页面错误，旧连接不变、导入草稿恢复与新授权、恢复/克隆和跨窗口关闭浏览全部通过。120 秒视频在 256 KiB/s 下约 1,411 ms 呈现首帧；刷新查询保留预览，90 秒实际呈现帧、暂停、9 秒持续流、断流后的 Range 恢复、30 秒 idle、流中关闭浏览和不写播放历史均通过。所有测试进程退出，9 条转发连接释放。结果是单次受控 loopback 样本，不是物理弱网性能分布。浏览器 artifact `10642874403` 为 268,540 bytes，不含原视频或业务数据库，核验记录在 `/tmp/bakabase-ci-ca55d473-browser/summary.json`；脱敏异常类型仍保留，未将断言通过表述为后台没有任何异常。

修复过程另保留 [CI 35604229653](https://github.com/anobaka/Bakabase/actions/runs/35604229653)（`1cfb8752`）的证据：全后端、前端、浏览器和三个平台通过，Intel 的 Protocol 为 48 / 49，其后步骤未执行。失败测试用固定 150 ms 等待推断后台清理完成，调度较慢时会提前断言；`ca55d473` 改为创建阻塞与释放完成信号，仍要求超时节点被省略、迟到快照恰好释放一次、后续分页不加入该节点，已在最终四平台实际通过。更早两轮被新运行取消的后端作业不计为全量通过。

### 三个平台、两个产品的真实安装

[安装验收 CI 35618999687](https://github.com/anobaka/Bakabase/actions/runs/35618999687) 在提交 `0e5281e6acd706a9000884e96d78535208714323` 上构建真实生产前端和六种 self-contained 包，前端及六个安装作业全部成功。通过已有 CI 的 `workflow_dispatch suite=packages` 调用新的 `_package_acceptance.yml`；该入口仅构建/测试，只有 `contents: read`，不调用部署或发布 Release，不读取生产签名密钥。常规完整 CI 没有在此选择下重复执行，其前次通过证据仍是上节 `ca55d473`。本表属于 `0e5281e6` 的包，不能记为 `d77021ed` 修复包已通过。

| 原生 runner / RID | 统一版 | 旧客户端 | 实际操作 |
| --- | --- | --- | --- |
| Windows / win-x64 | 通过 | 通过 | portable 启动；原 Setup.exe 静默安装到独立目录；已安装 EXE 启动；原 Update.exe 卸载 |
| macOS Intel / osx-x64 | 通过 | 通过 | portable 启动；原 `.pkg` 安装到 `/Applications`；原 postinstall 经 LaunchServices 自动启动 |
| macOS ARM / osx-arm64 | 通过 | 通过 | 同 Intel；使用原生 ARM runner |

每种组合分别检查 portable/full/installer 产物及哈希、真实主程序与安装内容、产品角色、实际本机进程、有效 AppData 和 UI 响应。macOS 安装版本使用默认 `~/Library/Application Support/Bakabase` 或 `Bakabase.Client`，没有 startup hook 或 AppData 覆盖；Windows 使用已记录的独立测试 AppData。统一版通过正式 API 创建资源，portable 与安装版各保留 1 条，退出后两份 SQLite 均通过完整性检查。六份报告均确认自有安装/数据已清理，没有 cleanup errors；Windows 自卸载延迟删除也已实际等待并确认完成。

小证据 artifact 与安装包分开保留，六份小证据共 309,325 bytes，源码 SHA、角色/RID 和下载 ZIP SHA256 对 GitHub digest 逐项核对；汇总在 `/tmp/bakabase-package-ci-0e5281e6-evidence/summary.json`。大安装包只保留为 Actions artifact，未下载安装到用户 Mac，也未发布给用户。前一轮 [35618018456](https://github.com/anobaka/Bakabase/actions/runs/35618018456) 暴露 Windows Python ZIP 路径规范化的夹具问题及 Velopack 1.2.0 不接受两个同时出现的关闭开关；修复后重跑完整六组合，未跳过失败断言。包装守卫套件通过：macOS 13 项；Windows 12 项，另 1 项 Unix 权限/符号链接检查按平台跳过。

上述结果记录了该批未签名包的全新安装/启动，不代表签名、公证、Gatekeeper/SmartScreen、两个已安装产品同机升级、历史官方包迁移或 updater 自动重启通过。后续 Intel 崩溃要求对修复后的真实包重新验收，不能用首次 HTTP 就绪推断桌面进程持续存活。macOS postinstall 的首次自动启动与 updater 重启是不同路径；包/feed 身份保持不变，生产渠道和下载入口未改动。

### Intel WebView 原生崩溃与 ABI 修复（2026-09-22）

[已安装生命周期 CI 35678757667](https://github.com/anobaka/Bakabase/actions/runs/35678757667) 的执行源码为 `0214b725`，使用的原安装包仍来自 `0e5281e6`。Intel 在 `initial-unified-install` 阶段报客户端连接拒绝；两个产品此前均完成自己的进程、默认 AppData 和 HTTP/UI 初始检查。系统诊断随后确认两者已发生原生崩溃，而非仅观察脚本没有找到 PID：

| 产品 / PID | `.ips` 中的启动时间 / 崩溃捕获时间（UTC） | 原生终止证据 |
| --- | --- | --- |
| 旧客户端 / 14827 | 02:19:18.0562 / 02:19:25.7869 | `EXC_BAD_INSTRUCTION` / `SIGILL`；`Invalid view geometry: width is NaN` |
| 统一版 / 15244 | 02:19:35.0526 / 02:19:54.3037 | 相同异常；主线程 `_NSViewValidateGeometry → NSView initWithFrame: → WKWebView initWithFrame:configuration:` |

客户端实际崩溃早于统一版启动，不能将报告中的粗粒度阶段解释为后者杀死前者。单实例 ID 分别为 `Bakabase.Client` 与 `Bakabase`，Mutex/Pipe 名称据此分开；本次终止原因是原生几何断言，不是单实例冲突或内存回收。两份 crash 及限定进程系统日志已随小证据归档，下载核验目录为 `/tmp/bakabase-installed-0214b725-evidence/osx-x64/installed-lifecycle-results/`：

- `report.json`：失败阶段、实际启动 PID、默认 AppData 与清理结果。
- `diagnostics/1-Bakabase.Client-2026-09-22-022011.000.ips`、`diagnostics/0-Bakabase-2026-09-22-022011.ips`：上述原生异常与主线程栈。
- `diagnostics/owned-process-system.log` 及双方 `application-logs/`：本轮进程日志；清理无错误。`.ips` 文件名中的时间不替代文件内部的崩溃捕获时间。

根因在共享 Shell 的 [NativeWebViewHost.MacOS.cs](../src/apps/Bakabase.Shell/Controls/NativeWebViewHost.MacOS.cs)：`initWithFrame:configuration:` 和 `setFrame:` 的 P/Invoke 原先把一个 `CGRect` 写成四个独立 `double`。Darwin x64 把 32 字节结构体按值放在栈上，四个独立浮点参数却进入 `xmm0–3`；因此即使创建调用传四个零，AppKit 也会从错误位置读到几何数据。ARM64 的四成员浮点聚合和四个独立参数恰好都使用 `d0–3`，解释了此前 ARM 成功不能发现这个 Intel 缺陷。

修复提交 `d77021ed0e75325a2466361a1c3694632d0b973a` 增加顺序布局的四字段 `CGRect`，将上述两条生产 P/Invoke 及调用都改为按值传递该结构体。新增 [Bakabase.Shell.NativeAbiProbe](../src/tests/Bakabase.Shell.NativeAbiProbe/Program.cs) 直接反射调用生产声明，由原生 Objective-C Foundation 夹具接收矩形并用 `NSValue getValue:size:` 回写核验，覆盖零值、不同负数/分数、矩形之后的指针参数，以及 void frame setter。它不创建窗口、不加载 WebKit、不启动产品或安装器。

本机 ARM64 / SDK 9.0.100 / runtime 9.0.0 的 Release 构建成功，三组矩形的两条原生调用全部通过；报告 `/tmp/bakabase-native-abi-arm64-20260922.json`，构建日志 `/tmp/bakabase-native-abi-build.log`。`_package_acceptance.yml` 与 `ci.yml` 均已接入 macOS Intel/ARM 原生 runner 的该探针，报告分别归档为 `package-native-abi.json` 和 `federation-results/native-abi.json`。随后 [安装验收 CI 35679854071](https://github.com/anobaka/Bakabase/actions/runs/35679854071) 在同一产品提交 `d77021ed` 重建六种包，前端及六个原生安装作业全部通过。版本为 `0.0.1-acceptance.35679854071.1`，core 为 `2.4.0-beta.366`。四个 macOS 安装作业均先通过真实 ABI 探针，Intel 的创建和调整矩形调用已获得实际 x64 证据；六组合 portable 启动、原始安装器、已安装启动及各自约定的数据路径和清理通过（macOS 默认路径；Windows 独立安装目录和 AppData）。统一版每个平台的 portable/installed 数据库各保留 1 条 API 创建资源且完整性为 `ok`。

六份小证据合计 311,995 bytes，GitHub digest、字节数、源码 SHA、角色、架构、所有清理结果逐项核对。汇总及逐包文件哈希位于 `/tmp/bakabase-package-ci-d77021ed-evidence/summary.json`；大包未下载到用户机器。该结果确认修复后的全新安装，完整 CI 及已安装双产品并存/授权自动更新仍按各自报告单独判定，不能从全新安装推定通过。

### 修复包的已安装双产品并存与移除

[并存 CI 35680717291](https://github.com/anobaka/Bakabase/actions/runs/35680717291) 在测试提交 `55daa0dcf2bf18c1e3d1522e3e7cfbaa7a8ffbeb` 上复用经过来源、ZIP digest 和包文件哈希验证的 `d77021ed` 六种安装包；Windows x64、macOS Intel/ARM 三个原生作业全部通过。产品源码与包来源一致，测试与产品提交分别记录。

每个平台先安装旧客户端，再安装统一版，均使用原始安装器和默认安装/AppData 位置。检查两个真实进程及 API/UI 同时可用、分别重启且另一产品 PID 不变；移除统一版后客户端仍能启动，再恢复统一版并保留库；移除客户端后统一版仍能启动。四个双产品检查点都通过，客户端两份真实配置哈希不变，统一版的 1 条 API 创建资源保留且 SQLite 完整性为 `ok`。Windows 两个方向均使用原生 `Update.exe uninstall`；macOS 只移除测试创建的 bundle/receipt。进程、默认目录和临时文件清理均无错误，没有强杀 updater。

三份小证据共 285,917 bytes，下载后的 GitHub digest、大小、产品源码及测试源码 SHA 均已核验；完整索引为 `/tmp/bakabase-installed-55daa0dc-evidence/summary.json`。报告明确 `automaticUpdatesRequested: false`：此处关闭修复包的并存、独立重启和双向移除门槛，不将结果冒充更新器或历史代码升级通过。默认缓存与真实更新/系统授权流程另行执行。

### Windows 默认安装位置的原生自动更新

[更新 CI 35681121264](https://github.com/anobaka/Bakabase/actions/runs/35681121264) 的 Windows x64 作业通过，执行提交 `870b616d`，原安装包来自 `d77021ed` / run `35679854071`。安装版本 `0.0.1-acceptance.35679854071.1` 经本地受控 feed 更新为 `0.0.2-updater.35681121264.1`。新版本复用相同产品二进制并增加验证标记，因此证明安装更新流程，不代表历史代码迁移或生产 stable/beta 通道验收。

统一版 PID `3920 → 6976`，原生 `Update.exe` PID `1524`；旧客户端 PID `5164 → 8680`，原生 updater PID `9184`。两者都通过真实产品接口检查/下载，达到等待重启状态，默认 `packages` 缓存中的文件大小、SHA 与完整 HTTP 交付一致。原生日志的 `Apply`、`Restart: true`、旧 PID、确切缓存包路径及新版本成功应用，与独立进程观测匹配；最终新进程 API/UI 和有效 AppData 均正确，没有脚本手动启动替代自动重启。观察器会等待该 updater 退出，避免把 Windows 的短暂 `--veloapp-updated` hook 误当最终应用。

更新每个产品时，另一产品分别连续观测 70 / 56 次，PID 和有效 AppData 不变，API 采样无错误；API/UI 在更新后的检查点通过。客户端配置哈希、统一版资源及 SQLite 保留检查通过。后续双向卸载、恢复和自有文件清理通过，未强杀 updater。三平台小证据合计 715,471 bytes，digest/size/产品及测试 SHA 已核验，索引为 `/tmp/bakabase-installed-870b616d-evidence/summary.json`。该 run 的两个 macOS 作业在授权能力预检失败，不能把 Windows 成功写成整个更新矩阵通过；macOS 诊断另记。

### macOS 授权预检的诊断与清理约束

[诊断 CI 35681836766](https://github.com/anobaka/Bakabase/actions/runs/35681836766) 在执行提交 `4a762650` 仅为无凭据预检增加有界 stderr。两种 macOS 架构都准确返回 `UIElementsEnabled is not a valid class for application System Events (-2700)`；系统脚本字典对应的属性是 `uiElementsEnabled`。这次失败是测试脚本的名称大小写错误，不能据此判定 runner 没有 GUI 权限。执行路径停在 `Context` 和 `create_account` 之前，产品也未安装/启动。

同轮 Windows 两产品再次通过默认缓存与原生自动重启，三平台均无遗留 updater 或文件清理错误。三份小证据合计 713,734 bytes，digest/size/来源已核验，索引为 `/tmp/bakabase-installed-4a762650-evidence/summary.json`。`fe41442c` 修正属性名，同时让账号准备失败的异常保留清理上下文：若首次清理失败，生命周期 runner 仍须记录并重查账号和授权写入进程，不能因准备函数未返回而误报清理成功。87 项相关纯测试通过，其中实际调用 runner 的故障测试确认残留授权进程会阻止默认目录和工作目录删除。后续 ARM 真实运行已通过 GUI 能力预检和临时账号创建；授权与更新的最终结论仍须完整链路证据。

随后 [CI 35682239910](https://github.com/anobaka/Bakabase/actions/runs/35682239910) / `fe41442c` 确认 Intel/ARM 均通过 GUI 预检、临时账号创建、原始安装和独立重启；统一版通过默认缓存下载校验并启动真实 `UpdateMac`。两者在确认阶段都观察到同一 updater 的两个同标题/正文窗口，因此报告 `Ambiguous updater elevation confirmation`，未点击确认或输入凭据。账号及家目录、自有测试文件均已移除，最终无残留进程；但 updater 需要强制停止，报告保留清理失败，不能写成正常更新或清理通过。两份实际生成的小证据共 610,475 bytes，digest/size/SHA 已核验，索引为 `/tmp/bakabase-installed-fe41442c-evidence/summary.json`。

同轮 Windows 在新增纯测试清空环境后无法解析 `Path.home()`，尚未取包或执行产品。`be68f192` 将该夹具的主目录明确指向临时目录，87 项本机纯测试通过；真实 Windows updater 的已通过证据仍属于此前两轮。`26242cab` 将 updater 确认限定为精确进程的 `AXFocusedWindow`，读取并在点击前重读焦点窗口内容；原始窗口数量只作诊断，不按相同文字推断原生窗口身份，SecurityAgent 仍保留独立唯一窗口检查。新增同名其他窗口、无焦点、内容变化、正常进程消失等回归，92 项纯测试通过；原生链路另按真实运行验收。

[CI 35683003654](https://github.com/anobaka/Bakabase/actions/runs/35683003654) / `26242cab` 已完成：Windows 两产品再次通过原生更新、自动重启、数据保留、卸载及清理，主目录夹具修复也通过原生 Windows 验证。macOS 两边均记录 `originalWindowCount: 2`、`focusedWindowAvailable: true`，实际只使用 1 个焦点窗口；随后确认动作未成功返回，Intel 为命令失败/超时，ARM 为退出码 1。尚未提交凭据，不能仅凭 `confirmationSubmitted: false` 推断动作从未发出。账号、家目录和自有文件已删除，最终无已识别的残留进程；updater 仍因强制停止记为失败。三份小证据共 963,556 bytes，digest/size/SHA 已核验，索引为 `/tmp/bakabase-installed-26242cab-evidence/summary.json`。纯测试在 Mac 各执行通过 92 项，Windows 通过 89 项，另 3 项既有 Unix 符号链接夹具按平台跳过。

[诊断 CI 35683824133](https://github.com/anobaka/Bakabase/actions/runs/35683824133) / `f89b577e` 的 Windows 原生更新再次通过；三份小证据共 957,676 bytes，digest/size/来源已核验，索引为 `/tmp/bakabase-installed-f89b577e-evidence/summary.json`。固定阶段诊断确认 Intel 在确认操作的 `read-dialog-tree` 超时，未进入原生授权脚本；账号和文件已清理，updater 强停仍记失败。ARM 则由原生日志证实用户确认成功并启动原始提权 `osascript`，随后系统实际提示 `Enter your password to allow this.`，测试错误地要求包含 `administrator` 而拒绝，尚未提交凭据。该等待中的原始 `osascript` 未退出，清理门禁正确阻止账号、家目录和测试文件删除；这轮不能记为清理通过。两种 Mac 都未完成统一版更新，也未进入旧客户端更新。Mac 各 96 项纯测通过；Windows 93 项通过、3 项既有平台跳过。

`5405560d` 针对上述真实失败支持系统的两种已知授权提示，仍要求确切 `osascript` 请求、唯一普通/安全输入框和唯一可用确认按钮。确认操作保留一次完整重读与内容比对，再检查该次读取的焦点窗口引用后使用其控件，移除重复的第二次整树读取；5 秒单次与原有 120 秒总期限不变。失败清理仅可取消从未提交凭据、已核对 PID/启动时间/实际程序/原始命令、原 updater 已停止且未观察到特权写入的自有 `osascript`，单次 `SIGTERM` 后最多等 5 秒；无法确认或已有凭据提交/特权写入时仍阻止账号和路径删除。取消只属于失败后的清理，不计为更新通过。101 项本机纯测试以及 JXA 编译通过，原生结果另记。

[CI 35684811164](https://github.com/anobaka/Bakabase/actions/runs/35684811164) / `5405560d` 的两种 Mac 均完成确认和一次凭据提交操作，之后在等待原始 updater 退出时达到 120 秒期限；Intel 已越过此前的整树读取超时。两者原生日志都停在启动确切 `osascript` 的位置：Intel `15818 → 15930`，ARM `17901 → 17946`。UI 命令返回 `submitted: true` 不能证明系统已接受凭据；尚未发生可验证的 apply/自动重启，也未进入旧客户端更新。原 updater 强停后，授权脚本仍存，已提交凭据的清理门禁阻止取消和账号/家目录/文件删除。Windows 本轮在安装前的观察器首个样本 15 秒超时，stdout 和样本均为 0，尚未取包；现有日志不能区分 PowerShell 启动与第一次 CIM 查询耗时。三份小证据共 627,806 bytes，digest/size/执行 SHA 逐项核验；汇总位于 `/tmp/bakabase-installed-5405560d-evidence/summary.json`。Mac 各 101 项纯测通过，Windows 98 项通过、3 项既有平台跳过。

`e34513b6` 在安装前通过独立 Python 子进程，对刚创建的本地随机账号调用一次 `ODRecordVerifyPassword`；密码仅通过 stdin/内存，精确核验账号、UID、家目录和管理员组，原有 45 秒准备期限内最多使用 5 秒。它验证密码有效性，不替代系统授权或 updater 成功证明。提交一次凭据后另做一次有界、同 PID/启动身份的只读系统窗口记录，不读取或保存输入字段值，不重试提交。Windows 首样本另记录固定 stderr 阶段、helper PID/退出码/启动时间，只查询三个必要 CIM 属性；15/8/3 秒期限不变，诊断输出不能算成功样本。127 项本机纯测试通过；本机仅解析 CF/OD 函数和常量，未调用账号认证或系统授权。原生执行为 [CI 35685780134](https://github.com/anobaka/Bakabase/actions/runs/35685780134)，结果另记。

该轮 Windows 首样本实际为 8.141 秒，两产品再次完成原始 updater 自动更新/重启、数据保留、双向卸载和清理；统一版 `7520 → 7460`、updater `3228`，旧客户端 `5500 → 3584`、updater `4936`。纯测 Windows 124 项通过、3 项平台跳过，Mac 各 127 项通过。两种 Mac 都在安装前的账号验证返回 `VerificationUnavailable`，无原生错误码；无法将该笼统结果解释为密码无效。本轮尚未运行原生更新授权或提交后快照。首次准备清理通过账号枚举/家目录缺失检查，生命周期 finally 的再次清理却又执行删除并收到 `sudo exit 255`；不能据此断言缓存原因或账号仍然存在，最终失败状态和两份默认 seed 数据目录保留属实。三份小证据共 719,644 bytes，digest/size/执行与产品 SHA 已核验，汇总 `/tmp/bakabase-installed-e34513b6-evidence/summary.json`。

`2f72cb21` 为验证器失败增加固定阶段和白名单异常类型；验证密码不要求家目录已物化，但目录服务中的 home 属性仍须准确，已有目录仍须属于该 UID 且不是符号链接。重复清理保留“此前已验证删除”的标记，重新检查进程、账号枚举和家目录缺失，不再执行删除；发现同名/同 UID 账号或路径重现则失败，后续重试也不能删除重建账号。132 项本机纯测试通过，原生复验为 [CI 35686328385](https://github.com/anobaka/Bakabase/actions/runs/35686328385)。

该轮 Windows 两产品原生更新、自动重启、数据保留、双向卸载和清理全部通过，129 项纯测通过、3 项既有平台跳过。两种 Mac 各 132 项纯测通过，临时账号的实际 OD 密码验证成功；确认和凭据操作完成，三秒后的同一系统进程快照仍为原提示，没有明确拒绝文字，也不包含字段值。随后首次失败均为 `ps returned exit 1`，现有错误不能区分全表查询与逐个 root shell 查询；原生日志未记录 apply 或自动重启成功，残留授权脚本及账号/文件继续受清理门禁保护。Intel 原 updater/osascript 为 `11051 → 11360`，ARM 为 `6663 → 6759`；两者尚未进入旧客户端更新。三份小证据共 968,584 bytes，完整性及来源已核验，汇总 `/tmp/bakabase-installed-2f72cb21-evidence/summary.json`。

`81b4e016` 修复静态审查发现的进程观察竞态：逐个读取命令后重新核验 PID/启动时间，只在原身份确已消失时略过；仍活着的进程读取失败、超时或无法复核继续阻断，已记录的 writer 不删除。错误增加固定阶段和 PID，不输出原始命令。授权操作要求两个字段的 `AXValue` 可写，用户名赋值后仅在内存读回比较，失败时不写密码或点击按钮；密码不读回、不重试。142 项纯测试通过，真实结果为 [CI 35687208452](https://github.com/anobaka/Bakabase/actions/runs/35687208452)。

该轮 Windows 再次通过两产品完整原生更新、自动重启、数据/卸载/清理，139 项纯测通过、3 项既有跳过。两种 Mac 各 142 项纯测通过；实际 OD 密码验证、控件可写与用户名回读均通过，但系统提示仍在，最终等待原 updater 超时；本轮没有 `ps` 读取失败。Intel 原 updater/osascript 为 `11008 → 11297`，ARM 为 `17852 → 17918`。没有 apply 或自动重启证据，旧客户端更新未执行，残留授权脚本使账号/家目录和文件保留。三份小证据共 983,303 bytes，完整性及来源已核验，汇总 `/tmp/bakabase-installed-81b4e016-evidence/summary.json`。

随后静态调查发现测试账号本身配置为 `/usr/bin/false`。Apple PAM 源码的 [shell 检查](https://github.com/apple-oss-distributions/pam_modules/blob/a8705983365bdb9b5c6c1ff5fd55c321dc01deec/common/Common.c#L463) 明确拒绝该值，[account 阶段](https://github.com/apple-oss-distributions/pam_modules/blob/a8705983365bdb9b5c6c1ff5fd55c321dc01deec/modules/pam_opendirectory/pam_opendirectory.m#L165) 默认执行这项检查；单独的密码验证不能证明完整授权有效。修复只给新建的临时账号使用 `/bin/zsh`，并在创建后与独立验证器中核验，保留现有单次 AX 输入；不修改系统 PAM、TCC、应用权限或原始 updater。已有日志尚未直接证实本轮经过该拒绝路径，因此实际修复效果仍按下一次原生运行判定。

修复提交 `42787f221c029882d09b5515a3ea19f7d0f9186a` 通过 156 项本机纯测试，并在 [CI 35688589630](https://github.com/anobaka/Bakabase/actions/runs/35688589630) 复用同一批 `d77021ed` 产品包。同时补充仅在授权失败后运行的系统诊断：读取本轮已核验的单个 SecurityAgent PID，从实际观察时间起、最多 180 秒的认证相关日志；总期限 5 秒、输入上限 256 KiB、最多 80 条。解析后再次检查 PID、系统程序路径和时间，先脱敏再截断，原始日志、可编辑字段值和密码均不落盘；诊断失败不能覆盖原更新失败，也不能作为认证成功证据。

该轮 Windows 与 macOS ARM 两产品完整验收通过。ARM 的统一版 `16652 → 17034`、updater `16714`、原提权脚本 `17003`；旧客户端 `16402 → 17535`、updater `17194`、原提权脚本 `17501`。两次实际系统授权均完成，原生日志确认通过 `osascript` 应用 bundle、合成新版成功和 `open -n` 自动重启；另一产品连续 API 采样分别为 41、32 次，无中断或身份变化。默认缓存哈希、更新后清单/标记、原库 1 条资源及客户端配置保留、双向移除与恢复、账号/家目录/文件清理均通过，没有强制终止 updater。ARM 单份小证据 290,833 bytes，独立核验在 `/tmp/bakabase-installed-42787f22-arm-early/independent-native-update-verification.json`。这证明 shell 修复后真实系统授权可以完成，但不是此前失败经过某条 PAM 分支的直接日志证据。

Intel 同样完成统一版系统授权、原生应用新版和自动重启（`9565 → 10209`、updater `9860`）；旧客户端停在确认前的 `validate-snapshot / SnapshotChanged`：同一焦点窗口在两次读取之间从 11 个节点变成 12 个，尚未点击确认或输入凭据。现有差异字段不足以确定新增节点内容，不能将其当作密码或系统授权失败。最终账号、家目录和自有文件已删除、授权进程无残留；但客户端 updater `10815` 需要强制停止，因此 Intel 的完整更新与清理仍记失败。

本轮三份小证据共 973,477 bytes，digest、size、执行 SHA 及产品包源均核验通过，汇总与逐项实际原生日志核验位于 `/tmp/bakabase-installed-42787f22-evidence/{summary,independent-verification}.json`。Windows 统一版 `2848 → 5672`、updater `5228`，旧客户端 `2484 → 8400`、updater `3060`；153 项纯测通过、3 项既有平台跳过。两种 Mac 各 156 项纯测通过，真实验收仍保留上述 ARM 成功、Intel 部分失败的分别结论。

`2a9e254ed478cecb3dce5891b0d6f7e005cc60c0` 修复确认前的观察竞态：只接受结构化且经过白名单验证的 `validate-snapshot / SnapshotChanged`，该固定检查发生在解析按钮和点击之前。前两次变化回到循环起点重新核验进程身份、其他脚本、系统窗口、焦点和完整快照，第三次变化仍失败；保存变化次数，原总期限不变。按钮动作错误、超时、其他阶段或仅含相似文字的异常均立即失败，凭据提交也不重试。163 项纯测试通过；真实三平台复验为 [CI 35689398805](https://github.com/anobaka/Bakabase/actions/runs/35689398805)，仍复用未变的 `d77021ed` 产品包。

### 三平台原生更新最终通过

上述 `2a9e254e` 复验三个原生作业全部成功，六次真实更新均完成默认缓存下载校验、原始 updater 应用新版、旧进程退出和自动启动的新进程 API/UI 验证。合成更新版本为 `0.0.2-updater.35689398805.1`，更新后的清单、验收标记和全部产品文件哈希均核验；没有手动重启替代 updater。

| 平台 | 产品 | 旧 PID → 自动重启 PID | 原 updater PID | 确认前快照变化次数 |
| --- | --- | --- | --- | --- |
| Windows x64 | 统一版 | `5916 → 7092` | `2760` | 不适用 |
| Windows x64 | 旧客户端 | `9808 → 1540` | `3312` | 不适用 |
| macOS Intel | 统一版 | `6468 → 7048` | `6722` | 1 |
| macOS Intel | 旧客户端 | `6265 → 7929` | `7545` | 0 |
| macOS ARM | 统一版 | `20383 → 20835` | `20540` | 1 |
| macOS ARM | 旧客户端 | `20143 → 21238` | `21143` | 1 |

四次 Mac 更新均通过实际系统授权，原生日志明确记录通过 `osascript` 应用 bundle、新版应用成功与 `open -n` 自动重启；三次实际遇到快照变化，重新完整观察后完成。Windows 原日志同样记录确切旧 PID、包路径、`Apply`、`Restart: true`、新版成功和真实启动。原生日志中的等待旧 PID 警告保留，结论同时依赖独立进程观测与最终 API/UI，而非把警告删除或假定等待成功。

另一产品连续 API 采样均通过且 PID 不变（Windows 69/53 次、Intel 90/61 次、ARM 52/35 次；UI 仅前后检查点验证）。三平台原库 1 条资源保留、SQLite 完整性为 `ok`、客户端配置哈希不变；双向移除/恢复和最终文件清理全部通过。两种 Mac 的临时账号、家目录与自有授权进程全部清理；三个平台均无强制终止或遗留 updater、无 cleanup errors。

本轮 Mac 各 163 项纯测通过，Windows 160 项通过、3 项既有平台跳过。三份小证据合计 945,668 bytes，digest、大小、执行 SHA 与 `d77021ed` 产品包源逐项核验，汇总与原生日志独立核验在 `/tmp/bakabase-installed-2a9e254e-evidence/{summary,independent-verification}.json`。本结果关闭未签名、同代码重打包的三平台系统安装更新验收；模拟用户授权不等于无人值守更新，也不替代历史签名包、生产通道、系统信任和物理多设备检查。

### 修复提交的完整 CI

[完整 CI 35679857625](https://github.com/anobaka/Bakabase/actions/runs/35679857625) 对产品提交 `d77021ed0e75325a2466361a1c3694632d0b973a` 的 7 个实作业全部通过：

- 前端 98 个测试文件、936 项测试通过，lint 与生产构建通过。
- 后端 13 个项目合计 2,617 项，实际通过 2,582、失败 0；35 项均为原有 ThirdParty 手动联网测试的显式忽略，逐项名称与原因已核对。主程序集 184 类/1,641 项与 TRX、选择记录、执行日志一致。
- Windows x64、Linux x64、macOS Intel/ARM 各 307 项专项测试全部通过；各平台完成 771 条资源、3 个独立宿主的互通及适用的发布内容检查。两种 macOS 架构均完成 3 组真实生产 P/Invoke ABI 验证。
- 浏览器完成 114 条资源/2 个来源、迁移与联合浏览，页面错误为 0；实际 120 秒视频、90.509 秒 seek、暂停后刷新查询、限速、断流恢复、idle 超时、关闭浏览取消及撤权均通过，媒体测试进程和 relay 清理完成。

6 份小证据合计 1,838,633 bytes，GitHub digest、大小、源码 SHA 均逐项验证；索引为 `/tmp/bakabase-ci-d77021ed-evidence/summary.json`。该轮使用修复后的产品源码，后续仅测试、工作流及文档变更另记执行提交，不把历史 `ca55d473` 结果当成当前产品证据。

## 3. 本轮本机证据与适用范围

执行机器为 macOS ARM64，.NET SDK 9.0.100 / runtime 9.0.0。以下证据均是实际执行结果；本机临时路径用于这次审计，远端 CI 的独立 artifact 见上一节，临时日志不是长期发布记录。

| 检查 | 结果 | 本机证据 |
| --- | --- | --- |
| 产品身份/源依赖 | 通过 | `/tmp/bakabase-release-readiness/*-contract.log` |
| 发布 guard / plist | 14 / 14 通过 | `test_release_contract.py` 实际执行 |
| AppData、旧更新源与迁移导出 | 131 / 131 通过，0 skipped | `/tmp/bakabase-release-readiness/compatibility/summary.json` |
| 实际 Service、统一桌面、旧客户端 publish | 三角色通过；前两者带真实 web | `/tmp/bakabase-release-readiness/{server,unified,client}-package.json` |
| 三真实宿主 HTTP | 771 行遍历及上述故障/开关场景通过 | `/tmp/bakabase-release-readiness/smoke/result.json`、`smoke.log` |
| Player 模块及本轮策略 | 模块 74 / 74、策略/参数 14 / 14、旧播放处理器 20 / 20 通过 | `/tmp/bakabase-player-policy-results.log`；本轮对应 TRX |
| 原生导出修复时的前端 | 98 文件、932 测试通过；生产构建、定向 lint/格式检查通过 | `/tmp/bakabase-native-migration-{all-tests,web-build}.log` |
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
| 最新主线集成 | 合入 `86d76392` 后 Federation/枚举绑定/重启 57 / 57；三宿主 771 条通过 | `/tmp/bakabase-main-integration-20260921/summary.json`、`/tmp/bakabase-main-smoke-20260921/result.json` |
| 配对重试与在途撤销修复 | 模块 49 / 49、Service Federation 48 / 48；三宿主验证等待审批后补邀请码可完成配对 | `/tmp/bakabase-federation-review-{module,service}-tests.log`、`/tmp/bakabase-pending-code-revocation-smoke/result.json` |
| 查询与预览生命周期修复 | 全量 936 / 936、构建及定向 lint/格式通过；类型检查仍为既有 310 条，无新增；真实视频查询刷新保留元素、票据和暂停位置 | `/tmp/bakabase-federation-library-review-all-tests.log`、`/tmp/bakabase-federation-library-review-tsc-comparison.json`、`/tmp/bakabase-ci-fixes-final-media-20260921/result.json` |
| Windows 迁移文件锁修复 | 远端捕获 SQLite pooling 阻止 staging 数据库移动；基础设施固定到单一修复 `dc6692a`，本机迁移回归 14 / 14；后续 Windows CI 兼容性 136 / 136 | `/tmp/bakabase-windows-relocation-fix-20260921/summary.json`、`/tmp/bakabase-ci-6fbe5b8f-evidence/` |
| 最新 macOS ARM 候选包及升级 | 代码 `6fbe5b8f`、基础设施 `dc6692a`，self-contained portable `0.0.2-federation.5` 实际内容审计通过；旧源码经真实 UpdateMac 更新到该包，2 条资源/127 条迁移记录保留，两份 SQLite 完整性通过，进程及工作数据清理完成 | `/tmp/bakabase-candidate-6fbe5b8f/report.json` |
| 真实旧客户端迁移浏览器链路 | 旧连接保持可用且文件未变；白名单导出/草稿恢复/幂等/新授权/114 条联合资源/跨窗口关闭及两种身份恢复；0 pageErrors | `/tmp/bakabase-browser-migration-recovery-final/result.json` |
| 双大库 HTTP | 2×10k、2×100k 冷/热完整遍历、传输量与 RSS、执行中取消和额度复用通过；只含 loopback 网络 | `/tmp/bakabase-federation-http-benchmark-final-20260921/result.json` |
| Linux ARM64 实际执行 | Federation 47、Player 72、兼容 131 通过；实际 Service 包角色审计通过。由 macOS SDK 跨平台构建，在 Ubuntu 24.04 实际运行 | `/tmp/bakabase-linux-cross-results/summary.json` |
| Linux 未完成项 | ARM 容器原生编译及三宿主启动遇 SIGILL；后续无业务依赖最小程序捕获 .NET 9 PAL 的 `rdvl` 已知缺陷。x64 SDK 镜像下载超时，未算通过 | 同上；`/tmp/bakabase-sigill-d4e97c86/` |

Linux runner 使用独立容器、源码副本、资源和总时限，已清理本轮创建的容器和数据；未关闭用户容器。早期 Windows 路径测试改为当前平台绝对路径，播放器发现测试隔离真实 `%ProgramFiles%` 内容，本机 27 项回归通过；后续实际 Windows CI 又捕获混合分隔符的夹具比较问题，`1cfb8752` 将夹具规范化为原生绝对路径，本机完整 Player 74 / 74 通过。

后续 [Linux ARM64 专项诊断](linux-arm64-runtime-diagnosis.md)在不包含 Bakabase 代码的同一 DLL 上得到 .NET 9.0.20 三次 SIGILL、官方 .NET 10.0.12 三次通过；捕获的指令及 SME/no-SVE 环境与官方 CoreCLR PAL 修复吻合。没有因此修改产品框架或把 ARM 诊断当成 Linux x64 CI 通过；当前发布目标仍是 Linux x64。

本轮使用 `run-linux-cross-container.py` 在 macOS 本机交叉构建后，以官方 ASP.NET 9 x64 运行时镜像执行，绕开的是不适用的 ARM 执行环境，没有修改产品运行时。镜像 manifest digest、平台、提交、选择的测试套件与 TRX 计数均保留；宿主挂载只读，测试需要写入的位置使用容器内临时副本。两个阶段各自清理自己的容器与源码副本，未修改用户容器。该 x64 仿真结果补足此前本机 Linux 执行证据，但仍不等于 GitHub 原生 Linux runner 结果。

真实升级使用基线 `f1fa1469` 及其锁定子模块构建旧后端和旧前端，再打包为合成版本 `0.0.1-upgrade.1`。旧应用通过 HTTP 创建两条资源，调用现有检查、下载、重启接口；实际 `UpdateManager` 从 loopback feed 下载校验新版，实际 `UpdateMac` 完成替换。新应用 `51601e31` 的 Service DLL SHA 与新版包一致，有效 AppData 路径不变，两条资源响应逐字段相同，升级前后 SQLite `integrity_check=ok`，外部数据文件 SHA 不变。最终运行的本地 feed 共 3 次请求、89,889,144 bytes（含新应用启动后的检查）；旧/新应用及 updater 进程均已退出，临时安装和数据目录已清理。资源检查另断言恰好返回所创建的两个唯一 ID、两份 SQLite 均有 2 行；空结果或缺项不能误报升级成功。

复现脚本为 `src/tests/upgrade-tests/run-velopack-macos.py`。测试专用 startup hook 将 Velopack cache/log 放进临时目录，并为真实 apply 加入 `--norestart --silent`；替换后由脚本以同一隔离环境启动新程序。没有复制新目录冒充 updater，也没有改产品代码。证据覆盖真实旧源码到新版的 portable 更新与数据保留；不覆盖历史官方签名包、默认用户缓存、LaunchServices 自动重启、生产 feed 或系统安装。macOS 管理的 WebKit/SavedState 缓存不在 AppData/Velopack 隔离范围内，未删除用户系统缓存。完整命令和边界见[升级测试说明](../src/tests/upgrade-tests/README.md)。

完成本轮修复后，又从代码提交 `6fbe5b8f86087189a71494f15c6a2b0ec20bd2b1` 构建最新 self-contained ARM 候选包 `0.0.2-federation.5`（core `2.4.0-beta.351`），并重跑同一真实旧版升级链路。应用 DLL 与下载包一致，2 条资源逐字段相同，原库及外部数据保留；2 次 loopback feed 请求共 89,891,891 bytes。所有应用/更新器进程退出并清理中间目录。便携包保留在 `/tmp/bakabase-candidate-6fbe5b8f/packages/Bakabase-federation-test-Portable.zip`，大小 87,847,615 bytes，SHA256 为 `cfa3467fb982e19a12524a9694e907a3652df2e0c5ec0f7d603765b3b193919c`；内容审计、源码/子模块记录和升级报告位于同目录的上级。该包未签名或公证，仍适用上述隔离 locator/显式重启边界，没有重复宣称所有原生 GUI 或安装器验收通过。

后续提交 `1cfb8752`、`ca55d473` 仅调整 Windows 播放器路径和迟到快照释放的测试夹具；产品源文件与该候选包的构建提交相同，包自身版本和 provenance 仍准确标记为 `6fbe5b8f`。

### 真实桌面与正式 Docker 服务互通

后续实际使用 `6fbe5b8f` 的 self-contained macOS ARM portable，与 `5a2ab6da` 发布的 Linux x64 Service 双向连接。两者之间仅测试和文档变化，产品源码一致。Docker 镜像使用仓库原始 `docker/Dockerfile`，镜像 `/app` 全部 296 文件与发布目录逐字节一致，33 个前端文件与生产构建一致，Service 角色检查通过。镜像 index 为 `sha256:4f0c08e2084819726ac835520b5994087ee7b355283076a5153d4d7b2fc77040`，amd64 manifest 为 `sha256:751fa42347bbce1b99eeb8154d8375beafd627d9cb550b4a1e380b7ea719ffb1`；实际 .NET/ASP.NET 为 9.0.19，运行于 OrbStack x64 仿真。

`src/tests/federation-smoke/docker-boundary.py` 通过正式资源 API 在两个独立 AppData 中各创建 17 条资源并物化测试音频，不使用 TestHost 或 SQL 灌入。最终 `run7` 用时 24.16 秒，9 项检查全部通过：默认关闭、各方向分别授权、双向 34 条完整分页及 cursor 重放、详情/音频 Range 字节一致、非 loopback 管理接口及伪造请求头拒绝、未授权 export 拒绝、Docker 停止与重启后身份/授权保持、关闭本机浏览仍可向对端分享、撤权使旧页面和媒体失效。停止两端后，各有 17 条资源、0 条 PlayedAt 写入，SQLite 完整性均为 `ok`，测试媒体哈希不变。数据库以 `mode=rw` 打开既有文件，允许 SQLite 恢复残留 journal/WAL；缺失数据库不会被创建，行数和播放历史仍严格检查。

该拓扑同时验证了 Docker 内部 loopback 管理与宿主发布端口。正式 ASP.NET 基础镜像默认端口是 8080，本次显式设置 `ASPNETCORE_HTTP_PORTS=34567`。另实测发现 OrbStack 的 `host.docker.internal` 将请求来源转换为宿主 `127.0.0.1`；这种本机代理路径不能证明远程接口隔离。因此最终使用宿主真实 LAN 地址，确认来源为非 loopback，并保留全部 403/401 断言。没有将同机 NAT 路径或伪造 Host 的本地请求误称为物理远程安全验证。

完整报告为 `/private/tmp/bakabase-docker-boundary-5a2ab6da/run7/report.json`，源码/镜像审计在同目录上级的 `build/report.json` 和 `provenance.json`；地址转换的独立探测在 `/tmp/bakabase-orbstack-source-eaab90ecd4/report.json`。所有自有进程、容器、网络、AppData 和媒体均已清理，未操作用户容器。新增 runner 的 13 项失败/清理守卫通过。该结果覆盖同机真实原生入口与容器网络边界，仍不等于两台物理设备、真实 NAS 或物理弱网验收。

计划 P00–P10 的行为覆盖、查询基线与全后端回归见实施记录。双大库的新 HTTP 基线补充了本机进程内存、传输量和取消后的额度释放；视频首帧另有受控限速样本，仍不能推导物理局域网或弱网性能。

## 4. 发布门禁状态

| 门禁 | 必须记录的操作与证据 | 当前状态 |
| --- | --- | --- |
| 新 CI 四平台作业 | 同一产品 commit SHA 的四平台 artifact；不得用工作区本机结果替代远端结果 | 当前产品 `ed61e4cc` 的 7 个实作业通过，前端 936、后端 2586 通过/35 既有显式跳过、四平台各 311 专项测试及 771 条三宿主链路通过；6 份小证据的 digest/size/SHA 和实际数量已核验 |
| 最终桌面包 | Windows x64、macOS ARM/Intel 的实际安装包，记录 SHA、版本、架构、签名/公证和启动结果 | 当前 `ed61e4cc` 六种候选包全部通过，运行核心 `2.4.0-beta.400`；两架构原生 ABI、便携包和原始安装器启动均通过；生产签名、公证及系统信任检查待执行 |
| 三种安装来源 | 全新安装；已有一体版原位升级；只有旧客户端时并装统一版；额外验证两者原本同机安装 | 当前候选六组合全新安装通过；`d77021ed` 三平台双产品并存、独立重启、双向移除/恢复及同代码重打包的真实 updater 通过，来源不混用；Windows 原 v349 两产品升级到 `ed61e4cc` 通过，Mac 原包首次正常打开失败 |
| 更新与数据隔离 | stable/beta 各按原 feed 更新，重启后有效 AppData 不变，独立单实例/端口/进程并存，原库 SQLite 完整性可复核 | `d77021ed` 三平台原生自动更新、另一产品连续 API、数据保留与 Mac 系统授权通过；本轮 Windows 原 v349→core400 两产品默认缓存/自动重启/数据保留通过。Mac 原 v349 客户端经 LaunchServices 提示可执行文件缺失，历史升级未进入；生产 stable/beta 通道未关闭 |
| 完整 GUI 主路径 | 最终桌面壳首启、空库设置/日志、开启浏览、配对、联合查询、详情、播放、离线、恢复、关闭窗口；确认远端不启动播放器 | 当前候选 Windows/ARM 的原生空库/开启浏览/范围搜索/状态保留通过；Intel 完整可见树在原预算内读取失败，无动作，未通过。直接 AX 显示已受信任但应用 role 不可读，不能归因缺权限；三平台完整配对/详情/离线恢复主流程未完成，较早 ARM 手工原生证据另记 |
| 物理设备和 NAS | Windows ↔ macOS ARM/Intel；至少一组桌面 ↔ Docker/NAS；各方向单独授权、撤销、断网和重启 | 已补真实 macOS ARM 桌面 ↔ 正式 Linux x64 Docker 的双向验收；同机 OrbStack/NAT 仍不能替代跨物理设备/NAS |
| 媒体和映射 | 真正安装的 VLC/IINA 等零映射流播放，seek、暂停续播、长流；Windows/macOS 映射与打开目录；来源离线与失效映射 | 原生音频、Finder 映射、IINA 与浏览器呈现/流故障证据已记录；当前产品三平台固定 mpv 的解码/暂停/seek/撤权/引用隔离通过。Intel 仅指定 Cocoa 软件输出，仍有 render-context 停滞诊断，默认 macvk 和持续上屏未通过；完整播放器、映射及物理弱网矩阵未关闭 |
| 性能验收 | 10k/100k 与两大库联合，冷/热状态、网络条件、准备/首屏/翻页 p50/p95、请求数/字节、内存高水位、取消后释放、媒体首帧 | `ed61e4cc` 四平台双库各 10k/100k、3 次新进程/热查询，共 48 次遍历和 8 次取消/额度复用通过，HTTP/RSS/分位数已独立复算；首屏 n=3，长元数据仍受 64 MiB/节点限制；物理网络和稳定尾延迟另验 |
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
