using Bakabase.Modules.RemoteAccess.Abstractions.Models;

namespace Bakabase.Modules.RemoteAccess.Components;

/// <summary>Deployment facts and seams for a deterministic clock/network inventory in tests.</summary>
public sealed class RemoteAccessAddressEnvironment
{
    public const string EndpointsVariable = "BAKABASE_DEPLOYMENT_ENDPOINTS";
    public bool IsContainer { get; init; } = string.Equals(
        Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase);
    public string? EndpointMetadata { get; init; } = Environment.GetEnvironmentVariable(EndpointsVariable);
    public TimeProvider Clock { get; init; } = TimeProvider.System;
    public Func<IReadOnlyList<RemoteAccessAddress>>? ReadInterfaceAddresses { get; init; }
}
