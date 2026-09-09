using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Services;

/// <summary>
/// What a platform says about a work, reduced to the parts needed to stand a resource up before
/// anything has been downloaded.
/// </summary>
public record ExternalIdentityDetail(string SourceKey, string? Title, List<string>? CoverUrls);

/// <summary>
/// Asks one platform what it knows about a work, given its id there. Implemented per source so the
/// callers never learn which third-party client answers, and so tests can answer for them.
/// </summary>
public interface IExternalIdentityLookup
{
    ResourceSource Source { get; }

    /// <summary>
    /// Returns what the platform says, or null when it does not know this id or cannot be reached.
    /// A resource is still worth creating in that case — the user knows what they are missing even
    /// when the platform is down.
    /// </summary>
    Task<ExternalIdentityDetail?> Lookup(string sourceKey, CancellationToken ct);
}

/// <summary>
/// Reads the title of a shared page. A page that shares a link is not an identity — nobody holds
/// anything there — so the only thing worth taking from it is what the shared thing is called.
/// </summary>
public interface ISharedUrlTitleResolver
{
    Task<string?> ResolveTitle(string url, CancellationToken ct);
}
