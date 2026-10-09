namespace Bakabase.Abstractions.Components.Tasks;

/// <summary>A durable domain checkpoint that releases the executor without completing its job.</summary>
public sealed class BTaskSuspendedException(string? message = null, bool preserveOnCancellation = false) : Exception(message)
{
    /// <summary>Optional localized prompt; Message remains the compatible literal fallback.</summary>
    public BTaskText? MessageText { get; init; }

    public bool PreserveOnCancellation { get; } = preserveOnCancellation;
}
