using Bootstrap.Components.Tasks;

namespace Bakabase.Abstractions.Components.Tasks;

public record BTaskArgs(
    object?[]? Args,
    PauseToken PauseToken,
    CancellationToken CancellationToken,
    Func<string, Task>? OnProcessChange,
    Func<int, Task>? OnPercentageChange);