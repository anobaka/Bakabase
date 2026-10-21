# Bakabase 产品分析 v2

更新：2026-10-21。此文描述当前代码契约，替代早期将部署 ID 当作用户 ID 的方案。看板定义在 [analytics-dashboards.json](analytics-dashboards.json)，验收步骤在 [analytics-verification.md](analytics-verification.md)。

## 1. 统计单位与身份

| 单位 | ID 与生命周期 | 用途 |
| --- | --- | --- |
| 部署 | 现有有效 AppData 下 `analytics/device-id`，升级保留 | 活跃部署、版本、部署方式、资源规模 |
| 桌面客户端 | 当前 OS 用户的桌面 profile 控制目录下 `analytics-client-id`；与库、profile、目标 Server 无关 | 一体版切换工作区或管理其他 Server 时保持同一客户端 |
| 浏览器客户端 | 当前 origin 的 LocalStorage 随机 UUID | 同一浏览器访问同一 origin 的使用情况 |
| 移动客户端 | SharedPreferences 独立随机 UUID，和配对凭证分开 | 切换 Server、重新配对仍保持同一客户端 |
| 运行实例 | 每个服务进程新建 `runtime_id` | 启动、心跳、停止关联 |
| 任务尝试 | 每次 attempt 新建 UUID | 区分重试、等待、取消和失败 |

客户端事件使用 `distinct_id=v2:client:<uuid>`；系统事件使用 `v2:deployment:<uuid>`。所有事件均设置 `schema_version=2`、`identity_version=2`、`$process_person_profile=false`，不创建用户画像，不调用 identify/alias 合并。无需 Group Analytics。

“唯一用户”在当前无统一登录账号的架构下应标为**唯一匿名客户端**。同一人使用多个设备或不同浏览器会产生多个客户端；清除站点数据、卸载移动 App 也可能生成新身份。浏览器不同 origin 无法可靠合并。复制 AppData 会复制部署 ID，不代表两份真实安装自动可区分。不要把这些数字称为自然人数。

Native 身份端点只对带原生窗口标记的 loopback、同源请求开放；桌面 relay 从本机读取，不向被管理 Server 索取身份。拿不到可信 native context 时停止采集，避免回退为多个浏览器身份。

## 2. 通用维度

| 字段 | 口径 |
| --- | --- |
| `deployment_type` | `desktop_all_in_one` / `standalone_server` / `docker_server` |
| `client_surface` | `browser` / `desktop_webview` / `mobile_app`；纯系统事件为 `server` |
| `actor_type` | `client` / `system`；心跳、定时后台任务不可当用户活跃 |
| `deployment_id` | 被操作 Server 的部署身份，客户端切换 Server 后改变 |
| `server_version` | Server 程序版本 |
| `ui_version`、`client_version` | 可用时发送 UI 或原生客户端版本；不能用 Server 版本冒充移动版本 |
| `release_channel` | Server 的 stable/beta/dev 发布渠道 |
| `server_os`、`server_arch` | 有限枚举，反映 Server，不冒充客户端平台 |
| `environment` | production/development/staging/test/unknown；生产看板只含 production |
| `session_id` / `$session_id` | 随机会话 ID，用于关联；当前 Web 是标签页会话，不等同 30 分钟 idle session |

版本分布应每个部署取时间窗口内**最后一次观测的版本**，避免升级前后都进入饼图。若按客户端版本统计，也采用最后一次事件口径。版本特性随事件记录，不读取可被覆盖的 person properties。

## 3. 事件目录及业务边界

### 运行与配置

| 事件 | 触发位置与含义 |
| --- | --- |
| `runtime_started` | HTTP host 的 hosted service 开始；不依赖浏览器 |
| `runtime_ready` | `ApplicationStarted`，`readiness_stage=http_listening` |
| `runtime_heartbeat` | 启动观测一次，默认之后每 6 小时；uptime、队列丢弃、发送失败累计数 |
| `runtime_stopped` | 服务正常停止时尽力发送 |
| `deployment_first_seen` | v2 首次观测；`new` 与 `existing_unknown` 分开 |
| `deployment_snapshot` | 默认最多每天一次：本地资源/媒体库数量、AI 是否配置、已配置增强器 |

启动计时从遥测服务实例化开始，**不覆盖此前的进程入口、配置读取和数据库迁移**；因此不能据此声称完整冷启动成功率。`runtime_stopped` 缺失可能由断电、强杀、网络、关闭采集导致；`unclean_or_unknown` 不能直接叫崩溃。心跳表明最近被观测运行，不能证明持续可用或在线 SLA。

