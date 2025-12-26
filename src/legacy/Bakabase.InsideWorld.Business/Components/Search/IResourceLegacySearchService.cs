using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.InsideWorld.Business.Components.Search
{
    public interface IResourceLegacySearchService
    {
        /// <summary>
        /// Search resources using legacy method (full scan with property value comparison)
        /// </summary>
        /// <param name="allResources">All resources to search from</param>
        /// <param name="group">Search filter group</param>
        /// <param name="tags">Optional resource tags filter</param>
        /// <returns>
        /// <para>Null: all resources are valid</para>
        /// <para>Empty: all resources are invalid</para>
        /// <para>Any: valid resource id list</para>
        /// </returns>
        Task<HashSet<int>?> SearchAsync(List<Abstractions.Models.Domain.Resource> allResources, ResourceSearchFilterGroup? group, List<ResourceTag>? tags = null);
    }
}
