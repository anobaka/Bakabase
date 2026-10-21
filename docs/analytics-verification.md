# 产品分析 v2 验收

配套契约：[analytics-design.md](analytics-design.md)。验收数据使用 staging 环境或专用测试项目，生产看板固定筛选 `schema_version=2`、`environment=production`。不修改真实媒体数据来制造成功率，也不把模拟事件叫作真实使用数据。

## 自动化检查

按仓库 storage rule，用 `src/scripts/with-scratch.sh` 运行构建、SDK 生成、Vitest、Flutter 和 .NET 测试。API 模型变化后运行 `corepack yarn gen-sdk`，不得手改生成文件。

重点测试：

- `ProductTelemetryTests`：client/system 身份隔离、有限字段、时间/UUID、无同意不退化成系统事件、有界队列、离线丢弃、撤销与在途取消、环境过滤。
- `DesktopAnalyticsTests`：跨进程稳定、关闭时不创建身份、损坏/空 UUID 不重建用户、native/loopback/同源边界。
- `ConsoleAnalyticsTests`、`RelayPipelineTests`：跨 Server 保持本机身份、context 不转发、关闭时剥离旧页面的统计 header。
- `ProductEventAttributeTests`：HTTP 200 中业务错误仍为 failed，异常原样传播，遥测故障不改变业务结果，零结果不是失败。
- BTask、Workflow、Enhancer、Subscription、AI、Download 的结果测试：等待/重试/取消与成功分离，逐批次而非逐进度采集，流式提前退出不算成功。
- Web analytics 与实际调用点测试：初始化竞态、重试、第一屏、切换、数据白名单、关闭、native context 不可用，以及播放确认/阅读首次显示。
- Flutter `analytics_test.dart` 与全量测试：稳定独立身份、私密字段过滤、专用 transport、关闭/切换丢弃队列、过期配置。
- 看板脚本测试与 `python3 src/scripts/posthog-dashboards.py` dry-run：目录可解析、查询版本过滤、重复对象保护、写入边界。

## 安装与运行

| 场景 | 期望 |
| --- | --- |
| 一体版本地 UI | deployment_type=desktop_all_in_one，client_surface=desktop_webview |
| 直接运行 Server + 浏览器 | standalone_server / browser |
| Docker + 浏览器 | docker_server / browser |
| 移动 App 连接 Docker | docker_server / mobile_app；client_version 来自 App 包 |
| Server 无浏览器运行 | 有 runtime 事件，无 ui_session_started，不增加活跃客户端 |
| 同一部署两个浏览器 | 一个 deployment_id、两个 client distinct ID |
| 同一桌面/手机切换 Server | client ID 不变，deployment_id 改变 |
| 升级并重启 | deployment ID、客户端 ID 保留；runtime_id 改变 |
| 测试/开发环境 | 不发送生产遥测；测试不能被映射成 production |

检查 `GET /app/analytics-info` 的 schema/deployment/版本与同意状态；普通浏览器读取 `/app/analytics-client-context` 应返回 404。仅原生窗口带有效本机上下文时才得到桌面 ID，关闭后不返回 ID。

观察 runtime_started → runtime_ready → runtime_heartbeat；ready 的阶段是 HTTP 监听就绪，不代表已测量进程早期迁移时间。正常停止尽力发送 runtime_stopped。强杀后只可判断上次未观测干净退出，不可直接推导崩溃率。

## 真实交互

1. 打开 Web UI，再进入资源页：检查固定 `route` 与功能曝光；载荷无 location、referrer、动态 hash/query 或资源标题。移动端检查会话、ui_ready 和业务事件，不期待尚未实现的 page_view/feature_exposed。
2. 手动提交搜索：UI 记录 `resource_query_presented`，后端记录 `resource_search_completed`；两者含义不同，不能相加为搜索次数。Web 的完成边界是响应或失败回调，不是完成渲染；首页自动加载与分页不属于手动搜索，“新标签页中查看”首轮当前归为 restored。
3. 打开资源、点击播放：仅确认播放才有 playback_started；外部播放器只可有 handoff。图片首帧加载失败不能有 reader_opened。
4. 保存配置、属性、合集：成功/业务错误各验证一次，一次 API 请求一个结果事件；数量字段缺失不能假定编辑了一个资源。
5. 启动后台任务：分别验证完成、失败、取消、等待和 retry attempt。Workflow 等待不发 run completed，增强器部分失败保留批次摘要。空闲系统的固定 recurring 维护循环不发送 attempt（所有结果均排除），手动运行仍发送。
6. AI 缓存命中不增加 provider 请求数；流式取消/提前关闭不是成功，未知 token 不填 0。不得上传 prompt、output、endpoint 或 key。
7. 下载预检查已有文件不算真实下载完成；状态重复通知不重复发终态事件。

## 关闭与边界

关闭 UI 主开关后检查：PostHog 待发事件清空，后续请求停止附带 `X-Bakabase-Analytics-*`；服务端队列清除，可取消的发送终止。重新打开后身份保持稳定、旧事件不补发。已送达的网络数据不能撤回。

桌面管理远端：关闭本机采集后，relay 在转发前剥离统计 header；长驻窗口恢复焦点时重读本机配置。Native context 超时或不可用时暂停采集，不能偷偷换成 browser ID。移动端本机开关立即生效，远端 Server 开关经刷新/配置过期生效。

生产 UI 中 Clarity 默认不加载。GA4 和 Sentry 验证各自关闭门控；Sentry 后端仍是详细诊断通道，不把其异常原文与 PostHog 字段白名单混为一谈。

断网、429、5xx 和队列满应不影响业务响应，不写磁盘事件箱，不无限重试。所有重试保留原时间与 UUID。Header 只加到 Bakabase API，不能进入 AI provider、CDN、媒体外链或配对密钥。

## PostHog 看板

1. 确认项目与 ingestion token 匹配，查看 v2 事件的实际载荷和 event properties。
2. 在测试环境检验摄取，再用相同查询验证。空结果可以证明查询可执行，不能证明埋点或业务有流量。
3. 六个看板覆盖运行、版本部署、活跃留存、功能采用、核心旅程和任务质量，定义以仓库 JSON 为准。
4. 系统心跳不得出现在客户端 UV；版本与资源快照先按部署取最新观测，不能把升级前后、每天快照重复累计。
5. 采用率检查分子是分母的子集；匿名客户端不等于自然人；不完整 cohort、未知/未观测数据不填假 0。
6. 确认旧看板未被覆盖，重复运行目录部署不产生重复看板，管理 key 不出现在代码、前端或日志。

## 本地 Docker

按 `.claude/rules/local-docker-deployment.md`：先构建，保留旧镜像，确认来源版本与现有部署一致，再停服备份有效 AppData（排除 `.bakabase.lock`）、复用既有 Compose/env/mounts 重建。核对版本、部署 ID、配对身份、挂载和新前端交互。

若现有服务来自另一个仍在工作的未提交分支，尤其包含额外数据库迁移，先做独立验收并确认替换顺序；不能用较旧分支覆盖真实服务或复制正在写入的数据库文件来冒充一致备份。
