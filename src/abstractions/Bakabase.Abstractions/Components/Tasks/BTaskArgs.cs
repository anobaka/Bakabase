using Bakabase.Abstractions.Models.Domain;
using Bootstrap.Components.Tasks;

namespace Bakabase.Abstractions.Components.Tasks;

public record BTaskArgs(
    PauseToken PauseToken,
    CancellationToken CancellationToken,
    BTask Task,
    Func<Action<BTask>, Task> UpdateTask,
    IServiceProvider RootServiceProvider);