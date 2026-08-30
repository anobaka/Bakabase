# Bakabase Mobile 本地编译指南

App 位于 `app/`（Flutter 3.47.2 stable，与 CI 锁定同版本）。包名统一为
`com.bakabase.mobile`。设计与里程碑记录见
[`docs/mobile-app-design.md`](../../../docs/mobile-app-design.md)。

以下命令均可直接复制执行。任何平台装完环境后先跑 `flutter doctor`，
Android 相关项全绿再构建。

## 0. 国内网络加速（可选，任何平台）

```bash
# Flutter/Dart 官方镜像（放进 shell profile / 系统环境变量长期生效）
export PUB_HOSTED_URL=https://pub.flutter-io.cn
export FLUTTER_STORAGE_BASE_URL=https://storage.flutter-io.cn
```

Windows PowerShell 写法：

```powershell
[Environment]::SetEnvironmentVariable("PUB_HOSTED_URL", "https://pub.flutter-io.cn", "User")
[Environment]::SetEnvironmentVariable("FLUTTER_STORAGE_BASE_URL", "https://storage.flutter-io.cn", "User")
```

## 1. 安装环境

### Windows（PowerShell）

```powershell
# Flutter SDK（锁定 3.47.2，与 CI 一致）
git clone --depth 1 -b 3.47.2 https://github.com/flutter/flutter.git "$env:USERPROFILE\flutter"
[Environment]::SetEnvironmentVariable("Path", "$([Environment]::GetEnvironmentVariable('Path','User'));$env:USERPROFILE\flutter\bin", "User")

# JDK 17 + Android Studio（Android SDK 随 Studio 首次启动安装）
winget install --id EclipseAdoptium.Temurin.17.JDK
winget install --id Google.AndroidStudio
```

重开一个终端，然后：

```powershell
flutter doctor --android-licenses
flutter doctor
```

### macOS

```bash
# Flutter SDK（锁定 3.47.2，与 CI 一致）
git clone --depth 1 -b 3.47.2 https://github.com/flutter/flutter.git ~/flutter
echo 'export PATH="$PATH:$HOME/flutter/bin"' >> ~/.zshrc
source ~/.zshrc

# JDK 17 + Android Studio
brew install --cask temurin@17 android-studio

flutter doctor --android-licenses
flutter doctor
```

如需构建 iOS：从 App Store 安装 Xcode，然后：

```bash
sudo xcode-select -s /Applications/Xcode.app/Contents/Developer
sudo xcodebuild -license accept
brew install cocoapods
```

### Ubuntu

```bash
sudo apt-get update
sudo apt-get install -y git curl unzip xz-utils zip openjdk-17-jdk

# Flutter SDK（锁定 3.47.2，与 CI 一致）
git clone --depth 1 -b 3.47.2 https://github.com/flutter/flutter.git ~/flutter
echo 'export PATH="$PATH:$HOME/flutter/bin"' >> ~/.bashrc
source ~/.bashrc

# Android SDK（随 Android Studio 首次启动安装）
sudo snap install android-studio --classic

flutter doctor --android-licenses
flutter doctor
```

## 2. 编译 Android APK（三平台命令相同）

在仓库根目录执行：

```bash
cd src/apps/mobile/app
flutter pub get
flutter build apk --release --split-per-abi
```

产物在 `build/app/outputs/flutter-apk/`：

- `app-arm64-v8a-release.apk` — 绝大多数手机装这个
- `app-armeabi-v7a-release.apk` — 老旧 32 位设备
- `app-x86_64-release.apk` — 模拟器

> 当前 release 构建沿用 Flutter 模板的 debug 签名，自装没有问题；
> 正式对外分发前需要配置正式 keystore。

## 3. 真机调试（改代码热重载）

手机开启开发者模式 + USB 调试并连接电脑，然后：

```bash
cd src/apps/mobile/app
flutter devices        # 确认设备被识别
flutter run            # 构建、安装并热重载
```

## 4. iOS（仅 macOS）

CI 出的是 unsigned IPA 走 SideStore 分发；本地想直接装机（免费 Apple ID，
7 天有效期）：

```bash
cd src/apps/mobile/app
flutter pub get
open ios/Runner.xcworkspace
```

在 Xcode 里：Runner → Signing & Capabilities → Team 选自己的
Personal Team（免费 Apple ID 登录即有）→ 连接 iPhone 直接 Run。
首次运行需在手机 设置 → 通用 → VPN 与设备管理 中信任开发者证书。

仅出未签名产物（不装机）：

```bash
flutter build ios --release --no-codesign
```

## 5. 常见问题

- `flutter doctor` 报 Android licenses 未接受 → 重跑 `flutter doctor --android-licenses` 一路 `y`。
- 首次构建卡在 Gradle 下载 → 网络问题，配置代理（`HTTPS_PROXY`）或耐心等待；仅首次慢。
- 桌面端/网页端目标：本项目未启用，只支持 Android / iOS。
- CI 参考：`.github/workflows/mobile-ci.yml`（PR 门禁）与 `mobile-release.yml`（`mobile-v*` tag 发布）。
