using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Bakabase.InsideWorld.Business.Components.Dependency.Models;

namespace Bakabase.InsideWorld.Business.Components.Dependency;

/// <summary>Installer target declarations shared by availability checks and dependency API metadata.</summary>
public static class DependentComponentPlatforms
{
    public static string CurrentOs => OperatingSystem.IsWindows() ? "windows" :
        OperatingSystem.IsMacOS() ? "macos" : OperatingSystem.IsLinux() ? "linux" :
        OperatingSystem.IsFreeBSD() ? "freebsd" : "unknown";
    public static string CurrentArchitecture => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
    public static string? CurrentRuntime => OperatingSystem.IsLinux()
        ? RuntimeInformation.RuntimeIdentifier.Contains("musl", StringComparison.OrdinalIgnoreCase) ? "musl" : "glibc"
        : null;

    private static IReadOnlyList<DependentComponentTarget> Targets(params DependentComponentTarget[] targets) =>
        Array.AsReadOnly(targets);

    public static IReadOnlyList<DependentComponentTarget> Desktop64 { get; } = Targets(
        new() { Os = "windows", Architecture = "x64" }, new() { Os = "windows", Architecture = "arm64" },
        new() { Os = "linux", Architecture = "x64" }, new() { Os = "linux", Architecture = "arm64" },
        new() { Os = "macos", Architecture = "x64" }, new() { Os = "macos", Architecture = "arm64" });

    // The current python-build-standalone installer selects GNU/Linux assets, not musl assets.
    public static IReadOnlyList<DependentComponentTarget> CloudPython { get; } = Targets(
        new() { Os = "windows", Architecture = "x64" }, new() { Os = "windows", Architecture = "arm64" },
        new() { Os = "linux", Architecture = "x64", Runtime = "glibc" },
        new() { Os = "linux", Architecture = "arm64", Runtime = "glibc" },
        new() { Os = "macos", Architecture = "x64" }, new() { Os = "macos", Architecture = "arm64" });

    // Official rclone v1.75.2 binary release minimums, reviewed 2026-10-20.
    // https://rclone.org/downloads/ (copy/list does not require FUSE or WinFsp).
    public static IReadOnlyList<DependentComponentTarget> Rclone { get; } = Targets(
        new() { Os = "windows", Architecture = "x64", MinimumVersion = "10.0" },
        new() { Os = "windows", Architecture = "arm64", MinimumVersion = "10.0" },
        new() { Os = "linux", Architecture = "x64", MinimumVersion = "3.2" },
        new() { Os = "linux", Architecture = "arm64", MinimumVersion = "3.2" },
        new() { Os = "macos", Architecture = "x64", MinimumVersion = "12.0" },
        new() { Os = "macos", Architecture = "arm64", MinimumVersion = "12.0" });

    // Official PikPak CLI v0.6.0 publishes all six standalone binaries.
    public static IReadOnlyList<DependentComponentTarget> PikPakCli { get; } = Targets(
        new() { Os = "windows", Architecture = "x64", MinimumVersion = "10.0" },
        new() { Os = "windows", Architecture = "arm64", MinimumVersion = "10.0" },
        new() { Os = "linux", Architecture = "x64" }, new() { Os = "linux", Architecture = "arm64" },
        new() { Os = "macos", Architecture = "x64", MinimumVersion = "12.0" },
        new() { Os = "macos", Architecture = "arm64", MinimumVersion = "12.0" });

    // Vendor CLI v1.1.0 ships five targets; Linux packages are static ELF binaries.
    // There is no Windows arm64 package in the reviewed vendor manifest.
    public static IReadOnlyList<DependentComponentTarget> TeraBoxCli { get; } = Targets(
        new() { Os = "windows", Architecture = "x64", MinimumVersion = "10.0" },
        new() { Os = "linux", Architecture = "x64" }, new() { Os = "linux", Architecture = "arm64" },
        new() { Os = "macos", Architecture = "x64", MinimumVersion = "12.0" },
        new() { Os = "macos", Architecture = "arm64", MinimumVersion = "12.0" });

