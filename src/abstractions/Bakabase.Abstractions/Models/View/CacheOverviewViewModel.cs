using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.View;

public record CacheOverviewViewModel
{
    public List<MediaLibraryCacheViewModel> MediaLibraryCaches { get; set; } = [];

    /// <summary>
    /// Cache stats for resources not associated with any media library
    /// </summary>
    public UnassociatedCacheViewModel? UnassociatedCaches { get; set; }

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

    public record UnassociatedCacheViewModel
    {
        /// <summary>
        /// <see cref="ResourceCacheType"/> - Count
        /// </summary>
        public Dictionary<int, int> ResourceCacheCountMap { get; set; } = [];

        public int ResourceCount { get; set; }
    }
}