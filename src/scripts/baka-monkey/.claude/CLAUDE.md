# baka-monkey

Bakabase 的 Tampermonkey 油猴脚本，用于在第三方网站上集成 Bakabase 功能（内容追踪、下载、解析等）。

## 技术栈

- **构建**: [vite-plugin-monkey](https://github.com/lisonge/vite-plugin-monkey) + Vite
- **框架**: React + TypeScript
- **UI 组件**: HeroUI（按需引入，不使用全量包）
- **样式**: Tailwind CSS
- **GM API**: 通过 `vite-plugin-monkey/dist/client` 导入（非全局声明），dev/prod 统一

## 项目结构

```
src/
├── main.tsx          # 入口，挂载 React + HeroUIProvider + ToastProvider
├── App.tsx           # 主组件，站点匹配 + 内容扫描 + Portal 渲染
├── api.ts            # GM API 封装（存储、网络请求）
├── settings.ts       # 跨组件共享的偏好设置（含变更订阅）
├── heartbeat.ts      # 后端连通性探测
├── overlay.ts        # HeroUI 浮层的 portal 容器
├── timezone.ts       # 时区偏好
├── types.ts          # SiteConfig / ContentStatus / CoverOverlayConfig 等类型
├── i18n/             # 国际化（中/英）
│   ├── index.ts      # t() 函数 + 语言切换
│   ├── zh.ts         # 中文翻译
│   └── en.ts         # 英文翻译
├── actions/               # 由 SiteConfig 声明的适配器驱动的共享按钮
│   ├── ContentTrackerBadge.tsx
│   ├── DownloadTaskButton.tsx  # 下载：创建 / 再点一次移除
│   ├── ParseTaskButton.tsx     # 解析：创建 / 排队中再点一次取消
│   └── CoverActionOverlay.tsx  # 覆盖封面的“大按钮”图层
├── components/
│   ├── SettingsPanel.tsx  # 浮动设置面板（标记开关、大按钮开关、API 地址、语言、时区）
│   └── Toast.tsx          # Toast 封装（基于 HeroUI addToast）
├── utils/
│   ├── batcher.ts    # 请求合批
│   └── cover.ts      # 从缩略图向外找出封面元素
└── sites/                 # 站点适配，每个目录实现 SiteConfig 接口
    ├── exhentai/          # config.tsx + adapters.ts
    └── soulplus/          # config.ts + adapters.ts
```

## 关键设计

### Dev 模式

- `vite.config.ts` 中 `isDev` 控制 userscript `match` 字段：dev 下匹配所有 URL (`*://*/*`)
- `__DEV__` 常量通过 Vite `define` 注入运行时，App 组件在 dev 模式下即使无匹配站点也显示 SettingsPanel
- GM API 通过模块导入 `vite-plugin-monkey/dist/client`，dev 和 prod 下均可用

### 站点适配

每个站点实现 `SiteConfig` 接口，定义域名匹配、内容发现、信息提取、标记渲染等逻辑。新增站点只需在 `sites/` 下创建新文件并在 `main.tsx` 注册。

### 封面大按钮（cover overlay）

站点在 `SiteConfig.coverOverlay` 中声明 `findCover` 后，即可在设置面板里逐站点
开启：`App.tsx` 会在封面上放一个 portal 宿主 `.bk-cover-overlay-host`（自身
`pointer-events:none`，未开启时不会抢走封面原有的点击），由
`CoverActionOverlay` 渲染出覆盖整张封面的图层，执行该站点的主操作（有
`downloadTask` 就是下载，否则是解析）。

由于图层接管了“点击封面进入详情页”的手势，Ctrl / ⌘ / 中键点击会改为打开条目原
页面而不是执行操作。

### 任务的二次点击 = 移除

`DownloadTaskButton` / `ParseTaskButton` 会根据后端返回的任务状态切换动作：已在
下载列表中的画廊、排队中的解析任务，再点一次即从任务列表中移除（用于误添加时的
取消）。下载记录（`downloadedAt`）不参与该判断——它在任务删除后依然保留，只作为
“以前下过”的提示。已完成的解析任务保留“重新提取”，避免一次误点丢掉解析结果。

### UI 组件

使用 HeroUI 按需安装的独立包（如 `@heroui/button`、`@heroui/chip`），不引入 `@heroui/react` 全量包。新增 UI 需求优先查找 HeroUI 组件。

### 分发

- 构建产物为 `dist/bakabase.user.js`
- CI 构建后上传到 OSS（`oss://anobaka-public/app/bakabase/scripts/bakabase.user.js`），通过 CDN 分发
- 脚本不依赖后端注入，API 地址由用户在 SettingsPanel 中配置（通过 GM_setValue 持久化）
- 后端 `TampermonkeyService.Install()` 打开 CDN URL 触发 Tampermonkey 安装
- 后端 `GET /tampermonkey/script/bakabase.user.js` 重定向到 CDN URL（兼容旧链接）
