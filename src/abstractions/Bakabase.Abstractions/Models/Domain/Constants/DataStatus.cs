namespace Bakabase.Abstractions.Models.Domain.Constants;

/// <summary>
/// Status of a data slot for a resource.
/// </summary>
public enum DataStatus
{
    NotStarted = 1,
    Ready = 2,
    Failed = 3,
}
