using Bakabase.Modules.HealthScore.Abstractions.Components;
using Bakabase.Modules.HealthScore.Abstractions.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.HealthScore.Components;

internal sealed class HealthScoreCacheWarmer<TDbContext> : IHealthScoreCacheWarmer
    where TDbContext : DbContext, IHealthScoreDbContext
{
    private readonly IServiceProvider _serviceProvider;

    public HealthScoreCacheWarmer(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task WarmAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var profiles = await sp.GetRequiredService<IHealthScoreService>().GetAll();
        sp.GetRequiredService<HealthScoreProfileCache>().Replace(profiles);

        await sp.GetRequiredService<ResourceHealthScoreOrm<TDbContext>>().Initialize(cancellationToken);
    }
}
