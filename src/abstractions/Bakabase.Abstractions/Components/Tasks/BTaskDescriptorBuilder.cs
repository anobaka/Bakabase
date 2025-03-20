using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain;
using System.Collections.Concurrent;

namespace Bakabase.Abstractions.Components.Tasks;

public class BTaskDescriptorBuilder
{
    public Func<string>? GetName { get; set; }
    public Func<string?>? GetDescription { get; set; }
    public Func<string?>? GetMessageOnInterruption { get; set; }
    public Func<string, Task>? OnProcessChange { get; init; }
    public Func<int, Task>? OnPercentageChange { get; init; }
    public Func<BTaskStatus, Task>? OnStatusChange { get; init; }
    public CancellationToken? CancellationToken { get; init; }
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public required string Key { get; init; }
    public required Func<BTaskArgs, Task> Run { get; init; }
    public object?[]? Args { get; init; }
    public HashSet<string>? ConflictKeys { get; init; }
    public required BTaskLevel Level { get; init; }
    public TimeSpan? Interval { get; init; }
}