using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Abstractions.Services;

public interface IResourceExternalIdentityService
{
    Task<List<ResourceExternalIdentity>> GetByResourceId(int resourceId);
    Task<Dictionary<int, List<ResourceExternalIdentity>>> GetByResourceIdsGrouped(int[] resourceIds);

    /// <summary>Find an exact site/id association, independently of the resource's source links.</summary>
    Task<int?> FindResource(ThirdPartyId thirdPartyId, string externalId);
    Task<List<int>> FindConflictingResourceIds(int resourceId);

    /// <summary>Add missing identities and preserve any already-known metadata and covers.</summary>
    Task EnsureIdentities(int resourceId, IEnumerable<ResourceExternalIdentity> identities);
    Task DeleteByResourceIds(IEnumerable<int> resourceIds);
    Task Update(ResourceExternalIdentity identity);
    Task ClearLocalCoverPaths(int resourceId);
}
