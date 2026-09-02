# 01 · 文本处理规则（TextRule）

> 层级：原子能力 ｜ 依赖：无（[SpecialText 词汇表](../foundations.md#现有资产复用总表)为可选引用） ｜ 被依赖：[02 条目提取器](02-extractors.md)
>
> 职责：让用户创建规则，对**一段文本**执行清洗与类型化提取。目标抽象为 text，**来源无关**——可能是文件名、目录名、相对路径、伴随文本，或未来任何调用方传入的字符串。

## 与 SpecialText 的关系

现状：SpecialText 是**固定枚举的清洗词汇表**（`Useless/Wrapper/Standardization/Volume/Trim/DateTime/Language` 七种，值为字符串对，语义硬编码在各消费点）。它是"词汇"，不是"提取规则"。

决策：**不动 SpecialText 的表与枚举**（legacy、消费点多）。TextRule 是它的概念泛化——SpecialText 的每个枚举值是硬编码的 token 类型，TextRule 的具名捕获组就是**用户定义的动态 token 类型**。TextRule 的清洗步骤可引用 SpecialText 词汇（包装符剥离、无用词、标准化替换）。

## 模型

```
TextRuleSet   名称 + 有序规则列表 + （可选）残余清洗开关（引用 SpecialText 词汇）
TextRule      Pattern：带具名捕获组的正则（强制 MatchTimeout 防 ReDoS）
              Bindings：捕获组 → 绑定目标
              Cleanup：命中后是否从待处理文本中剥离
Binding       目标二选一：
              a) 规范线索槽：title / year / code / episode / volume / …
              b) 域字段：FieldDefinition（Key + StandardValueType，附解析器如日期格式）
```

处理语义：规则依序执行；`Cleanup=true` 的命中从文本中剥离；全部规则跑完后，剩余文本经 SpecialText 清洗成为"残余标题"候选。

输出：

```csharp
public sealed record TextExtractionResult(
    IReadOnlyList<Clue> Clues,   // 类型化线索（绑定校验过 StandardValueType）
    string CleanedText);         // 残余文本
```

类型安全：Binding 声明了 `StandardValueType`，捕获值在产出时解析校验（如 `year` 必须能进 Decimal、日期按声明格式解析），失败的捕获丢弃并在试跑 UI 中标红——见 [foundations · 类型系统](../foundations.md#类型系统)。

## 数据表

```
TextRuleSet   Name, BuiltinKind?(内置种子来源), Rules(JSON), Enabled, CreatedAt/UpdatedAt
```

内置种子（只读，可 fork，语义见 [foundations · 组件模型](../foundations.md#组件模型所有槽位共享)）：`rj-code`（`[BVR]J\d{6,10}` → code）、`imdb-code`（`tt\d+` → code）、`sxxeyy`（→ season/episode）、`av-code`（番号 → code）、`volume`（卷号）等。可经 Sharable 体系分享。

## UI

独立管理页（不依赖整理器的任何其他部分）：规则集列表 + 规则编辑器 + **试跑区**——粘贴若干样例文本，实时显示每条规则的命中高亮、提取出的类型化值、残余文本。

## 完成后获得的能力

- 用户可为私有编号体系建规则：新建规则集「我的编号」，Pattern `(?<code>GB\d{4})` 绑定到 `code:String`；试跑 `[GB0421] 某标题 (2023)` → `code=GB0421, year=2023, cleaned="某标题"`。
- 内置 `rj-code` 等规则集开箱可用、可 fork 改造（如放宽位数）。
- 该能力**独立成立**：即使整理器一行未写，它已是一个可用的"文本规则工具"，并可被未来其他功能（文件名批量清洗、对比归一预处理）复用。
- 全量单测：规则引擎是纯函数（文本进、结果出）。

## 开放问题

- 多规则集组合时的优先级语义（02 中由提取器实例的顺序决定，是否够用）。
- 正则之外是否需要第二种规则形式（分隔符切段/模板反推）——倾向于先不做，用数据说话。
