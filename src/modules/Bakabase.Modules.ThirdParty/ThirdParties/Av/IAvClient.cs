using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Av;

/// <summary>
/// Common abstraction over all per-site AV scrapers. AvEnhancer / AvController
/// consume <see cref="System.Collections.Generic.IEnumerable{T}"/> of this so a
/// new source becomes discoverable by registering one DI binding, without
/// touching the dispatchers.
///
/// Each concrete client keeps its strongly-typed `SearchAndParseVideo`
/// overload for direct callers (tests, niche call sites). The interface method
/// is the lowest-common-denominator signature both dispatchers need.
/// </summary>
public interface IAvClient
{
    /// <summary>
    /// Must match the <see cref="AvSourceIds"/> entry and the value the client
    /// stamps onto <see cref="IAvDetail.Source"/>.
    /// </summary>
    string SourceId { get; }

    /// <param name="number">AV identifier (e.g. "SSIS-001").</param>
    /// <param name="appointUrl">
    /// Optional fully-qualified detail URL. When set, the client skips its own
    /// search and parses this page directly. Ignored by clients that don't
    /// support a designated URL.
    /// </param>
    /// <param name="language">
    /// Optional language hint (e.g. "zh_cn", "ja"). Only honored by clients
    /// whose URL or markup is locale-dependent (Airav, Iqqtv, Javlibrary); the
    /// rest ignore it.
    /// </param>
    Task<IAvDetail?> SearchAndParseVideo(string number, string? appointUrl = null, string? language = null);
}
