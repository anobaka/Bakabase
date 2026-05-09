namespace Bakabase.Modules.AI.Models.Domain;

/// <summary>
/// Returned by GetAiProviderKinds. Describes what a kind supports and the connection
/// requirements so the UI can show / hide fields per kind.
/// </summary>
public record AiProviderKindInfo
{
    public required AiProviderKind Kind { get; init; }
    public required string DisplayName { get; init; }

    public required AiProviderCapability Capabilities { get; init; }

    public required bool RequiresApiKey { get; init; }
    public required bool RequiresEndpoint { get; init; }
    public string? DefaultEndpoint { get; init; }

    /// <summary>Media types the AIGC capability can produce (empty if AIGC unsupported).</summary>
    public AigcMediaType[] SupportedAigcMediaTypes { get; init; } = [];
}
