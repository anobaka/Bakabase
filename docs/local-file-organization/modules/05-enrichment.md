# 05 · 字段富集（Enrichment）

> 层级：决策能力 ｜ 依赖：[03 Provider](03-providers.md)（`EnrichAsync`）、[04 身份判定](04-resolution.md)（身份集合）、[foundations 类型系统](../foundations.md#类型系统)（FieldBag/provenance） ｜ 被依赖：[06 布局](06-layout.md)（模板取值）、[07 执行](07-execution.md)（属性写入）
>
> 职责：身份定案后，从多个 Provider 并行取详情，把字段 **union** 成一个带来源追溯的 [FieldBag](../foundations.md#类型系统) + 规范字段集。**[04](04-resolution.md) 定身份，本模块填字段**——边界在 Final 之后、Plan 之前。

## 并行 union

- 对身份集合内每个 (source, key) 并行调用 `EnrichAsync`；策略可配：`PrimaryOnly`（只取主源）/ `UnionAll`（全并）。
- 富集默认只对 High 定案执行（配额保护）；Medium 进复核的条目在人工确认后补富集。

## per-field 合并策略

同一字段多库给值时：

| 情形 | 默认策略 |
|---|---|
| 标量字段冲突（发行日期不同） | fill-if-empty：按 Provider 顺序，先到先得，后来者只填空 |
| 列表字段（tags/genre） | 集合并（去重 union） |
| 多语言字段（标题/简介） | 按语言键合并（AniList 补日文与产地、Bangumi 补中文名） |
| 用户覆盖 | 任意字段可 per-field 指定 Provider 优先序，覆盖默认 |

**provenance 是硬要求**：FieldBag 每个值记录来源 Provider（[类型系统](../foundations.md#类型系统)的 `Set(field, value, providerName)`）。用途：复核 UI 展示"这个值哪来的"；各 Provider 原始 payload 分开缓存后，**换合并策略重算零网络请求**。

## 与资源属性的衔接

富集结果本身不写属性——写入发生在 [07 执行](07-execution.md)的落库阶段：字段按映射（`SuggestedPropertyType` 缺省自动建属性）经 `PropertyValueFactory` / StandardValue 转换规则写入，复用 Enhancer 的转换链路，不另造。

## 完成后获得的能力

- 例：`進撃の巨人` 定案为 `{bangumi:X, tmdb:Y}` 后，富集产出：
  `title{ja:進撃の巨人, zh-Hans:进击的巨人(来源:Bangumi), en:Attack on Titan(来源:TMDB)}, origin=JP(来源:TMDB), tags=…(两库并集)`。
- 复核页可以按字段展示来源与被覆盖的备选值；用户把"发行日期以 Bangumi 为准"设为 per-field 覆盖后，一键重算、不发请求。
- [06 布局](06-layout.md)的模板占位符（`{title:lang(zh-Hans)}`、`{dlsite.circle}`）自此有了完整、类型化、可追溯的取值来源。

## 开放问题

- 冲突可视化的信息密度（全部展示 vs 仅冲突字段）。
- 富集结果的过期与刷新策略（沿用 `ResourceSourceLink.MetadataFetchedAt` 的时效语义，见 [07](07-execution.md)）。
