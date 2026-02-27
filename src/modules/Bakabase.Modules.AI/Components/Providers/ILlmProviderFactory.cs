using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Microsoft.Extensions.AI;

namespace Bakabase.Modules.AI.Components.Providers;

public interface ILlmProviderFactory
{
    LlmProviderType ProviderType { get; }
    string DisplayName { get; }
    LlmCapabilities DefaultCapabilities { get; }
    bool RequiresApiKey { get; }
    bool RequiresEndpoint { get; }
    string? DefaultEndpoint { get; }

    IChatClient CreateClient(LlmProviderConfigDbModel config, string modelId);
    Task<IReadOnlyList<LlmModelInfo>> GetModelsAsync(LlmProviderConfigDbModel config, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(LlmProviderConfigDbModel config, CancellationToken ct = default);
}
