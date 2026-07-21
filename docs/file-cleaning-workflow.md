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
}
```

核心不变量：**链上一切文本变更只作用于 `WorkingName`；磁盘操作只发生在 saveName 节点**。这天然把"计算新名字"与"落盘"分成两段，预览因此免费。

## 节点设计

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

> 为什么第一版把 constants 与枚举合并进触发器：现有 Runner 的 `WorkflowItemOutcome` 只有 Keep/Drop/Replace，**没有链中 1→N 展开**。若后续需要"枚举出子级后再枚举孙级"这类链中展开，引擎增加一个 `ExpandTo(IReadOnlyList<object>)` outcome 即可（小改，向后兼容）——列入开放问题，第一版不做。

### Filter 活动（可选，收窄处理范围）

| kind | 行为 |
|---|---|
| `filter.fs.byRegex` | WorkingName 匹配/不匹配正则 |
| `filter.fs.byExtension` | 按扩展名/扩展名组（复用 ExtensionGroup） |
| `filter.fs.byType` | 只要文件 / 只要文件夹 |

### Transform 活动（文本变更节点，任意个、任意序）

| kind | 行为 | 配置 |
|---|---|---|
| `transform.text.fileNameOp` | 包装 FileNameModifier 的**全部**既有操作 | Target（全名/不含扩展名/扩展名）× Operation × Position × 参数——与 file-name-modifier 页同一套模型与 UI 组件 |
| `transform.text.removeWrapped` | **删除包装符内命中文本集的片段**（用户举例的场景） | 包装符对（引用 SpecialText Wrapper 或自定义对）+ 文本集引用（见下）+ 命中方式（等于/包含/正则） |
| `transform.text.removeTexts` | 删除命中文本集的裸文本片段 | 文本集引用 + 命中方式 |
| `transform.text.trim` | 清理残留：连续空格/头尾空白与分隔符/空括号对 | 开关组 |

每个 Transform 输出 `item with { WorkingName = 新值 }`（Workflow 的 `ReplaceWith`，类型不变仍是 `item.fs.entry`——编辑器类型校验直接通过）。

### Action 活动：`action.fs.saveName`

- `WorkingName == OriginalName` → 跳过（无操作）。
- 不同 → 按运行模式：
  - **Preview（默认）**：不碰磁盘，把 `(Path, OriginalName → WorkingName)` 记入本次 run 的改名计划；
  - **Apply**：执行改名，写 `FileRenameRecord` 日志。
- 内置防线（不可关闭）：改名前 sanitize（非法字符/保留名/结尾点空格）；目标重名冲突 → 该条记入冲突列表跳过，不中断整个 run；路径长度预检。

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

现状：`SpecialTextType` 固定枚举七种（Useless/Wrapper/Standardization/Volume/Trim/DateTime/Language），行为 (Type, Value1, Value2)。扩展方案（最小改动）：

1. 枚举新增 `SpecialTextType.Custom = 100`；
2. `SpecialText` 表加一列 `string? CustomTypeName`（仅 Type=Custom 时有值）——同名即同一个"文本集"，不引入新表；
3. text 管理页：自定义类型与内置类型并列展示，支持建集、批量录入、导入导出；
4. 节点里的"文本集引用" = 内置类型名或自定义类型名（如「字幕组名单」「广告词」）。

> EF 迁移按仓库规则 CLI 生成、纯 schema。若未来自定义类型需要元数据（描述/颜色/分享），再升级为独立表，引用方式不变。

## 执行与 UI

- 流程定义、编辑、运行记录全部走 workflow 页既有 UI；本功能新增的是：触发器配置面板（roots + 枚举配置）、三类活动的配置表单（复用 file-name-modifier 页的操作编辑组件）、预览 diff 面板（可参考 BulkModification 预览页交互）。
- 运行承载沿用 Workflow 的 run 机制；大目录场景下 run 内部逐条目 yield，进度在 run 详情展示。
- 入口：workflow 页新建"文件清洗"模板；file-name-modifier 页加一个"升级为清洗流程"引导（把当前操作集一键转为 `transform.text.fileNameOp` 节点）。

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

## 开放问题

- 链中 1→N 展开（`ExpandTo` outcome）：等真实需求出现再做。
- 递归深度 >1 时父子目录同 run 改名的顺序问题（先子后父可避免路径失效）——实现时定。
- 文本集是否需要分享（Sharable 体系）——倾向于要，随自定义类型使用量决策。
- 定时/监听触发（FileMover 进料口式自动清洗）——依赖 Workflow 触发器形态，后续版本。
