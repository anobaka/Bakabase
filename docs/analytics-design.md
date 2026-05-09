# Bakabase 埋点基建设计文档

| 字段 | 值 |
|---|---|
| 状态 | 已实施（凭证待填）— 见 §0 实施记录 |
| 分支 | `claude/analytics-infrastructure-Vqkw7` |
| 最后更新 | 2026-05-09 |

---

## 0. 实施记录

代码已落地，本节记录与原设计的偏差，方便对照：

| 偏差点 | 原设计 | 实际实现 | 原因 |
|---|---|---|---|
| AppInfo 字段扩展 | 在 `Bakabase.Infrastructures.AppInfo` 上加 `deviceId` / `ga4MeasurementId` / `sentryDsn` / `clarityProjectId` 等字段 | 新增独立端点 `GET /app/analytics-info` 返回 `AnalyticsAppInfoViewModel` | `AppInfo` 在 git submodule 中，不能从本仓库修改；独立端点也更内聚 |
| DeviceId 存储 | SQLite `app_meta` 表 + 新建 EF migration | 单文件 `{AppData}/analytics/device-id` | 单值文件比新建表轻得多，符合现有 `app.json` 模式 |
| Cache size 字段 | 在 telemetry snapshot 里上报 `cacheSizeBytes` | 暂未实现 | 需扫文件系统或加新缓存元数据列；首版不必要，留作后续 |
| 重置匿名 ID 按钮 | 配置页加按钮 + 后端 reset 端点 | 已移除 | 用户决定不需要；toggle 一个开关够用，且老 ID 跨会话稳定有助于排查 |
| 后端 ILogger → Sentry | 设计为 Serilog sink，标记为后续 | 直接接入 MSEL (`ILoggingBuilder.AddSentry`) | Serilog 在 submodule 里构造改不动；MSEL 是独立 provider，等价覆盖 |
| 前端 SDK 调用 | 通过 `BApi.app.*` typed SDK 调用 | 用 `fetch` 直接调，等待用户本地跑 `yarn gen-sdk` 后可迁移 | 沙盒环境无法跑 gen-sdk |

---

## 1. 背景与目标

### 1.1 现状调研

| 项 | 状态 |
|---|---|
| 已接入工具 | 仅 Microsoft Clarity（`@microsoft/clarity` v1.0.0） |
| Clarity ProjectId | 硬编码于 `src/web/src/components/ContextProvider/BakabaseContextProvider.tsx:197` (`r5xlbsu4fl`) |
| 隐私开关 | 后端字段 `EnableAnonymousDataTracking` 存在（`src/apps/Bakabase.Service/Controllers/OptionsController.cs:99-102`），但**前端不读** — Clarity 无条件初始化（**bug**） |
| 前端错误处理 | `ErrorBoundary` 仅 `console.error`（`src/web/src/components/Error/components/Boundary/index.tsx:36`），无 `window.onerror` / `unhandledrejection` 兜底 |
| 后端遥测 | 完全没有 |
| 量化数据 | 无（看不到版本分布、库/资源数、平台启用率、错误率） |

### 1.2 目标

| # | 需求 |
|---|---|
| 1 | 一段时间内用户的版本分布饼图 |
| 2 | 前/后端报错聚合 + 错误率指标 |
| 3 | 每个功能的使用情况（页面/路由维度即可） |
| 4 | 用户的媒体库数量、资源总数分布 |
| 5 | 外部平台 enhancer（Steam / DLsite / ExHentai / …）启用与触发情况 |

抽象出来是两类指标形态：

- **A. "X% 用户使用了 Y" 类**（含反向："X% 用户什么都没用"）— 需 cohort 分析
- **B. 错误日志 + 错误率指标**

### 1.3 非目标

- 不做用户画像 / 行为预测
- 不接阿里云 ARMS（开源项目对企业账号、备案、实名不友好）
- 不接 PostHog / GlitchTip 自托管（首版不做，但保留切换口子）
- 不主动收集任何 PII（路径、资源标题、文件名等）

---

## 2. 工具栈

### 2.1 选型

| 工具 | 用途 | 免费档 | 角色 |
|---|---|---|---|
| Microsoft Clarity（保留） | 录像 / 热图 / Rage Click | 完全免费 | 定性 |
| Google Analytics 4（新接） | User cohort / Custom Metric / 反向指标 | 完全免费 | 定量 |
| Sentry SaaS（新接） | 错误聚类 / Release Health / 错误率 | 5k errors/月 | 故障 |

三者覆盖面互补，谁也无法取代谁：

