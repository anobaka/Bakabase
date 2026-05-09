# Bakabase 埋点验收清单

落地分支 `claude/analytics-infrastructure-Vqkw7` 后，按以下顺序跑一遍能验证 5 个目标需求是否全部生效。

---

## 0. 先决条件

```bash
# 1. 拉子模块
git submodule update --init --recursive

# 2. 还原 NuGet（会拉 Sentry.AspNetCore）
dotnet build

# 3. 还原前端依赖（会拉 @sentry/react）
cd src/web && yarn install

# 4. 重新生成 SDK（让前端可以从 fetch 迁到 typed BApi.app.*，可选）
yarn gen-sdk

# 5. 启动后端 + 前端
dotnet run --project src/apps/Bakabase &
cd src/web && yarn dev
```

---

## 1. 申请并配置四个平台

### 1.0 概览

| 平台 | 用途 | 你需要拿到 | 难度 |
|---|---|---|---|
| Google Analytics 4 | 量化指标 / cohort 分析（主） | Measurement ID `G-XXXXXXXXXX` | 中（容易踩坑） |
| **PostHog** | 量化指标 / cohort 分析（**双线并行**） | Project API Key `phc_...` + region 选择 | 低 |
| Sentry | 错误聚合 / Release Health | 两个 DSN（前端 project + 后端 project） | 低 |
| Microsoft Clarity | 录像 / 热图 | Project ID（10 字符短码） | 低 — 主仓库已有 `r5xlbsu4fl`，**fork 才需要重做** |

> **关于 GA4 + PostHog 并行**：所有 user properties 和 events 同时上报给两边，互不干扰。GA4 在国内有时被墙或 Property 进入脏状态；PostHog 在国内访问稳定且后台直观，作为后备 / 主力都行。配哪个就跑哪个，都不配也不报错。

---

### 1.1 GA4 配置

#### 1.1.1 创建 Account + Property

1. 访问 https://analytics.google.com → 用 Google 账号登录
2. 左下角 ⚙️（**Admin**）→ "Create" 下拉 → **"Account"**
   - Account name: e.g., `Bakabase`
   - Data sharing settings: 默认即可
3. 同一向导继续到 **"Create a property"**
   - Property name: e.g., `Bakabase`
   - Reporting time zone / Currency: 选你的
   - Industry / Business size: 随便填，不影响功能
4. 点 "Create" → 跳转到 "Data collection" 步骤

#### 1.1.2 创建 Data Stream

1. 选 **Web** 平台（Bakabase 是 WebView，本质是网页 SDK）
2. **Stream URL** ⚠️：GA4 这里要求填一个网站 URL，但**完全不会校验内容**。Bakabase 跑在 localhost，所以填一个占位符即可：
   ```
   https://app.bakabase.local
   ```
   这个值只在 GA4 报表里用作流的标识，不影响数据收集。
3. **Stream name**: e.g., `Desktop App`
4. **Enhanced measurement**: 关掉或留默认都行 — 我们手动发 `page_view`，不依赖 GA4 自动追踪。如果担心 GA4 自动事件污染数据，**关掉**。
5. "Create stream" → 跳到 stream 详情页

#### 1.1.3 拿 Measurement ID

Stream 详情页顶部显示：

```
Measurement ID: G-XXXXXXXXXX  [📋 Copy]
```

复制下来，§1.4 要用。

#### 1.1.4 注册 Custom Definitions（**关键步骤**）

⚠️ **不做这步，需求 1 / 4 / 5 在 GA4 报表里看不到。** 我们在代码里发的字段如果没在 GA4 后台注册成 Custom Dimension / Metric，就只是事件附带数据，报表/Explorations 里没法当维度用。

1. ⚙️ Admin → Property 列里找 **"Custom definitions"**
2. 顶部有两个 tab：**"Custom dimensions"** 和 **"Custom metrics"**
3. 按下表逐个建（每条点 "Create custom dimension/metric"）：

