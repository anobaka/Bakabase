using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Microsoft.Extensions.AI;

namespace Bakabase.Modules.AI.Components.Providers;

public interface ILlmProviderFactory
{
    AiProviderKind Kind { get; }
    string DisplayName { get; }
    LlmCapabilities DefaultCapabilities { get; }
    bool RequiresApiKey { get; }
    bool RequiresEndpoint { get; }
    string? DefaultEndpoint { get; }

    IChatClient CreateClient(AiProviderDbModel config, string modelId);
    Task<IReadOnlyList<LlmModelInfo>> GetModelsAsync(AiProviderDbModel config, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(AiProviderDbModel config, CancellationToken ct = default);
}
