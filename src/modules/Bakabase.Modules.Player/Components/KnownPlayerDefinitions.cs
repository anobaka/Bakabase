using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Modules.Player.Abstractions.Models.Domain;
using Bakabase.Modules.Player.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Player.Components;

/// <summary>
/// Built-in catalog of players we can recognize and their batch capabilities.
/// PotPlayer deliberately gets only <see cref="BatchPlayCapability.PlaylistFile"/>:
/// passing multiple paths on its command line only opens the first one, so a
/// temporary m3u8 is the reliable route.
/// </summary>
public static class KnownPlayerDefinitions
{
    private static readonly IReadOnlySet<string> AvExtensions =
        InternalOptions.VideoExtensions.Union(InternalOptions.AudioExtensions);

    private static readonly IReadOnlySet<string> AudioOnlyExtensions = InternalOptions.AudioExtensions;

    public static readonly KnownPlayerDefinition PotPlayer = new()
    {
        Id = "PotPlayer",
        DisplayName = "PotPlayer",
        ExecutableNames = ["PotPlayerMini64.exe", "PotPlayer64.exe", "PotPlayerMini.exe", "PotPlayer.exe"],
        Capabilities = BatchPlayCapability.PlaylistFile,
        SupportedExtensions = AvExtensions,
        RegistryHints =
        [
            new RegistryHint("HKCU", @"Software\DAUM\PotPlayer64", "ProgramPath", false),
            new RegistryHint("HKCU", @"Software\DAUM\PotPlayer", "ProgramPath", false),
        ],
        CandidateDirectories =
        [
            @"%ProgramFiles%\DAUM\PotPlayer",
            @"%ProgramFiles(x86)%\DAUM\PotPlayer",
        ],
    };

    public static readonly KnownPlayerDefinition Vlc = new()
    {
        Id = "Vlc",
        DisplayName = "VLC media player",
        ExecutableNames = ["vlc.exe"],
        Capabilities = BatchPlayCapability.PlaylistFile | BatchPlayCapability.MultiFileArguments,
        SupportedExtensions = AvExtensions,
        RegistryHints =
        [
            new RegistryHint("HKLM", @"SOFTWARE\VideoLAN\VLC", "InstallDir", true),
            new RegistryHint("HKLM", @"SOFTWARE\WOW6432Node\VideoLAN\VLC", "InstallDir", true),
        ],
        CandidateDirectories =
        [
            @"%ProgramFiles%\VideoLAN\VLC",
            @"%ProgramFiles(x86)%\VideoLAN\VLC",
        ],
    };

    public static readonly KnownPlayerDefinition Mpv = new()
    {
        Id = "Mpv",
        DisplayName = "mpv",
        ExecutableNames = ["mpv.exe"],
        Capabilities = BatchPlayCapability.PlaylistFile | BatchPlayCapability.MultiFileArguments,
        SupportedExtensions = AvExtensions,
        CandidateDirectories =
        [
            @"%USERPROFILE%\scoop\apps\mpv\current",
            @"%ProgramFiles%\mpv",
        ],
    };

    public static readonly KnownPlayerDefinition MpcHc = new()
    {
        Id = "MpcHc",
        DisplayName = "MPC-HC",
        ExecutableNames = ["mpc-hc64.exe", "mpc-hc.exe"],
        Capabilities = BatchPlayCapability.PlaylistFile | BatchPlayCapability.MultiFileArguments,
        SupportedExtensions = AvExtensions,
        RegistryHints =
        [
            new RegistryHint("HKLM", @"SOFTWARE\MPC-HC\MPC-HC", "ExePath", false),
            new RegistryHint("HKCU", @"Software\MPC-HC\MPC-HC", "ExePath", false),
        ],
        CandidateDirectories =
        [
            @"%ProgramFiles%\MPC-HC",
            @"%ProgramFiles(x86)%\MPC-HC",
        ],
    };

    public static readonly KnownPlayerDefinition MpcBe = new()
    {
        Id = "MpcBe",
        DisplayName = "MPC-BE",
        ExecutableNames = ["mpc-be64.exe", "mpc-be.exe"],
        Capabilities = BatchPlayCapability.PlaylistFile | BatchPlayCapability.MultiFileArguments,
        SupportedExtensions = AvExtensions,
        RegistryHints =
        [
            new RegistryHint("HKLM", @"SOFTWARE\MPC-BE", "ExePath", false),
            new RegistryHint("HKCU", @"Software\MPC-BE", "ExePath", false),
        ],
        CandidateDirectories =
        [
            @"%ProgramFiles%\MPC-BE",
            @"%ProgramFiles(x86)%\MPC-BE",
        ],
    };

    public static readonly KnownPlayerDefinition Foobar2000 = new()
    {
        Id = "Foobar2000",
        DisplayName = "foobar2000",
        ExecutableNames = ["foobar2000.exe"],
        Capabilities = BatchPlayCapability.PlaylistFile | BatchPlayCapability.MultiFileArguments,
        SupportedExtensions = AudioOnlyExtensions,
        CandidateDirectories =
        [
            @"%ProgramFiles%\foobar2000",
            @"%ProgramFiles(x86)%\foobar2000",
        ],
    };

    public static readonly IReadOnlyList<KnownPlayerDefinition> All =
    [
        PotPlayer,
        Vlc,
        Mpv,
        MpcHc,
        MpcBe,
        Foobar2000,
    ];

    /// <summary>
    /// Recognizes a configured executable (e.g. from a resource profile
    /// player) as a known player by its file name.
    /// </summary>
    public static KnownPlayerDefinition? MatchByExecutable(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        var fileName = CrossPlatformPath.GetFileName(executablePath.Trim());
        if (string.IsNullOrEmpty(fileName))
        {
            return null;
        }

        // Allow extension-less matches so "/usr/bin/vlc" or shortcuts
        // resolved without ".exe" still hit the catalog.
        var withoutExtension = Path.GetFileNameWithoutExtension(fileName);

        return All.FirstOrDefault(d => d.ExecutableNames.Any(n =>
            n.Equals(fileName, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileNameWithoutExtension(n).Equals(withoutExtension, StringComparison.OrdinalIgnoreCase)));
    }
}
