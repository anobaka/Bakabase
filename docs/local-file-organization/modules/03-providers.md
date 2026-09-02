# 03 · 元数据 Provider

> 层级：原子能力 ｜ 依赖：[foundations 组件模型/类型系统](../foundations.md)、ThirdParty 模块（[复用总表](../foundations.md#现有资产复用总表)） ｜ 被依赖：[04 身份判定](04-resolution.md)、[05 字段富集](05-enrichment.md)
>
> 职责：把外部元数据源适配成统一的三操作契约。适配器很薄——HTTP、限流、Cookie、代理全部由 ThirdParty 模块现有基建承担。

## 契约

```csharp
public interface IMetadataProviderAdapter
{
    string Name { get; }                        // "DLsite"，与 ResourceSource 枚举对齐
    ProviderCapabilities Capabilities { get; }  // 见下
    Task<IReadOnlyList<MatchCandidate>> SearchAsync(ProviderQuery q, CancellationToken ct); // 检索：关键词 → 候选
    Task<MatchCandidate?> GetByCodeAsync(string code, CancellationToken ct);                // 精确码 → 详情
    Task<MatchCandidate?> EnrichAsync(WorkIdentity identity, CancellationToken ct);         // 已知身份 → 详情（富集用）
}

public sealed record ProviderCapabilities(
    bool SupportsExactCode,          // RJ 码 / tt / appid / 番号…
    bool SupportsFingerprint,        // AcoustID 等（后期）
    string[] Languages,              // 返回标题覆盖的语言
    string[] ExternalIdKinds);       // payload 携带哪些外站 id（如 AniList 带 MAL id）→ 供身份对齐
```

`MatchCandidate`（契约定义见 [04](04-resolution.md#候选契约matchcandidate)）中，适配器负责填充：作品身份/多语言标题/外站 id 链接（canonical 部分），以及按组件 `Fields` 声明填进 `Features`（[FieldBag](../foundations.md#类型系统)）的域数据（year/workType/albumArtist…——candidate 上**没有**这些专属属性，全部走类型化字段袋）；原始 payload 按源保留。

## 与 ThirdParty 模块的关系

- 每个适配器内部就是现有客户端调用（如 DLsite 适配器 ≈ `DLsiteClient.ParseWorkDetailById`；Bangumi 适配器包一层 subject 搜索）。33 个客户端中按场景需要逐个适配，**不要求一次适配全部**。
- 新数据源（AniList / MusicBrainz / MangaDex …）按 ThirdParty 现有约定先写客户端，再包适配器。
- `ResourceSource` 枚举需扩全至与 Provider 名对齐（现有 PathMark/Steam/DLsite/ExHentai/Aigc 五值）。

## 配额与缓存

- 每 Provider 独立信号量限流（站点 Handler 限流之上再加 Job 级并发上限）。
- 响应缓存：按 (Provider, key) 缓存原始 payload（进程内 + 落条目 `MetadataJson`，见 [08 数据表](08-orchestration.md#数据表)）；同一作品跨条目复用不重复请求。

## 完成后获得的能力

- 统一接口查询任意已适配站点，例：
  - `dlsite.GetByCodeAsync("RJ01017217")` → 标题/社团/CV/发售日的结构化详情
  - `bangumi.SearchAsync("進撃の巨人")` → 候选列表（中日标题、年份、外站 id）
- 一个简单的 Provider 调试页（选 Provider + 输入关键词/码 → 看结构化结果）即可验证本模块，且顺手成为用户排查"为什么匹配不到"的工具。
- [04](04-resolution.md)/[05](05-enrichment.md) 的全部外部数据入口自此就绪。

## 开放问题

- 需要登录态/风控严格的站点（ExHentai 等）在批量整理下的请求预算策略。
- `ProviderQuery` 是否要携带域提示（搜"漫画"还是"动画"）——倾向于要，作为可选过滤。
