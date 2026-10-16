namespace Bakabase.Abstractions.Models.Domain;

public enum ResourceProcessingExecutionKind
{
    Owner = 0,
    Local = 1,
    Managed = 2
}

/// <summary>Execution only; resource ownership and the authority to accept results never move.</summary>
public sealed record ResourceProcessingExecutionTarget(ResourceProcessingExecutionKind Kind,
    string? DeviceId = null, string? LibraryEpoch = null);
