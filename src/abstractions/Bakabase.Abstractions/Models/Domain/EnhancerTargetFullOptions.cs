using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Newtonsoft.Json;

namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// To be brief, we put all possible options into one model for now, even they may be not suitable for current target.
/// </summary>
[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public record EnhancerTargetFullOptions() : EnhancerTargetOptions
{
    public CoverSelectOrder? CoverSelectOrder { get; set; }
    public PropertyPool PropertyPool { get; set; }
    public int PropertyId { get; set; }
    public Property? Property { get; set; }

    /// <summary>
    /// User-provided prompt hint for AI enhancer, describing how to extract this target.
    /// </summary>
    public string? CustomPrompt { get; set; }

    /// <summary>
    /// Ordered list of data-source identifiers consulted for this target (AV enhancer only).
    /// The selector walks this list in order and uses the first source that supplied
    /// a non-empty value for this target. Null/empty falls back to the built-in source order.
    /// </summary>
    public List<string>? PreferredSources { get; set; }
}