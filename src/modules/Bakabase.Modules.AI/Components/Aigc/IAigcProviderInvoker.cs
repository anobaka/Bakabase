using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;

namespace Bakabase.Modules.AI.Components.Aigc;

/// <summary>
/// Concrete implementation per <see cref="AiProviderKind"/> capable of AIGC. Registered as
/// singletons and selected by Kind at runtime. Different from <c>ILlmProviderFactory</c>:
/// invokers produce file bytes (with optional polling) rather than chat completions.
/// </summary>
public interface IAigcProviderInvoker
{
    AiProviderKind Kind { get; }
    AigcMediaType[] SupportedMediaTypes { get; }

    Task<bool> TestConnectionAsync(AiProviderDbModel config, CancellationToken ct);

    Task<AigcInvocationResult> InvokeAsync(
        AiProviderDbModel config,
        AigcInvocationRequest request,
        CancellationToken ct);
}
