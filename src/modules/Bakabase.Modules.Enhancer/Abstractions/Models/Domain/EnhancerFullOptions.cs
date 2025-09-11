using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Abstractions.Models.Domain;

/// <summary>
/// To be simple, we put all possible options into one model for now, even they may be not suitable for current enhancer.
/// </summary>
public record EnhancerFullOptions
{
    public List<EnhancerTargetFullOptions>? TargetOptions { get; set; }
    public List<EnhancerId>? Requirements { get; set; }

    #region Regex

    public List<string>? Expressions { get; set; }

    #endregion

    #region Keyword related

    public ScopePropertyKey? KeywordProperty { get; set; }
    public bool? PretreatKeyword { get; set; }

    #endregion
}