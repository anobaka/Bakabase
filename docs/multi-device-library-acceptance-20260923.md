# 多设备媒体库：更新基线与原生验收（2026-09-23）

当前主仓库位于 `/Volumes/DevStorage/git/anobaka/Bakabase`，本任务继续使用
`/Users/anobaka/.codex/worktrees/multi-device-library/Bakabase`。不再使用旧的
`Desktop/git` 工作目录。同步主线使用 **rebase**，不向开发分支追加 merge。

## 基线和提交整理

重新获取的 `origin/main` 为 `1037a66ad46002570dac87ee114db6dfc7b4417e`。
开发分支已经 rebase 到它之上，移除开发历史中的两条 merge；随后将相邻的
安装验收、macOS 授权、AX 桥接修复和所属文档整理为 42 个提交（整理前为
73 个非 merge 提交）。八个独立的产品改动保留各自边界，其产品目录 diff
逐项一致。没有重新排序独立的产品修复。

整理后的候选提交为 `be16c2635e60cf27331ae9c8a2ba274b7f5121c5`，完整 tree
为 `fa770f13703cedcc465bc1ddeb1a3f147d11c04f`，与整理前完全一致。
重整时的未提交文件逐字节保留；squash 没有修改工作树或 index。
原历史在本地 `refs/codex/backups/` 保留，映射记录为
`/tmp/bakabase-squash-map-20260923.json`。远端开发分支通过精确旧 SHA 的
`--force-with-lease` 同步；没有修改或发布主线。

以下旧 SHA 仍是不可变 CI 证据的生产标识；不能因 rebase/squash 后源码内容
相同，就把旧 run 记为新提交的完整验收。

## 已核验的新安装包

[Packages 35815491996](https://github.com/anobaka/Bakabase/actions/runs/35815491996)
绑定上述 `be16c263`，六种组合全部通过：Windows x64、macOS Intel/ARM，
每个平台分别安装统一版和旧客户端。六份小证据 ZIP 合计 316,732 bytes，
run/head、大小和 SHA256 均与 GitHub 元数据一致。

十二次 portable / installed 启动均报告 **Core `2.4.0-beta.395`**，合成包版本
为 `0.0.1-acceptance.35815491996.1`；六组安装和清理全部通过，四个 Mac
产品组合的三项 ABI 检查各自通过。版本高度由提交图计算，squash 后不能
继续沿用旧包的 core416。产品文件树一致不等于运行时版本号一致。

独立核验索引：`/tmp/bakabase-package-ci-be16c263-evidence/summary.json`。
完整回归
[Full 35815997273](https://github.com/anobaka/Bakabase/actions/runs/35815997273)，
已通过并独立核验，执行提交精确匹配 `be16c263`。七个实际 job 全部通过；
六份小证据 ZIP 共 1,853,065 bytes，run/head、大小与 SHA256 均匹配。
前端 99 个文件、956 项通过；后端 13 个项目、185 个主测试类共 2,640 项，
其中 2,605 通过、35 项明确手动外网测试跳过。四个平台各 311 项契约通过，
合计 1,244；Mac 两架构 ABI、三节点 smoke、浏览器 114 资源 / 两参与节点、
真实视频 seek、取消、撤权及清理均通过。

独立核验索引：`/tmp/bakabase-ci-be16c263-evidence/summary.json`。

## macOS 数据验收来源

当前 consumer pins 指向 `be16c263` / core395。原始库 producer 单独固定为
run `35728456779`，执行 SHA `23dd11cf63d5eef017ab4be992654825824ab63e`，
产品 SHA `ed61e4cc76a9210db9598a823202ccd5037fb06b` / core400。
它的 **更新前**备份由未修改的已发布 v349 程序通过实际 API 创建。
producer 的 run、执行提交、产品提交、版本和 artifact digest 分别验证，
不会随 consumer 版本一起改写。

新一轮
[ARM 旧版数据升级 35816399510](https://github.com/anobaka/Bakabase/actions/runs/35816399510)
与
[Intel 原始库恢复 35816402586](https://github.com/anobaka/Bakabase/actions/runs/35816402586)
均绑定执行提交 `0ebfce32986d6e7b23aa4e211359379f7e139896`，使用新候选包。
两轮均已通过独立证据复核。`macos-data-arm` 是明确的 ARM 范围；它不把原 v349
Intel 客户端已知的启动 SIGILL 算作通过，也没有修改完整历史安装门禁。
Intel 的目标仍是原始库字节恢复后的候选安装、原生更新、自动及手动重启
与全部原始数据保留。

ARM 的 26 项独立复核全部通过：原 API v349 创建的七张表、三条资源和其余
原字段，在 core395 原生更新、自动及再次手动重启后全部保留；API 语义、
配置与媒体哈希一致。统一版 PID 为 `13283 → updater 13751 → auto 14056
→ restart 14469`；旧客户端为 `13020 → 14110 → 14433 → 14490`。
原生系统授权完成，另一产品连续存活检查分别采样 42 / 25 次，全部清理
通过且没有强制杀进程或残留。证据 ZIP 为 807,552 bytes，SHA256 为
`34ed1f1c1b840b61d13063ac3f2923b7658aafcf0d157207188ed5dc43645191`。

Intel 的 14 项独立复核全部通过：原始 producer artifact、report、更新前
数据库和 v349 API 证据闭环一致；首次安装、原生更新和再次手动重启后，
七表、API 语义、原配置与媒体保持。统一版 PID 为 `5285 → updater 5746
→ auto 5880 → restart 6777`；旧客户端为 `5056 → 6336 → 6691 → 6986`。
原生授权及两个实际默认缓存完整交付通过，另一产品连续性采样 64 / 52 次，
全部清理通过且无强制杀进程或残留。证据 ZIP 为 688,048 bytes，SHA256 为
`b903a2ce56592c86bda972a6f85680a8beba744e053fbe6c36379fbadc3b7591`。

独立细节分别在 `/tmp/bakabase-macos-data-arm-0ebfce32-evidence/` 与
`/tmp/bakabase-macos-data-restore-0ebfce32-evidence/`。上述数据来源和升级范围
没有把旧 Intel v349 启动失败改标为成功。

## 原生 GUI 的已知边界

[诊断 35814986975](https://github.com/anobaka/Bakabase/actions/runs/35814986975)
绑定重整前执行提交 `f844be1a`，使用 `0d968896` 的 core416 包。
三份证据 ZIP 共 469,873 bytes，来源和哈希均独立核验。Windows 空库界面
流程通过；Mac 两架构、两个产品都在同一个跨进程 AX 边界安全失败，不能
记为完整 GUI 流程通过。

四个角色各十次观察都确认：节点仍处于已验证父级的原 child edge，窗口仍
属于原应用，返回的 `AXParent` / `AXWindow` 引用分别与所持父节点 / 窗口
相等，两个 AXError 均为零且 PID 稳定。独立 libproc 采样确认四个关联
进程都是稳定的系统 WebKit WebContent 进程，PPID 为 1。诊断仅记录事实，
没有按可执行文件名称或 PPID 放行外部子树，且四个角色均未执行动作。

对应索引：`/tmp/bakabase-native-gui-f844be1a-evidence/pid-boundary-summary.json`。
后续完整联机界面门禁仍需实际执行并独立核验；浏览器或 API 测试不能替代。
