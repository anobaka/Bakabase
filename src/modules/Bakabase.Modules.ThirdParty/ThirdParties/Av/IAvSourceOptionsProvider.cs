using System.Collections.Generic;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Av;

public record AvSourceResolvedConfig(bool Enabled, string? BaseUrl, string? Cookie, string? UserAgent);

public interface IAvSourceOptionsProvider
{
    AvSourceResolvedConfig Resolve(string sourceId);

    /// <summary>
    /// Per-target preferred source order, keyed by the int value of AvEnhancerTarget.
    /// Returns null when no preferences are configured (use built-in source order).
    /// </summary>
    IReadOnlyDictionary<int, IReadOnlyList<string>>? GetPreferredSourcesByTarget();
}
