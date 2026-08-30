# Bakabase 双端 App 鸿蒙（HarmonyOS）适配技术验证报告

| 字段 | 值 |
|---|---|
| 状态 | 已完成（M5 交付物） |
| 结论 | **维持 Later**：暂不启动适配，按下文触发条件重新评估 |
| 最后更新 | 2026-08-29 |
| 关联 | `docs/mobile-app-design.md` §8.4 |

---

## 1. 背景

App（`src/apps/mobile/app`，Flutter 3.47.2）已完成 M1–M4。设计阶段即为鸿蒙预留了两个口子：发现层的 UDP 探测通道（不依赖平台 mDNS API）与播放/发现的可替换实现边界。本报告验证"现在启动 ohos 适配"的实际成本。

## 2. 工具链现状

- 鸿蒙 Flutter 走 OpenHarmony-SIG 的 [flutter_flutter](https://gitee.com/openharmony-sig/flutter_flutter) fork，可用版本停留在 **3.22.x / 3.27.4-ohos**，要求 API 12 与 DevEco Studio 5.0+；社区反馈升级到 3.30+ 会出现 hvigor 不兼容与 HAP 打包失败。
- 我们的 App 基于 **Flutter 3.47.2 / Dart 3.10**。落到 fork 意味着**降级约两个大版本**，且代码里已用到 Dart 3.8+ 语法（null-aware 集合元素、通配符参数——各一处，回退是小事），真正的负担是**双工具链长期并行维护**：依赖版本、lint、CI 全部要按两套 SDK 各自锁定。
- 构建链需要 DevEco/hvigor/ohpm；GitHub 托管 runner 无此环境，CI 需自托管 runner 或手工出包；发布走 AppGallery（HarmonyOS NEXT 侧载受限，没有 SideStore 式的分发路径）。

## 3. 依赖逐项核查（按 App 实际 pubspec）

| 依赖 | ohos 现状 | 应对 |
|---|---|---|
| dio / flutter_riverpod | 纯 Dart，无碍 | 直接可用 |
| shared_preferences / url_launcher | OpenHarmony-SIG [flutter_packages](https://gitee.com/openharmony-sig/flutter_packages) 有官方级适配（git 依赖 override） | 低成本 |
| cached_network_image | 依赖链（flutter_cache_manager → path_provider/sqflite）在 TPC 均有适配 | 低成本，需逐个 override |
| **bonsoir**（mDNS 发现） | **无 ohos 实现**；OpenHarmony 有原生 `@ohos.net.mdns` API，可自写插件 | **不阻塞**：设计上 ohos 客户端走 UDP 探测通道（`RawDatagramSocket` 纯 dart:io，开箱即用），mDNS 可后补 |
| **media_kit**（播放核心） | **无官方 ohos 支持**；有社区改造 fork（libmpv ohos 化），真机部署仍需手工修补，成熟度不足 | **主要阻塞点**。备选：`video_player_ohos`（系统 AVPlayer，编解码覆盖远弱于 libmpv，MKV/DTS 场景退化）或外部播放器 handoff（ohos Want 机制，需自写插件，且生态内可用第三方播放器有限） |
| android_intent_plus | 仅 Android | ohos 外部调起需自写（ArkTS Want） |

## 4. 结论与建议

**维持 Later。** 现在启动的话，真实工作量 ≈ 工具链降级回退 + 全依赖 override 验证 + 自写 2 个平台插件（发现调起）+ 播放核心换血或接受体验退化 + 自托管 CI，并从此背上双 SDK 维护；而收益端（鸿蒙设备上的真实用户需求）尚未得到验证。

**已付定金保持有效**：UDP 探测通道、`lib/discovery` / `lib/playback` 的实现边界、独立的 mobile 版本线，都让未来适配是"加一个平台实现"而非重构。

**重新评估的触发条件**（满足任意两条即可立项）：
1. flutter_flutter fork 跟进到 ≥ 我们正在用的 Flutter 大版本（消除降级/双工具链成本）；
2. media_kit 的 ohos 支持进入官方或稳定社区渠道（消除播放核心风险）；
3. 出现明确的鸿蒙用户需求（issue/讨论区可量化）；
4. 团队具备鸿蒙签名/上架条件（AppGallery 开发者账号 + 自托管构建机）。

立项时的第一步应是：用 fork 的 Flutter 版本单独出一个 `ohos/` 平台目录的 PoC 分支，仅验证「发现（UDP）→ 连接 → 网格浏览」链路，不动播放。

## 参考

- [OpenHarmony-SIG/flutter_flutter](https://gitee.com/openharmony-sig/flutter_flutter)（README 含版本与环境要求）
- [OpenHarmony-SIG/flutter_packages](https://gitee.com/openharmony-sig/flutter_packages)（官方插件 ohos 适配集）
- [使用 Flutter SDK 3.22.1 构建 HarmonyOS 应用](https://cloud.tencent.com/developer/article/2513164)
- [3.27.4-ohos 版本约束与 hvigor 兼容问题（社区实录）](https://blog.csdn.net/2503_93740796/article/details/162622537)
- [media_kit 鸿蒙适配现状（社区改造，需手工修补）](https://blog.csdn.net/baronbool/article/details/158322632)
- [video_player ohos 适配](https://openharmonycrossplatform.csdn.net/69d624f154b52172bc67c0fa.html)
- [url_launcher ohos 适配](https://openharmonycrossplatform.csdn.net/69c69efd54b52172bc6504aa.html)
