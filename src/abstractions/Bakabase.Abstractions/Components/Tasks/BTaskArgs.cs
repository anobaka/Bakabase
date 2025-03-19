namespace Bakabase.Abstractions.Components.Tasks;

public record BTaskArgs(
    object[]? Args,
    BTaskPauseToken PauseToken,
    CancellationToken CancellationToken,
    Func<string, Task> OnProcessChange,
    Func<int, Task> OnPercentageChange);