快照只统计本地数据库，不把远端副本资源再算进本地资产。查询失败为 null/partial，不伪装成 0。增强器“配置了”不等于“成功用过”。快照是部署状态，不做客户端事件，多个浏览器打开同一个 Server 不会重复生成部署快照。

### 客户端体验

| 事件 | 触发位置与含义 |
| --- | --- |
| `ui_session_started`、`page_view`、`feature_exposed` | UI 会话、固定路由、功能入口曝光 |
| `feature_invoked`、`feature_previewed` | 显式调用/预览；不能替代业务完成 |
| `resource_query_presented` | UI 查询结果或失败回调；使用 `search_kind` 和 `load_mode` 区分手动搜索、加载、分页。Web 耗时到响应回调，不是渲染完成耗时；移动端在展示回调记录 |
| `resource_opened` | 资源详情实际打开 |
| `playback_requested`、`playback_started` | 请求播放与内置播放器确认播放；浏览器 onPlaying、移动播放器 position>0 |
| `playback_handoff` | 交给原生/外部播放器或剪贴板的尝试结果；包括 failed、pending。success 仅代表交接被接受或复制完成，不保证外部播放成功 |
| `reader_opened` | 图片/阅读内容首次成功展示 |
| `help_opened`、`help_completed` | 帮助流程 |
| `pairing_started`、`pairing_result` | 配对入口与结果；仅在有效目标采集配置下发送 |
| `server_switch_started`、`server_switch_result` | Server 切换入口与结果 |

具体端的覆盖以调用点为准，不是每个客户端都具备所有事件。首次连接失败时尚未拿到目标 Server 的采集配置，移动端不向旧 Server 的 PostHog 项目发送该失败。两端客户端不是访问量的完整普查。

`page_view`、`feature_exposed` 当前仅由 Web 发送；移动端以 `ui_session_started`、`ui_ready` 和业务事件为主。跨端业务事件的固定 `feature_id` 为 search/resources/playback/reader，不代表移动端已经具备完整的功能曝光漏斗。`manual + replace` 是当前识别到的主动搜索子集：Web“在新标签页中查看”的首次加载仍归入 restored，不能称为所有手动搜索。

### 服务端真实结果

| 事件 | 计数单位 |
| --- | --- |
| `resource_search_completed` | 一次后端查询完成，含 result_count/zero_results/duration；可能来自首页、预览或分页，不能叫用户手动搜索次数 |
| `library_configured`、`resource_profile_saved`、`path_mark_configured` | 一次配置保存结果 |
| `property_edit_completed`、`bulk_edit_completed` | 一次修改请求结果，不等于修改的资源数量 |
| `collection_updated`、`saved_search_created`、`resource_merged` | 一次同步业务操作结果 |
| `workflow_configured`、`subscription_configured` | 一次定义保存结果 |
| `task_attempt_finished` | BTask 的每次 attempt：success/failed/cancelled/waiting/retry_scheduled，含运行与排队耗时 |
| `workflow_run_completed` | Workflow 持久化的终态，Waiting 不当作完成 |
| `enhancement_completed` | 一批、一个 provider 的汇总，成功/失败/跳过/未完成/零结果数量 |
| `ai_request_completed` | 真正调用 provider 后的结果；LLM 缓存命中不算调用，流式耗尽才成功，未知 token 为缺失 |
| `download_completed` | 真实下载运行终态持久化后一次；预检查发现已有文件不冒充下载成功 |
| `subscription_check_completed` | 一次订阅检查汇总；逐条失败/抓取失败与创建/跳过数量分别记录 |

同步 API 根据 HTTP 状态、业务 response code 和异常确定 outcome；不上传参数、response message 或异常原文。未能确定是用户主动操作时 `trigger=unknown`。

异步任务在创建时复制匿名 actor 到内存，retry 保留同一发起者，自动 recurring 不永久继承最初点击的人。不能把 BTask “成功返回”当作每个领域操作都成功；领域完成事件与底层 attempt 使用不同的分母。

无客户端发起者的自动 recurring 维护循环不发送 attempt 事件，成功和失败均排除，避免只过滤成功而夸大失败率；手动运行及独立领域结果仍保留。固定排除项为 MetadataSync、ResourceData、Enhancement、AcquisitionQueue、SubscriptionCheck、DataSync、WorkflowRunCleanup、SyncSteam、SyncDLsite、SyncExHentai、ResourceProcessing、DownloadResultDispatch、PlatformFetchWatch、WorkflowScheduler。看板因此明确称为“已采集任务尝试”，不代表所有后台循环。

没有增加每个文件、每个进度 tick 或每个 HTTP request 的产品事件。当前未全面接入扫描、导入、同步、升级的专用领域成功事件；其 BTask attempt 只能提供任务层观测，未来需逐一补真实终态边界。

