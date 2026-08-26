# 文件处理器 AV 分组改进设计

| 字段 | 值 |
|---|---|
| 状态 | **已实施** — 见 §0 实施记录 |
| 分支 | `claude/app-optimization-fixes-i2dsnx` |
| 最后更新 | 2026-08-26 |
| 相关代码 | `src/apps/Bakabase.Service/Services/FileSystemEntryGroupingService.cs`<br>`src/modules/Bakabase.Modules.ThirdParty/ThirdParties/Av/AvProductCode.cs`<br>`src/web/src/components/FileExplorer/components/GroupModal.tsx` |

---

## 0. 实施记录

方案 A + B 已落地，方案 C 按建议暂缓。与原设计的偏差：

| 偏差点 | 原设计 | 实际实现 | 原因 |
|---|---|---|---|
| 归一化的落点 | 独立的归一化管线，作用于所有策略 | 归一化收进 `AvProductCodeParser`，另外把分组字典改为 `OrdinalIgnoreCase` | 全局归一化会改变用户自填正则的语义；3.1/3.4 由字典比较器解决，其余归一化只有番号解析需要 |
| 大写噪声 (3.3) | 「厂牌只取 token 末尾连续的字母段」 | `SSSSSSSXDVD-101pl` 判定为**无番号**，不再产出任何键 | 没有厂牌名单时，`SSSSSSSXDVD` 这一整段 11 个字母里没有任何信号能指出噪声在哪里结束。产出 `SSXDVD-101` 或 `SSSSSSXDVD-101` 都只是换一个错误答案；不匹配会让文件留在 untouched 里被用户看见，更诚实。小写噪声（`sssssssXDVD-101pl`，有大小写边界）能正确解析 |
| 共享组件的范围 | 番号解析由分组 / Freejavbt / Airav 三处共用 | 只新增了共享组件并接入分组；两个 client 未改动 | 它们的正则锚定在页面标题上下文，替换需要单独验证抓取行为，不适合和本次改动混在一起 |

新策略为 `FileSystemEntryGroupStrategyType.ProductCode = 3`，前端多一个「番号 / Product code」页签，无需填正则也无需调阈值。第 3 节的输入表已固化为 `AvProductCodeParserTests` 的用例。

---

## 1. 问题

用户反馈：文件处理器对 AV 文件分组"总是不尽人意"，具体列举了三类文件名：

1. 前后有异常字符 —— `sssssssXDVD-101pl`
2. 有无中划线混用 —— `XDVD101` 与 `XDVD-101`
3. 大小写混用 —— `xdvd-101` 与 `XDVD-101`

本文先定位这三类为什么会失败，再给出可选方案。**不含实现**。

---

## 2. 现状

分组入口是 `FileSystemEntryGroupingService`，前端 `GroupModal` 提供三种策略：

| 策略 | 枚举 | 实现 | 说明 |
|---|---|---|---|
| 相似度 | `Similarity` | `GroupBySimilarity` | Levenshtein 归一化相似度 + 阈值贪心聚类 |
| 关键字提取 | `KeyExtraction` | `GroupByKeyExtraction` | 用户给正则，取 group[1] 作为分组键 |
| 前后缀 | `Affix` | `GroupByAffix` | 公共前缀/后缀长度 ≥ 阈值即同组 |

前端为 `KeyExtraction` 内置了 4 个预设，其中 AV 预设是：

```js
// src/web/src/components/FileExplorer/components/GroupModal.tsx
{ labelKey: "...presetAv", regex: "([A-Z]{2,6}-\\d{2,5})" }
```

**关键点：整条链路上没有任何归一化步骤。** 文件名从磁盘读出后，除了去扩展名
（`Candidate.Key`），直接进入正则匹配 / Levenshtein / 前缀比较。

---

## 3. 失败原因定位

以下结论均已用 AV 预设正则实测验证（正则语义 JS 与 .NET 在此一致）：

| 输入 | 提取结果 | 判定 |
|---|---|---|
| `XDVD-101` | `XDVD-101` | ✅ |
| `XDVD-102` | `XDVD-102` | ✅ |
| `[hd]XDVD-102 [1080p]` | `XDVD-102` | ✅ 小写/符号包裹无影响 |
| `sssssssXDVD-101pl` | `XDVD-101` | ✅ 小写噪声无影响 |
| `SSSSSSSXDVD-101pl` | **`SSXDVD-101`** | ❌ **键被污染** |
| `XDVD101` | **无匹配** | ❌ |
| `xdvd-101` | **无匹配** | ❌ |
| `Xdvd-101` | **无匹配** | ❌ |
| `XDVD_103` | **无匹配** | ❌ |
| `XDVD 104` | **无匹配** | ❌ |

对应到代码，一共四个独立缺陷：