```
              定性 (qualitative)            定量 (quantitative)         故障 (errors)
              ─────────────────             ─────────────────           ────────────
录像/热图     Clarity ✅                    —                           —
% cohort      —                             GA4 ✅                      —
版本分布饼图  —                             GA4 ✅                      Sentry (副产品)
错误聚类      —                             —                           Sentry ✅
功能使用      —                             GA4 ✅ (Event)              —
反向指标      —                             GA4 ✅ (Audience exclusion) —
```

### 2.2 不选别的原因

| 候选 | 不选原因 |
|---|---|
| 阿里云 ARMS | 开源项目对企业账号、备案、实名不友好；.NET 接入要走 OpenTelemetry，折腾 |
| PostHog 自托管 | 运维成本（PG + ClickHouse + Worker），首版不做 |
| GlitchTip 自托管 | 同上，但作为 Sentry 超限后的备选保留 |
| 自建后端日志 | 失去 stack trace 聚类、Release Health 这些核心价值 |
| GA4 替 Sentry | GA4 没有 stack trace fingerprint、没有 Release Health |
| Sentry 替 Clarity | Session Replay 仅围绕错误发生，免费档样本极少 |

### 2.3 多备口子（兼容 GlitchTip 的设计）

GlitchTip 与 Sentry 协议完全兼容（同一套 SDK、wire format、DSN 格式），所以"多备"几乎零成本：

- **不要硬编码 DSN**（避免重复 Clarity ProjectId 的覆辙）
- DSN 通过 `appsettings.json` + `/api/app/info` 端点注入前后端
- 切换 Sentry SaaS → GlitchTip 自托管 = 改一个字符串，零代码改动
- **不写抽象层 / wrapper**（YAGNI，现成的 API 兼容即抽象）

切换后会失效的功能：Sentry Replay、Performance — 但我们已决定关掉 (`tracesSampleRate=0`)，无影响。

---

## 3. 架构总图

```
            ┌───────────────────────────────────────────────────────┐
            │  enableAnonymousDataTracking（单一开关，默认 ON）       │
            │  复用现有字段，需修 bug 让它真正 gate 三个工具          │
            └─────────────────────────┬─────────────────────────────┘
                                      │
        ┌─────────────────────────────┼─────────────────────────────┐
        │                             │                             │
        ▼                             ▼                             ▼
   ┌────────┐                  ┌──────────┐                  ┌──────────┐
   │ Clarity│                  │   GA4    │                  │  Sentry  │
   │ (现有) │                  │  (新接)  │                  │  (新接)  │
   └────────┘                  └──────────┘                  └──────────┘
   定性: 录像                  定量: cohort                  故障: 错误率
                                      ▲
                                      │
                               所有三个工具共享:
                          deviceId (后端 UUID v4 文件持久化)
```

---

## 4. DeviceId 设计

### 4.1 为什么需要

1. **跨工具串联**：Clarity 录像 ↔ Sentry 错误 ↔ GA4 行为，同一 ID 才能拼出同一用户的全貌
2. **GA4 默认 client_id 不可靠**：浏览器/WebView 清缓存就重置，会高估"独立用户数"
3. **多次启动去重的基础**：用户多次启动同一 ID = 1 个用户

### 4.2 生成

| 维度 | 选择 |
|---|---|
| 算法 | UUID v4（纯随机 16 bytes） |
| **不要用** | MAC 地址、CPU 序列号、磁盘 ID、Windows `MachineGuid` — 是"硬件指纹"，GDPR 敏感 |
| 是否带可识别信息 | 不带任何，纯随机 |

### 4.3 存储

| 阶段 | 行为 |
|---|---|
| 存储位置 | 后端持久化，例如 `{AppData}/Bakabase/device-id` 或 SQLite `app_meta` 表新增一行（建议后者，避免与现有数据库分离） |
| 首次启动 | 不存在 → 生成 UUID v4 → 写入 |
| 后续启动 | 读出，只读不写 |
| 暴露给前端 | `/api/app/info` 加 `deviceId` 字段 |

### 4.4 生命周期

| 事件 | 行为 |
|---|---|
| 关闭 `enableAnonymousDataTracking` | **保留 ID 文件，停止上报**（重新开启后是同一身份，连续性好） |
| 卸载 / 清 AppData | 自然重置 |
| 多 OS 用户登录同机 | 各自独立 ID（因为 `%APPDATA%` per-user，这是合理行为） |

> 「重置匿名 ID」按钮在实施时被移除（见 §0），不在 v1 中提供。

### 4.5 注入到三个工具

| 工具 | 调用 |
|---|---|
| GA4 | `gtag('config', 'G-XXX', { client_id: deviceId })` |
| Sentry | `Sentry.setUser({ id: deviceId })` |
| Clarity | `Clarity.identify(deviceId)` |

