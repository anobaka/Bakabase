# 07 · 执行：计划 / journal / 回滚 / 资源一致性

> 层级：落盘能力 ｜ 依赖：[06 布局](06-layout.md)（目标路径）、[05 字段富集](05-enrichment.md)（属性写入内容）、Comparison / Enhancer 转换链 / `ResourceSourceLink`（[复用总表](../foundations.md#现有资产复用总表)） ｜ 被依赖：[08 编排](08-orchestration.md)
>
> 职责：把"目标路径"变成"安全落盘"。三段式：计划（只读）→ 执行（journal 护航）→ 回滚（逆序回放）。**整理不是裸文件操作**——资源一致性联动是本模块与独立整理工具的分水岭。

## 计划（干跑，默认形态）

- 逐条目产出有序原子操作：`Mkdir / Rename / Move / MergeDir`。
- 冲突检测：目标已存在、同批目标互撞、源在目标子树内。
- **去重闸门**：调用 Comparison 模块比对目标库存量——"库里已有同作" → 提示合并/跳过，而非制造重复。
- 预览 UI：目标树视图 + 逐条目 diff，可单条排除/改判。计划是只读的，**不动任何文件**。

## 执行（用户确认后）

逐操作流程：

```
journal 写 Intent → 文件系统操作 → 同事务更新 Resource.Path(级联子路径) + PathMark → journal 写 Done
```

- 崩溃恢复：按 Intent/Done 差集判断执行到哪，续跑或回退半途操作。
- 同卷 rename 与跨卷 copy+delete 区分记录（回滚语义不同）；执行前做剩余空间与路径长度预检。
- 执行前逐条目校验指纹（大小+mtime）——计划与执行之间文件被外部改动 → 该条目回退复核，不整批失败。
- 被覆盖/被合并的旧文件一律进系统回收站，不物理删除。

## 落库（执行成功后的资源侧收尾）

1. 目标根在媒体库内 → 触发既有同步管线；未入库文件成为新资源。
2. 按身份集合**逐源**写 `ResourceSourceLink`（Source + SourceKey + MetadataJson + MetadataFetchedAt）。
3. [05](05-enrichment.md) 的字段按映射写入资源属性（经 `PropertyValueFactory` / StandardValue 转换，复用 Enhancer 转换链）。整理完成的资源立即带全元数据。
4. **Enhancer 快路径**（对现有模块的调整）：`AbstractKeywordEnhancer` 构建 context 前先查资源的 `ResourceSourceLink`（同源且未过期）→ 直接用缓存元数据。整理与增强收敛为一套匹配；文件被改名后增强不再因文件名变化而失败。

## 回滚

`UndoAsync(jobId)`：逆序回放 journal（Done → 反向操作 → 标 Undone），同步回滚 `Resource.Path`。journal 保留期可配（默认 30 天），过期由清理任务收割。

## 数据表

```
OrganizeJournalEntry   JobId, Seq, Op(Move/Rename/Mkdir/MergeDir), From, To,
                       Phase(Intent/Done/Undone), VolumeMode(SameVolume/CrossVolume), At
```

## 完成后获得的能力

- 干跑计划 + 预览随便看，不碰文件；确认执行后 kill 进程再启动，续跑不重不漏。
- `Undo` 一键完整还原（含 Resource.Path）。
- 已入库资源被整理后，属性值、播放记录、封面**零丢失**；新文件整理完即是带全元数据的资源。
- 例：把 500 个音声压缩包从下载目录整理进库，中途断电——重启续跑；发现某条整错了——复核页改判后单条重整理；整批后悔——Undo 回到原状。

## 开放问题

- journal 与 `Resource.Path` 更新的事务边界（文件系统无事务，DB 侧补偿顺序需要精确定义）。
- 空目录清理的白名单策略（源目录残壳何时删）。
