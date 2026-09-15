using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Services;

/// <summary>One candidate for being the same work as a newly created resource.</summary>
/// <param name="CandidateResourceId">The resource that was already here.</param>
/// <param name="Score">How alike the two titles are, from 0 to 1.</param>
/// <param name="Reason">What made it worth asking about.</param>
public record ResourceMatchCandidate(int CandidateResourceId, double Score, string? Reason);

/// <summary>
/// The things that might already be here.
/// <para>
/// Bringing in a list from outside creates a resource for everything on it. Some of those are
/// already tracked under a slightly different name — a shared post's title, a circle page's title
/// and what the user typed are rarely spelled the same. Exact matching catches what it can; this
/// holds what only looks alike, for a person to decide.
/// </para>
/// <para>
/// Nothing here merges by itself. A wrong merge silently takes a resource's files, properties and
/// history with it, so the conservative default from the resolution design applies: suggest, never
/// decide.
/// </para>
/// </summary>
public interface IResourceMatchSuggestionService
{
    Task<List<ResourceMatchSuggestion>> GetPending(CancellationToken ct = default);

    Task<int> CountPending(CancellationToken ct = default);

    /// <summary>
    /// Records what a newly created resource might already be. A pair that has already been decided
    /// — either way — is never suggested again.
    /// </summary>
    Task Suggest(int resourceId, IReadOnlyList<ResourceMatchCandidate> candidates,
        CancellationToken ct = default);

    /// <summary>
    /// Says the two are the same work: everything the new resource carries — its identities, its
    /// leads, the collections it is in — moves onto the one that was already here, and the new one
    /// goes away.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The resource to be merged away has local files, so it is not a placeholder and merging it
    /// could lose something. Only a resource with nothing on disk can be merged.
    /// </exception>
    Task Confirm(int id, CancellationToken ct = default);

    /// <summary>Says they are different works, and remembers it so the pair is not raised again.</summary>
    Task Dismiss(int id, CancellationToken ct = default);

    /// <summary>Drops suggestions naming resources that no longer exist.</summary>
    Task DeleteByResourceIds(IReadOnlyCollection<int> resourceIds, CancellationToken ct = default);
}
