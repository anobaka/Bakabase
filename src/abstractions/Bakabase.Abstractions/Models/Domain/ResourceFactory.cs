using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// Builds the resource that stands for something Bakabase knows about but does not hold. Source sync
/// and the "I am missing this" flow both need exactly this shape, and it is easy to get subtly
/// wrong — a missing timestamp or an empty source link produces a resource that looks fine until it
/// is searched, sorted or synced.
/// </summary>
public static class ResourceFactory
{
    /// <summary>
    /// A resource identified by a platform or a metadata authority.
    /// </summary>
    /// <param name="path">
    /// Normally null: the source knows the work, the files are not here. Non-null when the source
    /// also reports a local presence, such as an installed Steam game.
    /// </param>
    /// <param name="metadataJson">
    /// The source's own fields, verbatim. Kept because a platform-specific reader can make sense of
    /// them later and nothing else will ever have them again.
    /// </param>
    public static Resource CreateForExternalIdentity(ResourceSource source, string sourceKey,
        string? displayName, List<string>? coverUrls = null, string? path = null,
        string? metadataJson = null)
    {
        var resource = CreateBase(displayName);
        resource.Path = path;
        resource.SourceLinks =
        [
            new ResourceSourceLink
            {
                Source = source, SourceKey = sourceKey, CoverUrls = coverUrls,
                MetadataJson = metadataJson
            }
        ];
        return resource;
    }

    /// <summary>
    /// A resource with no external identity at all — the user said "I am missing this" and only a
    /// name is known. It stays a first-class resource: searchable, taggable, and able to gain an
    /// identity later when one is found.
    /// </summary>
    public static Resource CreateWithoutIdentity(string displayName) => CreateBase(displayName);

    private static Resource CreateBase(string? displayName)
    {
        var now = DateTime.Now;
        return new Resource
        {
            Path = null,
            IsFile = false,
            Status = ResourceStatus.Active,
            DisplayName = displayName,
            FileCreatedAt = now,
            FileModifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
