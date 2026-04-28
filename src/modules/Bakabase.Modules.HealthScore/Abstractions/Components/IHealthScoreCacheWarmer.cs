namespace Bakabase.Modules.HealthScore.Abstractions.Components;

/// <summary>
/// Loads the profile cache and the <c>ResourceHealthScore</c> in-memory ORM
/// once at startup so resource health-score badges show immediately.
///
/// Must be invoked after EF Core migrations have applied — otherwise the
/// underlying tables don't exist on first run.
/// </summary>
public interface IHealthScoreCacheWarmer
{
    Task WarmAsync(CancellationToken cancellationToken = default);
}
