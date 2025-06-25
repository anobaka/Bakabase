using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.View;

public record CacheOverviewViewModel
{
    public List<CategoryCacheViewModel> CategoryCaches { get; set; } = [];

    /// <summary>
    /// V2
    /// </summary>
    public IEnumerable<MediaLibraryCacheViewModel> MediaLibraryCaches { get; set; } = [];

    public record CategoryCacheViewModel
    {
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = null!;

        /// <summary>
        /// <see cref="ResourceCacheType"/> - Count
        /// </summary>
        public Dictionary<int, int> ResourceCacheCountMap { get; set; } = [];

        public int ResourceCount { get; set; }
    }

    /// <summary>
    /// V2
    /// </summary>
    public record MediaLibraryCacheViewModel
    {
        public int MediaLibraryId { get; set; }
        public string MediaLibraryName { get; set; } = null!;

        /// <summary>
        /// <see cref="ResourceCacheType"/> - Count
        /// </summary>
        public Dictionary<int, int> ResourceCacheCountMap { get; set; } = [];

        public int ResourceCount { get; set; }
    }
}