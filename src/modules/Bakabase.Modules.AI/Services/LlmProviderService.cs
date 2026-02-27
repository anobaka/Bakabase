using Bakabase.Modules.AI.Components.Providers;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Models.Input;
using Bootstrap.Components.Orm.Infrastructures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.AI.Services;

public class LlmProviderService<TDbContext>(
    ResourceService<TDbContext, LlmProviderConfigDbModel, int> orm,
    IEnumerable<ILlmProviderFactory> factories,
    ILogger<LlmProviderService<TDbContext>> logger
) : ILlmProviderService where TDbContext : DbContext
{
    private readonly Dictionary<LlmProviderType, ILlmProviderFactory> _factoryMap =
        factories.ToDictionary(f => f.ProviderType);

    public async Task<IReadOnlyList<LlmProviderConfigDbModel>> GetAllProvidersAsync(CancellationToken ct = default)
    {
        return await orm.GetAll();
    }

    public async Task<LlmProviderConfigDbModel?> GetProviderAsync(int id, CancellationToken ct = default)
    {
        return await orm.GetByKey(id);
    }

    public async Task<LlmProviderConfigDbModel> AddProviderAsync(LlmProviderConfigAddInputModel input,
        CancellationToken ct = default)
    {
        var dbModel = new LlmProviderConfigDbModel
        {
            ProviderType = input.ProviderType,
            Name = input.Name,
            Endpoint = input.Endpoint,
            ApiKey = input.ApiKey,
            IsEnabled = input.IsEnabled,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        var result = await orm.Add(dbModel);
        return result.Data!;
    }

    public async Task<LlmProviderConfigDbModel> UpdateProviderAsync(int id,
        LlmProviderConfigUpdateInputModel input, CancellationToken ct = default)
    {
        var existing = await orm.GetByKey(id);
        if (existing == null)
        {
            throw new InvalidOperationException($"Provider config with id {id} not found");
        }

        if (input.ProviderType.HasValue) existing.ProviderType = input.ProviderType.Value;
        if (input.Name != null) existing.Name = input.Name;
        if (input.Endpoint != null) existing.Endpoint = input.Endpoint;
        if (input.ApiKey != null) existing.ApiKey = input.ApiKey;
        if (input.IsEnabled.HasValue) existing.IsEnabled = input.IsEnabled.Value;
        existing.UpdatedAt = DateTime.Now;

        await orm.Update(existing);
        return existing;
    }

    public async Task DeleteProviderAsync(int id, CancellationToken ct = default)
    {
        await orm.RemoveByKey(id);
    }

    public async Task<bool> TestConnectionAsync(int id, CancellationToken ct = default)
    {
        var config = await orm.GetByKey(id);
        if (config == null) return false;

        var factory = GetFactory(config.ProviderType);
        return await factory.TestConnectionAsync(config, ct);
    }

    public async Task<IReadOnlyList<LlmModelInfo>> GetModelsAsync(int id, CancellationToken ct = default)
    {
        var config = await orm.GetByKey(id);
        if (config == null) return [];

        var factory = GetFactory(config.ProviderType);
        return await factory.GetModelsAsync(config, ct);
    }

    public IReadOnlyList<LlmProviderTypeInfo> GetProviderTypes()
    {
        return _factoryMap.Values
            .Select(f => new LlmProviderTypeInfo
            {
                Type = f.ProviderType,
                DisplayName = f.DisplayName,
                DefaultCapabilities = f.DefaultCapabilities,
                RequiresApiKey = f.RequiresApiKey,
                RequiresEndpoint = f.RequiresEndpoint,
                DefaultEndpoint = f.DefaultEndpoint
            })
            .OrderBy(t => t.Type)
            .ToList();
    }

    public IChatClient CreateChatClient(LlmProviderConfigDbModel config, string modelId)
    {
        var factory = GetFactory(config.ProviderType);
        return factory.CreateClient(config, modelId);
    }

    private ILlmProviderFactory GetFactory(LlmProviderType providerType)
    {
        if (!_factoryMap.TryGetValue(providerType, out var factory))
        {
            throw new InvalidOperationException($"No factory registered for provider type: {providerType}");
        }

        return factory;
    }
}
