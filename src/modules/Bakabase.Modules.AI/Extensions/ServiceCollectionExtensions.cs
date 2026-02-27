using Bakabase.Modules.AI.Components.Providers;
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

        // ORM
        services.AddScoped<ResourceService<TDbContext, LlmProviderConfigDbModel, int>>();

        // Services
        services.TryAddScoped<ILlmProviderService, LlmProviderService<TDbContext>>();
        services.TryAddScoped<ILlmService, LlmService>();

        return services;
    }
}