### 3.1 正则大小写敏感

`[A-Z]{2,6}` 没有 `RegexOptions.IgnoreCase`
（`FileSystemEntryGroupingService.cs:168` 只传了 `RegexOptions.Compiled`）。
`xdvd-101` / `Xdvd-101` 直接不匹配，落入 `UntouchedEntries`。

### 3.2 分隔符被写死成 `-`

正则要求字面量 `-`。实际番号分隔符至少还有 空、`_`、`.`，以及干脆没有
（`XDVD101`）。这是用户第 2 条反馈的直接原因。

### 3.3 大写噪声会污染分组键

`[A-Z]{2,6}` 贪心且正则未锚定，`SSSSSSSXDVD-101pl` 会先在噪声里凑够 2 个大写字母，
得到 `SSXDVD-101`。这个键和干净的 `XDVD-101` **不相等**，于是两者被分到不同组 ——
比"不匹配"更糟，因为它会静默地建出一个错误的目录。

### 3.4 分组字典用 Ordinal 比较

```csharp
// FileSystemEntryGroupingService.cs:176
var byKey = new Dictionary<string, List<Candidate>>(StringComparer.Ordinal);
```

即使 3.1 修好、`XDVD-101` 与 `xdvd-101` 都能匹配出来，两个键在 Ordinal 下仍然不等，
依旧分成两组。**3.1 和 3.4 必须一起修**，只修一个没有效果。

### 3.5 相似度策略对噪声不鲁棒（用户第 1 条的另一面）

`sssssssXDVD-101pl` vs `XDVD-101`：编辑距离 9，最大长度 17，归一化相似度
`(17-9)/17 = 0.471`。要把它们聚到一起，阈值得压到 0.471 以下 —— 而那个阈值同时会把
`XDVD-101` 和 `XDVD-102`（编辑距离 1，相似度 `7/8 = 0.875`）以及几乎所有其他番号
也糊成一组。
**单一全局阈值无法同时满足"容忍噪声"和"区分番号"**，这是相似度策略的结构性问题，
不是参数没调好。

### 3.6 附带发现（不在用户列举内，但同源）

- `GroupBySimilarity` 是顺序相关的贪心聚类：新候选只和各组的 `CanonicalKey`
  （建组时第一个成员的键）比，结果依赖输入顺序，不稳定。
- `ComputeSimilarityBreakpoints` 对所有键做两两 Levenshtein，`O(n²·L²)`。
  目录里几千个文件时，光是打开弹窗就会明显卡顿。
- `GroupByAffix` 用公共前缀分组，对 AV 是错的粒度：`XDVD-101` 和 `XDVD-102`
  公共前缀 `XDVD-` 长度 5，会被合并成一组，而它们是不同作品。

---

## 4. 设计方案

三个方案不互斥，A 是 B/C 的基础。

### 方案 A：加一层文件名归一化（基础，建议必做）

在 `Candidate.Key` 之后、各策略之前插入一个可配置的归一化管线：

```
原始名 → 去扩展名 → 去括号段 → 折叠分隔符 → 大小写折叠 → 归一化键
```

- **去括号段**：剥掉 `[...]`、`(...)`、`【...】` 内容（画质、字幕组、站点后缀）。
- **折叠分隔符**：`-`、`_`、`.`、空格 归一为单一分隔符，或全部删除。
- **大小写折叠**：统一 `ToUpperInvariant`。
- 分组键用归一化后的值；**展示名仍用原始名**，避免用户看不懂建出来的目录。

配合把 `byKey` 的比较器换成 `StringComparer.OrdinalIgnoreCase`。

单这一层就能确定性地解决 3.1 / 3.2 / 3.4。

**但它救不了 3.5。** 实测：`sssssssXDVD-101pl` vs `XDVD-101` 相似度 0.471；归一化成
`SSSSSSSXDVD101PL` vs `XDVD101` 后反而降到 0.438 —— 因为去掉分隔符让干净的那一侧
缩得更多，噪声占比相对更高。**归一化对"长噪声前后缀"这一类完全无效**，必须靠
方案 B 的锚定解析。这一点是本设计的关键结论。

**代价**：归一化是有损的。`XDVD-101` 与 `XDVD_101` 归一化后同键 —— 这正是我们要的；
但 `A-1` 与 `A1` 也会同键，理论上可能误合。对 AV 场景这个取舍是划算的。

### 方案 B：专门的"番号"策略（建议，针对性最强）

新增第四种策略 `ProductCode`，不让用户写正则，而是内置一个番号解析器：

```
番号 := <厂牌: 2-6 字母> <可选分隔符> <序号: 2-5 数字>
```

关键在于**匹配必须锚定到词边界**，而不是像现在这样在噪声中间乱找：

