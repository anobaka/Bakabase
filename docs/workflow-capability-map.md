# Workflow 能力全景：现状盘点与扩展路线

> 视角：**Workflow 模块整体**，而非某个场景。[文件清洗](file-cleaning-workflow.md)是下表多数扩展的第一个用户，但每项扩展都是跨场景通用能力。以下现状均已在代码中核实（`src/modules/Bakabase.Modules.Workflow` + `src/apps/Bakabase.Service/Components/Workflow`）。

## 一、引擎现状（能力层）

| 能力 | 现状 |
|---|---|
| 定义与编辑 | `WorkflowDefinition` CRUD + 链式编辑器；`WorkflowDefinitionService` 左到右追踪"当前 item 类型"做静态校验 |
| 类型系统 | item type 字符串标签 + 描述符（`DisplayName` + `ClrType`）；活动契约 `AcceptedInputItemTypes`（空 = 接受任意；异构触发器的 "any" 哨兵不匹配非空清单）+ `OutputBehavior`（Passthrough / Fixed / **AdaptToNext**——AI 节点按下游需求适配输出类型，靠 ClrType 做 JSON 形状推断与反序列化） |
| 执行 | `WorkflowRunner` per-item 驱动；`WorkflowItemOutcome` = Keep / Drop / Replace；per-activity `OnItemError`；`WorkflowRunRehydrator` 运行恢复；runs 持久化 + 步骤统计 |
| 触发 | 触发器注册表；`Matches(payload, filterJson)` 定义级过滤；`ExtractItems` 产出初始 item 列表 |
| UI | workflow 页：定义编辑器（picker 按 Group 分桶）、运行记录 |

## 二、节点现状盘点

**触发器（2）**：

| kind | 产出类型 |
|---|---|
| `subscription.updated` | subscription item（源锁定时为具体类型，否则 "any" 哨兵） |
| `downloader.completed` | downloader completed task |

**Item 类型（5）**：subscription any ｜ pixiv illust ｜ exhentai gallery ｜ search query ｜ downloader completed。

**活动（5）**：

| kind | 类别 | 契约 | 说明 |
|---|---|---|---|
| subscriptionItemTitleContains | Filter | 任意 / passthrough | 名挂 subscription 组，契约实际通用 |
| ai transform | Transform | 任意 / **AdaptToNext** | 通用 AI 节点（提示词驱动，形状推断） |
| exhentai.queryToGallery | Transform | searchQuery → gallery（Fixed） | |
| notification.create | Action | 任意 / passthrough | 通用通知 |
| exhentai.enqueueDownload | Action | gallery | |

**结论**：当前节点面覆盖的是"订阅 → 过滤 → AI 加工 → 下载/通知"一条业务线的最小集；引擎骨架（类型校验、注册表、runs）质量好但被消费得很少。

## 三、引擎扩展清单（均为跨场景通用；标注第一个用户）

| # | 扩展 | 内容 | 第一个用户 | 通用价值 |
|---|---|---|---|---|
| E1 | **手动触发 + 参数面板** | 触发器新形态：用户带 payload 手动运行（当前只有事件触发） | `fs.manualScan`（[清洗](file-cleaning-workflow.md)） | 一切"对一批输入执行流程"的场景；也是调试任何工作流的入口 |
| E2 | **链中展开 `ExpandTo`** | `WorkflowItemOutcome` 增加 1→N 展开；步骤统计需理解基数变化 | `expand.fs.children` | 画廊→多图片、feed→多条目、压缩包→内容物… |
| E3 | **facet 契约** | 描述符增加 `AcceptedItemFacet: Type`（接口谓词，经 ClrType 校验）；facet 节点强制 Passthrough | `transform.text.*` 对 `ITextWorkpiece` | 一切"按能力而非按类型"的节点族（未来：IHasCover、IHasSourceUrl…） |
| E4 | **变量机制** | item 级 `Variables` + `{var:name(:formatter)}` 插值 + 系统变量注入 + 供需软契约 lint | capture/template + fs 系统变量 | 当前节点间**没有任何数据通道**（只有 item 本身）；变量是通用的节点联动机制 |
| E5 | **两阶段运行**（Preview → 确认 → Apply） | run 产出"计划"，确认后按计划执行（不重跑链） | `action.fs.saveName` | 一切有副作用的批量 action（批量入队、批量资源修改）都该有干跑 |
| E6 | 定时 / 监听触发（后续） | cron 触发器；目录监听（FileMover 进料口形态） | 自动清洗 | 全部定时自动化 |

## 四、节点扩展清单（按域）

| 域 | 节点 | 状态 |
|---|---|---|
| fs（新） | 触发器 `fs.manualScan`（constants + 枚举）；`filter.fs.byRegex / byExtension / byType`；`expand.fs.children`；`transform.fs.fileNameOp`；`action.fs.saveName` | [清洗设计](file-cleaning-workflow.md)已定稿 |
| text（新，facet 通用） | `transform.text.removeWrapped / removeTexts / trim / capture / template` | 同上；依赖 [SpecialText 扩展](file-cleaning-workflow.md#specialtext-扩展用户自定义文本类型文本集) |
| 通用（补充候选） | `filter.common.byVariable`（按变量过滤，E4 的自然配套）；`action.common.setVariable`（常量/映射写变量） | 实现 E4 时顺手评估 |
| 既有域延伸（候选） | `action.downloader.enqueue` 泛化（当前仅 exhentai）；`action.resource.registerPath`（把 fs 条目登记入库/触发同步，打通清洗→入库） | 按需求排期 |
| organizer（挂起） | 触发器"整理完成"、活动"运行某 Pipeline/规则集" | 随 [Organizer](local-file-organization/README.md) 复活 |

## 五、串联视角：这些扩展如何互相成立

- E1+E2+E4 组合出"手动对文件系统跑多层级联动流程"（清洗的骨架）；
- E3 使 text 节点族对**任何**未来 item 类型开放（画廊标题清洗零成本）；
- E5 是所有落盘类 action 的安全模型，与 E1 组合出"手动预览-确认"的标准交互；
- E6 + 既有 `downloader.completed` 触发器 + fs/text 节点 = "下载完成 → 自动清洗 → 通知"全自动链，一行引擎代码都不用再加。

## 实施顺序建议

E1 → E3+E4（text 节点按[清洗设计](file-cleaning-workflow.md)的规范**生而 facet 化**，与变量机制同批落地；前置 SpecialText 扩展）→ E2 → E5。E6 独立，随自动化需求排期。
