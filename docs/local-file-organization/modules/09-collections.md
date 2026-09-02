# 09 · 合集：franchise / 跨域聚合

> 层级：顶层编排 ｜ 依赖：[04 身份判定](04-resolution.md)、[05 字段富集](05-enrichment.md)、[06 布局](06-layout.md)、Alias（[复用总表](../foundations.md#现有资产复用总表)） ｜ 被依赖：无（最顶层）
>
> 职责：把不同 Pipeline 整理出的条目按 IP/系列在**目标布局**上重新聚拢——分流（路由）与聚合（布局）解耦。例：「物语系列」的动画、漫画、OST、画集落进同一个合集目录树。

## 设计要点

1. **IP 是规范化属性，不是新实体**：franchise 写入一个内置约定的资源属性（Multilevel「系列/IP」，支持层级如 `物语系列/伤物语`）。同时服务于：路径模板 `{franchise}`（[06](06-layout.md)）、资源搜索/筛选、显示名模板。
2. **FranchiseResolver 解析链**（证据强度降序）：
   1. Provider 关系图：Bangumi subject 关联（动画↔漫画↔音乐同 IP）、AniList relations、DLsite 系列字段——强证据，自动归并；
   2. 别名聚类：Alias 模块 + SpecialText 清洗后的标题聚类——中证据，**结果一律进复核**（错并风险）；
   3. 用户裁定：复核页手动归属 → 写 [Override](04-resolution.md#数据表)——最高优先级。
3. **跨域归一**：同一 Job 内不同 Pipeline 的条目解析出的 franchise 经别名表归一（「Monogatari Series」=「物语系列」），保证动画和 OST 落进同一合集。
4. **布局表达**：合集只是规则的一种写法——`{franchise}/{domainLabel}/{title…}`；不想要合集就不用该占位符。系统无"合集模式"开关。

## 示例

```
Z:/Library/物语系列/
  动画/化物語 [2009]/…
  音乐/君の知らない物語/…
  漫画/化物語 (vol.01-08)/…
  画集/VOFAN 画集/…
```

## 完成后获得的能力

- 混合类型库按 IP 聚拢的完整演示：一次干跑，动画/漫画/OST/画集各经其 Pipeline 判定身份，再被同一条布局规则聚进合集树。
- franchise 成为普通属性后，搜索"物语系列"直接命中全部跨域资源；显示名模板同样可用。

## 开放问题

- 跨库 IP 归属无全局权威源：聚类错并的复核成本 vs 覆盖率，需真实库数据标定。
- franchise 层级深度约定（系列/子系列/作品？）与 Provider 关系图的映射规则。
