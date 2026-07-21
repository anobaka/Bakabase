# 06 · 布局：路径模板与整理规则

> 层级：落盘能力 ｜ 依赖：[05 字段富集](05-enrichment.md)（取值）、[foundations 类型系统](../foundations.md#类型系统)、`ResourceSearchFilterGroup`/`PathFilter`（[复用总表](../foundations.md#现有资产复用总表)） ｜ 被依赖：[07 执行](07-execution.md)、[08 编排](08-orchestration.md)
>
> 职责：用户以规则集表达"哪些条目 → 放到哪、叫什么、撞了怎么办"。渲染是**纯函数**（条目 + 字段 → 目标相对路径），因此天然可预览。系统不预设任何目录结构。

## 模型

```
OrganizeRuleSet   规则集：Name, TargetRoot(整理目标根), Rules(有序), DefaultCollision, Enabled
OrganizeRule      规则：RuleSetId, Priority, Scope(过滤 JSON，复用 ResourceSearchFilterGroup + PathFilter),
                  PipelineId?(可选限定), PathTemplate, CollisionPolicy
```

- **有序首中生效**（防火墙规则心智）；无规则命中 → 条目进复核或原地不动（规则集可配）。
- **CollisionPolicy**：目标已存在时 → `AutoSuffix`（追加 [2]/码）/ `Merge`（并入同目录，多版本/多碟）/ `Skip` / `Review`。每规则可覆盖规则集默认。

## 路径模板 DSL

`/` 即目录层级——"IP>类型>文件"写三段、平铺 `<IP>-<类型>-<文件>` 写一段：

```
segment      := (literal | placeholder | group)*
placeholder  := "{" selector (":" formatter)* ("|" alternative)* "}"
selector     := title | year | code | franchise | domainLabel | fileName     ← 规范字段
              | 域字段：dlsite.circle · music.albumArtist · manga.volume …   ← FieldBag（05）
              | prop("属性名")                                               ← 任意资源属性
formatter    := lang(zh-Hans|ja|en|romaji) · pad(n) · date(yyyy) · case(upper|lower)
              · sanitize · truncate(n)
alternative  := 依序回退：{title:lang(zh-Hans)|title:lang(ja)|fileName}
group        := "[" … "]"    组内任一占位符为空 → 整组丢弃
```

示例——同一批文件，两种规则，两种世界：

```
# 规则 A：按 IP 分层合集
{franchise}/动画/{title:lang(zh-Hans)|title:lang(ja)} [{year}]
→ Z:/Library/物语系列/动画/化物語 [2009]/…

# 规则 B：平铺
{franchise|title}-{domainLabel}-{fileName}
→ Z:/Flat/物语系列-动画-化物語 [2009].mkv
```

## 渲染管线

占位符求值 → 空值组丢弃 → 逐段 sanitize（Windows 非法字符、保留名、结尾点/空格）→ **长度预算自顶向下分配**（260 限制，超限自动截断并在预览中高亮）→ 拼接目标路径。

- 求值：规范字段/域字段走 [05](05-enrichment.md) 的产出；`prop()` 走现有 StandardValue 显示值处理器——与显示名模板同一取值路径（"包装符空值丢弃"语义即复用其既有实现，[复用总表](../foundations.md#现有资产复用总表)）。
- **占位符可用性随 Pipeline**：域字段占位符按作用域内可能命中的 Pipeline 组件集合提示与校验（["字段随组件走"](../foundations.md#类型系统)）；引用不可用字段 → 预览标黄，运行时走 `|` 回退链。

## 与 ResourceProfile.NameTemplate 的分工

刻意分离：`NameTemplate` 决定**显示名**（虚拟层），本模块决定**物理路径**。共享模板引擎与语法，互不影响——用户可以物理平铺 + 显示名分层，反之亦然。

## 完成后获得的能力

- 规则编辑页：左侧规则、右侧抽样条目实时渲染目标树（SamplePaths 模式），所见即所得；渲染纯函数全量单测（非法字符/超长/空值回退边界）。
- 用户可零代码表达任意布局：分层合集、平铺、按社团、按艺术家/专辑/曲目三层……
- [07 执行](07-execution.md)的输入（每条目的目标路径 + 冲突策略）自此就绪。

## 开放问题

- formatter 集是否够用（roman 数字卷号、全半角转换？）——按需求追加，语法已预留。
- `Merge` 策略下子文件命名冲突的次级策略。
