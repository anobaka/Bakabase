using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Models.Domain.Constants;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Abstractions.Components.Tasks;

/// <summary>
/// Abstract base class for predefined task builders with common functionality.
/// Provides localization support and scoped service access.
/// </summary>
public abstract class AbstractPredefinedBTaskBuilder : IPredefinedBTaskBuilder
{
    protected readonly IServiceProvider ServiceProvider;
    protected readonly IBakabaseLocalizer Localizer;

    protected AbstractPredefinedBTaskBuilder(IServiceProvider serviceProvider, IBakabaseLocalizer localizer)
    {
        ServiceProvider = serviceProvider;
        Localizer = localizer;
    }

    /// <inheritdoc />
    public abstract string Id { get; }

    /// <inheritdoc />
    public abstract bool IsEnabled();

    /// <inheritdoc />
    public virtual TimeSpan? GetInterval() => TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    public virtual Type[] WatchedOptionsTypes => [];

    /// <inheritdoc />
    public virtual string GetName() => Localizer.BTask_Name(Id);

    /// <inheritdoc />
    public virtual string? GetDescription() => Localizer.BTask_Description(Id);

    /// <inheritdoc />
    public virtual string? GetMessageOnInterruption() => Localizer.BTask_MessageOnInterruption(Id);

    /// <inheritdoc />
    public virtual BTaskType Type => BTaskType.Any;

    /// <inheritdoc />
    public virtual BTaskResourceType ResourceType => BTaskResourceType.Any;

    /// <inheritdoc />
    public virtual HashSet<string>? ConflictKeys => [Id];

    /// <inheritdoc />
    public virtual HashSet<string>? DependsOn => null;

    /// <inheritdoc />
    public virtual BTaskLevel Level => BTaskLevel.Default;

    /// <inheritdoc />
    public virtual bool IsPersistent => true;

    /// <inheritdoc />
    public virtual BTaskRetryPolicy? RetryPolicy => null;

    /// <inheritdoc />
    public virtual BTaskDependencyFailurePolicy DependencyFailurePolicy => BTaskDependencyFailurePolicy.Wait;

    /// <inheritdoc />
    public abstract Task RunAsync(BTaskArgs args);

    /// <summary>
    /// Creates a scoped service provider for task execution
    /// </summary>
    protected AsyncServiceScope CreateScope() => ServiceProvider.CreateAsyncScope();
}
