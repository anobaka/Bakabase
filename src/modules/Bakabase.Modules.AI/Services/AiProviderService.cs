using Bakabase.Modules.AI.Components.Aigc;
using Bakabase.Modules.AI.Components.Providers;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Models.Input;
using Bootstrap.Components.Orm.Infrastructures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.AI.Services;

public class AiProviderService<TDbContext>(
    ResourceService<TDbContext, AiProviderDbModel, int> orm,
    IEnumerable<ILlmProviderFactory> llmFactories,
    IEnumerable<IAigcProviderInvoker> aigcInvokers,
    ILogger<AiProviderService<TDbContext>> logger
) : IAiProviderService where TDbContext : DbContext
{
    private readonly Dictionary<AiProviderKind, ILlmProviderFactory> _llmFactoryMap =
        llmFactories.ToDictionary(f => f.Kind);
    private readonly Dictionary<AiProviderKind, IAigcProviderInvoker> _aigcInvokerMap =
        aigcInvokers.ToDictionary(i => i.Kind);

    public async Task<IReadOnlyList<AiProviderDbModel>> GetAllAsync(CancellationToken ct = default) =>
        await orm.GetAll();

    public async Task<AiProviderDbModel?> GetAsync(int id, CancellationToken ct = default) =>
        await orm.GetByKey(id);

    public async Task<AiProviderDbModel> AddAsync(AiProviderAddInputModel input, CancellationToken ct = default)
    {
        ValidateCapabilities(input.Kind, input.LlmEnabled, input.AigcEnabled);

        var dbModel = new AiProviderDbModel
        {
            Kind = input.Kind,
            Name = input.Name,
            Endpoint = input.Endpoint,
            ApiKey = input.ApiKey,
            IsEnabled = input.IsEnabled,
            LlmEnabled = input.LlmEnabled,
            AigcEnabled = input.AigcEnabled,
            AigcConfigJson = input.AigcConfigJson,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        var result = await orm.Add(dbModel);
        return result.Data!;
    }

    public async Task<AiProviderDbModel> UpdateAsync(int id, AiProviderUpdateInputModel input,
        CancellationToken ct = default)
    {
        var existing = await orm.GetByKey(id) ?? throw new InvalidOperationException($"AI provider {id} not found");

        if (input.Kind.HasValue) existing.Kind = input.Kind.Value;
        if (input.Name != null) existing.Name = input.Name;
        if (input.Endpoint != null) existing.Endpoint = input.Endpoint;
        if (input.ApiKey != null) existing.ApiKey = input.ApiKey;
        if (input.IsEnabled.HasValue) existing.IsEnabled = input.IsEnabled.Value;
        if (input.LlmEnabled.HasValue) existing.LlmEnabled = input.LlmEnabled.Value;
        if (input.AigcEnabled.HasValue) existing.AigcEnabled = input.AigcEnabled.Value;
        if (input.AigcConfigJson != null) existing.AigcConfigJson = input.AigcConfigJson;

        ValidateCapabilities(existing.Kind, existing.LlmEnabled, existing.AigcEnabled);

        existing.UpdatedAt = DateTime.Now;
        await orm.Update(existing);
        return existing;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default) => await orm.RemoveByKey(id);

    public async Task<AiProviderTestResult> TestAsync(int id, CancellationToken ct = default)
    {
        var config = await orm.GetByKey(id);
        if (config == null) return new AiProviderTestResult();

        bool? llm = null;
        string? llmMsg = null;
        bool? aigc = null;
        string? aigcMsg = null;

        if (config.LlmEnabled && _llmFactoryMap.TryGetValue(config.Kind, out var llmFactory))
        {
            try { llm = await llmFactory.TestConnectionAsync(config, ct); }
            catch (Exception ex) { llm = false; llmMsg = ex.Message; }
        }

        if (config.AigcEnabled && _aigcInvokerMap.TryGetValue(config.Kind, out var invoker))
        {
            try { aigc = await invoker.TestConnectionAsync(config, ct); }
            catch (Exception ex) { aigc = false; aigcMsg = ex.Message; }
        }

        return new AiProviderTestResult { Llm = llm, Aigc = aigc, LlmMessage = llmMsg, AigcMessage = aigcMsg };
    }

    public IReadOnlyList<AiProviderKindInfo> GetKinds() =>
        AiProviderKindRegistry.All.Values.OrderBy(k => k.Kind).ToList();

    private void ValidateCapabilities(AiProviderKind kind, bool llmEnabled, bool aigcEnabled)
    {
        if (llmEnabled && !AiProviderCapabilities.SupportsLlm(kind))
        {
            throw new InvalidOperationException($"Provider kind {kind} does not support LLM capability.");
        }
        if (aigcEnabled && !AiProviderCapabilities.SupportsAigc(kind))
        {
            throw new InvalidOperationException($"Provider kind {kind} does not support AIGC capability.");
        }
    }
}
