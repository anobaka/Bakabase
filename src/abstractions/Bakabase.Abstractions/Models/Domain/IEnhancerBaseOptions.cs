namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// Base options interface for all enhancers containing common properties.
/// </summary>
public interface IEnhancerBaseOptions
{
    /// <summary>
    /// The identifier of the enhancer.
    /// </summary>
    int EnhancerId { get; }

    /// <summary>
    /// Target-specific options for the enhancement.
    /// </summary>
    List<EnhancerTargetFullOptions>? TargetOptions { get; }

    /// <summary>
    /// Run this enhancer only after these enhancers have completed.
    /// Values are enhancer ids.
    /// </summary>
    List<int>? Requirements { get; }
}
