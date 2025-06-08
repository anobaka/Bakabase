using Bakabase.Abstractions.Models.Domain.Constants;
using System.Collections.Concurrent;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Components.Tasks;

public class BTaskHandlerBuilder
{
    public required Func<string> GetName { get; init; }
    public Func<string?>? GetDescription { get; set; }
    public Func<string?>? GetMessageOnInterruption { get; set; }
    public CancellationToken? CancellationToken { get; init; }
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public required Func<BTaskArgs, Task> Run { get; init; }
    public HashSet<string>? ConflictKeys { get; init; }
    public BTaskLevel Level { get; init; } = BTaskLevel.Default;
    public TimeSpan? Interval { get; init; }
    public bool IsPersistent { get; init; }
    public IServiceProvider? RootServiceProvider { get; init; }
    public Func<BTaskStatus, BTask, Task>? OnStatusChange { get; set; }
    public Func<BTask, Task>? OnPercentageChanged { get; set; }
    public required BTaskType Type { get; init; }
    public required BTaskResourceType ResourceType { get; init; }
    public object[]? ResourceKeys { get; set; }
    public bool StartNow { get; set; }
}