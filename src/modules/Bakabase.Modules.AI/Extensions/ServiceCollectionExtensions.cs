using Bakabase.Modules.AI.Components.Cache;
using Bakabase.Modules.AI.Components.Enhancer;
using Bakabase.Modules.AI.Components.Observation;
using Bakabase.Modules.AI.Components.Providers;
using Bakabase.Modules.AI.Components.Tools;
using Bakabase.Modules.AI.Components.Providers.Claude;
using Bakabase.Modules.AI.Components.Providers.DashScope;
using Bakabase.Modules.AI.Components.Providers.Gemini;
using Bakabase.Modules.AI.Components.Providers.Ollama;
using Bakabase.Modules.AI.Components.Providers.OpenAI;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Services;
using Bootstrap.Components.Orm.Infrastructures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Modules.AI.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAI<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        // Provider Factories
        services.TryAddSingleton<ILlmProviderFactory, OpenAIProviderFactory>();
        services.AddSingleton<ILlmProviderFactory, OllamaProviderFactory>();
        services.AddSingleton<ILlmProviderFactory, ClaudeProviderFactory>();
        services.AddSingleton<ILlmProviderFactory, DashScopeProviderFactory>();
        services.AddSingleton<ILlmProviderFactory, GeminiProviderFactory>();

        // ORM for all AI DB models
        services.AddScoped<ResourceService<TDbContext, LlmProviderConfigDbModel, int>>();
        services.AddScoped<ResourceService<TDbContext, LlmUsageLogDbModel, long>>();
        services.AddScoped<ResourceService<TDbContext, LlmCallCacheEntryDbModel, long>>();
        services.AddScoped<ResourceService<TDbContext, AiFeatureConfigDbModel, int>>();

        // Observation & Cache
        services.TryAddScoped<ILlmUsageService, LlmUsageService<TDbContext>>();
        services.TryAddScoped<ILlmCacheService, LlmCacheService<TDbContext>>();
        services.TryAddScoped<LlmQuotaManager>();

        // Tools
        services.TryAddSingleton<LlmToolRegistry>();

        // Services
        services.TryAddScoped<ILlmProviderService, LlmProviderService<TDbContext>>();
        services.TryAddScoped<IAiFeatureService, AiFeatureService<TDbContext>>();
        services.TryAddScoped<ILlmService, LlmService>();
        services.TryAddScoped<IAiTranslationService, AiTranslationService>();
        services.TryAddScoped<IAiFileProcessorService, AiFileProcessorService>();

        // Enhancement post-processors
        services.TryAddScoped<IEnhancementPostProcessor, TranslationPostProcessor>();

        return services;
    }
}
