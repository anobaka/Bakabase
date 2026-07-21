# 08 · 编排：Pipeline / 状态机 / 宿主

> 层级：顶层编排 ｜ 依赖：下层全部（[01](01-text-processing.md)–[07](07-execution.md)）、BTask / Workflow / FileMover（[复用总表](../foundations.md#现有资产复用总表)） ｜ 被依赖：[09 合集](09-collections.md)
>
> 职责：把下层原子能力组合成可运行的整体。本模块**只做组合与调度**，不含任何一层的业务逻辑——这是"先底层原子能力、后顶层编排"划分的收口。

## Pipeline：组件组合的数据化配置

一条 Pipeline = 各槽位组件实例的组合（[组件模型](../foundations.md#组件模型所有槽位共享)）。**骨架固定、槽内自由**：阶段顺序不可重排，用户的自由是"每槽用哪些组件、什么顺序、什么参数"——非法组合从模型上无法表达，编辑器是「勾选 + 排序 + 调参」而非连线画布。

```
Pipeline = 作用域/Detector
         + 有序 Extractor 实例（02）
         + 有序 Provider 实例（03）
         + 候选生成器实例与判定配置（早停/consolidate/banding）（04）
         + 富集策略（05）
         + 默认规则预设绑定（06）
```

内置种子（只读、可 fork）覆盖主流场景；构成一览：

| 内置 Pipeline | Extractor 链 | Provider 链 | 候选生成器（有序）+ 判定配置 |
|---|---|---|---|
| 同人音声/DLsite | pattern(rj-code) → pattern(filename) | dlsite | code-lookup → fuzzy-search（Medium 全复核） |
| 通用音频 | embedded-tag → pattern(filename) | （空） | clue-direct(标签)（不联网定名；缺标签→复核） |
| 动画 | pattern(filename，字幕组词表) → nfo | bangumi → tmdb | fuzzy-search（FanOutUnion；启发式归拢开） |
| 漫画/本子 | comicinfo → pattern(gallery-token) → pattern(filename) | exhentai → bangumi | fuzzy-search |
| 音乐 | embedded-tag → pattern(filename) | musicbrainz | code-lookup(MBID) → fingerprint → fuzzy-search |
| 电影/剧集 | pattern(imdb-code) → pattern(sxxeyy) → pattern(filename) | tmdb | code-lookup → fuzzy-search |
| 游戏 | pattern(steam-appid) → pattern(filename) | steam | code-lookup → fuzzy-search |
| AV | pattern(av-code) | javbus → javdb → … | 仅 code-lookup（无码→复核） |

典型用例：非 RJ 规范的音声库 → 把目录作用域绑到「通用音频」，或 fork「同人音声/DLsite」摘掉 rj-code/dlsite 组件——零代码。

## 路由

显式作用域绑定（"这个目录用这条 Pipeline"）优先于 Detector 置信度竞争；多条得分接近（差 < 阈值）→ NeedsReview 而非硬选。Detector 组件很薄：`detector.extension`（扩展名组）为共同底座 + 各管线特征加分项。

## 条目状态机

```
Discovered → Routed → Extracted → Parsed → Resolved / NeedsReview → Planned → Applied
                                                                  ↘ Skipped / Failed（终态）
```

- 每次状态推进即落库（checkpoint）→ 断点续跑：重跑跳过已 Applied/Skipped，NeedsReview 保留待裁定，裁定（写 [Override](04-resolution.md#数据表)）后重入队。
- 干跑终点 Planned；Applied 只能由用户确认计划后的独立任务触发（[07](07-execution.md)）。

## 数据表

```
OrganizePipeline   Name, ForkedFromId?, ScopeJson, ComponentsJson(有序组件实例+配置),
                   ResolverConfigJson(生成器链/早停/consolidate/banding), FieldMappingJson, Enabled
OrganizeJob        RuleSetId, SourceRoots, Mode(DryRun/Apply), Stats, CreatedAt
OrganizeItem       JobId, Path, Fingerprint, State, PipelineId?,
                   CluesJson, MatchJson(候选+得分), MetadataJson(按 Provider 分开缓存),
                   PlannedPath?, Error?, Attempts
```

## 宿主与触发

- **BTask 承载**：`BTaskBuilder.Create($"Organize:{jobId}").ConflictsWith("MoveFiles", $"OrganizeRoot:{root}")…`——进度/暂停/取消（逐条目 `YieldAsync`）、SignalR 推送全部免费获得。
- **触发形态**：手动向导（选根目录 + 规则集 → 预览 → 确认）为第一形态；FileMover 定位为"进料口"（监听目录目标指向某规则集，移入暂存后自动触发干跑）；Workflow 注册触发器（"整理完成"）与活动（"运行某 Pipeline/规则集"）。
- **复核页**：NeedsReview 条目列表 + 候选对比（含 [05](05-enrichment.md) 的 provenance）+ 裁定动作（选定身份/改标题/换 Pipeline/跳过）→ 写 Override → 重入队。

## 与 Workflow 模块的关系

**复用模式，不复用引擎。** 组件注册表、descriptor 驱动的配置表单、运行统计展示借鉴或共用 Workflow 前端实现；但 Organizer 不跑在 WorkflowRunner 上，三个结构性错配：

1. 一次性流过 vs 跨轮状态机（NeedsReview 停车等人、Override 跨轮、断点续跑）；
2. 逐条独立 vs 聚合阶段（计划期撞车检测/去重/合集聚拢需要跨条目视角）；
3. 直通执行 vs 两阶段确认（干跑→确认→journal/回滚）。

分工：Workflow 管"什么时候整理、整理完干什么"；Organizer 管"怎么整理"。

## 完成后获得的能力

- 端到端：一个混放动画/音声/漫画的乱堆目录，一键干跑 → 预览目标树 → 确认执行 → 各归其所；断电续跑；复核裁定后重跑，人工量逐轮下降（Override 命中率可观测）。
- 内置 Pipeline 开箱即用；fork + 组件开关/排序/调参零代码适配非规范库。

## 开放问题

- fork 与内置种子的漂移管理（上游 diff 提示、组件级"重置为上游默认"、废弃组件兼容运行）。
- Job 级并发与单条目内 FanOutUnion 并发的预算分配。
