using Bakabase.Modules.AI.Components.Cache;
using Bakabase.Modules.AI.Components.Observation;
using Bakabase.Modules.AI.Models.Domain;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CachingChatClient = Bakabase.Modules.AI.Components.Cache.CachingChatClient;

namespace Bakabase.Modules.AI.Services;

public class LlmService(
    ILlmProviderService providerService,
    ILlmUsageService usageService,
    ILlmCacheService cacheService,
    LlmQuotaManager quotaManager,
    IAiFeatureService featureService,
    IOptionsMonitor<AiModuleOptions> aiOptions,
    ILogger<LlmService> logger
) : ILlmService
{
    public async Task<ChatResponse> CompleteAsync(
        int providerConfigId,
        string modelId,
        IEnumerable<ChatMessage> messages,
        LlmModelParameters? parameters = null,
        AiFeature? feature = null,
        CancellationToken ct = default)
    {
        await quotaManager.CheckQuotaAsync(ct);

        var config = await providerService.GetProviderAsync(providerConfigId, ct);
        if (config == null)
            throw new InvalidOperationException($"Provider config with id {providerConfigId} not found");
        if (!config.IsEnabled)
            throw new InvalidOperationException($"Provider '{config.Name}' is disabled");

        var rawClient = providerService.CreateChatClient(config, modelId);
        var client = BuildPipeline(rawClient, providerConfigId, modelId, feature);

        var chatOptions = new ChatOptions();
        if (parameters != null)
        {
            chatOptions.Temperature = parameters.Temperature;
            chatOptions.MaxOutputTokens = parameters.MaxTokens;
            chatOptions.TopP = parameters.TopP;
        }

        if (parameters?.UseJsonResponseFormat == true)
        {
            chatOptions.ResponseFormat = ChatResponseFormat.Json;
        }

        return await client.GetResponseAsync(messages.ToList(), chatOptions, ct);
    }

    public async Task<ChatResponse> CompleteWithDefaultAsync(
        IEnumerable<ChatMessage> messages,
        LlmModelParameters? parameters = null,
        AiFeature? feature = null,
        CancellationToken ct = default)
    {
        var defaultConfig = await featureService.GetConfigAsync(AiFeature.Default, ct);
        if (defaultConfig?.ProviderConfigId == null || string.IsNullOrEmpty(defaultConfig.ModelId))
            throw new InvalidOperationException(
                "Default provider/model not configured. Please configure AI default feature first.");

        return await CompleteAsync(defaultConfig.ProviderConfigId.Value, defaultConfig.ModelId, messages, parameters,
            feature, ct);
    }

    public async Task<ChatResponse> CompleteForFeatureAsync(
        AiFeature feature,
        IEnumerable<ChatMessage> messages,
        LlmModelParameters? parametersOverride = null,
        CancellationToken ct = default)
    {
        var featureConfig = await featureService.GetConfigAsync(feature, ct);

        // Fall back to AiFeature.Default config if the specific feature has no config or is set to use default
        if ((featureConfig == null || featureConfig.UseDefault) && feature != AiFeature.Default)
        {
            featureConfig = await featureService.GetConfigAsync(AiFeature.Default, ct);
        }

        var providerConfigId = featureConfig?.ProviderConfigId;
        var modelId = featureConfig?.ModelId;

        if (!providerConfigId.HasValue || string.IsNullOrEmpty(modelId))
            throw new InvalidOperationException(
                $"No provider/model configured for feature '{feature}' and no default configured.");

        var parameters = new LlmModelParameters
        {
            Temperature = parametersOverride?.Temperature ?? featureConfig?.Temperature,
            MaxTokens = parametersOverride?.MaxTokens ?? featureConfig?.MaxTokens,
            TopP = parametersOverride?.TopP ?? featureConfig?.TopP,
            UseJsonResponseFormat = parametersOverride?.UseJsonResponseFormat ?? false
        };

        return await CompleteAsync(providerConfigId.Value, modelId, messages, parameters, feature, ct);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> CompleteStreamingForFeatureAsync(
        AiFeature feature,
        IList<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await quotaManager.CheckQuotaAsync(ct);

        var featureConfig = await featureService.GetConfigAsync(feature, ct);
        if ((featureConfig == null || featureConfig.UseDefault) && feature != AiFeature.Default)
        {
            featureConfig = await featureService.GetConfigAsync(AiFeature.Default, ct);
        }

        var providerConfigId = featureConfig?.ProviderConfigId;
        var modelId = featureConfig?.ModelId;

        if (!providerConfigId.HasValue || string.IsNullOrEmpty(modelId))
            throw new InvalidOperationException(
                $"No provider/model configured for feature '{feature}' and no default configured.");

        var config = await providerService.GetProviderAsync(providerConfigId.Value, ct);
        if (config == null)
            throw new InvalidOperationException($"Provider config with id {providerConfigId} not found");
        if (!config.IsEnabled)
            throw new InvalidOperationException($"Provider '{config.Name}' is disabled");

        var rawClient = providerService.CreateChatClient(config, modelId);
        // For streaming, skip cache but keep observation
        IChatClient client = new ObservationChatClient(rawClient, usageService, providerConfigId.Value, modelId,
            feature.ToString(), aiOptions.CurrentValue.AuditLogRequestContent);

        options ??= new ChatOptions();
        options.Temperature ??= featureConfig?.Temperature;
        options.MaxOutputTokens ??= featureConfig?.MaxTokens;
        options.TopP ??= featureConfig?.TopP;

        await foreach (var update in client.GetStreamingResponseAsync(messages, options, ct))
        {
            yield return update;
        }
    }

    private IChatClient BuildPipeline(IChatClient rawClient, int providerConfigId, string modelId, AiFeature? feature)
    {
        var opts = aiOptions.CurrentValue;
        IChatClient client = rawClient;

        // Observation layer (outermost - tracks everything including cache hits)
        client = new ObservationChatClient(client, usageService, providerConfigId, modelId, feature?.ToString(),
            opts.AuditLogRequestContent);

        // Cache layer
        if (opts.EnableCache)
        {
            client = new CachingChatClient(client, cacheService, providerConfigId, modelId, opts.DefaultCacheTtlDays);
        }

        return client;
    }
}