| 在哪个 tab | Display name | Scope | Event/User parameter | Unit |
|---|---|---|---|---|
| Custom metrics | `media_library_count` | Event | `media_library_count` | Standard |
| Custom metrics | `resource_count` | Event | `resource_count` | Standard |
| Custom dimensions | `app_version` | **User** | `app_version` | — |
| Custom dimensions | `release_channel` | **User** | `release_channel` | — |
| Custom dimensions | `os` | **User** | `os` | — |
| Custom dimensions | `locale` | **User** | `locale` | — |
| Custom dimensions | `enabled_enhancers` | **User** | `enabled_enhancers` | — |
| Custom dimensions | `ai_enabled` | **User** | `ai_enabled` | — |
| Custom dimensions | `has_media_library` | **User** | `has_media_library` | — |
| Custom dimensions | `feature_id` | Event | `feature_id` | — |
| Custom dimensions | `enhancer_id` | Event | `enhancer_id` | — |
| Custom dimensions | `environment` | Event | `environment` | — |

**踩坑点**：
- `Event/User parameter` 一栏必须填**和我们前端代码里 `gtag('set', 'user_properties', { app_version: ... })` / `gtag('event', 'app_snapshot', { media_library_count: ... })` 完全一样的 key**（区分大小写、下划线分隔）
- Scope 选错（User 选了 Event 或反之）→ 报表里这个字段会一直空
- 注册后**有 24h 延迟才生效**：之前发的事件用不了新维度

#### 1.1.5 数据保留期（推荐改）

GA4 免费档默认数据保留 **2 个月**，建议改到上限 **14 个月**（仍免费）：

⚙️ Admin → Property 列 → **"Data Settings"** → **"Data Retention"** → 改为 `14 months` → Save

#### 1.1.6 调试用：DebugView

GA4 的常规报表延迟 24-48h，调试期间用 **DebugView** 实时看事件。

DebugView 只显示带 `debug_mode: true` 标记的事件。Bakabase 已经处理好这个：

- **`yarn dev` 启动的前端** → 代码里自动带 `debug_mode: true`（`src/web/src/services/Analytics.ts` 里 `import.meta.env.DEV` 分支），事件直接进 DebugView
- **`yarn build` 出来的生产构建** → 不带 debug 标记，事件只进 Reports / Realtime，不污染 DebugView

如果需要在生产构建上临时打开 DebugView（比如线下排查），有两条退路：

1. URL 末尾加 `?debug_mode=1` —— gtag.js 会读取这个参数
2. 浏览器装 [Google Analytics Debugger by David Vallejo](https://chrome.google.com/webstore/detail/google-analytics-debugger/ilnpmccnfdjdjjikgkefkcegefikecdc) —— 注意**不是** Google 官方的同名扩展（那个只输出 console 日志，不影响 DebugView）。装完后**必须 reload 页面**让 hook 生效

入口：⚙️ Admin → Property 列 → **"DebugView"** → 你的设备会出现在左侧列表，点进去看实时事件。

**DebugView 没数据的快速分诊：**

1. 先看 **Realtime**（Admin → 同列）有没有事件 —— 如果有 → 事件能到 GA，问题只在 debug 标记；如果没有 → 事件根本没到 GA（看 Network 里 `google-analytics.com/g/collect` 请求是否 200）
2. Network 看 collect 请求的 query string，找 `_dbg=1` —— 有它的话 GA 收到的请求是带 debug 标记的，DebugView 应该显示
3. 确认 DebugView 顶部"设备"下拉里选的是你这台机器（多设备调试时容易选错）

#### 1.1.7 GA4 常见踩坑速览

| 症状 | 原因 |
|---|---|
| 报表里看不到任何数据 | 24-48h 才同步；用 Realtime / DebugView 验证立即收到没 |
| Custom Dimension 永远空 | 1) 没注册；2) Scope 填错；3) Parameter name 大小写不一致；4) 注册后 24h 内 |
| 字段值变成 `(other)` | 单维度独立值 >500/天/property（我们字段都是低基数，不会触发） |
| Realtime 也没事件 | adblock / 隐私插件拦截 `googletagmanager.com`；用无痕窗口测 |
| Realtime 有事件但 Reports 没 | 正常的 24-48h 延迟 |

---

### 1.2 Sentry 配置

#### 1.2.1 注册

1. https://sentry.io/signup/ → Sign up（推荐用 GitHub 登录，免密码管理）
2. Organization name: e.g., `Bakabase`
3. 落到 onboarding 页面会让你选第一个 project 平台 — 选 **React**（先建前端 project）

#### 1.2.2 创建前端 project

按 onboarding 引导走，或在 Sentry 后台 **Projects → Create Project**：

