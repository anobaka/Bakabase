using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Modules.Enhancer.Abstractions.Models.Domain;

/// <summary>
/// Result of the enhancement process, containing values, logs, and error information.
/// </summary>
public record EnhancementResult
{
    /// <summary>
    /// Enhancement values produced by the enhancer.
    /// </summary>
    public List<EnhancementRawValue>? Values { get; set; }

    /// <summary>
    /// Logs collected during the enhancement process.
    /// </summary>
    public List<EnhancementLog>? Logs { get; set; }

    /// <summary>
    /// Error message if enhancement failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}
