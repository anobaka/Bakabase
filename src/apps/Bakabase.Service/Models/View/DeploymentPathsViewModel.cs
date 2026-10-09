using System.Collections.Generic;

namespace Bakabase.Service.Models.View;

public sealed class DeploymentPathsViewModel
{
    public bool IsContainer { get; init; }
    public IReadOnlyList<DeploymentPathViewModel> Paths { get; init; } = [];
}

/// <summary>Display-only deployment information. ServerPath remains the path used by all file APIs.</summary>
public sealed class DeploymentPathViewModel
{
    public string ServerPath { get; init; } = "";
    public string? HostPath { get; init; }
    /// <summary>bind, volume, container, unknown, or local (a non-container host).</summary>
    public string StorageKind { get; init; } = "unknown";
    /// <summary>The deployment's mount flag, not a filesystem permission check.</summary>
    public bool? ReadOnly { get; init; }
}
