# 多设备媒体库：扩展验收记录（2026-09-22）

开发分支为 `codex/multi-device-library`。本轮先验证播放器兼容修复 `094eed06df759af5fccaaf080788bc83f48f2a5d`，随后发现并修复查询准备调度问题，当前产品为 `ed61e4cc76a9210db9598a823202ccd5037fb06b`（`2.4.0-beta.400`）。验收脚本另记执行提交，各候选结果不混用。继承已合入的主线 `86d76392` 和基础设施迁移修复 `dc6692a`。未合并、发布、改动生产更新源或下载入口。

原生安装、账号、授权对话框、桌面操作和系统诊断均在一次性 GitHub-hosted runner 执行。本机只编译、运行纯测试和受控 HTTP 夹具、核验下载的小证据；未操作用户的应用、账号和媒体库。每个结果核对工作流 SHA、artifact 大小和 SHA256，再阅读实际报告；失败记录保留，不与其他提交的通过结果相加。

**验收结论：工程回归、新包安装和受控大库测试通过，完整发布验收尚未全部通过。** Windows 已发布旧包升级通过；macOS 原旧客户端无法从正常系统入口启动，历史升级受阻。新版 Windows/ARM 的原生空库流程通过，Intel 原生界面和三平台完整配对/详情/离线恢复流程尚未关闭。具体播放器输出模式及其限制见下文，生产签名、公证、物理设备网络与生产更新通道也不能由本轮受控测试替代。

## 当前候选的复验