1. **Platform**: Browser → **React**
2. **Alert frequency**: "Alert me on every new issue"（后续可调）
3. **Project name**: e.g., `bakabase-web`
4. **Team**: 默认或新建
5. "Create Project"
6. 创建完会跳到一页 "Configure SDK" 指引 — **暂时跳过**，因为我们代码里已经写好了；你只需要复制顶部的 **DSN**：
   ```
   https://<random>@oXXXXXX.ingest.sentry.io/YYYYYY
   ```

#### 1.2.3 创建后端 project

重复上面：

1. **Projects → Create Project**
2. **Platform**: Server → **ASP.NET Core (.NET)**
3. **Project name**: e.g., `bakabase-service`
4. 拿 DSN

→ 现在你应该有 **两个 DSN**，分别对应前后端。**不要混用**（错误会进错 project，难定位）。

#### 1.2.4 配额配置（可选但推荐）

免费档 5k errors/月。代码里我们已关掉 Performance 和 Replay (`tracesSampleRate=0`)，所以不会消耗那两类配额。

设个用量提醒，避免无声超限：

**Settings → Subscription → Usage** → 点 "Set up spend limit / alerts" → 设到 **80% 用量**邮件提醒

#### 1.2.5 Release Health 自动生效

代码里已设 `release: appVersion` 和 `environment: production/development`，Sentry 自动按 release 聚类。

进 **Releases** tab → 点具体 release → 看 "Crash-free Sessions / Crash-free Users" 即错误率指标。

#### 1.2.6 Sentry 常见踩坑

| 症状 | 原因 |
|---|---|
| Issues 里完全没东西 | DSN 填错了 / DSN 和 project 不对应（前端 DSN 写到 backend 配置了或反之） |
| 错误显示但没 stack trace | 浏览器有 source map 问题；后端 release build 没上传 PDB（这级深度先不做） |
| 大量重复噪音 (`ResizeObserver loop`、`AbortError`) | 用 `Sentry.init({ beforeSend })` 过滤；先观察 1-2 周看哪些是真噪音再加规则 |

---

### 1.3 Clarity 配置（fork 才需要）

主仓库已有 Project ID `r5xlbsu4fl`，**普通用户/二次开发不需要换**。如果你 fork 后想用自己的看板：

1. https://clarity.microsoft.com → Sign in（Microsoft / GitHub / Facebook 任一）
2. 顶部 **"+ New project"**
3. Project name + Website URL（同 GA4，URL 占位即可，e.g., `https://app.bakabase.local`）
4. Site category: 随便选
5. 创建完拿顶部的 **Project ID**（10 字符短码）

---

### 1.4 PostHog 配置

PostHog 与 GA4 平行运行 —— 同一份 user properties + events 会同时发给两边。配 PostHog 的成本很低且国内访问稳定，强烈推荐。

#### 1.4.1 注册 + 选 region

1. https://posthog.com/signup → 注册（GitHub / Google / Email 任选）
2. **关键步骤：选 Region**
   - **EU Cloud** (`https://eu.i.posthog.com`) — 国内访问通常最稳；GDPR 合规
   - **US Cloud** (`https://us.i.posthog.com`) — 默认；国内有时不太稳
   - 建议选 **EU**（国内访问更可靠）
3. 注册完成后跳转到 onboarding

#### 1.4.2 创建 Project + 拿 API Key

1. onboarding 走完即默认有一个 project；或顶部下拉 → **"+ New project"**
2. Project name: e.g., `Bakabase`
3. **关键步骤**：找到 Project API Key
   - 左下角 ⚙️ → **Project Settings**
   - 顶部就是 **Project API Key**，形如 `phc_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx`
   - **不要**用 "Personal API Key"（那个是管理用的，写到客户端会有安全风险）

#### 1.4.3 把 Region API Host 对应起来

PostHog Cloud 的 ingestion endpoint 跟 region 严格对应：

| Region | API Host |
|---|---|
| US Cloud | `https://us.i.posthog.com` |
| EU Cloud | `https://eu.i.posthog.com` |
| 自托管 | 你自己的 host |

填错 region 的 host → 数据进不去（不会报错，但 PostHog 后台空空）。

#### 1.4.4 PostHog 后台无需额外配置

跟 GA4 不同：PostHog **不需要预先注册 Custom Dimension / Metric**。你随便发什么 event 和 property，PostHog 都自动接收并立刻能在 Insights / Persons 里查询。这是它比 GA4 友好的地方。

