[中文](/README.md) | [English](/README-en.md)

# Bakabase

Bakabase 帮你管理本地媒体——动画、漫画、音声、本子、电影、图集，按你定义的属性组织起来，自动抓取元数据，并提供完整的资源和文件操作能力。

![Bakabase](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/hero.png)

## 目录

- [安装与更新](#安装与更新)
- [开发中的功能](#开发中的功能)
- [功能](#功能)
  - [数据卡片](#数据卡片)
  - [自定义资源详情布局](#自定义资源详情布局)
  - [Steam、DLsite、ExHentai 平台集成](#steamdlsiteexhentai-平台集成)
  - [Tampermonkey 脚本](#tampermonkey-脚本)
  - [AI 功能](#ai-功能)
  - [资源健康度评分](#资源健康度评分)
  - [其他改进](#其他改进)
  - [资源管理](#资源管理)
  - [元数据增强](#元数据增强)
  - [媒体库配置](#媒体库配置)
  - [内置播放器](#内置播放器)
  - [工具](#工具)
  - [数据管理](#数据管理)
- [文档](#文档)
- [参与贡献](#参与贡献)
- [Star History](#star-history)

## 安装与更新

- [正式版下载](https://github.com/anobaka/Bakabase/releases/latest)：适合日常使用。
- [测试版下载](https://github.com/anobaka/Bakabase/releases)：选择带 `2.4.0-beta` 的发布，体验下方[开发中的功能](#开发中的功能)。测试版功能和数据结构仍可能调整，升级前请保留完整数据备份。

### 桌面一体版

一体版包含本机服务端和桌面窗口，也能管理已配对的其他服务端。打开对应发布页，使用正文中的下载链接或底部的 **Assets**，按电脑平台选择：

| 平台 | 推荐安装包 | 其他选择 |
| --- | --- | --- |
| Windows x64 | `Bakabase-<版本>-win-x64-Setup.exe` | `…-Portable.zip` 免安装版，手动替换程序更新 |
| macOS Apple Silicon（M 系列） | `Bakabase-<版本>-osx-arm64-Setup.pkg` | — |
| macOS Intel | `Bakabase-<版本>-osx-x64-Setup.pkg` | — |
| Linux / NAS | 使用下方 Docker 服务端 | — |

运行安装包，按提示完成安装；不要将 `.nupkg` 更新包或 GitHub 的 **Source code** 压缩包当作安装包。2.4 测试版首次启动会显示初始化向导：选择数据保存目录，可创建空白数据，也可从已停止的其他 Bakabase 实例导入。媒体原文件不会随应用数据自动复制，详见 [Setup 与数据导入](docs/setup.md)。已有用户升级后沿用原数据目录。

**以后如何更新：**

1. 使用安装版，在 **系统 → 配置 → 系统信息 → 当前版本** 中点击“检查更新”。
2. 想接收 2.4 测试版时，开启“接收测试版更新”；关闭后只检查正式版，不会自动把已有数据降级到旧版本。
3. 有可用更新时按页面提示下载；如果已经自动开始下载，等待显示“更新就绪”后点击“重启更新”。更新保留数据目录、工作区、设备配对和路径映射；仅替换程序文件。

免安装版或源码运行不支持安装器更新，请正常退出后手动替换程序，并保留原数据目录。安装版更新失败时，也可从发布页下载同平台的新安装包更新。

**从 2.3 之前的版本升级：** 2.3 更换了安装器，旧更新器不能直接跨越到新版，需要手动下载安装包并全新安装一次。先完整备份旧数据并退出旧程序；使用 2.4 测试版时，在首次向导选择“从已有数据导入”，或进入 **系统 → 配置 → 工作区 → 当前工作区的数据目录 → 导入已有数据**。旧的 2.3 正式版请按其发布说明迁移数据。不要把数据保存在程序安装目录内，也不要让两个实例共用一个正在写入的数据目录。

### Docker 服务端

Docker 版包含服务端和网页界面，适合 NAS 或常开设备。以下流程以 2.4 测试版为准，提供 Linux AMD64 / ARM64 镜像，Apple Silicon 可直接使用 ARM64。镜像标签见 [Docker Hub](https://hub.docker.com/r/anobaka/bakabase/tags)，**使用明确版本号，没有 `latest` 标签**；旧标签的能力和架构以对应发布说明为准。

使用仓库的 Compose 脚本部署（需要 Docker Compose、Git、Bash 和 Python 3；Windows 可在 WSL 中执行）：

```sh
git clone https://github.com/anobaka/Bakabase.git
cd Bakabase
cp docker/.env.example docker/.env
```

**启动前先配置保存位置和挂载。** 容器不能自动访问电脑或 NAS 的所有目录：

| 用途 | 宿主机来源（`source`） | Bakabase 内的路径（`target`） |
| --- | --- | --- |
| 应用数据：数据库、设置、封面和设备连接，必需 | `.env` 中的 `BAKABASE_DATA_DIR` | `/data` |
| 媒体文件，可选 | `/absolute/path/to/media` | `/media` |
| 下载文件，可选 | `/absolute/path/to/downloads` | `/downloads` |

媒体、下载和导入来源需要另外挂载，不会因为挂载了 `/data` 就自动可用。NAS 目录应先在宿主机挂载并确认可访问。

编辑 `docker/.env`，替换以下版本和路径；数据目录需提前创建。版本必须是 Docker Hub 中已发布的标签：

```dotenv
BAKABASE_IMAGE=anobaka/bakabase:<版本号>
BAKABASE_DATA_DIR=/absolute/path/to/bakabase-data
```

再新建 `docker/compose.local.yaml`，将希望使用的目录挂载进去，例如：

```yaml
services:
  server:
    volumes:
      - type: bind
        source: /absolute/path/to/media
        target: /media
        bind:
          create_host_path: false
      - type: bind
        source: /absolute/path/to/downloads
        target: /downloads
        bind:
          create_host_path: false
```

`source` 是宿主机上**已存在的绝对路径**，`target` 是 Bakabase 中使用的容器路径：此例中媒体库选择 `/media`，下载目录选择 `/downloads`。替换占位路径，删除不需要的挂载；更多配置见[挂载示例](docker/compose.local.example.yaml)。需要导入旧数据时，将已停止的旧实例数据副本另行只读挂载为 `/import`，不要与新实例共用可写 `/data`。

配置后启动：

```sh
./docker/image.sh pull
./docker/image.sh up -d --no-build
./docker/image.sh logs -f server
```

首次启动使用日志中 `Server setup:` 后的完整链接完成设置，保留链接中的令牌；随后访问 `http://localhost:34567`。默认仅本机可访问；局域网访问、配对和导入流程见 [Docker 安装教程](docs/docker-installation.md)。以后添加或更改挂载，执行 `./docker/image.sh up -d --no-build --force-recreate` 重建容器；仅 `restart` 不会应用新挂载。

**更新：**Docker 网页内的“检查更新”不会替换镜像。先正常停止服务，备份完整的宿主机应用数据目录，备份放在目录之外；不要在服务运行时直接复制 SQLite 文件：

```sh
./docker/image.sh stop server
```

备份完成后，保留原 `.env` 其他设置、所有挂载和数据目录，只把 `BAKABASE_IMAGE` 改为目标版本，再执行：

```sh
./docker/image.sh pull
./docker/image.sh up -d --no-build --force-recreate
```

升级后检查页面中的版本、媒体库及目录访问。不要使用 `down -v` 清理旧服务，它会删除命名卷中的数据。

**从源码验证改动：**在同一仓库，将 `.env` 中的 `BAKABASE_IMAGE` 改为 `bakabase:local`，执行 `git submodule update --init --recursive`，再运行 `./docker/source.sh`，即可在 Docker 内构建并启动。切回官方镜像时，将 `BAKABASE_IMAGE` 改回发布标签，再执行上面的镜像更新命令。两种模式共用项目 `bakabase`、服务 `server` 及同一份本机配置，可互相替换升级；不用再启动第二套容器。保留兼容的数据版本，回退时使用对应版本的完整备份，不要仅回退镜像。

## 开发中的功能

以下功能正在 **2.4.0-beta** 中开发和验证，尚不属于当前正式版；截图后续补充。

- **纯服务端与 Docker**：浏览器管理媒体库，提供 ARM64 / AMD64 镜像；源码构建与已发布镜像共用部署配置和数据目录。
- **初始化与工作区**：选择数据保存目录，导入其他实例并预检、映射旧路径；桌面端支持命名及切换相互隔离的工作区。
- **统一多设备管理**：在原有资源列表中搜索、筛选和管理已授权设备的资源，以设备颜色区分来源；元数据同步到管理端后在本机搜索。
- **数据同步与冲突处理**：同步自定义属性、选项和扩展名组，集中查看设备、数据类型、进度与记录，并批量处理冲突。
- **可复用资源规则与跨设备处理**：复用筛选范围，分别配置播放器、可播放文件等规则，指定能访问文件的设备执行增强或可播放文件处理。
- **浏览器与本机播放**：按客户端能力使用网页或本机播放器，配合路径映射与本机规则打开远端资源，改进混合资源、批量播放和网页播放器操作。
- **集合、订阅与获取工作流**：追踪已有及缺失资源，发现已支持来源的新增内容，通过可配置工作流衔接下载、解压和文件归置。
- **帖子解析**：整理帖子正文与下载线索，结合 AI 提取链接、密码和处理指令，审阅后交给获取资源流程继续处理。
- **收集备忘录**：在时间条上记录收集目标的已收集区间、链接与备注。
- **日常操作改进**：自定义菜单布局、查看资源占用，改进文件处理、后台任务、下载操作、浏览记录及油猴脚本任务反馈。

路径映射不会复制媒体文件或建立网络挂载；跨设备播放和处理仍取决于文件是否可访问，以及播放器和执行设备的能力。

## 功能

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
- **跨平台**：macOS 已支持，Linux 仍在开发中。

<details open>
<summary>截图</summary>

![增强属性配置](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/enhancer-property-config.png)

| 预设配置 | 右键设置 |
|:---:|:---:|
| ![配置](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/quick-set-config.png) | ![设置属性](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/quick-set-context-menu.png) |

![UI 自定义](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/ui-customization.png)

</details>

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