- 先按分隔符和大小写变化把文件名切成 token（`SSSSSSSXDVD-101pl` →
  `SSSSSSSXDVD` / `101` / `pl`）；
- 厂牌只取 token **末尾**连续的字母段，避免 3.3 的键污染；
- 序号保留前导零原样（`XDVD-001` ≠ `XDVD-1`，实践中两者确实是不同作品）；
- 输出规范化番号 `XDVD-101` 作为分组键与目录名。

这样上表 10 个输入里，除了本就不是番号的，**全部**归到 `XDVD-101` / `XDVD-102`
两组，且目录名统一是规范形式。

**代价**：需要维护厂牌规则；对非 AV 内容无意义（所以是独立策略，不改默认行为）。

**已有可复用资产（重要）**：仓库里已经存在番号解析，而且比分组预设写得好：

```csharp
// src/modules/Bakabase.Modules.ThirdParty/ThirdParties/Freejavbt/FreejavbtClient.cs:48
Regex.Match(cleaned, @"^([A-Za-z]{2,10}-?\d{2,8})");

// src/modules/Bakabase.Modules.ThirdParty/ThirdParties/Airav/AiravClient.cs:144
Regex.Match(trimmed, @"^[A-Za-z]{2,10}-?\d{2,8}");
```

注意它们已经解决了本文 3.1（用 `[A-Za-z]` 而非 `[A-Z]`）和 3.2（用 `-?` 而非字面
`-`），并且用 `^` 锚定从而天然回避 3.3 的键污染。**分组预设与这两处规则不一致，
本身就是个应当收敛的问题**。

但**不能直接照搬**：`^` 锚定意味着任何前缀噪声都会导致完全不匹配。实测对比：

| 输入 | 分组预设 `([A-Z]{2,6}-\d{2,5})` | Freejavbt 式 `^([A-Za-z]{2,10}-?\d{2,8})` |
|---|---|---|
| `XDVD-101` | `XDVD-101` | `XDVD-101` |
| `XDVD101` | — | `XDVD101` ✅ |
| `xdvd-101` | — | `xdvd-101` ✅ |
| `Xdvd-101` | — | `Xdvd-101` ✅ |
| `sssssssXDVD-101pl` | `XDVD-101` ✅ | — ❌ |
| `SSSSSSSXDVD-101pl` | `SSXDVD-101` ❌ | — ❌ |
| `[hd]XDVD-102 [1080p]` | `XDVD-102` ✅ | — ❌ |
| `XDVD_103` | — | — |
| `XDVD 104` | — | — |

**两者互补，但都不完整**：一个能扛噪声却大小写/分隔符敏感，另一个反之；
`XDVD_103` / `XDVD 104` 两者都失败。这正说明方案 B 不是"换个正则"就行 ——
需要方案 A 的归一化（吃掉 `_`、空格、括号段、大小写）**加上**基于 token 边界的
锚定解析（吃掉前后缀噪声且不污染厂牌），单靠任何一条现成正则都不够。

方案 B 应把番号解析抽成一个共享组件，分组 / Freejavbt / Airav 三处共用，
而不是再写第四套正则。

（`Modules.Enhancer/Components/Enhancers/Av/` 下是 AV 增强器，负责按番号取元数据，
本身不含番号解析，但它是这个共享组件的第三个使用方。）

### 方案 C：改造相似度策略（可选，收益/成本比最低）

若想让"相似度"本身变好，需要：

- 用 token 集合相似度（Jaccard / Dice）替代字符级 Levenshtein，对噪声更鲁棒；
- 用凝聚层次聚类替代顺序贪心，消除 3.6 的顺序依赖；
- 断点计算加采样上限，修 `O(n²)` 卡顿。

**代价**：改动最大，且对 AV 这个具体场景，效果仍不如方案 B 直接。
建议只在确认"相似度"策略本身要长期维护时再做，或先只做断点采样上限（纯性能修复，
风险低，可独立于本设计单独落地）。

---

## 5. 建议

1. **先做方案 A**：改动小、无新 UI、立即修掉 3.1 / 3.2 / 3.4 三个确定性缺陷。
2. **再做方案 B**：作为新策略加入，AV 场景一次性解决，且不影响现有策略行为。
3. **方案 C 暂缓**，仅先单独修 `ComputeSimilarityBreakpoints` 的性能问题。

无论选哪条，都建议为 `FileSystemEntryGroupingService` 补单元测试，把本文第 3 节的
输入表直接固化成用例 —— 目前该服务没有任何测试覆盖。

---

## 6. 待确认

- 方案 B 的番号解析是否复用 `Modules.Enhancer` 的 AV 逻辑？
- 归一化是否要做成用户可见的开关，还是固定行为？
- 序号前导零（`XDVD-001` vs `XDVD-1`）是否按不同作品处理？本文按"是"设计。