#### 1.4.5 PostHog 常见踩坑速览

| 症状 | 原因 |
|---|---|
| Live Events 一直空 | 1) API Host 跟 region 不对应；2) API Key 用成了 Personal Key |
| Person 在 Persons 标签页找不到 | PostHog v1 默认用匿名 distinct_id，找 `bc18f236-...`（你的 deviceId）需要在 Persons 列表搜 ID |
| Property 字段类型错 | 第一次上报时定下；如果先发 `media_library_count: "15"`（字符串）再发 `15`（数字），后面那个会被丢。我们代码统一发数字所以无问题 |

---

### 1.5 把凭证填进 appsettings.json

编辑 `src/apps/Bakabase.Service/appsettings.json` 的 `Analytics` 段：

```json
"Analytics": {
  "Clarity": { "ProjectId": "r5xlbsu4fl" },           // 已存在；fork 改这里
  "Ga4":     { "MeasurementId": "G-XXXXXXXXXX" },     // ← §1.1.3 拿到的
  "Sentry":  {
    "FrontendDsn": "https://<key>@<id>.ingest.sentry.io/<proj>",  // ← §1.2.2 前端 project
    "BackendDsn":  "https://<key>@<id>.ingest.sentry.io/<proj>"   // ← §1.2.3 后端 project
  },
  "PostHog": {
    "ApiKey":  "phc_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",   // ← §1.4.2 拿到的 Project API Key
    "ApiHost": "https://eu.i.posthog.com"                           // ← §1.4.1 region 对应的 host
  }
}
```

**任何一项留空（`null` 或 `""`）→ 对应 SDK 自动跳过初始化，不报错。** 这样你可以增量配置：先只配 Sentry 调试错误，过几天再加 GA4，再下次加 PostHog。

也可以走环境变量覆盖（CI / 私有部署常用）：

```bash
export Analytics__Clarity__ProjectId=xxx
export Analytics__Ga4__MeasurementId=G-XXX
export Analytics__Sentry__FrontendDsn=https://...
export Analytics__Sentry__BackendDsn=https://...
export Analytics__PostHog__ApiKey=phc_...
export Analytics__PostHog__ApiHost=https://eu.i.posthog.com
```

ENV 优先级高于 `appsettings.json`。

---

## 2. 冒烟测试

### 2.1 后端

| 检查 | 怎么做 | 预期 |
|---|---|---|
| `device-id` 文件生成 | 启动一次后查看 `{AppData}/Bakabase.AppData/analytics/device-id` | 文件存在，内容是 UUID v4（36 字符） |
| `analytics-info` 端点可达 | `curl http://localhost:<port>/app/analytics-info` | 返回 `{ code: 0, data: { deviceId, appVersion, releaseChannel, ... } }` |
| `telemetry-snapshot` 端点可达 | `curl http://localhost:<port>/app/telemetry-snapshot` | 返回 `{ code: 0, data: { mediaLibraryCount, resourceCount, enabledEnhancers, ... } }` |
| 多次启动 deviceId 稳定 | 重启应用 3 次，每次读 `device-id` 文件 | 内容不变 |

### 2.2 前端

打开应用 → F12 DevTools → **Network** 面板 → 刷新一次。应当看到：

| 请求 | 预期 |
|---|---|
| `GET /app/analytics-info` | 200，返回 deviceId / 各 DSN |
| `GET /app/telemetry-snapshot` | 200（仅在 hash 变化时才会触发后续上报） |
| `GET https://www.googletagmanager.com/gtag/js?id=G-...` | 200（GA4 SDK） |
| `POST https://*.ingest.sentry.io/...` | 200（Sentry 心跳）|
| `POST https://www.clarity.ms/collect` | 200（Clarity 心跳）|
| `POST https://eu.i.posthog.com/e/` 或 `/decide/` | 200（PostHog 心跳；US region 是 `us.i.posthog.com`） |

DevTools → **Application** → **Local Storage** → 应当看到 `telemetry_last_hash` key（一次成功上报后写入）+ 一些以 `ph_` 开头的 PostHog 内部 key（distinct_id、session 等）。

---

## 3. 五个需求逐项验证

