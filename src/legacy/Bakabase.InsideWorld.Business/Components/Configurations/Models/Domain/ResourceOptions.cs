using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Search.Models.Db;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Doc.Swagger;
using static Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain.ResourceOptions.SynchronizationCategoryOptions;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain
{
    [Options]
    [SwaggerCustomModel]
    public record ResourceOptions
    {
        public DateTime LastSyncDt { get; set; }
        public DateTime LastNfoGenerationDt { get; set; }
        public ResourceSearchDbModel? LastSearchV2 { get; set; }
        public CoverOptionsModel CoverOptions { get; set; } = new();
        public bool HideChildren { get; set; }
        public PropertyValueScope[] PropertyValueScopePriority { get; set; } = [];
        public AdditionalCoverDiscoveringSource[] AdditionalCoverDiscoveringSources { get; set; } = [];
        public List<SavedSearch> SavedSearches { get; set; } = [];
        public int[]? IdsOfMediaLibraryRecentlyMovedTo { get; set; }
        public List<ResourceFilter> RecentFilters { get; set; } = [];
        public SynchronizationOptionsModel? SynchronizationOptions { get; set; } = new();
        /// <summary>
        /// When enabled, keep original resource identity when the folder path changes
        /// by writing a bakabase.json file containing the resource id and reusing it on next sync.
        /// </summary>
        public bool KeepResourcesOnPathChange { get; set; }

        /// <summary>
        ///
        /// </summary>
        /// <param name="id"></param>
        /// <param name="translationOfSearch">Default to 'Search' (in english).</param>
        /// <param name="search"></param>
        /// <param name="displayMode"></param>
        public SavedSearch BuildNewSavedSearch(string? id, string translationOfSearch, ResourceSearchDbModel? search, FilterDisplayMode displayMode = FilterDisplayMode.Simple)
        {
            id ??= Guid.NewGuid().ToString("N")[..6];
            var ss = SavedSearches.FirstOrDefault(x => x.Id == id);
            if (ss != null)
            {
                return ss;
            }

            search ??= new ResourceSearchDbModel { Page = 1, PageSize = 50 };

            var candidateNoList = SavedSearches.Where(x => x.Name.StartsWith(translationOfSearch))
                .Select(a => a.Name.Split(' ')).Where(x => x.Length > 1).Select(a => a[1])
                .Select(x => int.TryParse(x, out var no) ? no : 0).ToList();
            var nextNo = Math.Max((candidateNoList.Any() ? candidateNoList.Max() : 0) + 1, SavedSearches.Count);

            return new SavedSearch { Id = id, Name = $"{translationOfSearch} {nextNo}", Search = search, DisplayMode = displayMode };
        }

        public record ResourceFilter
        {
            public PropertyPool PropertyPool { get; set; }
            public int PropertyId { get; set; }
            public SearchOperation Operation { get; set; }
            /// <summary>
            /// Serialized
            /// </summary>
            public string? DbValue { get; set; }
        }

        public void AddIdOfMediaLibraryRecentlyMovedTo(int id)
        {
            const int capacity = 5;

            IdsOfMediaLibraryRecentlyMovedTo ??= [];
            var ids = IdsOfMediaLibraryRecentlyMovedTo.Where(x => x != id).ToList();
            ids.Insert(0, id);
            IdsOfMediaLibraryRecentlyMovedTo = ids.Take(capacity).ToArray();
        }

        public void AddRecentFilter(ResourceFilter filter)
        {
            const int capacity = 20;

            RecentFilters ??= [];
            
            // Remove any existing identical filter (based on PropertyPool, PropertyId, Operation, and Value)
            var existingFilters = RecentFilters.Where(f => 
                f.PropertyPool == filter.PropertyPool &&
                f.PropertyId == filter.PropertyId &&
                f.Operation == filter.Operation &&
                f.DbValue == filter.DbValue).ToList();
            
            foreach (var existing in existingFilters)
            {
                RecentFilters.Remove(existing);
            }
            
            // Add the new filter at the beginning
            RecentFilters.Insert(0, filter);
            
            // Keep only the most recent 20 filters
            if (RecentFilters.Count > capacity)
            {
                RecentFilters = RecentFilters.Take(capacity).ToList();
            }
        }

        public record CoverOptionsModel
        {
            public CoverSaveMode? SaveMode { get; set; }
        }

        public record SavedSearch
        {
            public string Id { get; set; } = Guid.NewGuid().ToString("N")[..6];
            public ResourceSearchDbModel Search { get; set; } = null!;
            public string Name { get; set; } = string.Empty;
            public FilterDisplayMode DisplayMode { get; set; } = FilterDisplayMode.Simple;
        }

        public record SynchronizationOptionsModel
        {
            public int? MaxThreads { get; set; }
            public bool? DeleteResourcesWithUnknownPath { get; set; }
            public bool? DeleteResourcesWithUnknownMediaLibrary { get; set; }
            [Obsolete] public Dictionary<int, SynchronizationCategoryOptions>? CategoryOptionsMap { get; set; }
            public Dictionary<int, SynchronizationEnhancerOptions>? EnhancerOptionsMap { get; set; }

            /// <summary>
            /// V2
            /// </summary>
            public Dictionary<int, SynchronizationMediaLibraryOptions>? MediaLibraryOptionsMap { get; set; }

            /// <summary>
            /// When enabled, path marks will be synced immediately when created or modified.
            /// When disabled, marks will be collected and synced manually or during the next sync operation.
            /// </summary>
            public bool? SyncMarksImmediately { get; set; } = true;
        }

        public record SynchronizationCategoryOptions
        {
            public bool? DeleteResourcesWithUnknownPath { get; set; }
            public Dictionary<int, SynchronizationEnhancerOptions>? EnhancerOptionsMap { get; set; }
            public Dictionary<int, SynchronizationMediaLibraryOptions>? MediaLibraryOptionsMap { get; set; }
        }

        public record SynchronizationMediaLibraryOptions
        {
            public bool? DeleteResourcesWithUnknownPath { get; set; }
            public Dictionary<int, SynchronizationEnhancerOptions>? EnhancerOptionsMap { get; set; }
        }

        public record SynchronizationEnhancerOptions
        {
            public bool? ReApply { get; set; }
            public bool? ReEnhance { get; set; }
        }
    }
}