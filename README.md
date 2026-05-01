[中文](/README.md) | [English](/README-en.md)

# Bakabase

Bakabase 帮你管理本地媒体——动画、漫画、音声、本子、电影、图集，按你定义的属性组织起来，自动抓取元数据，并提供完整的资源和文件操作能力。

![Bakabase](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/hero.png)

## 目录

- [下载](#下载)
- [Beta 功能](#beta-功能)
  - [数据卡片](#数据卡片)
  - [自定义资源详情布局](#自定义资源详情布局)
  - [Steam、DLsite、ExHentai 平台集成](#steamdlsiteexhentai-平台集成)
  - [Tampermonkey 脚本](#tampermonkey-脚本)
  - [AI 功能](#ai-功能)
  - [资源健康度评分](#资源健康度评分)
  - [其他改进](#其他改进)
- [正式功能](#正式功能)
  - [资源管理](#资源管理)
  - [元数据增强](#元数据增强)
  - [媒体库配置](#媒体库配置)
  - [内置播放器](#内置播放器)
  - [工具](#工具)
  - [数据管理](#数据管理)
- [文档](#文档)
- [参与贡献](#参与贡献)
- [Star History](#star-history)

## 下载

- [正式版](https://github.com/anobaka/Bakabase/releases/latest)
- [测试版](https://github.com/anobaka/Bakabase/releases) — 包含尚未稳定的新功能

> ## ✅ 数据丢失风险已彻底修复（2.3.0-beta.75）
>
> 从 `2.3.0-beta.75` 起，Windows 默认应用数据目录由 `%LocalAppData%\Bakabase`（与 Velopack 安装目录重合）改为 `%LocalAppData%\Bakabase.AppData`，**升级、卸载、安装包修复**都不再波及用户数据。**绝大多数用户无需任何操作**。
>
> **特殊情况**：如果你装的是 `2.3.0-beta.69` ~ `2.3.0-beta.74` 中的任意一个版本，且**没有**在设置里手动改过应用数据路径，那么升级到 `≥ 2.3.0-beta.75` 后，应用看起来会像"全新安装"——数据并未丢失，但不再被自动加载。恢复方式：在 Windows 资源管理器地址栏输入 `%LocalAppData%\Bakabase` 并回车，从地址栏复制展开后的完整路径（形如 `C:\Users\<你的用户名>\AppData\Local\Bakabase`），然后在 Bakabase **设置 → 系统信息 → 应用数据路径 → 修改** 中粘贴该路径并重启即可。
>
> 低于 `2.3.0-beta.69` 的 `2.3-beta.*` 版本仍然存在原始的应用内升级数据丢失风险，请按 [#1070](https://github.com/anobaka/Bakabase/issues/1070) 的步骤先备份数据再升级。macOS 与 Linux 不受这两个问题影响。

## Beta 功能

### 数据卡片

把"演员、作者、品牌、IP、人物"这类跨资源的实体抽象成卡片：每张卡片有自己的属性（姓名、国籍、代表作……），并通过你指定的属性值与资源自动关联。

- **自动关联资源**：在卡片类型上选一组「匹配属性」，资源在这些属性上与卡片值相同时即建立关联，支持「任一匹配 / 全部匹配」两种模式。
- **自动创建/更新**：增强器、同步等外部数据进入时，按你指定的「标识属性」查找已有卡片，命中则更新，未命中可新建。
- **从已有资源批量生成**：扫描资源里出现过的属性值，一键创建对应的卡片集合。
- **在资源详情中查看**：资源详情会列出与之关联的所有卡片。
- **由卡片反查资源**：点卡片上的搜索按钮，即可在资源页打开一个新搜索 tab，按这张卡片的匹配属性筛选资源。

<details open>
<summary>截图</summary>

![数据卡片0](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/data-card-0.png)
![数据卡片1](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/data-card-1.png)
![数据卡片2](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/data-card-2.png)
![数据卡片3](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/data-card-3.png)

</details>

### 自定义资源详情布局

把资源详情面板按你的喜好重新排版：哪些区块出现、放在哪、占多大。

- **11 个内置区块**：封面、评分、操作、基础信息、层级、简介、播放时间、属性、关联数据卡片、所属媒体库、所属档案，可逐个显隐。
- **网格拖拽排版**：在 4 / 6 / 8 / 12 列网格中拖动重排，拖拽边缘调整跨列、跨行；间距可自定义。
- **弹窗尺寸**：详情弹窗宽高在 50%–95% 视口范围内自由调节，左侧编辑器实时预览。
- **隐藏不丢配置**：隐藏区块保留原有位置和尺寸，下次显示时按原配置恢复；空格悬停可一键插回先前隐藏的区块。
- **动态高度提示**：高度由内容决定的区块在编辑器中以醒目轮廓标注，运行时按列瀑布流自然堆叠。
- **一键还原**：随时回到默认布局；配置全局生效，所有资源详情统一展示。

<details open>
<summary>截图</summary>

![布局编辑器1](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/resource-detail-layout-1.png)
![布局编辑器2](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/resource-detail-layout-2.png)

</details>

### Steam、DLsite、ExHentai 平台集成

直接在 Bakabase 内浏览和管理这些平台上的内容。

<details open>
<summary>截图</summary>

| Steam | DLsite | ExHentai |
|:---:|:---:|:---:|
| ![Steam](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/platform-steam.png) | ![DLsite](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/platform-dlsite.png) | ![ExHentai](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/platform-exhentai.png) |

</details>

### Tampermonkey 脚本

新版油猴脚本：在外部网站一键创建下载任务、快速获取下载地址；部分站点登录后还能自动导入 Cookie。

<details open>
<summary>截图</summary>

| ExHentai | DLsite |
|:---:|:---:|
| ![ExHentai](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/tampermonkey-exhentai.png) | ![DLsite](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/tampermonkey-dlsite.png) |

</details>

### AI 功能

- **AI 增强器**：让 AI 自动分析资源，提取并补全属性。
- **AI 翻译**：增强完成后把属性值翻译为目标语言。
- **AI 文件分析**：结构分析、相似度分组等多种模式。
- **AI 机器人**：对话，执行简单任务。

<details open>
<summary>截图</summary>

![AI 配置](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/ai-config.png)
![AI 文件分析](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/ai-fs-analysis.jpg)
![AI 机器人](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/ai-agent.png)

</details>

### 资源健康度评分

按你定义的规则给资源打分，一眼看出哪些资源缺封面、缺字幕、文件大小异常或目录已失联。

- **评分档（Profile）**：每个评分档包含「成员筛选」（哪些资源参与评分）+「评分规则」（命中规则后从起始分加减增量）。同一资源命中多个评分档时，取最高优先级中的最低分作为最终健康分。
- **文件谓词**：内置「按文件类型计数 / 大小」「文件名正则匹配数」「文件大小越界」「是否有封面」「根目录存在」等开箱即用的判定条件。
- **属性谓词**：直接复用资源筛选器，按任意属性的值参与判定。
- **资源卡片徽章**：70+ 绿色、40–70 黄色、&lt;40 红色，一眼分级；可在显示设置中关闭。
- **评分原因 modal**：点徽章查看哪些规则被命中、各贡献多少分。
- **后台批量评分**：评分作为 BTask 在后台执行，支持取消、重试、查看进度；改动评分档会清缓存触发重新评分。

<details open>
<summary>截图</summary>

| 评分规则配置 | 资源列表和评分原因 |
|:---:|:---:|
| ![评分规则配置](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/health-score-rules.png) | ![评分原因](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/health-score-diagnosis.png) |

</details>

### 其他改进

- **聚合增强属性配置**：在一个页面里管理所有增强器到属性的映射，看清每个属性的数据来自哪。
- **右键快速设置属性**：右键菜单直接设置或批量设置属性值，可配置预设值。
- **UI 自定义**：调整资源封面尺寸等显示细节。
- **macOS 支持**：测试版已可用，Linux 仍在开发中。

<details open>
<summary>截图</summary>

![增强属性配置](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/enhancer-property-config.png)

| 预设配置 | 右键设置 |
|:---:|:---:|
| ![配置](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/quick-set-config.png) | ![设置属性](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/quick-set-context-menu.png) |

![UI 自定义](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/ui-customization.png)

</details>

## 正式功能

### 资源管理

灵活的属性系统，支持动画、漫画、音声、本子、电影、图集等类型，可按属性搜索和筛选。

<details open>
<summary>截图</summary>

![资源列表](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/1-resource-list-v22.png)
![资源列表](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/1-resource-list.jpg)
![资源筛选](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/1-resource-filter.jpg)
![资源详情](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/1-resource-detail.jpg)
![多值属性](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/1-resource-multiple-scope-property-values.jpg)

</details>

### 元数据增强

从 ExHentai、Bangumi、DLsite、TMDB 等数据源抓取封面、标签等信息。

<details open>
<summary>截图</summary>

![增强器列表](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/4-enhancer-list.jpg)
![增强器详情](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/4-enhancer-detail.jpg)
![增强记录](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/4-enhancer-record.jpg)

</details>

### 媒体库配置

媒体库模板系统，可自定义文件定位规则、属性提取规则和显示名称。

<details open>
<summary>截图</summary>

![媒体库列表](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/2-media-library-list.jpg)
![播放器设置](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/2-media-library-player.jpg)
![模板详情](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/3-media-library-template-detail-1.jpg)
![模板详情](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/3-media-library-template-detail-2.jpg)
![模板列表](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/3-media-library-template-list.jpg)
![属性定位](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/3-media-library-template-property-locator.jpg)
![显示名称](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/3-media-library-template-resource-display-name.jpg)
![资源定位](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/3-media-library-template-resource-locator.jpg)

</details>

### 内置播放器

直接预览和播放图片、视频等媒体。

<details>
<summary>截图 (NSFW)</summary>

![内置播放器](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/1-resource-media-player.png)

</details>

### 工具

下载器（Bilibili、ExHentai、Pixiv 等）、文件移动器、文件名修改器等。

<details open>
<summary>截图</summary>

![Bilibili 下载](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/6-tool-downloader-bilibili.jpg)
![ExHentai 下载](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/6-tool-downloader-exhentai.jpg)
![Pixiv 下载](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/6-tool-downloader-pixiv.jpg)
![下载器](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/6-tool-downloader.jpg)
![文件移动](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/6-tool-file-mover.jpg)
![文件重命名](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/6-tool-file-name-modifier.jpg)

</details>

### 数据管理

别名、缓存、自定义属性、属性转换、扩展组件、同步选项等。

<details open>
<summary>截图</summary>

![别名管理](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/5-data-alias.jpg)
![缓存管理](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/5-data-cache.jpg)
![属性转换](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/5-data-custom-property-conversion.jpg)
![自定义属性](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/5-data-custom-property-detail.jpg)
![属性列表](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/5-data-custom-property.jpg)
![扩展组件](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/5-data-extensions-group.jpg)
![同步选项](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/5-data-synchronization-options.jpg)

</details>

## 文档

更多内容请访问 [项目首页](https://bakabase.anobaka.com/)

## 参与贡献

欢迎参与开发！请查看 [开发文档](https://bakabase.anobaka.com/#/dev/dev)

## Star History

[![Star History Chart](https://api.star-history.com/svg?repos=anobaka/Bakabase&type=Date)](https://www.star-history.com/#anobaka/Bakabase&Date)
