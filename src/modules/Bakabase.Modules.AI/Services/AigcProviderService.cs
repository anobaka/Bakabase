using Bakabase.Modules.AI.Components.Aigc;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Models.Input;
using Bootstrap.Components.Orm.Infrastructures;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Modules.AI.Services;

public class AigcProviderService<TDbContext>(
    ResourceService<TDbContext, AigcProviderConfigDbModel, int> orm,
    IEnumerable<IAigcProviderInvoker> invokers
) : IAigcProviderService where TDbContext : DbContext
{
    private readonly Dictionary<AigcProviderKind, IAigcProviderInvoker> _invokerMap =
        invokers.ToDictionary(i => i.Kind);

    public async Task<IReadOnlyList<AigcProviderConfigDbModel>> GetAllAsync(CancellationToken ct = default) =>
        await orm.GetAll();

    public async Task<AigcProviderConfigDbModel?> GetAsync(int id, CancellationToken ct = default) =>
        await orm.GetByKey(id);

    public async Task<AigcProviderConfigDbModel> AddAsync(AigcProviderConfigAddInputModel input, CancellationToken ct = default)
    {
        var dbModel = new AigcProviderConfigDbModel
        {
            Kind = input.Kind,
            Name = input.Name,
            Endpoint = input.Endpoint,
            ApiKey = input.ApiKey,
            ConfigJson = input.ConfigJson,
            IsEnabled = input.IsEnabled,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        var result = await orm.Add(dbModel);
        return result.Data!;
    }

    public async Task<AigcProviderConfigDbModel> UpdateAsync(int id, AigcProviderConfigUpdateInputModel input,
        CancellationToken ct = default)
    {
        var existing = await orm.GetByKey(id) ?? throw new InvalidOperationException($"Aigc provider {id} not found");
        if (input.Kind.HasValue) existing.Kind = input.Kind.Value;
        if (input.Name != null) existing.Name = input.Name;
        if (input.Endpoint != null) existing.Endpoint = input.Endpoint;
        if (input.ApiKey != null) existing.ApiKey = input.ApiKey;
        if (input.ConfigJson != null) existing.ConfigJson = input.ConfigJson;
        if (input.IsEnabled.HasValue) existing.IsEnabled = input.IsEnabled.Value;
        existing.UpdatedAt = DateTime.Now;
        await orm.Update(existing);
        return existing;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default) => await orm.RemoveByKey(id);

    public async Task<bool> TestConnectionAsync(int id, CancellationToken ct = default)
    {
        var config = await orm.GetByKey(id);
        if (config == null) return false;
        if (!_invokerMap.TryGetValue(config.Kind, out var invoker)) return false;
        return await invoker.TestConnectionAsync(config, ct);
    }

    public IReadOnlyList<AigcProviderKindInfo> GetProviderKinds() =>
        _invokerMap.Values
            .Select(i => new AigcProviderKindInfo
            {
                Kind = i.Kind,
                DisplayName = i.DisplayName,
                SupportedMediaTypes = i.SupportedMediaTypes,
                RequiresApiKey = i.RequiresApiKey,
                RequiresEndpoint = i.RequiresEndpoint,
                DefaultEndpoint = i.DefaultEndpoint
            })
            .OrderBy(t => t.Kind)
            .ToList();
}
