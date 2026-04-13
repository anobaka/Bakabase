---

### 🚨 Upgrade Notice: Data Migration from Pre-2.3 Versions

> **This notice applies to users upgrading from a version prior to 2.3. New users can ignore this.**

Starting from version 2.3, Bakabase uses a new installation and auto-update system. If you are upgrading from an older version, **you must reinstall Bakabase** using the new installer. Your existing data (database, configurations, etc.) remains in the old installation directory and needs to be manually migrated.

#### Migration Steps

1. **Close Bakabase**.
2. **Locate your old data**: Navigate to your old installation directory and find the `AppData` folder.
3. **Copy to new location**: Copy **the contents** of the old `AppData` folder into the new installation's `AppData` directory. You can find the new `AppData` path in **Settings > System > App Data Path**.
4. **Overwrite if prompted**.
5. **Restart Bakabase**.

#### Verify Correct Structure

After copying, your `AppData` directory should look like this:

```
AppData/
├── bakabase_insideworld.db        ← Main database (required)
├── bootstrap_log.db               ← Log database
├── configs/                       ← Configuration files (required)
│   ├── updater.json
│   └── ...
├── data/                          ← User data
├── backups/                       ← Version backups
├── temp/                          ← Temporary files
└── components/                    ← External components
```

> ⚠️ **Common mistake**: Do not copy the `AppData` folder itself, only its contents. Otherwise you'll end up with `AppData/AppData/...` which won't work.

You can also find a migration guide in the app at **Settings > System > App Data Path > Legacy Data Migration**.

---

### 🚨 升级提醒：2.3 之前版本的数据迁移

> **此提醒适用于从 2.3 之前版本升级的用户，新用户可忽略。**

从 2.3 版本开始，Bakabase 使用了新的安装和自动更新系统。如果你是从旧版本升级，**需要使用新安装包重新安装 Bakabase**。你的现有数据（数据库、配置等）仍保留在旧安装目录中，需要手动迁移。

#### 迁移步骤

1. **关闭 Bakabase** 应用。
2. **找到旧数据**：进入旧安装目录，找到 `AppData` 文件夹。
3. **复制到新位置**：将旧 `AppData` 文件夹中的**所有内容**复制到新安装目录的 `AppData` 目录中。新的 `AppData` 路径可在【系统】-【配置】-【应用数据路径】中找到。
4. **如有冲突选择覆盖**。
5. **重新启动 Bakabase**。

#### 验证目录结构

复制完成后，你的 `AppData` 目录应如下所示：

```
AppData/
├── bakabase_insideworld.db        ← 主数据库（必须存在）
├── bootstrap_log.db               ← 日志数据库
├── configs/                       ← 配置文件（必须存在）
│   ├── updater.json
│   └── ...
├── data/                          ← 用户数据
├── backups/                       ← 版本备份
├── temp/                          ← 临时文件
└── components/                    ← 外部组件
```

> ⚠️ **常见错误**：不要复制 `AppData` 文件夹本身，只复制其中的内容。否则会导致 `AppData/AppData/...` 的嵌套结构。

你也可以在应用内【系统】-【配置】-【应用数据路径】-【旧版数据迁移】找到迁移指南。
