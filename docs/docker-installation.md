# Docker 服务端安装

Docker 版适合在 NAS 或服务器上持续运行媒体库。它包含服务端和网页界面，可通过浏览器访问，也可以由另一台电脑上的 Bakabase 一体版配对后管理。Docker 版没有桌面窗口，也不能管理其他服务端。

## 选择镜像版本

官方镜像和已发布标签见 [Docker Hub](https://hub.docker.com/r/anobaka/bakabase/tags)。镜像使用明确的版本标签，**没有 `latest` 标签**。

下方的 `<VERSION>` 必须先替换为 Docker Hub 中已经发布的版本标签。例如 `2.3.0` 为正式版，`2.4.0-beta.489` 为测试版；这些只是版本示例，安装时请按需选择。多设备互联需要包含该功能的版本，旧的 `2.3.0` 正式版不具备本文的完整流程。目前发布的镜像为 `linux/amd64`，适用于 x64 NAS 和服务器；ARM 设备需要自行配置 amd64 模拟运行。

## 使用 Docker Compose

将以下内容保存为 `compose.yaml`：

```yaml
services:
  bakabase:
    image: "anobaka/bakabase:<VERSION>"
    platform: linux/amd64
    container_name: bakabase
    restart: unless-stopped
    ports:
      - "34567:34567"
    environment:
      API_LISTENING_PORTS: "34567"
      BAKABASE_DATA_DIR: /data
      BAKABASE_NODE_NAME: 我的媒体库
    volumes:
      - bakabase-data:/data

volumes:
  bakabase-data:
```

在该文件所在目录启动：

```sh
docker compose up -d
docker compose logs -f bakabase
```

等待日志显示服务已启动后，在部署机器上打开 `http://localhost:34567`，或从局域网其他设备打开 `http://服务器IP:34567`，并按网页提示完成首次初始化。

Docker 版默认使用“无限制”远程访问：能访问该端口的设备可以管理这个媒体库。上述端口映射用于可信局域网；需要按设备授权时，在“配置 → 远程访问”中选择开启远程访问并要求配对。不要把默认无限制模式的端口直接转发到公网。

### 等效的 docker run 命令

```sh
docker run -d \
  --name bakabase \
  --platform linux/amd64 \
  --restart unless-stopped \
  -p 34567:34567 \
  -e API_LISTENING_PORTS=34567 \
  -e BAKABASE_DATA_DIR=/data \
  --mount type=volume,source=bakabase-data,target=/data \
  "anobaka/bakabase:<VERSION>"
```

## 挂载媒体目录和保留数据

`/data` 保存数据库、配置、日志和设备授权；媒体文件需要单独挂载。在 Compose 的 `volumes` 下增加实际存在的宿主机媒体目录，例如：

```yaml
    volumes:
      - bakabase-data:/data
      - /srv/media:/media:ro
```

然后执行 `docker compose up -d` 应用变更。在 Bakabase 中添加媒体库时使用容器内路径 `/media`。示例为只读挂载；若需要由 Bakabase 下载、移动或删除文件，将 `:ro` 去掉，并确保宿主机目录允许容器写入。

容器只看到已挂载的目录。播放器、打开文件夹等桌面操作由使用者所在的电脑处理；从一体版管理服务端时，可按需配置容器路径到本机路径的映射，见[服务器切换](server-switching.md)。

可将数据卷改成宿主机目录，例如 `/srv/bakabase-data:/data`，便于自行备份。重建容器时保持同一个数据卷或目录；`docker compose down` 保留命名卷，`docker compose down -v` 会删除命名卷及其中的数据。设置了 `BAKABASE_DATA_DIR` 后，数据目录由部署配置决定，应用内的数据路径迁移不可用。

## 从一体版管理服务端

1. 在 Docker 服务端的网页中打开“配置 → 远程访问”，允许远程访问并要求配对。
2. 在电脑的一体版中打开“多设备互联 → 设备与分享 → 管理”，填写 `http://服务器IP:34567`，使用服务端生成的管理配对码完成配对。首次配对可按下方说明从容器日志取得配对码；已有其他已配对设备时，也可以提交请求，再从有管理权限的设备批准。
3. 配对完成后，使用一体版左侧顶部的设备下拉菜单进入这台服务端的界面。

若要求配对且尚无任何已配对设备，无界面服务端会在启动日志中输出首台设备的配对码，可通过 `docker logs bakabase` 查看。管理配对允许完整管理；下方的媒体库分享只授予资源的只读访问，两者分别建立授权。

## 向其他设备分享媒体库

容器的媒体库分享管理可以在容器内执行：

```sh
docker exec bakabase dotnet Bakabase.Service.dll federation status
docker exec bakabase dotnet Bakabase.Service.dll federation share on
docker exec bakabase dotnet Bakabase.Service.dll federation invite
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

备份数据目录和媒体文件后，将 `image` 改成 Docker Hub 中已经发布的新版本标签，再执行：

```sh
docker compose pull
docker compose up -d
```

保留原数据卷和媒体挂载路径。Docker 版通过替换镜像更新；桌面安装器的自动更新流程适用于一体版。

## 端口设置

Docker 版用 `API_LISTENING_PORTS` 指定实际监听端口，支持以逗号或分号分隔多个端口。本文只启用 `34567`，网页和 API 共用该端口；Dockerfile 中的 `EXPOSE 34567/34568/34569` 不会自动完成监听或端口映射。不要用 `ASPNETCORE_HTTP_PORTS` 替代 `API_LISTENING_PORTS`。

若只需更换宿主机端口，可将映射改为 `"8080:34567"`，并用 `http://服务器IP:8080` 访问，容器内仍为 `34567`。如果修改了容器内监听端口，映射和环境变量应一起修改；执行 `federation` 管理命令时默认读取 `API_LISTENING_PORTS` 的第一个端口，也可以显式追加 `--port 端口`。