> 5 个需求每个都给出 **GA4** 和 **PostHog** 两条验证路径。哪个工具数据流通就用哪个；都通就以 PostHog 为准（数据更直观）。

### 需求 1：版本分布饼图

**GA4 路径：**
1. **Reports → User → Tech → Tech details**
2. 把 dimension 切换为 `app_version`（已注册的 Custom Dimension）
3. 应当看到一个用户数饼图，按版本分片
4. **带 release_channel 切片：** 在 Explorations 里建表，行 = `app_version`，二级行 = `release_channel`

**PostHog 路径：**
1. **Insights → New insight → Trends**
2. Series: `Users` (or `$pageview` event with Aggregation = Unique users)
3. **Breakdown by:** Person property → `app_version`
4. 一键得到饼图 / 柱状图 / 时序图，切换 `release_channel` 同理

### 需求 2：错误聚合

**前端错误：**
```js
// 在浏览器 console 跑：
setTimeout(() => { throw new Error("frontend test event"); }, 100);
```
→ Sentry 前端 project → Issues → 应当出现一条 `Error: frontend test event`，带 stack trace、release（= appVersion）、user.id（= deviceId）。

**后端 ILogger 错误：**

随便找一个 controller 临时加：
```csharp
_logger.LogError(new InvalidOperationException("backend test"), "test from logger");
```
→ Sentry 后端 project → Issues → 应当出现 `InvalidOperationException: backend test`。**这条验证 MSEL → Sentry 桥接是否生效**。

**后端 unhandled exception：**

任何 controller 加 `throw new Exception("middleware test");` → 5xx 响应 → Sentry 后端 project 应当也出现这条（验证 ASP.NET Core diagnostic listener 自动捕获）。

**错误率指标：** Sentry → **Releases** 标签页 → 选当前 release → "Crash-free Users" 百分比。需要至少几个会话才能看出。

### 需求 3：功能使用情况

**GA4 路径：**
1. **Reports → Realtime**（最快验证）或 **Engagement → Pages and screens**
2. 在前端切换若干路由：`/`、`/resource`、`/dlsite-works`、`/configuration` 等
3. Realtime 应当**实时**看到 `page_view` 事件
4. 不要用 GA4 的 "Page" dimension（那是从 URL 推断的），用 Explorations 里 dimension = `page_path`

**PostHog 路径：**
1. **Activity → Live events**（最快验证）→ 切路由后立刻能看到 `$pageview` event 和 `page_path` property
2. **Insights → New insight → Trends** → Series: `$pageview` → Breakdown by: Event property → `page_path` → 路由排名一目了然

### 需求 4：媒体库 / 资源数分布

**GA4 路径：**
1. **Configure → Custom definitions** → 确认 `media_library_count` / `resource_count` 显示为 "Custom metric"
2. **Explorations → Free form**
   - Metrics: `media_library_count` (Average / P50 / P90 等聚合函数都可以)
   - Dimensions: 可以加 `app_version` 切片
3. **或 BigQuery export**（GA4 免费档也能用 BigQuery sandbox）→ SQL 任意切桶

**PostHog 路径**（**通常更顺手**）：
1. **Insights → New insight → Trends** → Series: `Users` → Aggregation: `Median of person property` → 选 `media_library_count`
2. 直接画出库数量的中位数 / P90 时间趋势
3. 想看分布饼图：换成 **Funnel** / **Stickiness** / **Lifecycle** insight，PostHog 的 numeric breakdown 自带分桶

### 需求 5：外部平台使用情况

#### 5a. 已启用列表（cohort 占比）

**GA4 路径：**
1. **Configure → Audiences → Create audience**
2. Condition: `User scoped → enabled_enhancers contains "DLsite"`
3. Save → Audience size 显示符合该条件的用户数 / 总用户数
4. 每个 enhancer 各建一个 audience，导出对比

**PostHog 路径：**
1. **Insights → Trends** → Series: `Users` → **Filter:** Person property → `enabled_enhancers` → contains → `DLsite`
2. 一行 filter 出 cohort
3. **People → Cohorts** 也能持久化保存这些条件供反复使用

#### 5b. 反向指标（"什么都没启用"）

**GA4 路径：**
1. **Audiences → Create audience**
2. Condition: `enabled_enhancers does not match regex .+`
3. 该 audience size = 启用列表为空字符串的用户数

