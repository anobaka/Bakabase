namespace Bakabase.InsideWorld.Business.Components.Dependency.Models;

/// <summary>A supported execution target and its managed installation capability, reported by the server.</summary>
public sealed record DependentComponentTarget
{
    /// <summary>Stable keys: windows, linux, macos.</summary>
    public required string Os { get; init; }
    /// <summary>Stable .NET architecture keys, including x64, arm64, x86 and arm.</summary>
    public required string Architecture { get; init; }
    public string? MinimumVersion { get; init; }
    public bool CanAutoInstall { get; init; } = true;
    /// <summary>Optional runtime requirement, for example glibc on Linux.</summary>
    public string? Runtime { get; init; }
    public string? Notes { get; init; }
}
