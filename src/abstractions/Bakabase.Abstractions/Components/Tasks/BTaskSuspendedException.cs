namespace Bakabase.Abstractions.Components.Tasks;

/// <summary>A durable domain checkpoint that releases the executor without completing its job.</summary>
public sealed class BTaskSuspendedException(string? message = null, bool preserveOnCancellation = false) : Exception(message)
{
    public bool PreserveOnCancellation { get; } = preserveOnCancellation;
}
