# 04 · 身份判定（Resolution）

> 层级：决策能力 ｜ 依赖：[02 条目提取器](02-extractors.md)（ParsedClues）、[03 Provider](03-providers.md)、Alias / SpecialText / Comparison（[复用总表](../foundations.md#现有资产复用总表)） ｜ 被依赖：[05 字段富集](05-enrichment.md)、[08 编排](08-orchestration.md)
>
> 职责：`ParsedClues → MatchDecision`（[WorkIdentity 集合](../foundations.md#核心概念) + [置信档 Band](../foundations.md#核心概念) + 可解释打分）。**只定身份，不填字段**——字段富集在 [05](05-enrichment.md)。

## Resolver 责任链

判定逻辑组织为策略链；每环是一个组件（`resolver.*`，见 [foundations · 组件模型](../foundations.md#组件模型所有槽位共享)）：

```csharp
public interface IResolverPolicy
{
    Task<ResolverOutcome> ResolveAsync(ResolverContext ctx, CancellationToken ct);
}
// ResolverOutcome 三选一：
//   Final(identity, band, explanation)   定案，链终止
//   Augment(enrichedContext)             补充了上下文，继续下一环
//   Pass                                 本环无话可说，交给下一环
```

链前置：先查 **Override**（按条目指纹）——人工裁定过的直接采用。这是收敛闭环的另一半。

### 内置环

| 环 | 行为 |
|---|---|
| `resolver.exact-code` | 线索含精确码 → 对 `SupportsExactCode` 的 Provider `GetByCodeAsync` → 命中即 `Final(High)`；查无此码（下架等）→ `Pass` 而非失败 |
| `resolver.fingerprint`（后期） | 声学指纹 → 高置信命中 |
| `resolver.fuzzy` | 清洗（SpecialText）→ 别名展开（Alias）→ 按**检索策略**搜 Provider → 每候选 × 各语言标题取最高 Dice 相似度 → 年份/类型一致性加权 |
| `resolver.merge` | 多 Provider 高分候选经外站 id 互指（`Capabilities.ExternalIdKinds`）或标题+年份强一致判定为同一作品 → 合并为身份集合；判定失败（同名不同作）→ 按配置保最高分单身份或降档复核 |

检索策略（Pipeline 可配）：`FirstHit`（串行短路，省配额，精确码场景默认）/ `FanOutUnion`（并行扇出、候选取并集，模糊场景默认，提高召回）。

### 收口规则

- 分数→档位映射（banding）是 **Pipeline 配置**，不硬编码在环内。High 自动定案；Medium 可配置（自动过 / 抽样复核 / 全复核）；整条链无人 Final → NeedsReview。
- 歧义保护：最高分与次高分差距小于歧义阈值 → 降档。

## 走查示例

```
# 链 exact-code → fuzzy ：[CANDYVOICE] RJ01017217 耳かきボイス.zip
exact-code 环：code=RJ01017217 → DLsite 命中 → Final(High, 100, ExactCode)，不进复核
若该码已下架(404) → Pass → fuzzy 环：清洗出「耳かきボイス」→ 最高 82 vs 次高 79
  → 差距小于歧义阈值 → Medium →（该 Pipeline 配置 Medium 全复核）→ NeedsReview + 前 5 候选与打分解释

# 链 fuzzy → merge ：[LoliHouse] 進撃の巨人 S03
fuzzy 环：Bangumi 候选 96、TMDB 候选 95 —— 两个身份 → Augment
merge 环：外站 id / 标题+年份强一致 → 同一作品 → Final(High, {bangumi:X, tmdb:Y})
```

## 数据表

```
OrganizeOverride   Fingerprint(条目指纹，键), Kind(ForcedIdentity/ForcedTitle/ForcedPipeline/Skip),
                   PayloadJson, CreatedAt        // 跨 Job 生效 → 越跑越少人工
```

## 完成后获得的能力

- 纯逻辑层：给定线索集 + Provider（可 mock）→ 可解释的身份判定，全量单测覆盖打分与歧义边界。
- 配合 01–03 的调试页即可端到端验证"这个文件名能不能认出来、为什么认错"——不需要任何文件操作或 UI 编排。
- Override 表落地后，人工纠错一次、永久生效。

## 开放问题

- 各场景歧义阈值与 banding 默认值（拿真实库跑分布后定）。
- 低置信条目的 AI 辅助消歧（AI 模块已有基建）放在哪一环、何时引入。