### 4.6 红线

- ❌ 不要把 deviceId 文件放到 git 同步目录、OneDrive、Dropbox 等自动同步路径
- ❌ 不要把 deviceId 与用户名 / 邮箱 / 任何 PII 一起上报
- ❌ 不要在 deviceId 里编码任何用户信息

---

## 5. 隐私开关

### 5.1 现有 bug

```
src/web/src/components/ContextProvider/BakabaseContextProvider.tsx:197
  Clarity.init("r5xlbsu4fl")          // ❌ 无条件
  ...
  Clarity.setTag("appVersion", ...)   // ❌ 无条件

src/apps/Bakabase.Service/Controllers/OptionsController.cs:99-102
  // EnableAnonymousDataTracking 字段被持久化，但没有任何代码读它
```

### 5.2 修复方案

- Provider 初始化时读取 `appOptions.enableAnonymousDataTracking`
- 关闭时所有三个工具都不 init（**不仅是不上报**，是不加载脚本）
- 运行时切换：建议提示"重启生效"（避免运行时拆装 SDK 的状态机复杂度）

### 5.3 配置页 UI

位置：`src/web/src/pages/configuration/components/Others/index.tsx:181-196`

新增内容：
- 文案改写：明示使用了 Microsoft Clarity / Google Analytics / Sentry 三个服务
- 文案分两层（建议）：
  - "错误诊断"（推荐开启，对项目维护有价值）
  - "使用行为分析"（可选，更敏感）
  - **首版可不分层**，单一开关；分层属于 future enhancement
- ~~增加"重置匿名 ID"按钮 + 二次确认 dialog~~（实施时移除，见 §0）

---

## 6. 数据模型

### 6.1 类型规则

| 数据形态 | GA4 形态 | 原因 |
|---|---|---|
| 状态 / 长期属性 | User Property | overwrite-safe，多次启动幂等 |
| 行为 / 动作 | Event | count-safe，自然累加 |
| 数字（无上界） | Event Param + 注册 Custom Metric | 无 cardinality 限制；事后任意切桶 |
| 字符串 / 枚举 | Dimension（User Property 或 Event Param） | 走 cardinality 限制，但枚举值少，无影响 |

**核心原则**：客户端只采、不分桶；分桶在 GA4 Explorations / BigQuery 端定义。

### 6.2 User Properties（启动时一次性 set）

| Key | Type | 来源 | 用途 |
|---|---|---|---|
| `app_version` | string | `IAppService.AppInfo.CoreVersion` | 需求 1（版本分布） |
| `release_channel` | string | 解析 `app_version` 的 semver 预发布后缀；值: `stable` / `beta` / `dev` | 需求 1 子维度（区分预发布与正式版） |
| `os` | string | `RuntimeInformation.OSDescription`，归一化为 `windows`/`macos`/`linux` | 平台分布 |
| `locale` | string | UI 设置 | 语言分布 |
| `enabled_enhancers` | string (csv) | `EnhancerService` 启用列表，按 ID 字典序排序后逗号拼接 | 需求 5 |
| `ai_enabled` | bool | `AiOptions.Enabled` | 需求 5 |
| `has_media_library` | bool | `mediaLibraryCount > 0` | 反向指标"什么都没建"用 |

> **不上报** `enabled_enhancer_count` 这种派生字段 — 在 GA4 端用 `enabled_enhancers` 计算。

#### `release_channel` 的归类规则

由后端从 `CoreVersion` 解析（在 `TelemetrySnapshotService` 内）：

| 版本号示例 | `release_channel` |
|---|---|
| `1.4.2` | `stable` |
| `1.4.2-beta.3` / `1.4.2-rc.1` / 任何含 `-` 的 SemVer 预发布后缀 | `beta` |
| `0.0.0-dev` / 调试 build / `EnvironmentName == Development` | `dev` |

只在后端解析一次、放进 `TelemetrySnapshot.releaseChannel`，前端不重复实现。

#### `environment` 维度

环境维度（`production` / `development`）走 GA4 **event-scoped** 路径，区别于 user-scoped 的 `release_channel`：

```typescript
gtag('set', { environment: import.meta.env.MODE });
```

这样所有 event 自动带上 `environment` param，看板里可独立切片。Sentry 端用其原生 `environment` 配置（详见 §10.2）。

### 6.3 Events

