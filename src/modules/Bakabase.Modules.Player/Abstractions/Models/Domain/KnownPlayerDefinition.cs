using Bakabase.Modules.Player.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Player.Abstractions.Models.Domain;

/// <summary>
/// Static description of a third-party player we can recognize on the user's
/// machine: how to find it and what batch capabilities it has.
/// </summary>
public record KnownPlayerDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>
    /// Executable file names (e.g. "PotPlayerMini64.exe"). Matched
    /// case-insensitively. On non-Windows platforms the ".exe" suffix is
    /// stripped when probing PATH.
    /// </summary>
    public required string[] ExecutableNames { get; init; }

    public BatchPlayCapability Capabilities { get; init; }

    /// <summary>
    /// File extensions (with leading dot) this player is a sensible target
    /// for. Null means no filtering. Used to annotate candidates with how
    /// many of the selected files they can actually open.
    /// </summary>
    public IReadOnlySet<string>? SupportedExtensions { get; init; }

    /// <summary>
    /// Windows registry locations that may point to the install path.
    /// </summary>
    public RegistryHint[] RegistryHints { get; init; } = [];

    /// <summary>
    /// Directories to probe for <see cref="ExecutableNames"/>. Environment
    /// variables (e.g. %ProgramFiles%) are expanded at scan time.
    /// </summary>
    public string[] CandidateDirectories { get; init; } = [];

    /// <summary>
    /// Whether to probe the PATH environment variable.
    /// </summary>
    public bool SearchInPath { get; init; } = true;
}

/// <summary>
/// A registry value that points either to the player executable itself or to
/// its install directory.
/// </summary>
/// <param name="Hive">"HKLM" or "HKCU".</param>
/// <param name="SubKey">Registry sub key path.</param>
/// <param name="ValueName">Value name; null reads the default value.</param>
/// <param name="ValueIsDirectory">
/// True when the value holds the install directory (executable names are
/// probed inside it); false when it holds the executable path directly.
/// </param>
public record RegistryHint(string Hive, string SubKey, string? ValueName, bool ValueIsDirectory);
