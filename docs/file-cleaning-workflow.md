# 文件清洗（File Cleaning Workflow）· 设计

> 状态：**设计中，当前焦点**。一句话：**Workflow + File Renamer 的组合**——用户自定义一条清洗流程（constants → 枚举子级 → 若干文本变更节点 → 保存文件名），针对文件夹执行，带预览与撤销。
>
> 与 [Organizer（已挂起）](local-file-organization/README.md)的关系：独立功能、独立成立；不依赖 Organizer 的任何概念。若 Organizer 复活，本功能的文本变更节点可被其复用为原子能力。

## 承载决策：直接构建在现有 Workflow 模块上

Organizer 当初不复用 WorkflowRunner 的三个理由（跨轮状态机、聚合阶段、两阶段确认）在本功能上**全部不成立**：清洗是线性 per-item 流程、无跨条目聚合、预览可由节点模式实现。而 Workflow 模块已有的正是本功能需要的全部骨架：

| 需求 | Workflow 现有能力 |
|---|---|
| 用户自定义流程 | WorkflowDefinition + 链式编辑器（typed-item 校验） |
| 节点分类 | Activity 三类：Filter / Action / Transform |
| 逐值轮询执行 | Runner 的 per-item 驱动（trigger `ExtractItems` 产出初始 item 列表） |
| 运行记录 | WorkflowRun 持久化 + 步骤统计 |

文本操作能力复用 **FileNameModifier**（Insert / AddDateTime / Delete / Replace / ChangeCase / AddAlphabetSequence / Reverse × 目标区域 × 位置，纯字符串函数，已有实现与测试），文本集能力复用并扩展 **SpecialText**（见下）。

## Item 模型

```csharp
// ItemType: "item.fs.entry"（注册 IWorkflowItemTypeDescriptor）
public sealed record FsEntryItem
{
    public required string Path { get; init; }         // 当前完整路径
    public required bool IsDirectory { get; init; }
    public required string OriginalName { get; init; } // 进入链时的名字（不含路径）
    public required string WorkingName { get; init; }  // 工作名：文本变更节点只改它，不碰磁盘
    public Dictionary<string, string> Variables { get; init; } = new();
    // 变量：capture 节点写入、后续节点经 {var:name(:formatter)} 插值消费；
    // expand 展开子项时可继承（inheritVariables）——这是"节点 x 依赖节点 y 结果"的传递通道。
    // fs 域产出 item 时自动注入只读系统变量：var:extension、var:parentName。
    // 变量只在 item 内流动、随继承下发，链保持线性 per-item，无全局状态。
}
// FsEntryItem 实现 ITextWorkpiece：WorkingText ≡ WorkingName（通用文本节点由此作用于它，见"规范"节）
```

核心不变量：**链上一切文本变更只作用于 `WorkingName`；磁盘操作只发生在 saveName 节点**。这天然把"计算新名字"与"落盘"分成两段，预览因此免费。

**类型契约（硬校验，编辑器报错级）**分两种形态：fs 域节点（filter/expand/saveName/fileNameOp）声明 `AcceptedInputItemTypes = ["item.fs.entry"]`；**通用文本节点声明 `AcceptedItemFacet = typeof(ITextWorkpiece)`**（见下"规范"节）——接受任何实现该 facet 的 item 类型，编辑器经 `IWorkflowItemTypeDescriptor.ClrType` 静态校验。两种形态都使本功能节点与其他功能（exhentai/pixiv 等）的专属节点**互斥或按契约互操作**，不存在冲突。变量提供/需求（capture 提供、template 需要）作为**软契约**由编辑器 lint（warning 而非 error——capture 可能条件性命中）。节点出入参明细见[示例](file-cleaning-workflow-example.html) §5。

**节点通用选项**：
- `requiredVars: string[]`——任一变量缺失则跳过本节点（如模板节点缺 `ep` 时保住上游清洗结果）。

## 节点设计

### 规范：通用文本节点与 facet 契约（"trim 的是哪个 property？"的系统性回答）

文本节点（trim/removeWrapped/removeTexts/capture/template）是**真正通用的** `transform.text.*`——它们**只认识"工作文本"**，不认识文件系统。规范分三条：

1. **facet 契约**：定义 `ITextWorkpiece { string WorkingText; IDictionary<string,string> Variables }`。文本节点的输入契约不是具体 item 类型，而是 `AcceptedItemFacet = typeof(ITextWorkpiece)`——接受任何 CLR 类型实现该接口的 item；编辑器经 `IWorkflowItemTypeDescriptor.ClrType`（描述符本就携带）静态校验实现关系，强校验不减弱。
2. **"作用于什么"由 item 类型声明，一处回答、处处生效**：`FsEntryItem.WorkingText ≡ WorkingName`（其类型描述符如此声明，编辑器在节点卡上显示"作用于：WorkingName"）。将来画廊 item 声明 `WorkingText ≡ Title`，同一批文本节点直接可用——不存在"每个节点各自解释 trim 什么"的问题。
3. **fs 语义进入文本节点的唯一通道是系统变量**：fs 域在产出 item 时自动注入只读变量 `var:extension`、`var:parentName`（模板写 `{var:extension}` 而非 fs 专属占位符）。文本节点因此零 fs 感知。

