using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        services.AddScoped<ResourceService<TDbContext, ChatConversationDbModel, int>>();
        services.AddScoped<ResourceService<TDbContext, ChatMessageDbModel, long>>();
        services.AddScoped<ResourceService<TDbContext, LlmToolConfigDbModel, int>>();

        // Observation & Cache
        services.TryAddScoped<ILlmUsageService, LlmUsageService<TDbContext>>();
        services.TryAddScoped<ILlmCacheService, LlmCacheService<TDbContext>>();
        services.TryAddScoped<LlmQuotaManager>();

        // Tools
        services.TryAddScoped<LlmToolRegistry, LlmToolRegistry<TDbContext>>();

        // Services
        services.TryAddScoped<ILlmProviderService, LlmProviderService<TDbContext>>();
        services.TryAddScoped<IAiFeatureService, AiFeatureService<TDbContext>>();
        services.TryAddScoped<ILlmService, LlmService>();
        services.TryAddScoped<IAiTranslationService, AiTranslationService>();
        services.TryAddScoped<IAiFileProcessorService, AiFileProcessorService>();
        services.TryAddScoped<IChatService, ChatService<TDbContext>>();

        // Enhancement post-processors
        services.TryAddScoped<IEnhancementPostProcessor, TranslationPostProcessor>();

        return services;
    }

    /// <summary>
    /// Scans the given assemblies for all concrete implementations of <see cref="ILlmTool"/>
    /// and registers them as scoped services.
    /// </summary>
    public static IServiceCollection AddLlmTools(this IServiceCollection services, params Assembly[] assemblies)
    {
        var toolType = typeof(ILlmTool);
        var types = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsAbstract: false, IsInterface: false } && toolType.IsAssignableFrom(t))
            .Distinct();

        foreach (var type in types)
        {
            services.AddScoped(toolType, type);
        }

        return services;
    }
}
