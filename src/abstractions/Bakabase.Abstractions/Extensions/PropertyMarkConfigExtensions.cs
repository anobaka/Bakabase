using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Extensions;

public static class PropertyMarkConfigExtensions
{
    /// <summary>
    /// V220 converted template property locators into property marks whose value-extraction mode
    /// was stored in <see cref="PropertyMarkConfig.MatchMode"/>, while both applicability selectors
    /// were left empty. The migrator treated that shape as covering every resource below the mark,
    /// so runtime consumers must preserve the same meaning for already-migrated databases.
    /// </summary>
    public static bool UsesLegacyV220WildcardApplicability(this PropertyMarkConfig config) =>
        config.ValueBasePath == PathMarkValueBasePath.MarkPath &&
        config.ValueMatchMode == null &&
        config.ValueType == PropertyValueType.Dynamic &&
        config.Layer == null &&
        string.IsNullOrEmpty(config.Regex) &&
        config.ApplyScope == PathMarkApplyScope.MatchedOnly &&
        ((config.MatchMode == PathMatchMode.Layer && config.ValueLayer.HasValue) ||
         (config.MatchMode == PathMatchMode.Regex && !string.IsNullOrEmpty(config.ValueRegex)));

    /// <summary>
    /// Returns the applicability selector runtime consumers should use without mutating the
    /// persisted configuration. Explicit modern selectors are returned unchanged.
    /// </summary>
    public static (PathMatchMode MatchMode, int? Layer, string? Regex, PathMarkApplyScope ApplyScope)
        GetEffectiveApplicability(this PropertyMarkConfig config) =>
        config.UsesLegacyV220WildcardApplicability()
            ? (PathMatchMode.Layer, 0, null, PathMarkApplyScope.MatchedAndSubdirectories)
            : (config.MatchMode, config.Layer, config.Regex, config.ApplyScope);

    /// <summary>
    /// V220 template regex locators matched each resource's path relative to the media-library
    /// root. The explicit flag is used by corrected migrations; the selectorless shape covers
    /// databases already converted by the original V220 migrator.
    /// </summary>
    public static bool UsesResourceRelativeValueRegex(this PropertyMarkConfig config) =>
        config.ValueRegexMatchesResourcePath ||
        (!string.IsNullOrEmpty(config.ValueRegex) &&
         config.UsesLegacyV220WildcardApplicability() &&
         config.MatchMode == PathMatchMode.Regex);

    public static PathMatchMode GetEffectiveValueMatchMode(this PropertyMarkConfig config) =>
        config.UsesResourceRelativeValueRegex()
            ? PathMatchMode.Regex
            : config.ValueMatchMode ?? (!string.IsNullOrEmpty(config.ValueRegex) ? PathMatchMode.Regex : PathMatchMode.Layer);
}
