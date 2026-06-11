using Bakabase.Modules.Player.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Player.Abstractions.Models.Domain;

/// <summary>
/// A known player found on this machine.
/// </summary>
public record DiscoveredPlayer
{
    public required string DefinitionId { get; init; }
    public required string DisplayName { get; init; }
    public required string ExecutablePath { get; init; }
    public BatchPlayCapability Capabilities { get; init; }
}
