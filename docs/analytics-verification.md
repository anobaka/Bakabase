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

## 1. 凭证填写

编辑 `src/apps/Bakabase.Service/appsettings.json` 的 `Analytics` 段：

```json
"Analytics": {
  "Clarity": { "ProjectId": "r5xlbsu4fl" },           // 已存在
  "Ga4":     { "MeasurementId": "G-XXXXXXXXXX" },     // ← 填
  "Sentry":  {
    "FrontendDsn": "https://<key>@<id>.ingest.sentry.io/<proj>",  // ← 填（前端 project）
    "BackendDsn":  "https://<key>@<id>.ingest.sentry.io/<proj>"   // ← 填（后端 project）
  }
}
```

任何一个留空（`null` 或 `""`）→ 对应 SDK 自动跳过初始化，不报错。

**GA4 后台一次性配置**（`Admin → Custom definitions`）：

| 类型 | Name | Event/User param |
|---|---|---|
| Custom Metric (number) | `media_library_count` | event param on `app_snapshot` |
| Custom Metric (number) | `resource_count` | event param on `app_snapshot` |
| Custom Dimension (text) | `app_version` | user-scoped |
| Custom Dimension (text) | `release_channel` | user-scoped |
| Custom Dimension (text) | `os` | user-scoped |
| Custom Dimension (text) | `locale` | user-scoped |
| Custom Dimension (text) | `enabled_enhancers` | user-scoped |
| Custom Dimension (boolean) | `ai_enabled` | user-scoped |
| Custom Dimension (boolean) | `has_media_library` | user-scoped |
| Custom Dimension (text) | `feature_id` | event-scoped (on `feature_used`) |
| Custom Dimension (text) | `enhancer_id` | event-scoped (on `enhancer_triggered`) |
| Custom Dimension (text) | `environment` | event-scoped |

不注册不会报错，只是这些字段不会作为可分析维度出现在报表里。

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
| `GET /app/telemetry-snapshot` | 200（仅在 hash 变化时才会触发后续 GA4 上报） |
| `GET https://www.googletagmanager.com/gtag/js?id=G-...` | 200（GA4 SDK） |
| `POST https://*.ingest.sentry.io/...` | 200（Sentry 心跳）|
| `POST https://www.clarity.ms/collect` | 200（Clarity 心跳）|

DevTools → **Application** → **Local Storage** → 应当看到 `telemetry_last_hash` key（一次成功上报后写入）。

---

## 3. 五个需求逐项验证

### 需求 1：版本分布饼图

1. **GA4 → Reports → User → Tech → Tech details**
2. 把 dimension 切换为 `app_version`（已注册的 Custom Dimension）
3. 应当看到一个用户数饼图，按版本分片

**带 release_channel 切片：** 在 Explorations 里建一个表，行 = `app_version`，二级行 = `release_channel`，看 stable / beta / dev 各自的版本分布。

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

1. **GA4 → Reports → Realtime** （最快）或 **Engagement → Pages and screens**
2. 在前端切换若干路由：`/`、`/resource`、`/dlsite-works`、`/configuration` 等
3. Realtime 应当**实时**看到 `page_view` 事件，每个事件的 `page_path` 即对应路由
4. 一段时间后 Reports 里能看到各路由的 page_view 排名

**注意：** 不是用 GA4 的 "Page" dimension（那是从 URL 推断的），而是用我们自己传的 `page_path` 参数。建议在 Explorations 里建表 dimension = `page_path`。

### 需求 4：媒体库 / 资源数分布

1. **GA4 → Configure → Custom definitions** → 确认 `media_library_count` / `resource_count` 显示为 "Custom metric"（不是 Dimension）
2. **GA4 → Explorations → Free form**
   - Technique: Free form
   - Metrics: `media_library_count` (Average / P50 / P90 等聚合函数都可以)
   - Dimensions: 可以加 `app_version` 切片
3. 应当看到不同用户的库数量分布

**或 BigQuery export** （GA4 免费档也能用 BigQuery sandbox）→ SQL 任意切桶。

### 需求 5：外部平台使用情况

#### 5a. 已启用列表（cohort 占比）

1. **GA4 → Configure → Audiences → Create audience**
2. Condition: `User scoped → enabled_enhancers contains "DLsite"`
3. Save → Audience size 显示符合该条件的用户数 / 总用户数

可以为每个 enhancer 各建一个 audience，导出对比。

#### 5b. 反向指标（"什么都没启用"）

1. **GA4 → Audiences → Create audience**
2. Condition: `enabled_enhancers does not match regex .+`
3. 该 audience size = 启用列表为空字符串的用户数

#### 5c. 实际触发率（vs 仅启用）

`enhancer_triggered` event 当前**没有 call site**（helper 已存在但未在任何地方调用，由设计 §16 #5 决定先不上）。
要看实际触发率，需要后续在 enhancer 实际跑完的地方加一句 `trackEnhancerTriggered(id, success)`。

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
| Sentry Dashboard 空白 | DSN 错 / Sentry project 选错（前后端各一个，别填混了） |
| GA4 DebugView 看不到事件 | 浏览器没装 GA Debugger 扩展 / 启动开关在 GA4 控制台 DebugView 切换 |
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
