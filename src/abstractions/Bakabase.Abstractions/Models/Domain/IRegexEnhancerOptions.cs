namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// Options interface for regex-based enhancers.
/// </summary>
public interface IRegexEnhancerOptions : IEnhancerBaseOptions
{
    /// <summary>
    /// Regular expressions used to extract data from resource filenames.
    /// </summary>
    List<string>? Expressions { get; }
}
