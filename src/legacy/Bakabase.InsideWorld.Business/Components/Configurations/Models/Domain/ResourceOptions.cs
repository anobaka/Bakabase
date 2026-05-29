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
        /// Build a new SavedSearch with an empty Name. The frontend derives a
        /// display name from the current filter values; when nothing is
        /// available it falls back to a localized "Search N" placeholder.
        /// </summary>
        public SavedSearch BuildNewSavedSearch(string? id, ResourceSearchDbModel? search, FilterDisplayMode displayMode = FilterDisplayMode.Simple)
        {
            id ??= Guid.NewGuid().ToString("N")[..6];
            var ss = SavedSearches.FirstOrDefault(x => x.Id == id);
            if (ss != null)
            {
                return ss;
            }

            search ??= new ResourceSearchDbModel { Page = 1, PageSize = 50 };

            return new SavedSearch { Id = id, Search = search, DisplayMode = displayMode };
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
            /// <summary>
            /// When enabled, path marks will be synced immediately when created or modified.
            /// When disabled, marks will be collected and synced manually or during the next sync operation.
            /// </summary>
            public bool? SyncMarksImmediately { get; set; } = true;
        }
    }
}