    // Linux managed packages are the reviewed Debian 12 builds (glibc >= 2.35).
    // Other desktop packages require the official OS installer and are discovered without launching a server.
    public static IReadOnlyList<DependentComponentTarget> MegaCmd { get; } = Targets(
        new() { Os = "linux", Architecture = "x64", Runtime = "glibc" },
        new() { Os = "linux", Architecture = "arm64", Runtime = "glibc" },
        new() { Os = "windows", Architecture = "x64", CanAutoInstall = false },
        new() { Os = "macos", Architecture = "x64", CanAutoInstall = false },
        new() { Os = "macos", Architecture = "arm64", CanAutoInstall = false });

    // The installer fetches the current stable Go toolchain; reviewed 2026-10-19 (Go 1.27).
    // https://go.dev/wiki/MinimumRequirements
    public static IReadOnlyList<DependentComponentTarget> BaiduPcsGo { get; } = Targets(
        new() { Os = "windows", Architecture = "x64", MinimumVersion = "10.0" },
        new() { Os = "windows", Architecture = "arm64", MinimumVersion = "10.0" },
        new() { Os = "linux", Architecture = "x64", MinimumVersion = "3.2" },
        new() { Os = "linux", Architecture = "arm64", MinimumVersion = "3.2" },
        new() { Os = "macos", Architecture = "x64", MinimumVersion = "13.0" },
        new() { Os = "macos", Architecture = "arm64", MinimumVersion = "13.0" });

    // Run support includes system-installed binaries. Automatic installation reflects the keys
    // selectable by the current ffbinaries installer (not linux-armel/linux-armhf).
    public static IReadOnlyList<DependentComponentTarget> FfMpeg { get; } = Targets(
        new() { Os = "windows", Architecture = "x64" },
        new() { Os = "windows", Architecture = "arm64", CanAutoInstall = false },
        new() { Os = "windows", Architecture = "x86", CanAutoInstall = false },
        new() { Os = "linux", Architecture = "x86" }, new() { Os = "linux", Architecture = "x64" },
        new() { Os = "linux", Architecture = "arm64" },
        new() { Os = "linux", Architecture = "arm", CanAutoInstall = false },
        new() { Os = "macos", Architecture = "x64" },
        new() { Os = "macos", Architecture = "arm64", CanAutoInstall = false });

    // https://www.7-zip.org/download.html; matches SevenZipService.SelectReleaseAsset.
    public static IReadOnlyList<DependentComponentTarget> SevenZip { get; } = Targets(
        new() { Os = "windows", Architecture = "x86" }, new() { Os = "windows", Architecture = "x64" },
        new() { Os = "windows", Architecture = "arm64" }, new() { Os = "linux", Architecture = "x64" },
        new() { Os = "linux", Architecture = "arm64" }, new() { Os = "linux", Architecture = "arm" },
        new() { Os = "macos", Architecture = "x64" }, new() { Os = "macos", Architecture = "arm64" });

    public static IReadOnlyList<DependentComponentTarget> LocaleEmulator { get; } = Targets(
        new() { Os = "windows", Architecture = "x86" }, new() { Os = "windows", Architecture = "x64" });

    public static bool Matches(DependentComponentTarget target, string os, string architecture, string? runtime) =>
        target.Os == os && target.Architecture == architecture &&
        (target.Runtime == null || target.Runtime == runtime);

    public static bool IsAvailable(IReadOnlyList<DependentComponentTarget> targets) => targets.Any(target =>
        Matches(target, CurrentOs, CurrentArchitecture, CurrentRuntime) && MeetsMinimumVersion(target));

    public static bool CanAutoInstall(IReadOnlyList<DependentComponentTarget> targets) => targets.Any(target =>
        target.CanAutoInstall && Matches(target, CurrentOs, CurrentArchitecture, CurrentRuntime) && MeetsMinimumVersion(target));

    private static bool MeetsMinimumVersion(DependentComponentTarget target)
    {
        if (target.MinimumVersion == null) return true;
        if (!Version.TryParse(target.MinimumVersion, out var version)) return false;
        var build = Math.Max(version.Build, 0);
        return target.Os switch
        {
            "windows" => OperatingSystem.IsWindowsVersionAtLeast(version.Major, version.Minor, build),
            "macos" => OperatingSystem.IsMacOSVersionAtLeast(version.Major, version.Minor, build),
            "linux" => Environment.OSVersion.Version >= version,
            _ => false
        };
    }
}
