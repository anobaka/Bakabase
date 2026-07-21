# 04 · 身份判定（Resolution）

> 层级：决策能力 ｜ 依赖：[02 条目提取器](02-extractors.md)（ParsedClues）、[03 Provider](03-providers.md)、Alias / SpecialText / Comparison（[复用总表](../foundations.md#现有资产复用总表)） ｜ 被依赖：[05 字段富集](05-enrichment.md)、[08 编排](08-orchestration.md)
>
> 职责：`ParsedClues → MatchDecision`（[WorkIdentity 集合](../foundations.md#核心概念) + [置信档 Band](../foundations.md#核心概念) + 可解释依据）。**只定身份，不填字段**——字段富集在 [05](05-enrichment.md)。

## 模型：候选生成器 + 固定三阶段 Runner

身份判定的本质是一个「发散假设 → 归拢 → 裁决」的过程，其中**只有"发散假设"存在值得用户插拔的自由度**（用什么线索、查什么源、用什么方法产生候选）；归拢与裁决的算法是唯一的，用户能调的只是参数。因此：

- 组件只有**一种**概念：**候选生成器 `ICandidateGenerator`**——从线索产出候选。用户自由组合其实例。
- **Runner 是内核**（不是组件），固定三阶段，行为由 Pipeline 配置驱动：

```
ParsedClues ──（Override 前置：人工裁定过的条目直接采用）──▶
  ① Generate     依序运行生成器实例；出现确定性候选可提前终止（早停，可配）
  ② Consolidate  候选池归拢：等价分组 → 合并（内核固定算法，配置驱动）
  ③ Decide       top1 + 歧义差 + banding 映射 → Resolved(band) 或 NeedsReview
```

> 演进备注：早期草稿把判定逻辑做成"责任链节点"，并分 Certainty/Generate/Transform 三种角色、把归拢（merge）做成可编排节点——**已废弃**。原因：确定性不是节点的属性而是**候选的属性**（见 Basis）；归拢必然发生在全部生成之后、算法唯一，没有编排自由度，做成节点只会让链变得不可理解。

## 候选契约（MatchCandidate）

所有生成器产出、Runner 各阶段消费的统一结构。**薄字段清单是固定契约**，判据：身份判定所需（打分、等价判定、复核展示）才进契约；权威数据一律留在 `RawPayloads`，字段真相由 [05](05-enrichment.md) 决定。

```csharp
public sealed record MatchCandidate
{
    public required WorkIdentitySet Identity { get; init; } // (source, key) 集合；生成时通常单元素
    public required decimal Score { get; init; }            // 与线索的匹配程度 0–100
    public required CandidateBasis Basis { get; init; }     // 这个候选"怎么来的"，见下
    public LocalizedText Titles { get; init; }              // 语言 → 标题
    public int? Year { get; init; }                         // 可空：Provider 没给就是 null
    public string? WorkType { get; init; }
    public ExternalIdLink[] Links { get; init; }            // payload 声明的外站 id
    public MatchExplanation Explanation { get; init; }      // 复核页展示的判定依据
    public IReadOnlyDictionary<string, string> RawPayloads { get; init; } // 按源保留原始响应，不融合
}

public enum CandidateBasis
{
    KeyLookup = 1,      // 键查得：精确码/包名/MBID 直查 → 确定性
    UserTable = 2,      // 用户映射表命中 → 确定性
    Fingerprint = 3,    // 指纹命中 → 高置信
    ClueDirect = 4,     // 线索直取（不查外部源）
    TextSimilarity = 5, // 文本相似度搜索 → 概率性
    Inference = 6,      // AI 推断（后期）→ 概率性
}
```

映射责任：Provider 适配器（[03](03-providers.md)）负责把各站 payload 映射进薄字段；缺失即 null，所有消费方按可空处理——"归拢怎么知道有没有 year"的答案就是读 `candidate.Year`，null 即没有（启发式连接会因此退化，见下）。

## ① Generate：候选生成器

```csharp
public interface ICandidateGenerator
{
    Task<IReadOnlyList<MatchCandidate>> GenerateAsync(ResolutionContext ctx, CancellationToken ct);
}
// AbstractCandidateGenerator<TConfig>：configJson → TConfig，镜像 AbstractEnhancer 的类型化配置模式
```

实现类是**无状态单例**（DI 枚举注册，同 Enhancer 模式）；Pipeline 里的生成器链是**数据行**（kind + configJson，存于 [OrganizePipeline](08-orchestration.md#数据表)）；同一 kind 可多实例、各带配置（如两个 `code-lookup` 分别处理 RJ 码与私有码、限定不同 Provider）。

| kind | 类名 | Basis | 配置 |
|---|---|---|---|
| `resolver.code-lookup` | `CodeLookupCandidateGenerator` | KeyLookup | CodeSlots（哪些线索槽算码）、ProviderFilter |
| `resolver.fuzzy-search` | `FuzzySearchCandidateGenerator` | TextSimilarity | SearchStrategy（FirstHit/FanOutUnion）、MinScore、TitleSlots、UseAliasExpansion、Year/Type 权重 |
| `resolver.clue-direct` | `ClueDirectCandidateGenerator` | ClueDirect | 取哪些线索槽（通用音频"以标签定名"即它的一个实例） |
| `resolver.lookup-table` | `LookupTableCandidateGenerator` | UserTable | 用户映射表引用（线索值 → 身份/标题） |
| `resolver.fingerprint`（后期） | `FingerprintCandidateGenerator` | Fingerprint | 指纹源、命中阈值 |
| `resolver.ai`（后期） | `AiCandidateGenerator` | Inference | 模型 / 提示词模板 / 置信要求 |

生成器排序即优先级：**先便宜先准**（确定性来源在前）。**早停**是 Runner 的配置行为：出现 KeyLookup/UserTable 候选即跳过后续生成器——"精确码短路"由此实现，不再是特殊节点。

**kind 清单不钉死**：内核只认识 `ICandidateGenerator` 契约；实现类注册即出现新 kind（同新增 Enhancer 的贡献模式）。终端用户不能凭空造 kind（刻意不做脚本引擎），但 `lookup-table` / `clue-direct` 的行为本身由用户数据定义，覆盖绝大多数"自有逻辑"。

## ② Consolidate：候选归拢（内核固定阶段，不是组件）

跨 Provider 的同一作品会以多个候选出现（Bangumi 96 分、TMDB 95 分）。不归拢的后果：top1/top2 差 1 → **假歧义**被迫进复核；且定案只带单库身份，多源关联永久丢失。归拢没有值得用户重排/替换的自由度，因此固化在内核、仅暴露参数：

1. **等价分组**（并查集；两类连接在算法上本质不同）：
   - **确定性连接**——身份键或外站链接相等：`A.Identity ∩ B.Identity ≠ ∅`，或 A 的 `Links` 指向 B 的 (source, key)。键相等是 join，不存在"判错"（除非源数据本身错）。**永远启用，无需配置。**
   - **启发式连接**——SpecialText 归一化后标题相等 + 年份差 ≤ 容差（默认 ±1）+ 类型兼容。**显式开关 + 参数**（模糊场景默认开）；`Year == null` 时年份条件不可用 → 该连接自动收紧（仅标题相等不足以并组）。
2. **组内合并**，每组产出一个候选：
   - `Identity` 取并集——多源身份在此固化，供 [05](05-enrichment.md) 并行富集、[07](07-execution.md) 逐源写 `ResourceSourceLink`；
   - `Score = max(成员)`（可选小额"佐证加分"，默认关——两库共现是佐证，不代表标题更匹配；绝不能相加，否则热门作品靠收录量碾压更匹配的冷门候选）；
   - 薄字段浅并：`Titles` 按语言键并、冲突取高分方；`Year`/`WorkType` 取高分方。**这些值仅供复核展示与重打分，不是权威值，不会落库**——写进属性的最终值由 [05](05-enrichment.md) 按 per-field 策略从各源 payload 选出；
   - **冲突打旗**：确定性连接成立但薄字段冲突（如两库 year 不同）→ MergeFlag，复核页展示双值；可配"带旗降一档"（High→Medium 进复核），防御外链本身指错的罕见情况；
   - `RawPayloads` 按源原样保留，绝不融合。
3. 启发式连接判不上的候选保持独立——同名的原作与重制版靠这个不被错并。

## ③ Decide：裁决

- top1 分数 + 与 top2 的歧义差 + banding 映射，全部是 Pipeline 配置：`{ high, medium, ambiguityGap, mediumAction(自动过/抽样/全复核) }`。
- **Basis 感知**：KeyLookup / UserTable 候选 → High 直通；TextSimilarity / Inference 走分数映射。
- 无候选或歧义 → NeedsReview，携带候选池与 `Explanation` 供复核页展示。

## 走查示例

```
生成器链：code-lookup → fuzzy-search ｜ consolidate{启发式:开} ｜ banding{high:90, ambiguityGap:5}

输入 1：[CANDYVOICE] RJ01017217 耳かきボイス.zip
  ① code-lookup：code 线索 → DLsite 直查命中 → [{dlsite:RJ01017217, Basis=KeyLookup}]
     → 早停，fuzzy-search 不执行
  ② consolidate：单候选，无事可做
  ③ decide：Basis=KeyLookup → Resolved(High)。不进复核。

输入 2：[LoliHouse] 進撃の巨人 S03
  ① code-lookup：无码线索 → 0 候选；fuzzy-search：池 = [A{bangumi, 96}, B{tmdb, 95}]
  ② consolidate：A/B 经外站 id 互指（确定性连接）→ 池 = [AB{bangumi+tmdb, 96}]
  ③ decide：96 ≥ high，次高候选 60、差 36 > ambiguityGap → Resolved(High)

  反事实：若跳过 consolidate → 96 vs 95 差 1 < ambiguityGap → 假歧义进复核，
  且即便人工选了 A，身份也只剩 {bangumi}——这两点就是归拢阶段存在的全部理由。
```

## Override（人工裁定，Runner 前置）

```
OrganizeOverride   Fingerprint(条目指纹，键), Kind(ForcedIdentity/ForcedTitle/ForcedPipeline/Skip),
                   PayloadJson, CreatedAt        // 跨 Job 生效 → 越跑越少人工
```

## 用户如何自定义

三层阶梯，前两层零代码：

1. **组链**：fork Pipeline 后对生成器列表增删/重排/调参——AV 库只留 `code-lookup`（宁复核不猜）、动画库调大 `ambiguityGap`、私有码库加一个限定自有 Provider 的 `code-lookup` 实例。
2. **数据驱动生成器**：`lookup-table`（本地映射表：私有编号、自建元数据）、`clue-direct`（不联网，以标签/文件名直接定名）——行为即用户数据。
3. **写代码**：实现 `AbstractCandidateGenerator<TConfig>` + 注册即出现在调色板，同 Enhancer 贡献模式。

## 完成后获得的能力

- 纯逻辑层：线索集 + Provider（可 mock）→ 可解释的身份判定；打分、归拢、歧义边界全量单测。
- 配合 01–03 的调试页端到端验证"这个文件名能不能认出来、为什么认错"，无需任何文件操作。
- Override 落地后，人工纠错一次、永久生效。

## 开放问题

- 佐证加分是否默认开、幅度多少（拿真实库标定）。
- 启发式连接的年份容差 / 标题归一强度的各场景默认值。
- `resolver.ai` 生成器的引入时机与置信要求。
