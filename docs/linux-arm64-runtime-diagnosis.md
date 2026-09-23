# 本机 Linux ARM64 容器 SIGILL 诊断

2026-09-21，源码基线 `d4e97c860bef3f7cb840fc48a56ec428ea9b4222`。

## 结论与适用范围

本机 Linux ARM64 验证中的崩溃已定位到 **.NET 9 CoreCLR PAL 的信号上下文处理**：在支持 SME、但不支持独立 SVE 的 CPU 环境中，运行时执行 `rdvl` 指令触发 SIGILL。无需启动 Bakabase、访问数据库或加载项目依赖即可复现；因此它不能作为联邦业务代码启动失败的证据。

这与官方 [dotnet/runtime #127398](https://github.com/dotnet/runtime/pull/127398) 修复的机制吻合。该修复已[回移到 release/10.0](https://github.com/dotnet/runtime/pull/127518)，而本轮实际运行的 .NET 9.0.20 及检查时的 release/9.0 源码仍保留相关调用。本轮没有修改 `TargetFramework`、`global.json`、发布运行时或用户虚拟机配置。

当前发布和联邦 CI 的 Linux 目标均为 **linux-x64**。这次故障来自 Apple M4 上额外进行的 **linux-arm64** 本机容器验证，不代表 Linux x64 失败，也不能据此声称 Linux x64 已通过；后者仍应以相应原生 CI 结果为准。macOS ARM64 原生运行不属于此 Linux PAL 路径。

## 环境和直接证据

| 项目 | 本次值 |
| --- | --- |
| 宿主 | Apple M4，macOS |
| 容器平台 | OrbStack，Linux aarch64，Ubuntu 24.04 |
| 内核 | `7.0.14-orbstack-00380-ga7e0a2dc9535` |
| 镜像 | `mcr.microsoft.com/dotnet/sdk:9.0-noble`，ARM64 |
| 镜像 ID | `sha256:d07d5770b2b74717bfe882250e981a4e7a756e0240fc4aefcc0ce93c1e8a9c36` |
| SDK / 运行时 | 9.0.318 / 9.0.20 |
| 资源限额 | 4 CPU、4 GiB 内存、512 PID；始终保留超过 5 GiB 宿主空闲磁盘 |
| CPU 特征 | `/proc/cpuinfo` 包含 `sme`、`sme2`，不含 `sve` |

首先在 SDK 的 `csc.dll` 编译进程中捕获 SIGILL；随后在下述不引用任何 Bakabase 文件或 NuGet 包的控制台程序中捕获相同原生故障。GDB 记录的关键部分为：

```text
runtime=9.0.20 cpu=4
Thread 1 "dotnet" received signal SIGILL, Illegal instruction.
0x0000fffff77ff174 in ?? () from .../Microsoft.NETCore.App/9.0.20/libcoreclr.so
=> 0xfffff77ff174: rdvl x0, #1
```

编译器故障的原生回溯同样位于 `libcoreclr.so`，并包含 `<signal handler called>`。发布二进制未带完整调试符号，所以没有把 `??` 伪写成已解析的函数名；指令与官方源码中的 [`CONTEXT_GetSveLengthFromOS`](https://github.com/dotnet/runtime/blob/3879076d9a/src/coreclr/pal/src/arch/arm64/context2.S#L303) 完全对应，其[调用路径](https://github.com/dotnet/runtime/blob/3879076d9a/src/coreclr/pal/src/thread/context.cpp#L1162)来自信号上下文。

官方修复说明解释：SME 环境的 Linux 信号帧可以包含长度为零的 SVE 记录，旧路径却据此执行只适用于 SVE 的 `rdvl`；SIGILL 处理再次读取上下文会重复触发。修复直接检查信号帧提供的向量长度。该解释与本轮指令、CPU 特征及修复后对照一致；本轮没有额外读取内核信号帧中的 `vl` 字段，也没有证明 OrbStack 错误通告了 CPU 能力。

## 无业务依赖的最小复现

创建两个文件；项目无需额外包或本仓库源码：

`minimal.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

`Program.cs`：

```csharp
using System.Diagnostics;
Console.WriteLine($"runtime={Environment.Version} cpu={Environment.ProcessorCount}");
var stop = Stopwatch.StartNew();
Parallel.For(0, 8, i => {
    long n = 0;
    while (stop.Elapsed < TimeSpan.FromSeconds(10)) {
        var a = new byte[32768];
        a[n % a.Length] = (byte)n++;
        if ((n & 255) == 0) GC.Collect();
        GC.KeepAlive(a);
    }
    Console.WriteLine($"worker={i} loops={n}");
});
Console.WriteLine("OK");
```

在装有 .NET 9 SDK 的目录中执行 `dotnet build minimal.csproj -p:UseSharedCompilation=false`。受影响的 ARM64 容器可能在编译器阶段就以 132 退出；也可先在不受影响的机器上构建这个平台无关的 DLL，再复制整个 `bin/Debug/net9.0` 目录执行：

```sh
# 在受影响的 Linux ARM64 .NET 9 容器内；禁用 core 文件避免反复崩溃占盘。
ulimit -c 0
for attempt in 1 2 3; do
  dotnet bin/Debug/net9.0/minimal.dll
  printf 'attempt=%s exit=%s\n' "$attempt" "$?"
done
```

如需原生指令证据，仅给独立诊断容器添加 `SYS_PTRACE` 和 `seccomp=unconfined`，安装 GDB，不修改宿主 daemon 或内核设置。GDB 使用 `handle SIG34 nostop noprint pass`、`handle SIGSEGV nostop noprint pass`、`handle SIGILL stop print pass`，随后 `run`、`x/16i $pc-24` 和 `thread apply all bt 12`。SIG34 和 SIGSEGV 是运行时可能自行处理的信号，本轮只把实际 SIGILL 作为故障记录。

## 官方修复运行时对照

使用**完全相同的 net9.0 DLL**，只在独立容器中切换运行时：

| 执行环境 | 次数 | 结果 |
| --- | --- | --- |
| .NET 9.0.20，无调试器 | 3 | 全部 SIGILL；Python 子进程退出码 `-4`，分别 0.052 / 0.060 / 0.028 秒 |
| .NET 9.0.20，GDB | 1 | 捕获上述 `rdvl` 故障指令 |
| 官方 .NET 10.0.12，`DOTNET_ROLL_FORWARD=Major` | 3 | 全部完成 10 秒负载并输出 `OK`，退出码 0 |

对照运行时来自[官方 10.0 发布元数据](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json)中的 `dotnet-runtime-10.0.12-linux-arm64.tar.gz`，下载 34,397,485 字节，并按元数据核验 SHA-512。未使用指令 hook、CPU 特征伪装或修改过的运行时。先前单独设置 `DOTNET_EnableHWIntrinsic=0` 仍会崩溃，不能作为修复。

.NET 10 对照仅证明这个最小运行时故障的修复效果。**没有**据此执行或宣称 Bakabase 全套测试、三个真实宿主 smoke、Linux 发布包验收通过，也没有把产品隐式升级到 .NET 10。

## 后续处理与证据位置

- 保持当前 Linux x64 发布 / CI 配置，由对应架构的 CI 完成验收；不要将本机 ARM64 的失败改成跳过后成功。
- 需要在此类 SME-only ARM64 Linux 环境正式支持 .NET 9 时，应等待 / 采用经过验证的官方回移运行时；或者另行规划包含兼容性测试的框架升级。本轮未验证任何 .NET 9 修复版本，不能提供一个已经可用的版本号。
- 若重复本机容器验证，日志应同时保存 `uname -a`、`dotnet --info`、`/proc/cpuinfo`、镜像架构和 ID；单独记录 132 无法区分应用、编译器和运行时故障。
- 完整本机诊断材料保留在 `/tmp/bakabase-sigill-d4e97c86/`：`platform.txt`、`csc-repeat-1.log`、`minimal-gdb.log`、`minimal-runtime9-results.json`、`minimal-runtime10.log`、官方修复元数据和最小项目。临时证据不是可移植 CI artifact，因此关键机制、复现源码和对照结果已同时记录在本文。

诊断容器及独立源码副本完成后已清理；没有操作用户已有容器、修改用户 VM / daemon 或下载新的大型镜像。
