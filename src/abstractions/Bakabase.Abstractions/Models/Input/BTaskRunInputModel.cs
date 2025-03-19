using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Models.Input;

public record BTaskRunInputModel(
    string Key,
    Func<BTaskArgs, Task> Run,
    Func<string>? Name = null,
    Func<string?>? Description = null,
    Func<string?>? MessageOnInterruption = null,
    CancellationToken? CancellationToken = null,
    string[]? ConflictWithTaskKeys = null,
    bool IgnoreConflicts = false,
    BTaskLevel Level = BTaskLevel.Default,
    bool StopPrevious = false);