[完整 CI 35701814056](https://github.com/anobaka/Bakabase/actions/runs/35701814056) 的 7 个实际作业全部通过，源码均为 `ed61e4cc`。独立核验结果：前端 98 文件/936 测试及 lint/生产构建通过；后端 13 项目共 2,621 项，2,586 通过、0 失败、35 项既有第三方手动联网测试显式跳过。主程序集 184 个实际类/1,642 项的 selection、summary、TRX 和日志逐一一致，其他模块每个只计一次。

Windows、Linux、macOS Intel/ARM 各 311 项专项测试通过，均无跳过；各平台 3 个独立宿主遍历 771 条资源，发布角色审计通过，两种 Mac 的实际 CGRect ABI 探针通过。浏览器旧端迁移/联合查询为 114 条资源、2 个来源、0 页面错误；120 秒真实视频在受控 256 KiB/s 下首帧约 1,794 ms，实际暂停/90 秒帧/查询刷新、慢流、断流后 Range 恢复、30 秒 idle、流中撤销和无历史写入均通过。6 份小证据共 1,842,721 bytes，索引为 `/tmp/bakabase-ci-ed61e4cc-evidence/summary.json`。这个首帧值是单次受控样本，不能推导物理网络分布。

[安装包 CI 35701815533](https://github.com/anobaka/Bakabase/actions/runs/35701815533) 已核验 `ed61e4cc` 的两产品 × 三平台全部通过。实际包版本为 `0.0.1-acceptance.35701815533.1`，六组便携包及安装版的运行核心均为 `2.4.0-beta.400`；包内容、角色、原始安装器、启动、macOS ABI 和进程/目录清理均通过。六份小证据共 315,159 bytes，SHA、大小和来源一致，索引为 `/tmp/bakabase-package-ci-ed61e4cc-evidence/summary.json`。后续原生 GUI 与历史升级均复用这批包。

## 播放器兼容修复候选的完整回归与安装包

[完整 CI 35697804273](https://github.com/anobaka/Bakabase/actions/runs/35697804273) 的 7 个实际作业全部通过：

- 前端 98 个文件、936 项测试，以及 lint 和生产构建。
- 后端 13 个项目：2,583 通过、0 失败、35 项既有第三方手动联网测试显式跳过，共 2,618 项。
- Windows x64、Linux x64、macOS Intel/ARM 各 308 项专项测试，四平台各 771 条资源的三独立宿主链路。两种 macOS 的真实 CGRect ABI 探针通过。
- 真实旧客户端/统一版浏览器流程覆盖 114 条资源、2 个来源，无页面错误；视频实际呈现、暂停/seek、查询刷新、慢流、断流、撤销及清理通过。

6 份小证据共 1,837,443 bytes，独立计数与哈希核验索引为 `/tmp/bakabase-ci-094eed06-evidence/summary.json`。

[安装包 CI 35697798838](https://github.com/anobaka/Bakabase/actions/runs/35697798838) 的两产品 × 三原生平台全部通过。包版本为 `0.0.1-acceptance.35697798838.1`，运行代码版本为 `2.4.0-beta.387`；实际便携包、原始安装器、安装后启动、内容/身份/路径、macOS ABI 和清理分别核验。6 份小证据共 317,145 bytes，索引为 `/tmp/bakabase-package-ci-094eed06-evidence/summary.json`。这些包未用于生产签名、公证和系统信任验收。

## 验收发现的 mpv 兼容性修复

原生 Windows mpv 测试实际捕获 `Could not resolve proxy name`：新 mpv 的 curl 网络后端将原有 `http-proxy=direct://` 当成代理地址。`094eed06` 只对已验证的本机媒体票据选择 `lavf://` 输入，继续经 FFmpeg 直连；同时在同一个单文件选项作用域中加 `access-references=no`，禁止播放列表与嵌套媒体引用。原有本机路径、VLC 和 IINA 的参数不变。

选择输入后端会改变 mpv 的来源分类，因此引用限制与直连修复一并交付。7 项定向参数测试以及上述 `094eed06` 完整 CI 均通过。真实验收使用固定第一方开发构建 `mpv-player/mpv` 的 `c6c4c38d7` / build `35659722192`，校验三个二进制包的固定 ID、字节数与 SHA256；不能推导所有 mpv/VLC/IINA 版本均通过。

输入实现及引用限制参照 [mpv curl 源码](https://github.com/mpv-player/mpv/blob/c6c4c38d7/stream/stream_curl.c)、[FFmpeg 输入源码](https://github.com/mpv-player/mpv/blob/c6c4c38d7/stream/stream_lavf.c)和 [mpv 选项文档](https://mpv.io/manual/stable/#options-access-references)。这不是对任意媒体格式的安全沙箱承诺。

### 实际播放器及引用隔离

[播放器 CI 35702258250](https://github.com/anobaka/Bakabase/actions/runs/35702258250) 的执行提交为 `5c9829c9`，产品代码与 `ed61e4cc` 一致。Windows 与 macOS ARM 的全部场景通过：生产 API 选择并启动固定 mpv，播放器 PID 的父进程为接收节点，来源节点不启动播放器；96×64、120 秒 AVI 实际时钟前进，暂停稳定、恢复及 90.2 秒跳转后解码画面由红变蓝。mpv 的实际 VO 为 `gpu-next`。

撤销浏览后对未缓存的 55 秒位置发起 seek，Windows/ARM 分别实际收到 14/7 次 403，播放器进入 EOF/idle，最终解码图仍与 90 秒蓝帧哈希相同，没有解码 55 秒红帧。两节点数据库各保留 3 条资源、`PlayedAt` 均为空、完整性正常。代理正控实际命中 1 次，生产播放为 0 次；两种引用场景分别用 M3U 和强制 lavf HLS，允许引用的正控均实际请求自有回环 canary 1 次，生产 `access-references=no` 后均为 0，且有真实 start/end-error 与进程退出证据。HLS 日志明确记录引用限制；M3U 两侧均继承单文件选项，HLS 两侧均强制格式、关闭 curl，避免把未能识别媒体误判为引用隔离成功。

该轮 Intel 已识别 AVI，但日志在 `gpu-next → macvk → Creating vulkan instance` 后结束，IPC 返回 BrokenPipe，尚无首帧。原证据没有退出码或崩溃信号，因此只记录观察到的初始化末点；生产播放、撤权和引用场景均未通过。进程与文件已清理，但请求播放器退出时也记录了一项 BrokenPipe，不能写成清理零错误。三份小证据共 86,419 bytes，索引与独立分析在 `/tmp/bakabase-player-5c9829c9-evidence/{summary,independent-verification}.json`。

后续 [CI 35703965526](https://github.com/anobaka/Bakabase/actions/runs/35703965526)，执行提交 `8b31b33c`，仅将 Intel 的私有图形配置固定为该构建确实支持的 Cocoa/OpenGL `vo=libmpv`，其他平台保留 `gpu-next`。Intel 的真实 VO、时钟、CGL 像素格式、OpenGL 4.1 与 Apple Software Renderer 均已出现，但观察器误把真实 logger `[cocoacb/cocoacb]` 当成缺少 `[cocoacb]` 初始化记录而中止；未执行后续帧、暂停、seek 和引用隔离，不能补判通过。日志保留了 4 条 `render_context_render` 未调用或卡住的诊断。`76730dbc` 只修正同名 logger 前缀匹配，用真实日志作回归夹具，继续拒绝无 CGL/版本/renderer 或任意 logger 的情况；34 项纯测通过，没有放宽播放断言或时限。该轮三份小证据共 89,948 bytes，独立核验位于 `/tmp/bakabase-player-8b31b33c-evidence/independent-verification.json`。

上游固定 release 资产后来返回 404。夹具仅在该确切状态下使用同一次第一方 build 的原始 Actions archive，并核对仓库 ID、构建 SHA、artifact ID、大小和原始固定 SHA256；没有更换版本或接受不匹配字节。Windows/Intel/ARM archive 分别为 `10667534204` / `10667436118` / `10667356116`。所有原生运行均使用自有临时配置和有限缓存，不加载用户脚本；使用真实 VO 和解码图但不声称验证了物理显示器呈现，静音 `ao=null` 不证明音频输出，受控 loopback 不代表物理弱网。

### 最终受控播放器结果与 Intel 限制

[最终播放器 CI 35705112537](https://github.com/anobaka/Bakabase/actions/runs/35705112537) 执行提交为 `76730dbcee6a8025e2f36294ae6f8df5241c8838`，产品代码仍为 `ed61e4cc`。三个平台均完整执行并通过解码时钟、暂停/恢复、seek、撤权、代理旁路、M3U/HLS 引用隔离、无播放历史及清理断言，三份小证据共 103,341 bytes，全部大小、SHA256 和来源一致。独立结果为 `/tmp/bakabase-player-76730dbc-evidence/independent-verification.json`。

| 平台 | 实际 VO | seek 位置（s） | 撤权后真实 403 次数 | 两种引用分别：正控 / 生产请求数 | 纯测试实际执行 / 跳过 |
| --- | --- | ---: | ---: | --- | --- |
| Windows x64 | gpu-next | 90.2 | 17 | 各 1 / 0 | 33 / 1 |
| macOS ARM | gpu-next | 90.0 | 7 | 各 1 / 0 | 34 / 0 |
| macOS Intel | libmpv / Cocoa OpenGL | 90.2 | 17 | 各 1 / 0 | 34 / 0 |

Intel 实际图形记录为 CGL 像素格式创建成功、OpenGL `4.1 APPLE-21.1.1`、`Apple Software Renderer`，暂停时钟 1.2→1.2 秒。撤权后未缓存的 55 秒 seek 最终进入 EOF，解码图仍与 90 秒蓝帧一致；已人工查看该轮红色首帧、蓝色 seek 帧与相同蓝色撤权帧。生产播放器 PID 52533、两个引用场景 PID 53297/53340 均由读端 51874 启动，来源 51878 不启动播放器。三个平台的两份数据库均为 3 条资源、0 条播放时间写入、完整性正常；`observerErrors=[]`、`cleanupErrors=[]`，自有进程和文件清理完成。

**Intel 的默认 macvk 及持续窗口呈现仍未通过验收。** 当前成功仅覆盖指定 Cocoa/OpenGL 的初始化、解码帧和控制/权限行为；它回退到软件渲染，原生日志保留硬件 CGL 创建失败 10002、软件/间接上下文等 3 条 `[w]` 警告，以及 3 条 `[v]` 级别的 `mpv_render_context_render() not being called or stuck.` 诊断。解码图与播放时钟不能证明窗口持续上屏或实际显示器刷新，不能用这轮断言成功消除这些诊断或推导默认用户配置可用。Windows/ARM 没有该 render-context 诊断，但本夹具也未测物理显示器或音频输出。

## 已发布历史安装包升级

旧版本为已发布的 `v2.4.0-beta.349`，release ID `392491206`、源码 `86d76392b1f715fa08d952a81d96053ec363e294`。原始 Setup/pkg 使用固定发布资产及哈希；不重新构建旧版或修改其 plist。各轮目标复用对应的已验证候选二进制：旧轮为 `094eed06` / core387，当前复验为 `ed61e4cc` / core400；仅用合成版本 `2.4.1-historical.<run>.1` 在自有回环更新源上触发升级。

[CI 35698783236](https://github.com/anobaka/Bakabase/actions/runs/35698783236) 的 Windows 两产品均通过：原程序创建资源，原 updater 从默认缓存更新并自动启动 `2.4.0-beta.387`；旧/新进程与原生 updater 日志相互印证，资源 13 个原字段、SQLite 完整性、两产品配置及客户端连接文件保留。另一产品在更新期间持续 API 可用且身份不变，统一版/客户端更新分别采样 79/62 次；这是连续 API 检查，界面是前后检查点。清理通过。

该轮两种 macOS 原始 pkg 安装均成功，但没有观察到旧客户端进程或 API，因此停在首次启动，尚未执行历史升级。旧包实际缺少 `CFBundleExecutable`；尚不能仅凭这个字段认定启动失败根因。首次安装后的显式用户打开与 updater 自动重启是不同操作，后续验收分别记录，绝不以手动重启替代 updater 自动重启。原始三个平台小证据共 1,261,754 bytes，索引及独立核验为 `/tmp/bakabase-historical-9f00f1f8-evidence/{summary,independent-verification}.json`。

旧版 manifest 只有 `win`/`osx`，旧 macOS plist 也可能没有可执行文件字段。验收仅对固定历史资产接受这些实际格式，并同时核验资产架构与安装后全部产品文件；新候选包仍严格要求完整 RID 和正确 plist。Velopack 1.2 的 [postinstall 模板](https://github.com/velopack/velopack/blob/f2edcbcafb81da5b3c884aaea330e225ad91d8b6/src/vpk/Velopack.Packaging.Unix/OsxBuildTools.cs#L101-L112)在调用 `open` 后固定返回成功，所以 installer 退出码不能单独证明首次启动成功；模板语义也不等于已确定历史资产的具体启动失败原因。

后续 [CI 35700017871](https://github.com/anobaka/Bakabase/actions/runs/35700017871) 在两个 macOS 上对原始安装内容再做完整哈希审计、确认没有旧进程，再通过 LaunchServices 明确执行一次首次打开。两者均退出 1，原生日志均为 `The application cannot be opened because its executable is missing.`。这证明原始旧客户端在这两个环境不能通过正常系统入口启动，历史原生 updater 流程因此未进入；没有补写旧 plist 或直接执行内部二进制后冒称正常打开成功。缺字段与该现象相符，但没有用改变历史包的实验宣称单因果证明。两份小证据共 732,053 bytes，索引在 `/tmp/bakabase-historical-df357810-evidence/summary.json`，清理均通过。该轮 Windows 卡在新加纯夹具未提供 `USER` 的错误，未运行安装；`bbce1bb9` 已把该环境显式隔离，23 项本机纯测通过，不能将此轮 Windows 记为升级通过；Windows 的真实升级证据仍属于前一轮 `35698783236`。

### 当前候选的历史升级复验

[历史升级 CI 35703240523](https://github.com/anobaka/Bakabase/actions/runs/35703240523) 的执行提交为 `fc134623`，候选已固定为 `ed61e4cc` / packages `35701815533` / core `2.4.0-beta.400`。Windows 两产品均从上述真实已发布旧包完成原生更新和自动重启：统一版旧 PID 10124 → updater 6152 → 新 PID 3088；客户端 9976 → 5228 → 8484。原始/目标产品文件分别为 608/613、519/520，内容来源与哈希核验一致。原资源 13 列逐值保留，SQLite 完整性及资源数正常，配置与旧客户端连接文件不变。更新另一产品期间分别连续采样 82/62 次，最大采样间隔 0.390/0.375 秒，API 与进程身份持续有效；两个默认缓存均完整交付一次，随后由原 updater 自动重启。

两种 macOS 再次止于原旧客户端首次安装/正常打开，日志均为 `The application cannot be opened because its executable is missing.`。原 pkg 的固定身份和字节一致，installer 本身成功，但没有产品 PID、feed 请求、完整包交付或更新器动作，也没有进入系统更新授权。不能将这一项判为目标候选升级失败后恢复，更不能判为历史升级通过；目标候选的独立全新安装成功另有前述证据。所有平台自有进程/目录/临时账号清理完成，`cleanupErrors=[]`。三份小证据共 1,261,404 bytes，索引与两份独立核验在 `/tmp/bakabase-historical-fc134623-evidence/{summary.json,independent-verification.json,independent-macos-boundary.json}`。

## 性能与原生界面

以下性能使用 Debug 配置构建的两个 TestHost，复用生产库读取和联合查询实现，配合受控元数据样本与 loopback HTTP。“首屏”计时包含完整 HTTP 响应读取和 JSON 解析，不包含 WebView 渲染、真实媒体扫描或文件分析。

[性能 CI 35698658657](https://github.com/anobaka/Bakabase/actions/runs/35698658657) 的执行提交为 `30bbcb1e`，保持全部产品预算。Windows、Linux、macOS ARM 完成两种规模（每节点 10k/100k）、各 3 次新进程与 3 次热查询；macOS Intel 的 10k 全部完成，100k 在第二次新进程查询发生 `QueryDeadlineExceeded`，没有替代样本或自动重试，整体性能门禁未通过。

| 平台 | 100k/节点的新进程首屏 p50/p95（ms） | 热首屏 p50/p95（ms） | 热翻页 p50/p95（ms） | 关闭浏览/取消（ms） |
| --- | ---: | ---: | ---: | ---: |
| Windows x64 | 3399.67 / 3655.36 | 1506.86 / 1632.94 | 6.07 / 26.35 | 54.85 |
| Linux x64 | 3959.80 / 4126.30 | 1482.46 / 1530.38 | 5.68 / 9.02 | 20.86 |
| macOS ARM | 3355.44 / 4029.54 | 2377.86 / 2447.54 | 11.60 / 40.86 | 79.90 |
| macOS Intel | 未完成，不能计算完整样本统计 | 未完成 | 未完成 | 未执行 |

每个首屏分组仅 n=3，nearest-rank p95 是三次样本的最大值，不能当稳定尾延迟；100k 翻页每组 n=2997。新进程不表示操作系统页缓存已冷却。Intel 100k 第一轮新进程/热首屏分别为 5928.919/2074.794 ms，两次均完整遍历 200,000 条/1,000 页；第二轮远端节点准备超时。代理未记录上游或 worker-admission 错误，不能把这次失败归咎于代理，也不放宽原有 8 秒准备期限。较早 `35697473142` 的 Intel 曾通过，但不与本次不完整样本混算。

全轮实际完成 44 次完整遍历、7 次取消与双额度复用。所有已完成遍历的条数、页数、两节点 RSS、原始 HTTP 请求与字节、分位数均独立复算；四平台进程与临时数据清理通过。100k 三完整平台的单节点采样 RSS 最大值约为 Windows 764.2 MiB、Linux 858.0 MiB、ARM 1102.5 MiB；包括共享页，既不是托管存活堆，也不能把不同时间的两个最大值当同时峰值。完整两规模表和传输量见 `/tmp/bakabase-performance-30bbcb1e-evidence/measurement-summary.md`，原始索引及统计在同目录的 `summary.json`、`measurement-summary.json`。

性能夹具固定节点标签为 `benchmark-a`/`benchmark-b`，保留真实节点 ID、代际、鉴权和产品查询预算。单节点 100k 条固定样本的估算快照为 58,200,302 bytes。初轮 macOS 使用长 CI 机器名时超过原有 64 MiB 快照预算而明确失败；短标签基准不会修复或隐藏这个产品适用范围：100k 不是任意路径、属性和机器名长度下的容量保证。统计同时记录真实机器名及其对应估算，不提高生产预算或减少数据量。

### 查询准备调度修复

进一步审查发现 `Select(Prepare).ToArray()` 在创建任务数组时，会直接执行每个异步方法的同步前缀。本地 SQLite 读取与投影若没有真正挂起，远端准备在本地工作完成后才开始，但共用的 8 秒期限已经计时。新增回归用两个明确的同步事件证明该因果关系：本地前缀等待远端进入才能释放；旧实现直到 5 秒防死锁看门狗结束才让远端进入，因此断言失败，而非依靠比较机器速度猜测。

`ed61e4cc` 先捕获取消令牌，再独立调度每个节点的 `Prepare`。原有最多 16 个节点、4 个准备许可、工作空间/会话额度、8 秒期限和真实完成时间戳检查保持不变；已排队任务可取消，迟到快照仍释放。另验证已取消请求不解析节点并返还额度、准备许可占用期间取消排队节点、后续许可与工作空间可复用。原时间戳测试去除了节点串行完成顺序的假设。

修改后 Federation 模块 52/52、Service Federation 49/49 通过，编译无错误。证据在 `/tmp/bakabase-coordinator-dispatch-{module-final,service}.log` 和 `/tmp/bakabase-coordinator-dispatch-service/summary.json`。确定性测试证明并修复调度缺陷，但不单独证明它是前述 Intel 实测超时的唯一原因；新候选完整回归、安装包和下述四平台性能已分别使用新的 CI 结果验收。

### 修复后的四平台实测

[性能 CI 35701816514](https://github.com/anobaka/Bakabase/actions/runs/35701816514) 对应产品 `ed61e4cc`，四平台全部通过。8 个规模分组、48 次完整遍历和 8 次取消/双额度复用均完成；各次条数、页数、两节点 RSS、HTTP 请求与字节及分位数均独立复算。没有替换失败样本或重试失败请求，四平台清理无错误。

| 平台 | 每节点条数 | 新进程首屏 p50/p95（ms） | 热首屏 p50/p95（ms） | 热翻页 p50/p95（ms） | 关闭浏览/取消（ms） |
| --- | ---: | ---: | ---: | ---: | ---: |
| Windows x64 | 10,000 | 760.985 / 764.145 | 123.526 / 157.151 | 13.311 / 35.908 | 48.918 |
| Windows x64 | 100,000 | 2559.773 / 3040.742 | 1189.513 / 1311.967 | 7.109 / 29.567 | 150.867 |
| Linux x64 | 10,000 | 551.628 / 576.307 | 105.976 / 106.337 | 8.510 / 14.996 | 16.704 |
| Linux x64 | 100,000 | 1578.672 / 1659.769 | 722.344 / 739.033 | 4.385 / 6.884 | 18.020 |
| macOS Intel | 10,000 | 2152.265 / 2424.007 | 188.780 / 229.468 | 24.526 / 52.927 | 90.322 |
| macOS Intel | 100,000 | 3691.397 / 4004.021 | 1411.936 / 1722.671 | 12.172 / 22.009 | 37.628 |
| macOS ARM | 10,000 | 877.206 / 968.892 | 117.933 / 123.897 | 25.181 / 43.774 | 28.031 |
| macOS ARM | 100,000 | 2035.796 / 2190.927 | 1427.490 / 2014.975 | 12.115 / 41.505 | 76.253 |

Intel 100k 的三次新进程首屏为 4004.021、3237.111、3691.397 ms，本轮均未触及 8 秒准备期限。首屏每组仍仅 n=3，p95 是最大观测值；后续页 n=297/2997，新进程不等于冷 OS 缓存。100k 单节点采样 RSS 最大值约为 Windows 736.9 MiB、Linux 880.9 MiB、Intel 904.2 MiB、ARM 1038.5 MiB，含共享页且节点峰值不一定同时发生。受控标签下的快照仍为 58,200,302 B；用两种 Mac 的真实长机器名估算分别为 68,200,302/68,400,302 B，仍超过 64 MiB，这是保留的容量边界。此轮通过不表示任意元数据长度或物理 NAS 网络均满足同一规模。

独立核验索引、完整原始统计及 HTTP/RSS 表分别位于 `/tmp/bakabase-performance-ed61e4cc-evidence/{summary.json,measurement-summary.json,measurement-summary.md}`。

### 原生界面结果

[原生 GUI CI 35703235174](https://github.com/anobaka/Bakabase/actions/runs/35703235174) 执行提交为 `fc134623`，使用已验证的 `ed61e4cc` / core `2.4.0-beta.400` 安装包。Windows 和 macOS ARM 完整空库流程通过，各 10 个检查点、8 次实际原生动作：首启提示、默认关闭的联合浏览、设备设置中开启浏览、返回后选择本设备并搜索、0 条资源/1 个设备及只读提示、再进设置确认状态保留。只读 API 交叉核验浏览已启用、分享仍关闭、无对端；API 不负责改变界面状态。

Windows 的 WebView 控件可能以重复树路径暴露同一 RuntimeId，验收仅合并身份及语义均一致的控件，并在动作前后再次核验身份。范围控件实际支持 UIA TogglePattern，因此用观察到的 `toggle` 操作；普通 Search 仍用 InvokePattern。macOS 对应范围为 AXCheckBox，普通 Search 为 AXButton。没有按重复文字任意选择、坐标点击或键盘回退。

Intel 的原始完整树读取在既定单次 24 秒、初始状态等待 90 秒预算内未完成，未执行界面动作；整条 GUI 流程上限为 600 秒。此平台原生流程未通过，不能以 API 可用判定界面通过。补充的直接 AX 诊断在两个 Mac 的四个自有产品进程均看到 `trusted=true`，但读取应用 role 时返回 `DirectAXApplicationUnavailable`，未进入窗口计数，故不能归因为权限未授予，也不能断言这个替代观察接口可用。当前诊断未区分原始 AX 错误与桥接类型/role 不匹配，保留为待定位的测试观察问题；没有修改 TCC、请求新授权或扩大树读取期限。

本轮所有动作均依赖完整可见树与已验证的 PID/路径/启动时间，清理全部通过。三份小证据共 394,745 bytes，索引为 `/tmp/bakabase-native-gui-fc134623-evidence/summary.json`。`emptyLibraryFlowPassed` 与 `mainFlowPassed` 分开：本轮未覆盖完整配对、联合查询、远端详情、离线恢复等跨平台原生主流程，三个报告的 `mainFlowPassed` 均为 false；较早 ARM 手工原生流程及浏览器证据保持各自归属。

## 发布边界

本记录不关闭生产签名/公证和系统信任、生产 stable/beta feed、物理 Windows/macOS/NAS 网络及弱网矩阵，也不授权改变旧客户端更新源或停止维护。较早 `d77021ed` 的三平台双产品并存与同代码重打包更新证据继续保留在[发布准备记录](multi-device-library-release-readiness.md)，其产品 SHA 不冒充本轮 `ed61e4cc`。