两个刻意的例外与删减：

- `transform.fs.fileNameOp` **留在 fs 域**：它包装 FileNameModifier 的操作集，其 Target（全名/不含扩展名/扩展名）语义就是文件名结构——名字本身已回答作用目标。
- **删除 `scope` 选项**：它是文本节点上的 fs 泄漏。混合流（目录+文件）改由**链序**表达：目录段 → `saveName`（目录计划）→ `expand`（ChildrenOnly，之后链上只有文件）→ 文件段 → `saveName`（文件计划）。文本节点因此不需要知道 item 是文件还是目录。

纯文本算法仍沉淀在无状态服务 `TextOps`（供 file-name-modifier 页与将来的 Organizer 复用）；文本节点是 `TextOps` 经 facet 暴露给 workflow 的形态。

### 触发器：`fileCleaning.manual`（= 用户说的 constants 节点 + 获取子级节点）

手动触发器，payload 即用户录入的常量与枚举配置：

```jsonc
{
  "roots": ["D:/Anime", "E:/Downloads"],   // constants：写死的目录列表
  "enumerate": {
    "target": "Directories | Files | Both",
    "depth": 1,                            // 枚举深度（1 = 直接子级）
    "extensionFilter": ["mkv", "zip"]      // 可选
  }
}
```

`ExtractItems` 对每个 root 按配置枚举，产出 `FsEntryItem` 列表——"针对全部子文件（夹）轮询跑后面的节点"就是 Runner 的既有语义，引擎零改动。

> constants 与首层枚举合并进触发器；**更深层级用链中展开节点**（下方 Expand）。引擎为此新增 `WorkflowItemOutcome.ExpandTo(IReadOnlyList<object>)`（小改、向后兼容）——多层级示例（父目录净标题传给文件命名）证明了它是必需项，已从开放问题转正。

### Expand 活动：`expand.fs.children`（链中 1→N）

| 配置 | 说明 |
|---|---|
| `target` / `extensionFilter` | Files / Directories / Both + 扩展名过滤 |
| `emit` | v1 仅 `ChildrenOnly`（父项被展开消费——其改名计划应在展开**前**由 saveName 节点记录，混合流经链序表达；`ChildrenThenSelf` 留待真实需求） |
| `inheritVariables` | 子项继承父项 Variables（跨层级联动的通道）；展开时同步注入子项的系统变量（extension/parentName） |

### Filter 活动（可选，收窄处理范围）

| kind | 行为 |
|---|---|
| `filter.fs.byRegex` | WorkingName 匹配/不匹配正则 |
| `filter.fs.byExtension` | 按扩展名/扩展名组（复用 ExtensionGroup） |
| `filter.fs.byType` | 只要文件 / 只要文件夹 |

### Transform 活动（文本变更节点，任意个、任意序）

| kind | 契约 | 行为 | 配置 |
|---|---|---|---|
| `transform.text.removeWrapped` | facet | **删除包装符内命中文本集的片段**（用户举例的场景） | 包装符对（TextSetRef，如 builtin:Wrapper）+ 文本集引用 + 命中方式（等于/包含/正则） |
| `transform.text.removeTexts` | facet | 删除命中文本集的裸文本片段 | 文本集引用 + 命中方式 |
| `transform.text.trim` | facet | 清理残留：连续空格/头尾空白与分隔符/空括号对 | 开关组 |
| `transform.text.capture` | facet | **不改文本，只捕获**：正则具名捕获组 → 写入 `Variables` | pattern + onMiss（SkipSilently/标记）。作用于工作文本；路径/扩展名等 fs 上下文经系统变量获得 |
| `transform.text.template` | facet | 以模板**重建**工作文本（跨层级信息组合命名的表达方式） | template（`{var:x(:pad(n))}` / `{originalText}` 插值；文件扩展名写 `{var:extension}`）+ requiredVars |
| `transform.fs.fileNameOp` | item.fs.entry | 包装 FileNameModifier 的**全部**既有操作（**刻意留在 fs 域**：其 Target 语义即文件名结构，名字已回答作用目标） | Target（全名/不含扩展名/扩展名）× Operation × Position × 参数——与 file-name-modifier 页同一套模型与 UI 组件 |

每个 Transform 输出 `item with { WorkingText = 新值 }`（Workflow 的 `ReplaceWith`，item 类型不变——编辑器校验直通）。

### Action 活动：`action.fs.saveName`

- `WorkingName == OriginalName` → 跳过（无操作）。
- 不同 → 按运行模式：
  - **Preview（默认）**：不碰磁盘，把 `(Path, OriginalName → WorkingName)` 记入本次 run 的改名计划；
  - **Apply**：执行改名，写 `FileRenameRecord` 日志。
