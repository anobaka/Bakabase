using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;

namespace Bakabase.Modules.AI.Components.Aigc;

/// <summary>
/// Concrete implementation per <see cref="AigcProviderKind"/>. Registered as singletons
/// and selected by Kind at runtime, mirroring <c>ILlmProviderFactory</c>.
/// </summary>
public interface IAigcProviderInvoker
{
    AigcProviderKind Kind { get; }
    string DisplayName { get; }
    AigcMediaType[] SupportedMediaTypes { get; }
    bool RequiresApiKey { get; }
    bool RequiresEndpoint { get; }
    string? DefaultEndpoint { get; }

    Task<bool> TestConnectionAsync(AigcProviderConfigDbModel config, CancellationToken ct);

    Task<AigcInvocationResult> InvokeAsync(
        AigcProviderConfigDbModel config,
        AigcInvocationRequest request,
        CancellationToken ct);
}