**PostHog 路径：**
1. **Insights → Trends** → Series: `Users` → **Filter:** Person property → `enabled_enhancers` → is empty
2. 直接出反向指标

#### 5c. 实际触发率（vs 仅启用）

`enhancer_triggered` event 当前**没有 call site**（helper 已存在但未在任何地方调用，由设计 §16 #5 决定先不上）。
要看实际触发率，需要后续在 enhancer 实际跑完的地方加一句 `trackEnhancerTriggered(id, success)`。

到时候 GA4 用 Custom Dimension `enhancer_id` + Audiences；PostHog 直接 Trends → `enhancer_triggered` event → Breakdown by `enhancer_id`。

---

## 4. 隐私开关验证

| 步骤 | 预期 |
|---|---|
| 关闭"启用匿名数据追踪" | 提示重启 |
| 重启应用 | DevTools Network 应当**没有任何**对 `gtag/js`、`ingest.sentry.io`、`clarity.ms` 的请求；`/app/analytics-info` 仍会调一次但返回的 toggle=false 后前端立即 short-circuit |
| 重新打开开关 + 重启 | 三个 SDK 都重新初始化；`device-id` 文件保持不变（同一身份恢复） |

---

## 5. 多次启动不累加验证

按照设计 §6.5 的节流逻辑：

1. 启动 → DevTools Network 看 `/app/telemetry-snapshot` 调用 → 之后看 `gtag` 是否发了 `app_snapshot` 事件（GA4 DebugView 最方便）
2. 立即重启（不改任何状态）→ `app_snapshot` event **不应该再次触发**（因为 `localStorage.telemetry_last_hash` 没变）
3. 增加一个媒体库 → 重启 → `app_snapshot` event 应当**重新触发**（hash 变了）

**GA4 DebugView 启用方法**：在浏览器装 [GA Debugger 扩展](https://chrome.google.com/webstore/search/Google%20Analytics%20Debugger)，启动后 GA4 → Configure → DebugView 实时看你这一台机器发出的所有事件。

---

## 6. 故障排查

| 症状 | 可能原因 |
|---|---|
| Network 里没有 `gtag/js` 请求 | `Analytics:Ga4:MeasurementId` 没填 / `enableAnonymousDataTracking=false` |
| Network 里没有 `posthog.com` 请求 | `Analytics:PostHog:ApiKey` 没填 / region 与 ApiHost 不对应 |
| GA4 Realtime 一直空（PostHog 正常） | GA4 Property/Stream 进了僵尸态 — 重建 stream 或 account（见我们调试历史） |
| PostHog Live Events 空（GA4 正常） | API Key 错 / Region host 不匹配 / 网络层拦了 `*.posthog.com` |
| Sentry Dashboard 空白 | DSN 错 / Sentry project 选错（前后端各一个，别填混了） |
| GA4 DebugView 看不到事件 | 见 §1.1.6（dev 构建已自动加 debug_mode；prod 需扩展或 URL 参数） |
| `app_snapshot` 永远不发 | `localStorage.telemetry_last_hash` 已存且 hash 没变 → 改个状态（加/删媒体库）/ 清 localStorage |
| 后端 Sentry 里没有 `_logger.LogError` 输出 | MSEL provider 没注册成功（看启动日志有没有 `Sentry` 相关错误）；或 LogError 级别低于 `MinimumEventLevel = Error` |
| `device-id` 文件不在预期位置 | macOS / Linux 下查 `~/Library/Application Support/Bakabase` 或 `$XDG_DATA_HOME/Bakabase`；Windows 在 `%LocalAppData%\Bakabase.AppData\analytics\` |

---

## 7. 验证完成的标志

5 个需求都能从 GA4 / Sentry 看板里直接读出来 ⇒ 落地完成。
任何一项失败 → 对照本表的"故障排查"段先自查；仍解不了贴 DevTools Network / Sentry 错误截图。

---

## 附：手动 SDK 切换（可选）

前端目前用 `fetch` 直接调三个新端点，方便不依赖 `yarn gen-sdk`。`yarn gen-sdk` 跑过之后 `BApi.app` 会多出 `getAnalyticsAppInfo()` / `getAppTelemetrySnapshot()` 两个 typed 方法，可以替换 `src/web/src/services/Analytics.ts` 里的 `fetchJson` 调用，更类型安全。**功能等价**，迁不迁取决于你的偏好。