- 内置防线（不可关闭）：改名前 sanitize（非法字符/保留名/结尾点空格）；目标重名冲突 → 该条记入冲突列表跳过，不中断整个 run；路径长度预检。
- **Apply 排序 `DeepestFirst`**：按路径深度从深到浅执行（先文件后父目录）——父目录改名不会使子条目计划路径失效。

## 预览 → 确认 → 应用 → 撤销

```
用户点「预览」 ─▶ run(Preview)：全链执行，saveName 只记计划
                    └─▶ UI 展示 diff 列表（旧名 → 新名，冲突高亮），可逐条勾选排除
用户点「应用」 ─▶ 对预览产出的计划执行改名（不重跑链，避免两次运行间磁盘漂移）
                    └─▶ 逐条写 FileRenameRecord
用户点「撤销」 ─▶ 逆序回放该次 run 的 FileRenameRecord（To → From）
```

```
FileRenameRecord   RunId, Seq, Path(父目录), From, To, RenamedAt, Undone
```

保留期可配（默认 30 天），由既有清理任务收割。

## SpecialText 扩展：用户自定义文本类型（文本集）

现状：`SpecialTextType` 固定枚举七种（Useless/Wrapper/Standardization/Volume/Trim/DateTime/Language），行为 (Type, Value1, Value2)。扩展方案：

1. 枚举新增 `SpecialTextType.Custom = 100`；
2. 新增小表 `CustomTextType(Id, Name, Description?)`——**类型需要稳定 id**：节点按 id 引用，改名不破坏引用；元数据与将来的分享有落点；
3. `SpecialText` 表加可空列 `CustomTypeId`（仅 Type=Custom 时有值，FK → CustomTextType）；自定义行 Value1 = 文本，Value2 暂不用；
4. 节点里的"文本集引用"统一为一个判别引用 `TextSetRef`：序列化形如 `builtin:{枚举值}` 或 `custom:{id}`——`removeWrapped.textSet` 等参数因此既能引用内置类型也能引用自定义集；
5. text 管理页：自定义类型与内置类型并列展示，支持建集、批量录入、导入导出；示例用的「质量与发布标签」以**可编辑的种子数据**下发（而非硬编码内置类型）；
6. **对既有消费点零影响**：legacy 代码均按具体枚举值查询，`Custom` 行对它们不可见。

> EF 迁移按仓库规则 CLI 生成、纯 schema。**实施顺序：本扩展是整个功能唯一的 schema 变更，第一步先做**——独立可交付（text 页增强本身就有价值），节点实现全部依赖它。

## 执行与 UI

- 流程定义、编辑、运行记录全部走 workflow 页既有 UI；本功能新增的是：触发器配置面板（roots + 枚举配置）、三类活动的配置表单（复用 file-name-modifier 页的操作编辑组件）、预览 diff 面板（可参考 BulkModification 预览页交互）。
- 运行承载沿用 Workflow 的 run 机制；大目录场景下 run 内部逐条目 yield，进度在 run 详情展示。
- 入口：workflow 页新建"文件清洗"模板；file-name-modifier 页加一个"升级为清洗流程"引导（把当前操作集一键转为 `transform.fs.fileNameOp` 节点）。

## 走查示例（用户场景原样落地）

```
触发器: roots=[D:/Anime], enumerate={target:Directories, depth:1}
节点 1: transform.text.removeWrapped  包装符=[]/【】  文本集=自定义「字幕组名单」
节点 2: transform.text.trim           清理残留空格
节点 3: action.fs.saveName

预览:
  [LoliHouse] 進撃の巨人 S03        → 進撃の巨人 S03
  [桜都字幕组] 药屋少女的呢喃        → 药屋少女的呢喃
  Comiket103                        → （无变化，跳过）
应用 → 2 条改名，FileRenameRecord 落库；发现错了 → 一键撤销还原
```

## 完成后获得的能力

- 用户零代码定义任意清洗流程：录常量目录 → 枚举 → 组合任意文本变更 → 预览确认 → 落盘可撤销。
- 自定义文本集成为一等公民（字幕组名单、广告词、发布组后缀…），跨流程复用、可维护。
- Workflow 模块获得第一个文件系统域的触发器与活动集，为后续自动化（下载完成→自动清洗）铺路。
- FileNameModifier 的能力从单页工具升级为可编排节点，原页面保留不动。

## 完整示例

多层级 + 节点联动（文件命名依赖父目录节点捕获的净标题）的 12 节点完整走查，含每节点配置与 item 流转表：**[file-cleaning-workflow-example.html](file-cleaning-workflow-example.html)**。

## 开放问题

- 文本集是否需要分享（Sharable 体系）——倾向于要，随自定义类型使用量决策。
- 定时/监听触发（FileMover 进料口式自动清洗）——依赖 Workflow 触发器形态，后续版本。
- `ExpandTo` 在 Workflow 编辑器类型校验中的表达（展开节点的输出 item 类型仍是 `item.fs.entry`，校验可直通；但步骤统计需理解 1→N）。
