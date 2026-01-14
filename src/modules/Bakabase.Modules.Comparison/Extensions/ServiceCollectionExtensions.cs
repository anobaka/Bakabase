using Bakabase.Modules.Comparison.Abstractions.Services;
using Bakabase.Modules.Comparison.Components;
using Bakabase.Modules.Comparison.Components.Strategies;
using Bakabase.Modules.Comparison.Models.Db;
using Bakabase.Modules.Comparison.Services;
using Bootstrap.Components.Orm.Infrastructures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Comparison.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddComparison<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext, IComparisonDbContext
    {
        // ORM services
        services.AddScoped<ResourceService<TDbContext, ComparisonPlanDbModel, int>>();
        services.AddScoped<ResourceService<TDbContext, ComparisonRuleDbModel, int>>();
        services.AddScoped<ResourceService<TDbContext, ComparisonResultGroupDbModel, int>>();
        services.AddScoped<ResourceService<TDbContext, ComparisonResultGroupMemberDbModel, int>>();
        services.AddScoped<ResourceService<TDbContext, ComparisonResultPairDbModel, int>>();

        // Comparison strategies
        services.AddSingleton<IComparisonStrategy, StrictEqualStrategy>();
        services.AddSingleton<IComparisonStrategy, TextSimilarityStrategy>();
        services.AddSingleton<IComparisonStrategy, RegexExtractNumberStrategy>();
        services.AddSingleton<IComparisonStrategy, FixedToleranceStrategy>();
        services.AddSingleton<IComparisonStrategy, RelativeToleranceStrategy>();
        services.AddSingleton<IComparisonStrategy, SetIntersectionStrategy>();
        services.AddSingleton<IComparisonStrategy, SubsetStrategy>();
        services.AddSingleton<IComparisonStrategy, TimeWindowStrategy>();
        services.AddSingleton<IComparisonStrategy, SameDayStrategy>();
        services.AddSingleton<IComparisonStrategy, ExtensionMapStrategy>();

        // Main service
        services.AddScoped<IComparisonService, ComparisonService<TDbContext>>();

        return services;
    }
}
