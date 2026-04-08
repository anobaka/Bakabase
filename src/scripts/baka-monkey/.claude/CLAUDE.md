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
├── types.ts          # SiteConfig / ContentStatus 等类型
├── i18n/             # 国际化（中/英）
│   ├── index.ts      # t() 函数 + 语言切换
│   ├── zh.ts         # 中文翻译
│   └── en.ts         # 英文翻译
├── components/
│   ├── SettingsPanel.tsx  # 浮动设置面板（标记开关、API 地址、语言切换）
│   └── Toast.tsx          # Toast 封装（基于 HeroUI addToast）
└── sites/                 # 站点适配，每个文件实现 SiteConfig 接口
    ├── exhentai.tsx
    └── soulplus.tsx
```

## 关键设计

### Dev 模式

- `vite.config.ts` 中 `isDev` 控制 userscript `match` 字段：dev 下匹配所有 URL (`*://*/*`)
- `__DEV__` 常量通过 Vite `define` 注入运行时，App 组件在 dev 模式下即使无匹配站点也显示 SettingsPanel
- GM API 通过模块导入 `vite-plugin-monkey/dist/client`，dev 和 prod 下均可用

### 站点适配

每个站点实现 `SiteConfig` 接口，定义域名匹配、内容发现、信息提取、标记渲染等逻辑。新增站点只需在 `sites/` 下创建新文件并在 `main.tsx` 注册。

### UI 组件

使用 HeroUI 按需安装的独立包（如 `@heroui/button`、`@heroui/chip`），不引入 `@heroui/react` 全量包。新增 UI 需求优先查找 HeroUI 组件。

### 分发

- 构建产物为 `dist/bakabase.user.js`
- CI 构建后上传到 OSS（`oss://anobaka-public/app/bakabase/inside-world/scripts/bakabase.user.js`），通过 CDN 分发
- 脚本不依赖后端注入，API 地址由用户在 SettingsPanel 中配置（通过 GM_setValue 持久化）
- 后端 `TampermonkeyService.Install()` 打开 CDN URL 触发 Tampermonkey 安装
- 后端 `GET /tampermonkey/script/bakabase.user.js` 重定向到 CDN URL（兼容旧链接）
