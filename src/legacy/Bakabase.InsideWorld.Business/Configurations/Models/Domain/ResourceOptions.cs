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
using static Bakabase.InsideWorld.Business.Configurations.Models.Domain.ResourceOptions.SynchronizationCategoryOptions;

namespace Bakabase.InsideWorld.Business.Configurations.Models.Domain
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
        public SynchronizationOptionsModel? SynchronizationOptions { get; set; }

        public void AddIdOfMediaLibraryRecentlyMovedTo(int id)
        {
            const int capacity = 5;

            IdsOfMediaLibraryRecentlyMovedTo ??= [];
            var ids = IdsOfMediaLibraryRecentlyMovedTo.Where(x => x != id).ToList();
            ids.Insert(0, id);
            IdsOfMediaLibraryRecentlyMovedTo = ids.Take(capacity).ToArray();
        }

        public record CoverOptionsModel
        {
            public CoverSaveMode? SaveMode { get; set; }
        }

        public record SavedSearch
        {
            public ResourceSearchDbModel Search { get; set; } = null!;
            public string Name { get; set; } = string.Empty;
        }

        public record SynchronizationOptionsModel
        {
            public bool? DeleteResourcesWithUnknownPath { get; set; }
            public bool? DeleteResourcesWithUnknownMediaLibrary { get; set; }
            [Obsolete] public Dictionary<int, SynchronizationCategoryOptions>? CategoryOptionsMap { get; set; }
            public Dictionary<int, SynchronizationEnhancerOptions>? EnhancerOptionsMap { get; set; }

            /// <summary>
            /// V2
            /// </summary>
            public Dictionary<int, SynchronizationMediaLibraryOptions>? MediaLibraryOptionsMap { get; set; }
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