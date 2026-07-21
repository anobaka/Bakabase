# 基础定义

> 被所有模块引用的共享概念、组件模型与类型系统。本文件不含任何流程编排——流程见[08 编排](modules/08-orchestration.md)。

## 核心概念

| 概念 | 定义 | 详见 |
|---|---|---|
| Item（条目） | 一次整理的处理单位：一个文件或目录（含压缩包），带稳定指纹（路径+大小+mtime 摘要），用于断点续跑与 Override 定位 | [08](modules/08-orchestration.md) |
| Clue（线索） | 从条目上提取出的**类型化**键值：`title/year/code/episode…` 或域字段，绑定 `StandardValueType` | [02](modules/02-extractors.md) |
| TextRuleSet（文本规则集） | 用户定义的清洗 + 提取规则，作用于"一段文本"，来源无关 | [01](modules/01-text-processing.md) |
| Extractor（提取器） | 面向条目产出 Clue 的组件：文本规则应用器或结构化读取器 | [02](modules/02-extractors.md) |
| Provider（数据源） | 一个外部元数据库的统一适配：搜候选 / 精确码取 / 按身份富集 | [03](modules/03-providers.md) |
| Candidate / WorkIdentity | 匹配候选；定案后的作品身份是 **(source, key) 的集合**（同一作品在多库的 id） | [04](modules/04-resolution.md) |
| Band（置信档） | High / Medium / Low / None。分数→档位映射是 Pipeline 配置 | [04](modules/04-resolution.md) |
| FieldBag（字段袋） | 类型安全的域字段容器，每个值带 provenance（来源 Provider） | 本文件[类型系统](#类型系统) |
| Rule / RuleSet（整理规则） | 作用域 + 路径模板 + 冲突策略；有序首中生效 | [06](modules/06-layout.md) |
| Journal（执行账本） | 逐操作 Intent/Done 两段记录；回滚 = 逆序回放 | [07](modules/07-execution.md) |
| Pipeline（流水线） | 一条可运行的整理配置：各槽位组件实例的组合，**数据而非代码** | [08](modules/08-orchestration.md) |
| Override（人工裁定） | 复核时人的决定，按条目指纹持久化、跨轮生效 | [04](modules/04-resolution.md) |
| Franchise（IP/系列） | 跨域的作品归属，规范化为一个资源属性 | [09](modules/09-collections.md) |

### 命名约定：为什么不叫 Signal

早期草稿把线索采集层叫 "Signal"。弃用理由：signal 暗示"信号/触发/流"，而这一层实际做的是**从原始材料中提取类型化线索**。现命名：通用文本层 = TextRule，条目层组件 = Extractor，产物 = Clue，对应的条目状态 = `Extracted`。

## 组件模型（所有槽位共享）

Extractor、Provider、Resolver 策略、Detector 都遵循同一套组件机制（注册表/描述符/配置表单三件套借鉴 Workflow 模块的 `WorkflowActivityRegistry` 模式，但**不复用其执行引擎**，理由见 [08](modules/08-orchestration.md#与-workflow-模块的关系)）：

```csharp
public interface IOrganizerComponentDescriptor
{
    string Kind { get; }                          // "extractor.pattern" / "provider.dlsite" / "resolver.fuzzy"
    OrganizerSlot Slot { get; }                   // Detector / Extractor / Provider / ResolverPolicy
    string DisplayName { get; }
    Type? ConfigType { get; }                     // 配置 schema，编辑器据此渲染表单
    ComponentApplicability Applicability { get; } // 适用形态：音频/压缩包/任意…（编辑器校验）
    FieldDefinition[] Fields { get; }             // 该组件产出的域字段 → "字段随组件走"
}
```

- **组件 = 代码，实例 = 数据**：同一组件可在多处以不同配置实例化（configJson）。
- **内置皆种子**：内置组件实例、内置 TextRuleSet、内置 Pipeline 都是只读种子数据；用户 fork（copy-on-write）后自由修改。上游更新时 fork 收到提示，不强制同步。
- **可分享**：种子数据形态的配置（TextRuleSet、Pipeline、规则集）走现有 Sharable 体系分享。
- **DI 注册**：同 Enhancer 的多实现枚举注册模式；不做独立程序集/热插拔。

## 类型系统

**唯一类型骨架是 `StandardValueType`（9 种）**，不发明第二套类型系统。先例：`IEnhancerTargetDescriptor` 已同时携带 `StandardValueType ValueType` + `PropertyType PropertyType`。

```csharp
public sealed record FieldDefinition(
    string Key,                          // "circle"；模板中经 "{域标签.Key}" 引用
    StandardValueType ValueType,         // String / ListString / Decimal / DateTime…
    PropertyType SuggestedPropertyType,  // 写回资源属性时的建议类型
    bool Multilingual = false);          // true → 值为多语言文本，模板可 :lang() 选取

public sealed class TypedField<T>(string key, StandardValueType type) { /* 编译期检查的字段句柄 */ }

public sealed class FieldBag                     // 域字段容器
{
    public T? Get<T>(TypedField<T> field);
    public void Set<T>(TypedField<T> field, T? value, string providerName);  // 写入校验 ValueType；记录 provenance
    public string SerializeToJson();             // 持久化进 MetadataJson
}
```

- **通用规范字段**（title/year/code/workType/franchise）是 `MatchedWork` 的强类型属性；FieldBag 只装真正域特定的部分（circle/cv/albumArtist…）。
- **provenance**：每个值记录来源 Provider——复核 UI 展示依据；"换合并策略重算"零网络请求（见 [05](modules/05-enrichment.md)）。
- **与资源属性双向转换**：字段映射到属性（缺省按 `SuggestedPropertyType` 自动建属性，Enhancer target options 已是同一模式）；写入走 `PropertyValueFactory`，跨类型走 `StandardValueSystem.GetConversionRule` 既有转换规则。
- **字段随组件走**：一条 Pipeline 可用的域字段 = 其组件 `Fields` 声明的并集；模板占位符按此提示与校验（见 [06](modules/06-layout.md)）。

## 数据表索引

表定义写在归属模块内，此处仅索引。所有表经 EF 迁移生成（禁手写，见仓库根 `CLAUDE.md`）；文件类产物一律入 `IAppService.AppDataDirectory`（见 `.claude/rules/appdata-paths.md`）。

| 表 | 归属 |
|---|---|
| TextRuleSet / TextRule | [01](modules/01-text-processing.md) |
| OrganizeOverride | [04](modules/04-resolution.md) |
| OrganizeRuleSet / OrganizeRule | [06](modules/06-layout.md) |
| OrganizeJournalEntry | [07](modules/07-execution.md) |
| OrganizePipeline / OrganizeJob / OrganizeItem | [08](modules/08-orchestration.md) |

## 现有资产复用总表

以下路径与类型均已在代码库核实：

| 现有资产 | 位置 | 被谁复用 |
|---|---|---|
| 33 个第三方客户端 + 统一 HTTP 基建（`AddBakabaseHttpClient`、站点专属 Handler：限流/Cookie/代理） | `src/modules/Bakabase.Modules.ThirdParty/ThirdParties/` | [03](modules/03-providers.md) |
| Enhancer 转换链（context→StandardValue→属性，含选项自动建属性）；`AbstractEnhancer<,,>` | `src/modules/Bakabase.Modules.Enhancer/` | [05](modules/05-enrichment.md)、[07](modules/07-execution.md) |
| `ResourceSourceLink`（Source + SourceKey + MetadataJson + 封面缓存），一资源多行 | `src/abstractions/.../Models/Domain/ResourceSourceLink.cs` | [04](modules/04-resolution.md)、[07](modules/07-execution.md) |
| `ISpecialTextService`：固定枚举清洗词汇表（Useless/Wrapper/Standardization/Volume/Trim/DateTime/Language） | legacy | [01](modules/01-text-processing.md)、[04](modules/04-resolution.md) |
| Alias 模块（别名解析） | `src/modules/Bakabase.Modules.Alias/` | [04](modules/04-resolution.md)、[09](modules/09-collections.md) |
| Comparison 模块（加权规则去重，11 种策略含文本相似度） | `src/modules/Bakabase.Modules.Comparison/` | [04](modules/04-resolution.md)、[07](modules/07-execution.md) |
| `ResourceSearchFilterGroup` + `PathFilter`（过滤模型与 UI） | `src/abstractions/.../Models/Domain/` | [06](modules/06-layout.md)、[08](modules/08-orchestration.md) |
| 显示名模板引擎（`{属性名}` 占位 + 包装符空值丢弃，`ResourceUtils.SplitDisplayNameTemplateIntoSegments`，有测试） | legacy `ResourceService` | [06](modules/06-layout.md) |
| BTask（一次性 fluent builder、进度/暂停/取消、冲突键、SignalR） | `src/abstractions/.../Components/Tasks/` | [08](modules/08-orchestration.md) |
| Workflow 模块（注册表/描述符/配置表单模式；触发器与活动） | `src/modules/Bakabase.Modules.Workflow/` | 本文件组件模型、[08](modules/08-orchestration.md) |
| `CompressedFileService`（压缩包处理） | legacy | [02](modules/02-extractors.md)、[07](modules/07-execution.md) |
| FileMover / FileNameModifier / file-processor 页 | legacy + `src/web/src/pages/` | [08](modules/08-orchestration.md)（触发形态）、[07](modules/07-execution.md) |
| Sharable 体系（配置分享） | `src/abstractions/.../Models/Domain/Sharable/` | 组件模型（种子分享） |
| AI 模块（多 Provider、缓存、Enhancer 桥接） | `src/modules/Bakabase.Modules.AI/` | [04](modules/04-resolution.md)（后期辅助消歧） |
