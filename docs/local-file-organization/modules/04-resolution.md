# 04 · 身份判定（Resolution）

> 层级：决策能力 ｜ 依赖：[02 条目提取器](02-extractors.md)（ParsedClues）、[03 Provider](03-providers.md)、Alias / SpecialText / Comparison（[复用总表](../foundations.md#现有资产复用总表)） ｜ 被依赖：[05 字段富集](05-enrichment.md)、[08 编排](08-orchestration.md)
>
> 职责：`ParsedClues → MatchDecision`（[WorkIdentity 集合](../foundations.md#核心概念) + [置信档 Band](../foundations.md#核心概念) + 可解释依据）。**只定身份，不填字段**——字段富集在 [05](05-enrichment.md)。

## 模型：候选生成器 + 固定三阶段 Runner

身份判定的本质是「发散假设 → 归拢 → 裁决」，其中**只有"发散假设"存在值得用户插拔的自由度**；归拢与裁决算法唯一、仅参数可调。因此组件只有一种概念——**候选生成器 `ICandidateGenerator`**；Runner 是内核（非组件），固定三阶段：

```
ParsedClues ──（Override 前置：人工裁定过的条目直接采用）──▶
  ① Generate     依序运行生成器实例；出现确定性候选可提前终止（早停，可配）
  ② Consolidate  候选池归拢（断言 > 键 join > 启发式；纯函数，可无损重放）
  ③ Decide       top1 + 歧义差 + banding 映射 → Resolved(band) 或 NeedsReview
```

> 演进备注：早期草稿的"责任链节点 + Certainty/Generate/Transform 三角色 + merge 节点"已废弃——确定性不是节点的属性而是**候选的属性**（Basis）；归拢没有编排自由度。

## 候选契约（MatchCandidate）

所有生成器产出、Runner 消费的统一结构。边界判据：**是不是"作品"这个概念本身必有的东西**——而不是"判定是否需要"（那个判据挡不住域字段蔓延；`year` 本身就是影视偏见，单曲/软件/图集没有年份这个身份概念）。

```csharp
public sealed record MatchCandidate
{
    // 作品概念本身：任何域的作品必有身份与称谓
    public required WorkIdentitySet Identity { get; init; }   // (source, key) 集合；生成时通常单元素
    public LocalizedText Titles { get; init; }                // 语言 → 标题
    public ExternalIdLink[] Links { get; init; }              // payload 声明的外站 id（等价 join 的输入）

    // Runner 簿记：与域无关
    public required decimal Score { get; init; }              // 与线索的匹配程度 0–100
    public required CandidateBasis Basis { get; init; }       // 候选"怎么来的"，见下
    public MatchExplanation Explanation { get; init; }        // 复核页展示的判定依据
    public IReadOnlyDictionary<string, string> RawPayloads { get; init; }  // 按源保留原始响应，不融合

    // 其余一切域数据（year/workType/albumArtist/volume…）：无专属属性，全部进类型化字段袋
    public FieldBag Features { get; init; }
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

- `Features` 就是 [foundations 类型系统](../foundations.md#类型系统)的 FieldBag：Provider 组件经 `FieldDefinition[]` 声明产出字段（"字段随组件走"），适配器负责把 payload 填进去（[03](03-providers.md)）。字段词汇按组件声明、按 key 隔离，不在中心 record 上累积属性。
- 判定逻辑对 year 之类的使用退化为**配置里的 feature 引用**（见 FeaturePredicate）——内核代码里没有任何域字段名。

## ① Generate：候选生成器

```csharp
public interface ICandidateGenerator
{
    Task<IReadOnlyList<MatchCandidate>> GenerateAsync(ResolutionContext ctx, CancellationToken ct);
}
// AbstractCandidateGenerator<TConfig>：configJson → TConfig，镜像 AbstractEnhancer 的类型化配置模式
```

实现类是**无状态单例**（DI 枚举注册）；Pipeline 里的生成器链是**数据行**（kind + configJson，存 [OrganizePipeline](08-orchestration.md#数据表)）；同一 kind 可多实例、各带配置。

| kind | 类名 | Basis | 配置 |
|---|---|---|---|
| `resolver.code-lookup` | `CodeLookupCandidateGenerator` | KeyLookup | CodeSlots、ProviderFilter、**CrossValidateWithTitle**（命中后与标题线索交叉检验，见误判防线） |
| `resolver.fuzzy-search` | `FuzzySearchCandidateGenerator` | TextSimilarity | SearchStrategy（FirstHit/FanOutUnion）、MinScore、TitleSlots、UseAliasExpansion、**FeatureWeights**（feature 一致性加权，与 FeaturePredicate 同词汇） |
| `resolver.clue-direct` | `ClueDirectCandidateGenerator` | ClueDirect | 取哪些线索槽（通用音频"以标签定名"即它的实例） |
| `resolver.lookup-table` | `LookupTableCandidateGenerator` | UserTable | 用户映射表引用（线索值 → 身份/标题） |
| `resolver.fingerprint`（后期） | `FingerprintCandidateGenerator` | Fingerprint | 指纹源、命中阈值 |
| `resolver.ai`（后期） | `AiCandidateGenerator` | Inference | 模型 / 提示词模板 / 置信要求 |

排序即优先级（先便宜先准）；**早停**为 Runner 配置：出现 KeyLookup/UserTable 候选即跳过后续生成器。**kind 清单不钉死**：内核只认识契约，实现类注册即新增 kind（同 Enhancer 贡献模式）；终端用户不造 kind（无脚本引擎），但 `lookup-table`/`clue-direct` 的行为由用户数据定义。

## ② Consolidate：候选归拢（内核固定阶段）

**它只回答一个封闭问题：这几个候选是不是同一作品。** 三个容易混淆的问题及其归属——理解本阶段的前提：

| 问题 | 谁回答 | 规则 | 错了的后果 |
|---|---|---|---|
| ① 是不是同一作品 | Consolidate | 断言 > 键 join > 标题门槛+谓词表 | 错并/漏并——有旗、有断言、可重放 |
| ② 合并候选上展示哪个值 | Consolidate 的唯一合并规则 | 全字段同一条：fill-empty + 打旗 | 复核页显示偏差；**不落库** |
| ③ 字段的权威值到底是什么 | **[05 富集](05-enrichment.md)**（不是本阶段） | per-field 策略 + provenance | 可改策略零请求重算、[07](07-execution.md) 可重整理 |

问题③是无限的（每个域有自己的字段与权威源）——所以被整个隔离出本阶段。**一旦让 Consolidate 顺便回答"哪个值对"，它就会膨胀成无限逻辑的泥球；这条边界是本设计最重要的一条。**

### 逻辑封闭性（穷举）

```
等价判定（问题①）：
  a. 人工断言：NotSameWork 一票否决；IsSameWork 直接并          ← 最高优先级（人 > 源数据）
  b. 确定性：身份键相交 或 外站链接互指                          ← 键 join，不存在"判错"
  c. 启发式：归一化标题相等（门槛）且 谓词表全过                  ← 谓词表是配置数据，不是代码分支
合并（问题②）：
  d. 唯一规则：按分数序 fill-empty——先到先写、后来者只填空、
     同键不同值 → 打旗。不存在"替代/覆盖"。全字段同构，零特判。
产出：
  e. 身份并集 + max 分数（+可选佐证加分）+ 旗 + RawPayloads 按源原样保留
```

**这就是全部逻辑，不随域增长**：谓词表之外的任何字段不参与等价判定——两个候选可以有 50 个 feature 不同，只要键连上（或断言/门槛+谓词成立）照并，没连上照分。新增一个域，Consolidator 的代码增量为**零**，变的只是该域内置 Pipeline 的谓词配置行。

### 纯函数与非破坏

```
Consolidate : (候选池, 配置, 断言) → (新池, 冲突旗)     // 无 I/O、确定性
```

合并候选是**派生视图**：原料（各源 RawPayloads）绝不融合，因此任何等价判定错误都可**无损重放**——修正断言/配置后重算，什么都不会丢。"错并"不是数据损坏，是一个可重算视图的错误。

### 配置与实现

比较语义复用 **Comparison 模块**策略（StrictEqual/FixedTolerance/SameDay/SetIntersection…）：

```csharp
public sealed record ConsolidationConfig
{
    public bool EnableHeuristicJoin { get; init; }
    // 启发式并组谓词表。动画内置 Pipeline 默认：[("year", FixedTolerance{1}), ("workType", StrictEqual)]
    // —— "year" 只出现在这里（配置），不出现在内核代码里。
    public IReadOnlyList<FeaturePredicate> HeuristicPredicates { get; init; } = [];
    public decimal CorroborationBonus { get; init; } = 0;
}
public sealed record FeaturePredicate(string FeatureKey, ComparisonMode Mode, string? ArgJson);

// 人工同一性断言（来自 OrganizeOverride 的 IsSameWork / NotSameWork），跨 Job 生效
public sealed record IdentityAssertions(
    IReadOnlySet<SourceKeyPair> SameWork, IReadOnlySet<SourceKeyPair> NotSameWork);
```

```csharp
public sealed class CandidateConsolidator(
    ISpecialTextService specialText, IAliasService alias, IComparisonStrategyResolver strategies)
{
    public ConsolidationResult Consolidate(
        IReadOnlyList<MatchCandidate> pool, ConsolidationConfig config, IdentityAssertions assertions)
    {
        if (pool.Count < 2) return new(pool.ToList(), []);

        var uf = new UnionFind(pool.Count);
        for (var i = 0; i < pool.Count; i++)
        for (var j = i + 1; j < pool.Count; j++)
        {
            var (a, b) = (pool[i], pool[j]);
            if (assertions.DeniesUnion(a.Identity, b.Identity)) continue;   // a. 否决高于键 join：
                                                                            //    人 > 源数据（外链也会指错）
            if (assertions.ForcesUnion(a.Identity, b.Identity)
                || IsDeterministicallyLinked(a, b)                          // b.
                || (config.EnableHeuristicJoin && IsHeuristicallyLinked(a, b, config)))  // c.
                uf.Union(i, j);
        }

        var conflicts = new List<MergeConflict>();
        var merged = uf.Groups()
            .Select(g => g.Count == 1 ? pool[g[0]] : MergeGroup(g.Select(x => pool[x]).ToList(), config, conflicts))
            .ToList();
        return new(merged, conflicts);
    }

    private static bool IsDeterministicallyLinked(MatchCandidate a, MatchCandidate b) =>
        a.Identity.Overlaps(b.Identity)
        || a.Links.Any(l => b.Identity.Contains(l.Source, l.Key))
        || b.Links.Any(l => a.Identity.Contains(l.Source, l.Key));

    private bool IsHeuristicallyLinked(MatchCandidate a, MatchCandidate b, ConsolidationConfig config)
    {
        if (!HasNormalizedTitleMatch(a, b)) return false;      // 门槛
        foreach (var p in config.HeuristicPredicates)
        {
            var va = a.Features.GetRaw(p.FeatureKey);
            var vb = b.Features.GetRaw(p.FeatureKey);
            if (va is null || vb is null) return false;        // 缺值 → 谓词不可用 → 保守拒绝
            if (!strategies.Get(p.Mode).AreEquivalent(va, vb, p.ArgJson)) return false;
        }
        return true;
    }

    // 门槛比较在「标题 ∪ 别名展开」集合上做；SpecialText 归一化吸收写法变体（全半角/空格/标点）
    private bool HasNormalizedTitleMatch(MatchCandidate a, MatchCandidate b)
    {
        var ta = a.Titles.Values.SelectMany(alias.Expand)
                 .Select(specialText.Standardize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return b.Titles.Values.SelectMany(alias.Expand)
                 .Select(specialText.Standardize).Any(ta.Contains);
    }

    private static MatchCandidate MergeGroup(List<MatchCandidate> members, ConsolidationConfig config,
        List<MergeConflict> conflicts)
    {
        var byScore = members.OrderByDescending(m => m.Score).ToList();
        var top = byScore[0];
        var identity = WorkIdentitySet.Union(members.Select(m => m.Identity));

        // 规则 d（唯一合并规则）：fill-empty——已写入的值不会被覆盖，不存在"替代"
        var titles = new Dictionary<string, string>();
        foreach (var m in byScore)
        foreach (var (lang, title) in m.Titles)
        {
            if (titles.TryGetValue(lang, out var existed))
            { if (existed != title) conflicts.Add(new(identity, $"title[{lang}]", existed, title)); }
            else titles[lang] = title;
        }

        var features = FieldBag.Empty();
        foreach (var m in byScore)
        foreach (var (key, value) in m.Features.Raw)
        {
            if (features.TryGetRaw(key, out var existed))
            { if (!Equals(existed, value)) conflicts.Add(new(identity, key, existed, value)); }
            else features.SetRaw(key, value, provenance: m.Identity.Primary.Source);
        }

        return top with
        {
            Identity = identity,
            Score = Math.Min(100, top.Score + (members.Count > 1 ? config.CorroborationBonus : 0)),
            Titles = new LocalizedText(titles),
            Features = features,
            Explanation = MatchExplanation.Concat(members.Select(m => m.Explanation)),
            RawPayloads = members.SelectMany(m => m.RawPayloads).ToDictionary(kv => kv.Key, kv => kv.Value),
        };
    }
}
```

### title 在归拢中的角色

title 是唯一有特殊地位的字段（它是启发式路径的门槛），其余字段一律走谓词表或被无视：

| 路径 | title 的角色 | title 不一样时 |
|---|---|---|
| 断言 / 键 join | **不参与判定**——键说了算 | 照并；同语言不同值 → `title[lang]` 冲突旗。该旗常是**粒度错位**信号（外链把系列条目连到季条目/剧场版）——`DowngradeOnMergeConflict` 针对的正是它 |
| 启发式 | **门槛条件**：别名展开 + 归一化后任一语言对相等才继续 | **不并，无降级手段**——保守漏并 → 假歧义进复核 → 人工 `IsSameWork` + 沉淀 Alias → 重放即并上 |

```
A{bangumi, ja:進撃の巨人, 96}  B{tmdb, ja:進撃の巨人 第三期, 95}，外链互指
  → 键 join 照并；title[ja] 冲突旗 →（可配）降档进复核     ← 粒度错位让人看一眼
A{ja:進撃の巨人, 96}  B{ja:進撃の巨人, 95}，无外链
  → 门槛过 + year±1/workType 过 → 启发式合并（带 HeuristicallyMerged 旗）
A{ja:進撃の巨人, 96}  B{en:Attack on Titan, 95}，无外链
  → 任何语言对不相等 → 不并 → 假歧义复核 → IsSameWork → 下轮并上
```

跨语言不同名（一边只有 ja、一边只有 en）字符串比较无解——那是键连接与人工断言的领地，不是启发式该赌的。

## ③ Decide：裁决

```csharp
public sealed record BandingConfig
{
    public decimal High { get; init; } = 90;
    public decimal Medium { get; init; } = 70;
    public decimal AmbiguityGap { get; init; } = 5;
    public MediumAction MediumAction { get; init; } = MediumAction.ReviewAll; // AutoPass / Sample / ReviewAll
    public bool DowngradeOnMergeConflict { get; init; } = false;
}

public sealed class MatchDecider
{
    public MatchDecision Decide(ConsolidationResult consolidated, BandingConfig config)
    {
        var pool = consolidated.Candidates;
        if (pool.Count == 0) return MatchDecision.NeedsReview(consolidated, ReviewReason.NoCandidates);

        var ranked = pool.OrderByDescending(c => c.Score).ToList();
        var top = ranked[0];

        // 1) 确定性 Basis 直通（早停通常已在 Generate 发生，这里兜底；交叉检验失败旗除外）
        if (top.Basis is CandidateBasis.KeyLookup or CandidateBasis.UserTable
            && !top.HasFlag(CandidateFlag.CrossValidationFailed))
            return MatchDecision.Resolved(top, MatchBand.High);

        // 2) 歧义保护：Consolidate 之后仍打架的才是真歧义
        if (ranked.Count > 1 && top.Score - ranked[1].Score < config.AmbiguityGap)
            return MatchDecision.NeedsReview(consolidated, ReviewReason.Ambiguous(top.Score, ranked[1].Score));

        // 3) 分数 → 档位
        var band = top.Score >= config.High ? MatchBand.High
                 : top.Score >= config.Medium ? MatchBand.Medium
                 : MatchBand.Low;

        // 4) 保守降档（可配）：合并冲突旗 / 交叉检验失败旗
        if (band == MatchBand.High &&
            ((config.DowngradeOnMergeConflict && consolidated.Conflicts.Any(c => c.Identity.Overlaps(top.Identity)))
             || top.HasFlag(CandidateFlag.CrossValidationFailed)))
            band = MatchBand.Medium;

        return band switch
        {
            MatchBand.High => MatchDecision.Resolved(top, band),
            MatchBand.Medium => config.MediumAction switch
            {
                MediumAction.AutoPass => MatchDecision.Resolved(top, band),
                MediumAction.Sample => MatchDecision.ResolvedWithSampling(top, band),
                _ => MatchDecision.NeedsReview(consolidated, ReviewReason.MediumBand),
            },
            _ => MatchDecision.NeedsReview(consolidated, ReviewReason.LowScore),
        };
    }
}
```

## 误判模式与防线

判定是不完美信息下的统计决策，**误判不可能为零**。架构目标是控制"错误的期望成本"：把错误推向"多一次人工"（便宜、可见），而不是"错数据落盘"（贵、隐蔽）。四条通用防线：**保守默认**（缺值拒并、歧义降档、Medium 默认复核）、**可见**（Explanation/冲突旗/HeuristicallyMerged 旗/provenance）、**可纠**（断言 + 重放 + [Undo](07-execution.md)）、**可调**（谓词/阈值/gap/降档开关全是 Pipeline 配置）。

| 误判模式 | 错误方向 | 防线 |
|---|---|---|
| **错并**：启发式把同名同年的不同作品并成一个 | 派生视图错误（可重算） | 门槛+谓词全过才并；`HeuristicallyMerged` 旗；错并常伴字段冲突 → 冲突旗 + 降档；复核"拆开合并" → `NotSameWork` 断言 → **重放归拢，无损重算** |
| **漏并**：同作不同写法没并上 → 假歧义 | 多人工（不错数据） | 复核确认 → `IsSameWork` 断言 + 沉淀 Alias → 重放即并 |
| **薄值选错**：合并候选展示了错误一方的值 | 展示偏差 | fill-empty 不覆盖 + 冲突旗展示双值；权威值在 [05](05-enrichment.md) |
| **高分错配**：线索误导（嵌错码/蹭名作关键词） | 错数据（最危险类） | `code-lookup` 的 CrossValidateWithTitle 打旗降档；journal 可回滚；复核改判 → 自动重整理 |
| **阈值边界**：89 被拦 / 91 错配 | 双向 | 阈值 per-Pipeline 可调；`MediumAction.Sample` 抽样审计；banding 分布可观测 |
| **歧义差错判**：gap 太小放过真歧义 / 太大复核爆量 | 双向 | 同上标定；复核量本身是收敛指标 |

## Override（人工裁定）

```
OrganizeOverride   Fingerprint(条目指纹，键), Kind(ForcedIdentity / ForcedTitle / ForcedPipeline / Skip
                   / IsSameWork / NotSameWork), PayloadJson, CreatedAt
```

- 条目级断言（ForcedIdentity/ForcedTitle/ForcedPipeline/Skip）在 Runner 前置消费。
- **同一性断言**（IsSameWork/NotSameWork，以 (source,key) 对为主体）作为 `IdentityAssertions` 输入 Consolidate，**优先级最高**（否决高于键 join——外链也会指错，人说了算），跨 Job 生效。

## 走查示例

```
生成器链：code-lookup → fuzzy-search ｜ consolidate{启发式:开, 谓词:[year±1, workType 相等]} ｜ banding{high:90, gap:5}

输入 1：[CANDYVOICE] RJ01017217 耳かきボイス.zip
  ① code-lookup：code 线索 → DLsite 直查命中，标题交叉检验通过 → [{dlsite:RJ01017217, Basis=KeyLookup}]
     → 早停
  ② consolidate：单候选，无事可做
  ③ decide：Basis=KeyLookup 且无旗 → Resolved(High)

输入 2：[LoliHouse] 進撃の巨人 S03
  ① code-lookup：无码 → 0 候选；fuzzy-search：池 = [A{bangumi, 96}, B{tmdb, 95}]
  ② consolidate：A/B 外站 id 互指（键 join）→ 池 = [AB{bangumi+tmdb, 96}]
  ③ decide：96 ≥ high，次高 60、差 36 > gap → Resolved(High)

  反事实：跳过 consolidate → 96 vs 95 差 1 < gap → 假歧义进复核，且人工选 A 后身份只剩 {bangumi}
  ——归拢阶段存在的全部理由。
```

## 用户如何自定义

三层阶梯，前两层零代码：① **组链**（fork 后增删/重排/调参生成器实例：AV 库只留 `code-lookup`；私有码加一个限定自有 Provider 的实例）；② **数据驱动生成器**（`lookup-table`/`clue-direct`——行为即用户数据）；③ **写 C#**（实现 `AbstractCandidateGenerator<TConfig>` + 注册，同 Enhancer 贡献模式）。

## 完成后获得的能力

- 纯逻辑层：线索集 + Provider（可 mock）→ 可解释的身份判定；打分、归拢（含断言重放）、歧义、误判防线全量单测。
- 配合 01–03 的调试页端到端验证"这个文件名能不能认出来、为什么认错"。
- Override（含正/负同一性断言）落地后，人工纠错一次、永久生效。

## 开放问题

- 佐证加分、启发式谓词、banding 的各场景默认值（拿真实库标定）。
- `CrossValidateWithTitle` 的相似度阈值与多语言标题的检验策略。
- `resolver.ai` 生成器的引入时机与置信要求。
