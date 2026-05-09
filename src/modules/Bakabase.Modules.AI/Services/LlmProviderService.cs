using Bakabase.Modules.AI.Components.Providers;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Bootstrap.Components.Orm.Infrastructures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace Bakabase.Modules.AI.Services;

public class LlmProviderService<TDbContext>(
    ResourceService<TDbContext, AiProviderDbModel, int> orm,
    IEnumerable<ILlmProviderFactory> factories
) : ILlmProviderService where TDbContext : DbContext
{
    private readonly Dictionary<AiProviderKind, ILlmProviderFactory> _factoryMap =
        factories.ToDictionary(f => f.Kind);

    public async Task<IReadOnlyList<AiProviderDbModel>> GetEnabledLlmProvidersAsync(CancellationToken ct = default)
    {
        var all = await orm.GetAll(p => p.IsEnabled && p.LlmEnabled);
        return all;
    }

    public async Task<IReadOnlyList<LlmModelInfo>> GetModelsAsync(int providerId, CancellationToken ct = default)
    {
        var config = await orm.GetByKey(providerId);
        if (config == null || !config.LlmEnabled) return [];
        var factory = GetFactory(config.Kind);
        return await factory.GetModelsAsync(config, ct);
    }

    public IChatClient CreateChatClient(AiProviderDbModel config, string modelId)
    {
        if (!config.LlmEnabled)
            throw new InvalidOperationException(
                $"Provider {config.Id} ({config.Name}) does not have LLM capability enabled.");
        var factory = GetFactory(config.Kind);
        return factory.CreateClient(config, modelId);
    }

    private ILlmProviderFactory GetFactory(AiProviderKind kind)
    {
        if (!_factoryMap.TryGetValue(kind, out var factory))
        {
            throw new InvalidOperationException($"No LLM factory registered for kind {kind}");
        }
        return factory;
    }
}