## 4. 分析口径

- 活跃客户端：窗口内有明确产品交互的 v2/client distinct ID；不能把 server 心跳算 DAU。打开页面与完成价值动作可分别展示“访问活跃”和“有效使用活跃”。
- 活跃部署：窗口内有 Server 运行事件的 deployment ID；“最近 24 小时有心跳”应明确叫最近被观测的部署。
- 功能覆盖/曝光率：曝光该功能的客户端 / 有 UI 活动的客户端。
- 功能采用率：完成该功能的客户端 / 同窗口活跃且具备该功能的客户端；资格数据不足时明确改称“活跃客户端中的使用占比”，不虚构资格分母。
- 功能转化：曝光 → 发起 → 成功；同一个操作须避免 UI/API 双端重复上报同名完成事件。
- 激活：首次被观测客户端在选定窗口内完成一次有价值操作，例如资源打开后确认播放。v2 首次出现的老用户不能叫新安装。
- 留存：使用原生 Retention 的首次/重复口径；展示完整 D1/D7/D30 或周留存 cohort，尚未成熟的日期不当 0。
- 质量：完成事件中 failed 的比例、明确取消占比、p50/p95 耗时、零结果率、retry attempt 比例。缺失、未完成与失败不同。
- 规模：先取每部署最新 snapshot 再分桶/汇总，不能对每天快照反复求和。

匿名 ID、部署 ID、会话 ID、attempt ID 用于关联/去重，不用作高基数图例。业务字段仅固定枚举和有界数字。不应靠 IP、MAC、路径、设备指纹或 pairing key 拼用户。

## 5. 采集与用户控制

主开关仍是 `AppOptions.EnableAnonymousDataTracking`。客户端和目标 Server 均需允许；移动端还有本机开关，桌面访问远端还读取本机同意状态。关闭时清空 PostHog 待发队列、取消可取消的在途请求，后端在捕获与发送前检查。不能撤回已送达网络或 PostHog 的事件。移动端会在恢复前台和定期刷新时检查 Server 设置，有最长约 5 分钟的配置有效期。

PostHog：无 autocapture、session replay、console capture、全量 request URL 或 referrer；设 `$geoip_disable=true`。Web/移动端使用专用 HTTP emitter，后端使用 bounded channel。只保存随机身份和运行状态，不建立磁盘离线事件箱；队列满/超时/离线可丢数据。Web 短时重试复用原事件 UUID 和时间，切换上下文不重放旧目标的数据。

GA4 保留现有兼容用途，使用固定虚拟 page URL；PostHog 是 v2 主分析源。Clarity 需同时满足主开关与 `Analytics:Clarity:EnableSessionReplay=true`，默认不启动录像。

Sentry 是错误诊断通道，主开关同样生效，但后端保留异常诊断上下文，不能将产品事件白名单理解成所有错误报告都绝不含文件路径或原始文本。前端主动删除请求内容、动态 URL 和异常文本。不要在日志中写 token/密码。

## 6. 配置与维护

沿用 `Analytics:PostHog:ApiKey` 和 `ApiHost`。Cloud ingestion host 与管理页面 host 不同（如 `eu.i.posthog.com` 与 `eu.posthog.com`）。项目 ingestion token 可发给客户端；Personal API Key 只用于管理，不得放入配置接口、代码或镜像。

| PostHog 配置项 | 默认 |
| --- | --- |
| `QueueCapacity` | 512 |
| `BatchSize` | 50 |
| `FlushIntervalSeconds` | 10 |
| `HeartbeatIntervalMinutes` | 360 |
| `SnapshotIntervalHours` | 24 |

看板部署：`python3 src/scripts/posthog-dashboards.py` 默认只预览；显式 `--validate --apply --host https://eu.posthog.com --project <id>` 才写入，需要进程环境中的 `POSTHOG_PERSONAL_API_KEY`。脚本只更新带自有 tag 和精确 marker 的对象，保留其他看板与手动标签。也可以经 PostHog 插件创建相同定义。

变更事件名、字段、单位或结果含义时，同步更新三端 policy、调用点测试、看板目录和本文件；破坏口径时提升 schema_version。不要把空看板当作线上采集成功：需分别验证请求载荷、摄取、查询、真实业务边界。

参考：[PostHog 匿名事件](https://posthog.com/docs/data/anonymous-vs-identified-events)、[事件捕获](https://posthog.com/docs/product-analytics/capture-events)、[Personal API Key](https://posthog.com/docs/api/personal-api-keys)。本方案中的指标是项目当前实现口径，不是已审批的 Data Catalog canonical metric。