| Event | Params | 用途 | 触发时机 |
|---|---|---|---|
| `app_snapshot` | `media_library_count` (number)<br>`resource_count` (number)<br>`cache_size_bytes` (number) | 需求 4（数值分布） | 启动时（带节流） |
| `page_view`（GA4 自动） | `page_path` | 需求 3（页面/路由） | 路由切换 |
| `feature_used` | `feature_id` (string) | 需求 3 进阶（关键操作） | 调用关键功能时 |
| `enhancer_triggered` | `enhancer_id` (string)<br>`success` (bool) | 需求 5 进阶（实际跑过 vs 仅启用） | enhancer 跑完时 |

### 6.4 GA4 后台需要的配置

- **Custom Metrics** 注册（数字 Event Param）：
  - `media_library_count`
  - `resource_count`
  - `cache_size_bytes`
- **Custom Dimensions** 注册（字符串 Event Param / User Property）：
  - `enabled_enhancers`（user-scoped）
  - `feature_id`（event-scoped）
  - `enhancer_id`（event-scoped）
- **IP 匿名化** 默认开启（GA4 强制）
- **数据保留期** 14 个月（默认即可）

### 6.5 节流策略

避免多次启动产生重复噪音 + 节省 GA4 配额：

```
1. 启动时收集 snapshot
2. 计算 hash(snapshot.json) (e.g. SHA256 前 16 字节)
3. 与 localStorage 中的 last_snapshot_hash 对比
4. 相同 → 跳过上报
5. 不同 → gtag('set','user_properties',...) + gtag('event','app_snapshot',...) + 写新 hash
```

`feature_used` / `enhancer_triggered` **不节流**，按实际触发上报。

---

## 7. Telemetry Snapshot 端点

### 7.1 API 契约

```
GET /api/app/telemetry-snapshot

Response 200:
{
  "appVersion": "1.4.2",
  "releaseChannel": "stable",
  "os": "windows",
  "locale": "zh-CN",
  "mediaLibraryCount": 15,
  "resourceCount": 12847,
  "cacheSizeBytes": 5368709120,
  "enabledEnhancers": ["DLsite","Bangumi","AI"],
  "aiEnabled": true,
  "hasMediaLibrary": true
}
```

要求：
- 全部为已知/可控数值，不含路径、文件名、资源标题等 PII
- 列表字段（`enabledEnhancers`）字典序排序，保证 hash 稳定
- 计算开销低，可在启动时同步调用
- 响应不缓存（每次都重新计算，因为状态会变）

### 7.2 实现位置

- **DTO**：`src/abstractions/Bakabase.Abstractions/Models/Domain/TelemetrySnapshot.cs`（新增）
- **Controller**：在 `src/apps/Bakabase.Service/Controllers/AppController.cs` 加方法
- **Service**：`src/apps/Bakabase.Service/Services/TelemetrySnapshotService.cs`（新增），通过 DI 调用：
  - `IMediaLibraryV2Service` → 库数量
  - `IResourceService` → 资源总数
  - `ICacheService` 或文件系统统计 → 缓存大小
  - `IEnhancerService` → 启用列表
  - `IBOptions<AiOptions>` → AI 开关
  - `IAppService` → 版本

---

## 8. AppInfo 端点扩展

`src/apps/Bakabase.Service/Controllers/AppController.cs:50-55` 的 `GetAppInfo` 已暴露版本，扩展返回前端初始化埋点所需的全部数据：

```typescript
type AppInfo = {
  coreVersion: string;            // 现有
  // 新增:
  deviceId: string;               // UUID v4
  ga4MeasurementId: string | null;     // null = 未配置/被禁用
  sentryDsn: string | null;            // null = 未配置/被禁用
  clarityProjectId: string | null;     // 从硬编码迁出，集中管理
  enableAnonymousDataTracking: boolean; // 从 AppOptions 镜像，避免前端再多一次请求
}
```

为什么把开关也放这：前端首屏初始化埋点时只需要一个端点，少一次往返。

---

## 9. GA4 集成

### 9.1 SDK

