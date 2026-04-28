using Bakabase.Abstractions.Services;
using Bakabase.Modules.HealthScore.Abstractions.Components;
using Bakabase.Modules.HealthScore.Abstractions.Services;
using Bakabase.Modules.HealthScore.Components;
using Bakabase.Modules.HealthScore.Components.Predicates;
using Bakabase.Modules.HealthScore.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.HealthScore.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHealthScore<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext, IHealthScoreDbContext
    {
        // Matcher engine
        services.AddSingleton<IFilePredicateRegistry, FilePredicateRegistry>();
        services.AddScoped<IResourceMatcherEvaluator, ResourceMatcherEvaluator>();

        // Built-in file predicates
        services.AddSingleton<IFilePredicate, MediaTypeFileCountPredicate>();
        services.AddSingleton<IFilePredicate, MediaTypeTotalSizePredicate>();
        services.AddSingleton<IFilePredicate, FileNamePatternCountPredicate>();
        services.AddSingleton<IFilePredicate, HasCoverImagePredicate>();
        services.AddSingleton<IFilePredicate, FileSizeOutOfRangePredicate>();
        services.AddSingleton<IFilePredicate, RootDirectoryExistsPredicate>();

        services.AddSingleton<HealthScoreProfileCache>();
        services.AddSingleton<HealthScoreMembershipCountCache>();
        services.AddSingleton<ResourceHealthScoreOrm<TDbContext>>();
        services.AddSingleton<IResourceHealthScoreReader, HealthScoreAggregateReader<TDbContext>>();
        services.AddScoped<IHealthScoreEngine, HealthScoreEngine>();
        services.AddScoped<IHealthScoreService, HealthScoreService<TDbContext>>();

        services.AddTransient<IHealthScoreLocalizer, HealthScoreLocalizer>();

        // Warmed from the host's post-migration step (BakabaseHost.ExecuteCustomProgress).
        // Not a hosted service: hosted services run before MigrateDb on first launch,
        // when the HealthScore tables don't yet exist.
        services.AddSingleton<IHealthScoreCacheWarmer, HealthScoreCacheWarmer<TDbContext>>();

        return services;
    }
}
