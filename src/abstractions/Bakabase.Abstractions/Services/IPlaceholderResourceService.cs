using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Services;

/// <summary>
/// The resource that now stands for what was asked for.
/// </summary>
/// <param name="ResourceId">Always a real resource, whether it was just created or already existed.</param>
/// <param name="Created">
/// False when an existing resource already covered it. Saying which happened matters: the user
/// pasting a list of twenty works wants to know that eighteen of them were already tracked.
/// </param>
/// <param name="Name">The name the resource is known by.</param>
public record PlaceholderResourceResult(int ResourceId, bool Created, string? Name);

/// <summary>
/// What a caller already read about the thing, so the platform is not asked again for what it just
/// said. A source listing twenty works knows all twenty titles; looking each of them up one by one
/// would be twenty requests to learn nothing new.
/// </summary>
/// <param name="Title">What the source calls it.</param>
/// <param name="CoverUrls">Cover images the source offers, best first.</param>
/// <param name="MetadataJson">The source's own fields, kept verbatim on the resource's source link.</param>
public record KnownItemDetail(string? Title = null, List<string>? CoverUrls = null,
    string? MetadataJson = null);

/// <summary>
/// Creates resources for things the user does not have yet.
/// <para>
/// "I am missing X, I should get it via Y" is a complete thought on its own — it needs no
/// collection, no subscription and no acquisition pipeline. This service is that thought: it turns
/// a title, an external identity or a shared link into a resource with no local files, matching an
/// existing resource wherever one already stands for the same thing so the library does not fill up
/// with duplicates.
/// </para>
/// </summary>
public interface IPlaceholderResourceService
{
    /// <summary>
    /// By name alone. Matches an existing resource whose name is the same once both are normalized;
    /// otherwise creates one with no external identity.
    /// </summary>
    Task<PlaceholderResourceResult> CreateByTitle(string title, CancellationToken ct = default);

    /// <summary>
    /// By an identity on a platform or metadata authority. Reuses the resource already carrying that
    /// identity if there is one; otherwise asks the platform what the work is called and creates a
    /// resource carrying the identity.
    /// </summary>
    /// <param name="known">
    /// What the caller already read. Absent asks the platform; present takes its word for it.
    /// </param>
    Task<PlaceholderResourceResult> CreateOrMatchByExternalIdentity(ResourceSource source, string sourceKey,
        KnownItemDetail? known = null, CancellationToken ct = default);

    /// <summary>
    /// By a link someone shared. The link is not an identity, so it is attached as an acquisition
    /// lead rather than a source link. Matching goes: the link itself if it is already attached
    /// somewhere, then the page's title against existing resource names, and only then a new
    /// resource.
    /// </summary>
    /// <param name="known">
    /// What the caller already read — a listing usually knows the post's title, which saves
    /// fetching the page just to read it back.
    /// </param>
    Task<PlaceholderResourceResult> CreateOrMatchBySharedUrl(string url, KnownItemDetail? known = null,
        CancellationToken ct = default);
}
