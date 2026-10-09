# Docker 服务端安装

Docker 版适合在 NAS 或服务器上持续运行媒体库。它包含服务端和网页界面，可通过浏览器访问，也可以由另一台电脑上的 Bakabase 一体版配对后管理。Docker 版没有桌面窗口，也不能管理其他服务端。

## 选择镜像版本

官方镜像和已发布标签见 [Docker Hub](https://hub.docker.com/r/anobaka/bakabase/tags)。镜像使用明确的版本标签，**没有 `latest` 标签**。

下方的 `<VERSION>` 必须先替换为 Docker Hub 中已经发布的版本标签。例如 `2.3.0` 为正式版，`2.4.0-beta.489` 为测试版；这些只是版本示例，安装时请按需选择。多设备互联和数据导入需要包含这些功能的版本，旧的 `2.3.0` 正式版不具备本文的完整流程。

本仓库的发布流程会分别构建、验证 `linux/amd64` 和 `linux/arm64`，再发布到同一个版本标签。**工作树里的改动不代表 Docker Hub 的旧标签已有 ARM 镜像**；选择版本时先检查：

```sh
docker buildx imagetools inspect "anobaka/bakabase:<VERSION>"
```

包含 `linux/arm64` 的标签可在 Apple Silicon 的 Docker 环境中直接运行，无需 x64 模拟。Docker 在 macOS 上运行的是 Linux 容器，因此这里用 `linux/arm64`，不是 `osx-arm64`。若旧标签只有 `linux/amd64`，可以显式指定该平台进行模拟，或按下文从源码构建 ARM 镜像。

## 使用 Docker Compose 挂载本机目录

创建独立的服务端数据目录；不要直接把仍在运行的一体版目录交给第二个实例：

```sh
mkdir -p "$HOME/BakabaseServer/appdata" "$HOME/BakabaseServer/downloads"
```

将以下内容保存为 `compose.yaml`，替换镜像版本：

```yaml
name: bakabase
services:
  server:
    image: "anobaka/bakabase:<VERSION>"
    restart: unless-stopped
    stop_grace_period: 60s
    ports:
      - "127.0.0.1:34567:34567"
    environment:
      API_LISTENING_PORTS: "34567"
      BAKABASE_DATA_DIR: /data
      BAKABASE_NODE_NAME: 我的媒体库
    volumes:
      - type: bind
        source: ${HOME}/BakabaseServer/appdata
        target: /data
        bind:
          create_host_path: false
      - type: bind
        source: ${HOME}/BakabaseServer/downloads
        target: /downloads
        bind:
          create_host_path: false
```

无需固定 `platform`：Docker 会从标签中选择与当前主机匹配的架构。`create_host_path: false` 可在路径写错或外置磁盘未挂载时直接报错，避免误启动一个空媒体库。

在该文件所在目录启动：

```sh
docker compose up -d
docker compose logs -f server
```

全新数据目录会先进入首次设置，确认前不会创建应用数据库。日志中的 `Server setup:` 会给出带 `#setupToken=...` 的链接；用该完整链接进入设置。若 Docker 映射了其他宿主机端口，或运行在 NAS 上，只替换链接的地址和端口，保留 `#` 后面的令牌。Docker 不会自动向访客签发首次设置权限。

设置页面第一步选择“不导入”或“从已有数据导入”；导入来源应只读挂载。第二步显示数据保存目录，Docker 的目标由 `/data` 挂载决定，页面不会另选一个容器内临时目录。导入时接着预检来源中的路径，以目录树建立可选的前缀映射并预览修改数量；目标填写容器实际可见的挂载路径。选择和映射自动保存为本机 JSON 草稿，重开向导可恢复、清空，完成后删除。最终确认后自动开始初始化或导入，并显示进度；待显示服务已就绪后，打开 `http://localhost:34567`。首次设置不需要另外重启容器。

已有 Docker 服务也不能在页面中迁移 AppData 路径。若要换宿主机目录，先停止容器，将整个 AppData 复制到新目录，再修改 Compose 的挂载来源并启动；容器内继续使用 `/data`。保留旧目录，确认新目录中的数据库和媒体引用正常后再处理旧副本。

默认仅本机可访问。需要从局域网访问时，把端口映射改为 `"34567:34567"`。Docker 版默认使用“无限制”远程访问：能访问该端口的设备可以管理这个媒体库。需要按设备授权时，在“配置 → 远程访问”中要求配对，然后再开放局域网访问；不要把默认无限制模式的端口直接转发到公网。

### 等效的 docker run 命令

```sh
docker run -d \
  --name bakabase \
  --restart unless-stopped \
  -p 127.0.0.1:34567:34567 \
  -e API_LISTENING_PORTS=34567 \
  -e BAKABASE_DATA_DIR=/data \
  --mount "type=bind,source=$HOME/BakabaseServer/appdata,target=/data" \
  --mount "type=bind,source=$HOME/BakabaseServer/downloads,target=/downloads" \
  "anobaka/bakabase:<VERSION>"
```

## 挂载媒体目录和保留数据

`/data` 保存数据库、配置、封面、日志和设备授权；媒体文件需要单独挂载。例如在 Compose 的 `volumes` 下增加：

```yaml
      - type: bind
        source: /Volumes/Media
        target: /Volumes/Media
        read_only: true
        bind:
          create_host_path: false
```

然后执行 `docker compose up -d` 应用变更。在 Bakabase 中使用容器内路径 `/Volumes/Media`。若原一体版也使用该路径，同路径挂载能保留数据库中的媒体文件引用；挂载为 `/media` 则需要另外调整媒体库中的实际路径。示例为只读挂载；需要下载、移动或删除文件时，删除 `read_only: true` 并确保宿主机目录允许写入。

容器只看到已挂载的目录。浏览器中的资源菜单提供“查看文件夹位置”，可以查看并复制服务端目录路径；浏览器不能直接打开当前电脑的文件管理器。从一体版管理服务端时，播放和打开文件夹由使用者所在的电脑处理，可按需配置容器路径到本机路径的映射，见[服务器切换](server-switching.md)。这类客户端路径映射不会自动改写服务端数据库中的媒体路径。

重建容器时保留同一个本机数据目录和挂载目标。也可继续使用命名卷 `bakabase-data:/data`；`docker compose down` 保留命名卷，`docker compose down -v` 会删除命名卷及其中的数据。设置了 `BAKABASE_DATA_DIR` 后，运行数据目录由部署配置决定；导入会复制数据到该目录，不会改成从来源目录运行。

## 导入已有 AppData

使用包含导入功能的新版本启动服务端后，可在“配置 → 系统信息 → 导入已有数据”中导入一体版或其他服务端实例的数据。

1. 在来源实例中确认**实际数据目录**并完全停止该实例；保留一份完整备份。macOS 默认目录为 `~/Library/Application Support/Bakabase`，但使用过迁移或自定义路径的实例可能不同。
2. 给服务端使用独立的可写 `/data`，将旧目录额外挂载为只读导入来源，例如：

```yaml
      - type: bind
        source: ${HOME}/Library/Application Support/Bakabase
        target: /import
        read_only: true
        bind:
          create_host_path: false
```

3. 执行 `docker compose up -d`，在导入界面中选择或填写 **`/import`**。浏览器选择的是服务端可访问的路径；容器不能直接读取一个尚未挂载的 macOS 路径。
4. 如果旧版把 AppData 的绝对路径写进封面或配置，填写来源实例当时的原始 AppData 路径，例如 `/Users/用户名/Library/Application Support/Bakabase`，以便改写这些引用；它不同于容器中的 `/import`。在共享设置页面中校验并确认。首次设置直接开始；已有服务确认后，由 Setup 自动停止业务子进程，等待其释放数据库与目录锁，再启动维护工作进程。完成后自动启动业务服务，无需重启整个容器。导入复制到 `/data`，旧目录保留。目标现有数据会备份到 `/data/backups/appdata-imports/<id>`；目标原有备份保留，来源的 `backups` 不重复复制。升级所需的数据迁移在服务启动时执行。
5. 挂载原媒体目录并检查资源、封面、配置和路径。Windows 或 macOS 的可执行组件不能在 Linux 容器中运行；本镜像包含发行版原生 `ffmpeg`、`ffprobe` 和 `7zz`。组件发现会跳过旧平台或无法执行的副本，改用容器中的工具；其他依赖仍应在服务端重新发现或安装。

如果来源明确设置了远程访问“禁用”，Docker 导入会拒绝它，避免导入后浏览器无法进入。先在 macOS 原生服务中导入一份副本，将副本配置为允许远程访问并要求配对，再停机、从该副本导入 Docker；程序不会自动放宽旧配置中的权限。

导入完成后可以移除 `/import` 挂载。不要让一体版与服务端同时使用同一个可写 AppData，也不要在来源应用还在写 SQLite 时复制数据库。备份应保留整个目录，而不是只取一个 `.db` 文件。

进度页在扫描、分块复制、校验、备份、安装和数据库启动期间显示阶段、当前文件、文件数、已复制字节、速度和耗时。复制百分比到 100% 后仍需等待校验和服务启动。连接状态与工作进展分别显示；重启或断线时保留最后确认状态并自动重连，持续失联会显示无法确认服务状态，不会据此判断成功或失败。容器内的轻量 Setup 父进程持续提供状态，复制由独立工作进程执行；工作进程异常退出时，父进程仍报告失败并保留进度页。整个容器或父进程退出期间没有实时状态；重启后复制阶段会从头复制，备份和安装按日志恢复。失败时显示所处阶段和恢复建议，技术错误可展开查看。日志中的 `Server maintenance:` 链接也能重新打开进度页；该令牌只允许读取这一轮进度，不能修改设置或绕过导入后的配对要求。

## 从当前源码构建镜像

这条路径在 Docker 内编译前后端，本机不必安装 .NET 或 Node.js。在仓库根目录执行：

```sh
git submodule update --init --recursive
cp docker/.env.example docker/.env
mkdir -p "$HOME/BakabaseServer/appdata" "$HOME/BakabaseServer/downloads"
./docker/source.sh
```

`docker/.env` 可修改镜像名、本机 AppData、下载目录、监听地址、端口和节点名。`BAKABASE_DOWNLOADS_DIR` 默认是 `${HOME}/BakabaseServer/downloads`，统一挂载到 `/downloads`，与 `/data` 内的数据库、配置和缓存分开。升级已有部署时也需先创建这个宿主机目录，或把变量改为已有目录。默认镜像名为 `bakabase:local`；Apple Silicon 默认构建 Linux ARM64，不必启用 amd64 模拟。需要导入或挂载媒体时，复制 `docker/compose.local.example.yaml` 为 `docker/compose.local.yaml` 并调整路径。两个文件均属于本机配置，不进入 Git；源码和镜像模式都会加载相同配置。

Docker 模式的文件选择、文件处理和下载路径只使用已挂载的持久目录；不显示容器内部目录，应用数据目录由 Setup 和对应系统功能管理。手动输入路径和执行历史任务也会检查这一边界，不自动改写旧路径。目录标注只读仍可选择，实际写入由挂载权限和文件系统决定。Setup 使用相同的存储位置列表，并允许选择应用数据挂载作为导入来源或目标。直接运行的原生 server 和一体版保持操作系统目录行为，不要求挂载。

容器固定使用官方 .NET SDK `10.0.401-noble` 和 ASP.NET Runtime `10.0.11-noble`。项目仍以 `net9.0` 编译，容器通过 `DOTNET_ROLL_FORWARD=Major` 使用新运行时；本机原生部署的 SDK 配置不变。本机 Apple M5 / OrbStack 上实测 .NET 9 容器偶发 `SIGILL`；选择 .NET 10 是为了包含官方的 [ARM64 SME/SVE 信号处理修复](https://github.com/dotnet/runtime/pull/127518)，本机退出的具体崩溃栈尚未确认。发布流程为容器生成框架依赖产物，避免内嵌旧运行时；`docker/Dockerfile` 会拒绝携带 `libcoreclr.so` 的旧自包含产物。

也可独立构建并验证：

```sh
./docker/source.sh build
python3 docker/smoke-test.py --image bakabase:local --architecture arm64 --runtime-version 10.0.11
```

验证只使用新建临时目录，检查首次设置、固定挂载路径约束、网页、SQLite、替换容器后的数据保留，以及从只读 `/import` 导入、备份和自动恢复业务服务的实际流程。来源使用较旧的应用版本，确保启动时实际完成升级前自动备份，且备份不包含运行期锁和维护控制文件。测试也会检查工作进程被终止后父进程继续提供失败状态，以及导入后要求配对时，进度仍可读取而普通 API 不会被监控令牌放行。Intel 主机自动构建 `linux/amd64`，验证参数改成 `amd64`。

`source.sh` 先用容器中的 NBGV 读取当前提交的完整版本（支持 Git worktree），再传给构建，避免源码镜像丢失提交高度、错误显示为较旧版本。可用 `./docker/source.sh logs -f server` 查看日志。

修改源码后重新执行 `./docker/source.sh` 即可升级；构建失败时，现有容器仍保留。新镜像替换旧容器，宿主机 AppData 不变，启动时走相同的数据迁移流程。源码构建与官方镜像之间切换时，保持 `/data` 和媒体挂载一致，避免运行比数据库版本更旧的代码。若要回退，使用升级前备份；只回退镜像不保证数据库兼容。

### 源码与打包镜像互相升级

两种方式都使用项目 **`bakabase`**、服务 **`server`**，默认容器名为 **`bakabase-server-1`**。源码模式只在基础配置上增加构建步骤，镜像模式直接使用已有镜像。二者读取同一 `.env`、本机挂载配置和 AppData；不要另外指定项目名称，也不需要先 `down`。

通过 `source.sh` 或 `image.sh` 创建容器时，需要宿主机提供 Python 3（仅使用标准库）。脚本从最终合并并展开变量的 Compose 配置自动读取挂载关系，供系统信息显示宿主机目录和容器内目录，无需再填一份路径映射，也不向容器开放 Docker socket。源码、镜像和独立导出包使用同一机制；挂载修改后重建容器，显示信息随部署更新。`build`、`pull`、`down`、`ps`、`logs`、`config` 等命令不需要 Python。

同一脚本还从这份 Compose 配置读取 HTTP 监听端口对应的 TCP 发布端口，配合宿主机地址生成多设备互联使用的访问地址，无需再配置一次端口。只有当前 Docker context 使用本地 Unix socket 时，才会读取 macOS/Linux 的活跃宿主机 IPv4 网卡；Linux 需要 `ip` 命令。绑定具体 IPv4 时使用该地址，绑定 `0.0.0.0` 时使用检测到的宿主机局域网地址；不公布 loopback、容器网桥或 VPN 隧道地址。远程 Docker context、无法确认的网络、随机发布端口和缺少系统工具时保守留空。`compose run` 默认不发布端口；仅显式 `--service-ports` 且未覆盖端口、环境或入口程序时复用配置。网络或发布端口变更后，通过脚本重建容器即可更新元信息。

地址元信息只描述部署，不会开启局域网访问、调整防火墙或转发发现广播。直接使用 `docker compose` 时没有这份自动元信息，页面仍可根据浏览器实际使用的服务地址提供候选；反向代理、远程 Docker 和多网卡等需要不同地址的部署，可在多设备互联的“本机”页指定“对外访问地址”。不要将 `localhost` 或桌面客户端的本地转发地址分享给其他设备。

直接使用 `docker compose` 仍可正常运行；没有自动生成的挂载信息时，界面显示容器路径，不推测宿主机位置。命名卷不会被显示成宿主机普通目录。`compose run -v ...` 的临时挂载和 `volumes_from` 的继承挂载不属于完整可解析的本服务挂载配置，该次运行同样回退为容器路径。所有文件操作始终使用容器路径，宿主机路径仅用于显示和复制。

若已有部署使用旧默认项目 `bakabase-server` 和服务 `bakabase`，需一次性修改项目名与所有 Compose 文件（包括本机 override）中的服务键。先停止旧容器 `bakabase-server-bakabase-1`，再使用同一镜像和挂载启动新项目；确认可访问、数据正常后再删除旧容器。项目改名不会自动接管旧容器，不能让两个实例同时写同一数据目录。使用命名卷时，还需以 `external: true` 和 `name` 指向原有卷的实际名称，避免新项目创建空卷；本文使用的本机目录挂载不受项目名影响。

源码升级：

```sh
./docker/source.sh up -d --build --force-recreate
```

切到已加载的打包镜像：保持 `BAKABASE_IMAGE` 同名，或在 `docker/.env` 改为目标镜像标签，再执行：

```sh
docker load -i /path/to/bakabase-image.tar
./docker/image.sh up -d --no-build --force-recreate
```

返回源码时，再执行源码升级命令即可。若使用远端发布镜像，先在 `.env` 设置明确的已发布标签，再拉取并替换：

```sh
./docker/image.sh pull
./docker/image.sh up -d --no-build --force-recreate
```

同一标签也可能指向更新的镜像，`--force-recreate` 明确替换现有容器。升级始终保留 `/data` 的宿主机路径；回退程序不能降级数据库，应使用与旧版本匹配的完整数据备份。

构建好的本地镜像还可打包为不依赖源码仓库的部署目录：

```sh
./docker/source.sh build
./docker/package.sh "$HOME/BakabaseServer/packages/my-release" bakabase:local
```

输出包括 `bakabase-image.tar`、基础 `compose.yaml`、`.env.example`、可选挂载示例及镜像架构信息。按其中的 `README.txt` 加载镜像并启动；已有部署应保留原 `.env` 和挂载，不要用示例覆盖。目录中的基础 Compose 与仓库版本使用同一项目名和服务名，所以可替换由源码方式启动的实例。`package.sh` 拒绝覆盖已经存在的输出目录。

### 本机 ARM64 与 NAS Root 挂载

本机示例只挂载两个 NAS 的 Root 内容。先在 macOS 将 Root 分别挂到 `/Volumes/nas-bakabase` 和 `/Volumes/nas-anobaka`，再复制本机 override：

```sh
cp docker/compose.local.example.yaml docker/compose.local.yaml
./docker/image.sh config
```

示例固定 `linux/arm64`，将这两个 Root 挂到容器内相同绝对路径，初始均为只读，不挂载 NAS 的 Public 或 Container。`BAKABASE_MEDIA_ROOT_1`、`BAKABASE_MEDIA_ROOT_2` 可调整宿主机路径；容器路径仍应与媒体库记录一致。`BAKABASE_IMPORT_DIR` 指向离线导入副本，默认 `${HOME}/Downloads/Bakabase`，只读映射为 `/import`。准备好的导入副本可通过修改该变量切换，无需修改基础 Compose。数据目录默认 `${HOME}/BakabaseServer/appdata`，网页默认仅本机 `127.0.0.1:34567`。

`create_host_path: false` 只能阻止路径不存在时创建空目录；使用 NAS 前还应确认该路径实际挂载的是预期 Root。需要媒体写操作时，再明确调整对应挂载权限。修改任何挂载后必须重建容器，普通 `restart` 不会应用新挂载。

配置契约和双向替换验收只使用临时项目、随机端口及临时数据：

```sh
python3 docker/test-compose.py -v
python3 docker/compose-smoke-test.py --image bakabase:local
```

第二项实际导出、重新加载镜像包，再运行源码 → 镜像包 → 源码的三次 Compose 替换，核对容器名称、设备身份、数据文件及 SQLite 完整性。它不启动正式项目或读取正式 AppData。

macOS 原生源码服务部署见[本机服务端部署](server-deployment.md)。

## 从一体版管理服务端

1. 在 Docker 服务端的网页中打开“配置 → 远程访问”，允许远程访问并要求配对。
2. 在电脑的一体版中打开“多设备互联 → 设备与分享 → 管理”，填写 `http://服务器IP:34567`，使用服务端生成的管理配对码完成配对。首次配对可按下方说明从容器日志取得配对码；已有其他已配对设备时，也可以提交请求，再从有管理权限的设备批准。
3. 配对完成后，使用一体版左侧顶部的设备下拉菜单进入这台服务端的界面。

若要求配对且尚无任何已配对设备，无界面服务端会在启动日志中输出首台设备的配对码，可通过 `docker compose logs server` 查看。管理配对允许完整管理；下方的媒体库分享只授予资源的只读访问，两者分别建立授权。

## 向其他设备分享媒体库

服务端浏览器现在也提供“多设备互联”，包括多设备资源库、设备与分享、设备地图和数据同步。可在“设备与分享 → 资源库分享”中启用分享、生成分享码或添加要浏览的设备；数据同步也位于同一菜单下。页面遵循服务端的远程访问设置：无限制模式允许管理员直接使用，要求配对时仍须通过已配对设备访问。服务端可以浏览和分享资源，但不能像桌面客户端一样切换窗口去管理其他服务端；媒体在浏览器中预览，不会在服务端启动播放器或文件管理器。

容器的媒体库分享管理可以在容器内执行：

```sh
docker compose exec server dotnet Bakabase.Service.dll federation status
docker compose exec server dotnet Bakabase.Service.dll federation share on
docker compose exec server dotnet Bakabase.Service.dll federation invite
```

在另一台设备的“多设备互联 → 设备与分享 → 资源库分享”中填写服务端地址和输出的一次性分享码。也可以不填分享码，先提交连接请求，再使用 `federation status` 查看请求 ID，通过 `federation approve 请求ID` 批准。

需要在每次启动时开启分享并输出新分享码时，在 Compose 中增加：

```yaml
    environment:
      API_LISTENING_PORTS: "34567"
      BAKABASE_DATA_DIR: /data
      BAKABASE_NODE_NAME: 我的媒体库
      BAKABASE_FEDERATION_SHARING: "true"
    command: ["--federation-invite-on-start"]
```

分享码在启动日志中显示，并有有效期；过期后可执行 `federation invite` 生成新的分享码。保留 `BAKABASE_FEDERATION_SHARING=true` 会在下次启动时重新开启分享；要持续关闭分享，应先移除该环境变量，再执行 `federation share off`。

浏览器直接访问服务端和分享给其他设备的联合浏览分别处理权限；分享开关不替代远程访问设置。移动端目前可浏览其他设备的媒体库，暂不支持向其他设备分享自己的媒体库。更多说明见[联合媒体库](multi-device-library-implementation.md)。

## 更新

**旧容器若没有配置 `BAKABASE_DATA_DIR`**，升级前先从网页配置确认它实际使用的数据路径。新镜像默认使用 `/data`，因此需要把旧数据复制到持久目录并挂载为 `/data`，或者继续显式配置旧的有效路径及挂载。不要直接替换镜像后把空媒体库误认为旧数据丢失。

停止服务并备份完整数据目录后，将 `image` 改成 Docker Hub 中已经发布的新版本标签，再执行：

```sh
docker compose pull
docker compose up -d
```

保留原数据目录或数据卷以及媒体挂载路径。Docker 版通过替换镜像更新；源码镜像通过重新构建和替换容器更新，两者都由启动迁移升级数据库。桌面安装器的自动更新流程适用于一体版。

## 端口设置

Docker 版用 `API_LISTENING_PORTS` 指定实际监听端口，支持以逗号或分号分隔多个端口。本文只启用 `34567`，网页和 API 共用该端口；Dockerfile 中的 `EXPOSE 34567/34568/34569` 不会自动完成监听或端口映射。不要用 `ASPNETCORE_HTTP_PORTS` 替代 `API_LISTENING_PORTS`。

若只需更换宿主机端口，可将映射改为 `"8080:34567"`，并用 `http://服务器IP:8080` 访问，容器内仍为 `34567`。如果修改了容器内监听端口，映射和环境变量应一起修改；执行 `federation` 管理命令时默认读取 `API_LISTENING_PORTS` 的第一个端口，也可以显式追加 `--port 端口`。