- 前端：[`gtag.js`](https://developers.google.com/analytics/devguides/collection/ga4) 通过 `<script>` 注入，或用 `react-ga4` npm 包包装
- 后端：不接（GA4 走 Measurement Protocol 也行，但本场景前端足够）

### 9.2 配置注入

```
appsettings.json
  Analytics:Ga4:MeasurementId = "G-XXXXXXXXXX"     ← 编译期或环境变量注入

→ 后端启动时读
→ /api/app/info 透出
→ 前端 BakabaseContextProvider 拿到后初始化
```

### 9.3 初始化伪代码

```typescript
// in BakabaseContextProvider, 替换当前的无条件 Clarity.init
async function initAnalytics(appInfo: AppInfo) {
  if (!appInfo.enableAnonymousDataTracking) return;

  // Clarity
  if (appInfo.clarityProjectId) {
    Clarity.init(appInfo.clarityProjectId);
    Clarity.identify(appInfo.deviceId);
    Clarity.setTag("appVersion", appInfo.coreVersion);
  }

  // GA4
  if (appInfo.ga4MeasurementId) {
    gtag('config', appInfo.ga4MeasurementId, {
      client_id: appInfo.deviceId,
      anonymize_ip: true,
    });
    // event-scoped, applies to every subsequent event
    gtag('set', { environment: import.meta.env.MODE });

    const snapshot = await BApi.app.getTelemetrySnapshot();
    const hash = sha256(JSON.stringify(snapshot)).slice(0, 16);
    if (hash !== localStorage.getItem('telemetry_last_hash')) {
      gtag('set', 'user_properties', {
        app_version: snapshot.appVersion,
        release_channel: snapshot.releaseChannel,
        os: snapshot.os,
        locale: snapshot.locale,
        enabled_enhancers: snapshot.enabledEnhancers.join(','),
        ai_enabled: snapshot.aiEnabled,
        has_media_library: snapshot.hasMediaLibrary,
      });
      gtag('event', 'app_snapshot', {
        media_library_count: snapshot.mediaLibraryCount,
        resource_count: snapshot.resourceCount,
        cache_size_bytes: snapshot.cacheSizeBytes,
      });
      localStorage.setItem('telemetry_last_hash', hash);
    }
  }

  // Sentry
  if (appInfo.sentryDsn) {
    Sentry.init({
      dsn: appInfo.sentryDsn,
      release: appInfo.coreVersion,
      environment: import.meta.env.MODE,
      tracesSampleRate: 0,
      replaysSessionSampleRate: 0,
      replaysOnErrorSampleRate: 0,
    });
    Sentry.setUser({ id: appInfo.deviceId });
  }
}
```

---

## 10. Sentry 集成

### 10.1 SDK

- 前端：`@sentry/react`
- 后端：`Sentry.AspNetCore` + `Sentry.Serilog`

### 10.2 配置注入

```
appsettings.json
  Sentry:Dsn = "https://xxx@yyy.ingest.sentry.io/zzz"
  Sentry:Environment = "production"

→ 后端 Program.cs UseSentry()
→ 后端通过 /api/app/info 把同一 DSN 透传给前端（也可分别配置不同 DSN）
```

### 10.3 采样配置

```typescript
// 前端
Sentry.init({
  dsn,
  release: appVersion,
  environment,
  tracesSampleRate: 0,           // 关 APM
  replaysSessionSampleRate: 0,   // 关 Replay (Clarity 已覆盖)
  replaysOnErrorSampleRate: 0,
  sampleRate: 1.0,
  beforeSend(event, hint) {
    // 过滤已知噪音: 网络中断、用户取消、AbortError 等
    if (isKnownNoise(hint.originalException)) return null;
    return event;
  },
});
```

```csharp
// 后端 Program.cs
builder.WebHost.UseSentry(o => {
    o.Dsn = configuration["Sentry:Dsn"];
    o.Release = appVersion;
    o.Environment = configuration["Sentry:Environment"];
    o.TracesSampleRate = 0;
    o.SetBeforeSend(@event => IsKnownNoise(@event) ? null : @event);
});
```

### 10.4 错误源

| 来源 | 改造点 |
|---|---|
| 前端 ErrorBoundary | `src/web/src/components/Error/components/Boundary/index.tsx:36`：把 `console.error` 改为 `Sentry.captureException(error, { contexts: { errorInfo } })` |
| 前端全局未捕获 | Provider 初始化时挂 `window.addEventListener('error', ...)` 与 `window.addEventListener('unhandledrejection', ...)`（Sentry SDK 会自动挂，但确认一下） |
| 前端 API 错误 | 在 axios / SDK 包装层（`src/web/src/sdk/...`）interceptor 里 `Sentry.captureException` |
| 后端 ASP.NET 中间件 | `Sentry.AspNetCore` 自动捕获 unhandled exception |
| 后端 ILogger | Serilog `WriteTo.Sentry()` sink |

### 10.5 Tags / Context

| Tag | 来源 | 用途 |
|---|---|---|
| `release` | `appVersion` | Sentry 自动按 release 聚合 → Release Health |
| `environment` | `production` / `dev` | 排除 dev 噪音 |
| `transaction` | 路由名 / Controller.Action | 错误集中在哪 |
| `user.id` | `deviceId` | Crash-free Users 计算 |
| `enhancer` | 出错时正在跑的 enhancer ID（如有） | 平台维度错误分布 |
| `os` | RuntimeInformation | 平台特有错误 |

### 10.6 Sentry 提供的视图

| 视图 | 满足 |
|---|---|
| Issues 列表 | 需求 2（报错聚合） |
| Release Health → Crash-free Users | 需求 2（错误率指标） |
| Issues filtered by `release` tag | 需求 1 与 2 交叉（哪个版本错最多） |
| Issues filtered by `enhancer` tag | 需求 2 与 5 交叉（哪个平台代码最差） |

### 10.7 配额管理

Sentry SaaS 免费档 **5k errors/月**。Bakabase 用户基数估算 ≥1000，极端崩溃日可能一天就用完。

策略：
1. **首版直接上 Sentry SaaS**（最快接入，零运维）
2. 用 `beforeSend` 过滤已知噪音（网络中断、AbortError、`ResizeObserver loop limit exceeded` 等）
3. 观察 1 个月的实际用量
4. 若稳定 ≥80% 配额 → 升级套餐 / 切 GlitchTip 自托管

超限时客户端行为（无需特殊处理）：
- Sentry 服务端返 429 + Retry-After
- SDK 启用本地 rate limiter，后续事件丢弃，**不重试不堆积**
- 不阻塞主线程，用户无感知
- 副作用：超限期间错误数据丢失

---

## 11. 数据流时序

```
启动 (Cold Start):

  [后端]
    1. 读 device-id 文件 / SQLite
    2. 不存在 → 生成 UUID v4 → 持久化
    3. 启用 Sentry SDK (UseSentry, 中间件)

  [前端 BakabaseContextProvider 首屏]
    4. GET /api/app/info → 拿 deviceId / ga4MeasurementId / sentryDsn / clarityProjectId / enableAnonymousDataTracking
    5. if (enableAnonymousDataTracking)
         a. Clarity.init + Clarity.identify(deviceId) + Clarity.setTag("appVersion",...)
         b. Sentry.init({ dsn, release: appVersion }) + Sentry.setUser({ id: deviceId })
         c. gtag('config', ga4MeasurementId, { client_id: deviceId })
         d. GET /api/app/telemetry-snapshot
         e. compute hash, compare with localStorage.telemetry_last_hash
         f. if changed:
              gtag('set', 'user_properties', {...})
              gtag('event', 'app_snapshot', {...})
              localStorage.telemetry_last_hash = newHash

运行时:

  [路由切换] gtag 自动 page_view
  [关键功能调用] gtag('event', 'feature_used', { feature_id })
  [enhancer 触发] gtag('event', 'enhancer_triggered', { enhancer_id, success })
  [前端报错] ErrorBoundary / window.onerror → Sentry.captureException
  [后端报错] ASP.NET middleware → Sentry 自动上报
                ILogger.LogError → Serilog Sentry sink

关闭:
  Sentry / GA4 SDK 自带 beforeunload flush，无需额外处理
```

---

## 12. 文件改动清单

### 12.1 后端

| 文件 | 改动 |
|---|---|
| `src/abstractions/Bakabase.Abstractions/Models/Domain/AppInfo.cs` | 加字段 `DeviceId`、`Ga4MeasurementId`、`SentryDsn`、`ClarityProjectId`、`EnableAnonymousDataTracking` |
| `src/abstractions/Bakabase.Abstractions/Models/Domain/TelemetrySnapshot.cs` | **新增** DTO |
| `src/apps/Bakabase.Service/Services/TelemetrySnapshotService.cs` | **新增**，组装 snapshot |
| `src/apps/Bakabase.Service/Services/DeviceIdService.cs` | **新增**，UUID v4 生成 + 持久化 |
| `src/apps/Bakabase.Service/Controllers/AppController.cs` | `GetAppInfo` 扩展返回新字段；新增 `GetTelemetrySnapshot` 端点 |
| `src/apps/Bakabase.Service/appsettings.json` | 新增 `Analytics:Ga4:MeasurementId`、`Analytics:Clarity:ProjectId`、`Sentry:Dsn`、`Sentry:Environment` |
| `src/apps/Bakabase.Service/Program.cs` | `UseSentry`；注册 `IDeviceIdService`、`ITelemetrySnapshotService` |
| `BakabaseDbContext.cs`（如选 SQLite 存 deviceId） | 新增 `app_meta` 表 + EF migration |
| `*.csproj` | NuGet：`Sentry.AspNetCore`、`Sentry.Serilog` |

### 12.2 前端

| 文件 | 改动 |
|---|---|
| `src/web/src/components/ContextProvider/BakabaseContextProvider.tsx:197` | 把无条件 Clarity.init 包进 `if (enableAnonymousDataTracking)` 分支；统一封装为 `initAnalytics(appInfo)` |
| `src/web/src/components/Error/components/Boundary/index.tsx:36` | 把 `console.error` 改为 `Sentry.captureException` |
| `src/web/src/pages/configuration/components/Others/index.tsx:181-196` | 文案更新（明示三个服务）；新增"重置匿名 ID"按钮 |
| `src/web/src/sdk/Api.ts` 等 SDK 文件 | `yarn gen-sdk` 重新生成 |
| `src/web/src/services/Analytics.ts` | **新增**，封装 `trackFeatureUsed(featureId)`、`trackEnhancerTriggered(id, success)` |
| `src/web/package.json` | 加依赖：`@sentry/react`（gtag 用 `<script>` 注入即可，不必加 npm 包） |

### 12.3 配置 / CI

| 文件 | 改动 |
|---|---|
| `.github/workflows/*` | 编译时通过 GitHub Actions secrets 注入 GA4 / Sentry / Clarity 凭证到 `appsettings.json` 或环境变量 |

---

## 13. 实施分阶段

| Phase | 内容 | 工作量 | 依赖 |
|---|---|---|---|
| **P0** | 修 `enableAnonymousDataTracking` 不生效的 bug | ~1h | — |
| **P1** | 后端 DeviceIdService + 持久化 + AppInfo 暴露 | 0.5d | — |
| **P2** | TelemetrySnapshotService + `/api/app/telemetry-snapshot` 端点 | 0.5d | P1 |
| **P3** | 前端 GA4 接入（gtag 引入 + 启动时 user property + app_snapshot event + 节流） | 0.5d | P1, P2 |
| **P4** | 关键 events 埋点（`feature_used`, `enhancer_triggered`） | 0.5–1d（取决于埋多细） | P3 |
| **P5** | Sentry 前端（ErrorBoundary 改造 + 全局监听 + release tag） | 0.5d | P1 |
| **P6** | Sentry 后端（`Sentry.AspNetCore` + Serilog sink + middleware） | 0.5d | — |
| **P7** | 配置页 UI（隐私文案 + 重置 ID 按钮） | 0.5d | P1 |
| **P8** | GA4 后台配置（注册 Custom Metric/Dimension + 建好看板模板） | 0.5d | P3 |
| **P9** | 1 个月观察期：Sentry 配额 / GA4 数据质量 | — | 全部上线 |

**关键路径**：P0 → P1 → P2 → P3。其余阶段可在任何顺序并行。

总工作量估计：**4–6 人日**（不含观察期）。

---

## 14. 安全与防滥用

### 14.1 凭证类型

| 凭证 | 是否能进客户端 | 原因 |
|---|---|---|
| GA4 MeasurementId | ✅ 可以 | 设计上是公开的（类似 Clarity ProjectId） |
| Sentry DSN | ✅ 可以 | 设计上是 write-only token |
| Clarity ProjectId | ✅ 可以 | 同上 |
| AccessKey / Secret（任何云厂商） | ❌ **绝不** | 主账号或子账号都不行，反编译即裸奔 |
| RAM 子账号 + 严格策略 | ❌ 也不行 | 即使权限收窄，仍可被薅免费额度 / 塞脏数据 |

### 14.2 桌面应用的特殊限制

- **没有"自己的后端"做代理**（Bakabase 后端就跑在用户机器上）
- **referrer 白名单可能因 localhost 失效** — 需在接入时实测（GA4 / Sentry 都需测）
- **接受被反编译拿到 token 的风险** — 任何客户端 SDK 都有此问题，靠服务端限流 + 异常字段过滤防护

### 14.3 防滥用措施

1. 服务端凭证（如有）只放后端，编译时注入
2. 前端只通过 `/api/app/info` 拿 token，避免 git 仓库中出现明文 DSN（除非接受默认值给开发者用）
3. 异常字段服务端清洗（GA4 / Sentry 都自带）
4. 多端独立 token：未来如有 web 版，单独 PID/DSN，便于定向止损

---

## 15. 隐私 / 合规

### 15.1 收集的数据清单

✅ 收集：
- 应用版本、OS、locale
- 媒体库 / 资源数（数字）
- 缓存大小（字节）
- 启用的 enhancer ID 列表（来自固定枚举）
- AI 是否启用
- 路由 path（`/resource`, `/dlsite-works` 等）
- 错误 stack trace（含代码文件名、行号，但**不含用户文件路径**）
- 匿名 deviceId（UUID v4，与现实身份无关联）

❌ 不收集：
- 用户名 / 邮箱 / 任何账号信息
- 文件路径、文件名
- 资源标题、内容
- 任何媒体库具体内容
- IP 地址（GA4 自动匿名化；Sentry 默认不存储）

### 15.2 用户控制权

- 单一开关 `enableAnonymousDataTracking` 默认 ON
- 关闭开关 → 不发任何数据（保留 deviceId 文件，重新开启即可恢复同一身份）

### 15.3 文案要求

配置页 `Others/index.tsx` 该项的描述需包含（中英双语）：

> 启用后将上报匿名诊断数据，用于改进产品质量。使用以下服务：
> - Microsoft Clarity（会话录像 / 热图，用于排查 UX 问题）
> - Google Analytics 4（功能使用统计 / 版本分布）
> - Sentry（错误诊断 / 报错聚合）
>
> 不会收集您的文件路径、文件名、媒体库内容或任何账号信息。
> 您可以随时关闭。

---

## 16. 决策记录

| # | 问题 | 决定 | 备注 |
|---|---|---|---|
| 1 | GA4 Property 数量 | **一个 Property** + 两个区分维度 | `environment`（event-scoped, `production` / `development`）+ `release_channel`（user-scoped, `stable` / `beta` / `dev`）。后者由后端从版本号 semver 后缀解析，详见 §6.2 |
| 2 | Sentry 项目数量 | **前后端各一个** | stack trace 来源不同，分队列管理更清晰 |
| 3 | 启动上报频率 | **snapshot hash 变化时才发**；`feature_used` 实时不节流 | 见 §6.5 节流策略 |
| 4 | 离线事件处理 | **丢弃** | 走 GA4 / Sentry SDK 自带本地缓冲，不额外做 |
| 5 | `enabled_enhancers` 取值口径 | **已配置启用**（user property） + `enhancer_triggered` event 反映实际触发 | 启用 ≠ 触发，两个口径都有价值 |
| 6 | `feature_used` 埋点范围 | **首版仅页面级**（按路由） | 后续按需加按钮级 |
| 7 | 凭证注入方式 | `appsettings.json` 写默认 + ENV 覆盖 | 二次开发者可指向自己的看板 |
| 8 | 默认凭证是否提交 git | **提交** | 让所有用户上报到同一聚合视图 — 这就是数据价值所在 |
| 9 | 隐私开关分层 | **首版单一开关** | 分层（错误诊断 vs 行为分析）属 future enhancement |
| 10 | DeviceId 存储位置 | **SQLite `app_meta` 表** | 与现有数据库一致，避免散文件；需新增 EF migration |

---

## 17. 验收标准

P0–P9 全部上线后，应能：

1. ✅ 在 Clarity 看到带 `appVersion` tag 的会话录像
2. ✅ 在 GA4 Reports → User attributes 看到 `app_version` 维度的用户数饼图（**需求 1**）
3. ✅ 在 Sentry Issues 看到聚类后的前后端错误，按 release 切片（**需求 2**）
4. ✅ 在 Sentry Releases 看到 Crash-free Users %（**需求 2**）
5. ✅ 在 GA4 Reports 看到各路由的 page_view 排名（**需求 3**）
6. ✅ 在 GA4 Explorations 用 numeric histogram 看到 `media_library_count` / `resource_count` 的分布（**需求 4**）
7. ✅ 在 GA4 Audiences 创建 "用户启用了 DLsite" 受众，看到占比（**需求 5**）
8. ✅ 在 GA4 Audiences 创建 "30 天内无 feature_used 事件" 受众（**反向指标**）
9. ✅ 关闭 `enableAnonymousDataTracking` 后，三个工具都不再发出请求（DevTools Network 验证）
10. ✅ 后端 `_logger.LogError(...)` 出现在 Sentry Issues（验证 MSEL → Sentry 桥接）

---

## 18. 后续可能的演进

| 方向 | 触发条件 |
|---|---|
| 切 GlitchTip 自托管 | Sentry SaaS 配额持续超限 |
| 接入 PostHog（自托管或 SaaS） | GA4 不能满足复杂 funnel/retention 分析 |
| 拆分隐私开关（错误 vs 行为）| 用户反馈隐私顾虑 |
| 接入 Sentry Replay | Clarity 某天关闭免费档 |
| 后端 OpenTelemetry → 自管 Jaeger / Prometheus | 需要更深的性能洞察 |

---

## 19. 参考

- [Microsoft Clarity Docs](https://learn.microsoft.com/en-us/clarity/)
- [GA4 Custom Definitions](https://support.google.com/analytics/answer/10075209)
- [GA4 User Properties](https://support.google.com/analytics/answer/9268042)
- [Sentry SDK Configuration (React)](https://docs.sentry.io/platforms/javascript/guides/react/)
- [Sentry .NET (ASP.NET Core)](https://docs.sentry.io/platforms/dotnet/guides/aspnetcore/)
- [GlitchTip Sentry SDK Compatibility](https://glitchtip.com/documentation/install)
