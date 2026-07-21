# 02 · 条目提取器（Extractor）

> 层级：原子能力 ｜ 依赖：[01 文本处理规则](01-text-processing.md)、[foundations 组件模型/类型系统](../foundations.md) ｜ 被依赖：[04 身份判定](04-resolution.md)、[08 编排](08-orchestration.md)
>
> 职责：面向**条目**（文件/目录/压缩包）采集类型化线索（Clue）。命名说明见 [foundations · 为什么不叫 Signal](../foundations.md#命名约定为什么不叫-signal)。

## 两类组件

| 类型 | Kind | 说明 |
|---|---|---|
| 文本规则应用器 | `extractor.pattern` | 把某个 [TextRuleSet](01-text-processing.md) 应用到条目的一个**文本面**：FileName / DirName / RelativePath / SidecarText。配置 = (文本面, 规则集引用)。用户可自定义任意多个实例 |
| 结构化读取器（代码组件） | `extractor.embedded-tag`（TagLib，音频标签）、`extractor.comicinfo`（ComicInfo.xml）、`extractor.nfo`、`extractor.acoustid`（声学指纹，后期） | 读二进制/结构化内容，正则表达不了，保持为代码 |

所有提取器实现统一契约（描述符携带 `Applicability` 适用性与 `Fields` 产出字段声明，见 [foundations · 组件模型](../foundations.md#组件模型所有槽位共享)）：

```csharp
public interface IExtractor
{
    Task<IReadOnlyList<Clue>> ExtractAsync(OrganizeItemContext item, CancellationToken ct);
}
```

## Clue 与归并（Parse）

每条 Clue 携带：`目标槽或字段 + 类型化值 + 来源提取器 + 强度`。强度序（用于归并与后续判定）：

```
精确码（rj/tt/序列号/包名） > 指纹 > 内嵌标签/伴随文件 > 文件名
```

**Parse 归并**：多个提取器对同一槽给出冲突值（如标签里的 title 与文件名里的 title 不同）时，按提取器实例顺序 + 强度归并成 `ParsedClues`（每槽一个胜出值 + 保留候选列表供复核展示）。归并是纯函数，独立可测。

## 压缩包语义

音声/漫画大量以压缩包为条目单位。第一阶段把压缩包视为**叶子条目**（只读文件名与伴随文件，不解包）；解包嗅探（借 `CompressedFileService` 读取包内条目名）作为可选提取器后续加入——它只是又一个结构化读取器，契约不变。

## 完成后获得的能力

- 对任意条目一键产出线索集，例：
  - `[CANDYVOICE] RJ01017217 耳かきボイス.zip` → `{code=RJ01017217(精确码), title=耳かきボイス(文件名), circle=CANDYVOICE(文件名)}`
  - `01 - Intro.flac` → `{trackNo=1, title=Intro, albumArtist=…(内嵌标签)}`
- 调试页：选一个文件/目录 + 一组提取器实例 → 展示逐提取器命中与归并结果。这个页面同时就是 [08](08-orchestration.md) Pipeline 编辑器里"提取预览"的雏形。
- [04 身份判定](04-resolution.md)的全部输入（ParsedClues）自此就绪。

## 开放问题

- SidecarText 的发现规则（同名 .txt/.nfo/.xml？目录内唯一说明文件？）需要一个小型约定表。
- 指纹提取（acoustid）的算力与依赖成本，放到音乐场景实装时再评估。
