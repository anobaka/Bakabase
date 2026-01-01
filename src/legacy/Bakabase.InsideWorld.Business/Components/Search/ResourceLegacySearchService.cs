using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Property;
using Bakabase.Modules.Property.Abstractions.Services;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Components.Search
{
    public class ResourceLegacySearchService : IResourceLegacySearchService
    {
        private readonly IReservedPropertyValueService _reservedPropertyValueService;
        private readonly ICustomPropertyValueService _customPropertyValueService;
        private readonly IMediaLibraryResourceMappingService _mediaLibraryResourceMappingService;

        public ResourceLegacySearchService(
            IReservedPropertyValueService reservedPropertyValueService,
            ICustomPropertyValueService customPropertyValueService,
            IMediaLibraryResourceMappingService mediaLibraryResourceMappingService)
        {
            _reservedPropertyValueService = reservedPropertyValueService;
            _customPropertyValueService = customPropertyValueService;
            _mediaLibraryResourceMappingService = mediaLibraryResourceMappingService;
        }

        public async Task<HashSet<int>?> SearchAsync(List<Abstractions.Models.Domain.Resource> allResources, ResourceSearchFilterGroup? group, List<ResourceTag>? tags = null)
        {
            var resourceMap = allResources.ToDictionary(d => d.Id, d => d);
            var context = new ResourceSearchContext(allResources);

            await PreparePropertyDbValues(context, group);
            var resourceIds = SearchResourceIds(group, context);

            if (tags?.Any() == true)
            {
                resourceIds ??= allResources.Select(r => r.Id).ToHashSet();
                resourceIds.RemoveWhere(r =>
                    tags.Any(t => resourceMap.GetValueOrDefault(r)?.Tags?.Contains(t) != true));
            }

            return resourceIds;
        }

        private async Task PreparePropertyDbValues(ResourceSearchContext context, ResourceSearchFilterGroup? group)
        {
            if (group != null)
            {
                var filters = group.ExtractFilters() ?? [];
                if (filters.Any() && context.ResourcesPool?.Any() == true)
                {
                    context.PropertyValueMap = new();
                    var internalPropertyFilters = filters.Where(f => f.PropertyPool == PropertyPool.Internal).ToArray();
                    if (internalPropertyFilters.Any())
                    {
                        // Pre-fetch MediaLibraryV2Multi matching resource IDs from filter values
                        HashSet<int>? matchingMlResourceIds = null;
                        Dictionary<int, List<string>>? resourceMediaLibraryMap = null;
                        var mlFilters = filters.Where(f =>
                            f is { PropertyPool: PropertyPool.Internal, PropertyId: (int)ResourceProperty.MediaLibraryV2Multi }).ToList();
                        if (mlFilters.Any())
                        {
                            // Check if any filter uses IsNull/IsNotNull operation (needs all resource mappings)
                            var hasNullCheckOperation = mlFilters.Any(f =>
                                f.Operation == SearchOperation.IsNull || f.Operation == SearchOperation.IsNotNull);

                            // Extract all media library IDs from filters' DbValue
                            var allFilterMediaLibraryIds = new HashSet<int>();
                            var mlAccessor = PropertySystem.Builtin.MediaLibraryV2Multi;
                            foreach (var f in mlFilters)
                            {
                                var ids = mlAccessor.ParseDbValueAsLibraryIds(f.DbValue);
                                if (ids != null) allFilterMediaLibraryIds.UnionWith(ids);
                            }

                            if (hasNullCheckOperation)
                            {
                                // For IsNull/IsNotNull operations, we need mappings for all resources in the pool
                                var allResourceIds = context.ResourcesPool.Keys.ToArray();
                                if (allResourceIds.Length > 0)
                                {
                                    var mappings =
                                        await _mediaLibraryResourceMappingService.GetMediaLibraryIdsByResourceIds(
                                            allResourceIds);
                                    resourceMediaLibraryMap = mappings.ToDictionary(
                                        kv => kv.Key,
                                        kv => kv.Value.Select(id => id.ToString()).ToList());
                                }
                            }
                            else if (allFilterMediaLibraryIds.Count > 0)
                            {
                                // Get resource IDs that belong to those libraries (O(m) lookup where m = number of filter library IDs)
                                matchingMlResourceIds =
                                    await _mediaLibraryResourceMappingService.GetResourceIdsByMediaLibraryIds(
                                        allFilterMediaLibraryIds);

                                // Get detailed mappings only for resources in the pool that potentially match
                                var relevantResourceIds = context.ResourcesPool.Keys
                                    .Where(id => matchingMlResourceIds.Contains(id)).ToArray();
                                if (relevantResourceIds.Length > 0)
                                {
                                    var mappings =
                                        await _mediaLibraryResourceMappingService.GetMediaLibraryIdsByResourceIds(
                                            relevantResourceIds);
                                    resourceMediaLibraryMap = mappings.ToDictionary(
                                        kv => kv.Key,
                                        kv => kv.Value.Select(id => id.ToString()).ToList());
                                }
                            }
                        }

                        var getValue = SpecificEnumUtils<InternalProperty>.Values.ToDictionary(d => d, d => d switch
                        {
                            InternalProperty.Filename => (Func<Abstractions.Models.Domain.Resource, object?>) (r => r.FileName),
                            InternalProperty.DirectoryPath => r => r.Directory,
                            InternalProperty.CreatedAt => r => r.CreatedAt,
                            InternalProperty.FileCreatedAt => r => r.FileCreatedAt,
                            InternalProperty.FileModifiedAt => r => r.FileModifiedAt,
                            InternalProperty.Category => r => r.CategoryId.ToString(),
                            InternalProperty.MediaLibrary => r => new List<string> {r.MediaLibraryId.ToString()},
                            InternalProperty.MediaLibraryV2 => r => (r.CategoryId == 0 ? r.MediaLibraryId : -1).ToString(),
                            InternalProperty.MediaLibraryV2Multi => r => (r.CategoryId == 0 ? resourceMediaLibraryMap?.GetValueOrDefault(r.Id) : null),
                            InternalProperty.ParentResource => r => r.ParentId?.ToString(),
                            InternalProperty.PlayedAt => r => r.PlayedAt,
                            _ => null
                        });
                        context.PropertyValueMap[PropertyPool.Internal] = getValue.Where(x => x.Value != null)
                            .ToDictionary(d => (int) d.Key,
                                d => context.ResourcesPool.ToDictionary(x => x.Key,
                                    x =>
                                    {
                                        var v = d.Value!(x.Value);
                                        return v == null ? null : (List<object>?) [v];
                                    }));

                        var validData = context.PropertyValueMap[PropertyPool.Internal]
                            ?.GetValueOrDefault((int)ResourceProperty.MediaLibraryV2Multi)?.Where(x => x.Value == null)
                            .ToList();
                    }

                    if (filters.Any(f => f.PropertyPool == PropertyPool.Reserved))
                    {
                        var reservedValue =
                            (await _reservedPropertyValueService.GetAll(x =>
                                context.AllResourceIds.Contains(x.ResourceId)))
                            .GroupBy(d => d.ResourceId).ToDictionary(d => d.Key, d => d.ToList());
                        var getValue = SpecificEnumUtils<Bakabase.Abstractions.Models.Domain.Constants.ReservedProperty>.Values.ToDictionary(d => d, d => d switch
                        {
                            Bakabase.Abstractions.Models.Domain.Constants.ReservedProperty.Rating => (Func<ReservedPropertyValue, object?>) (r => r.Rating),
                            Bakabase.Abstractions.Models.Domain.Constants.ReservedProperty.Introduction => r => r.Introduction,
                            Bakabase.Abstractions.Models.Domain.Constants.ReservedProperty.Cover => r => r.CoverPaths,
                            _ => null
                        });
                        context.PropertyValueMap[PropertyPool.Reserved] = getValue.Where(x => x.Value != null)
                            .ToDictionary(d => (int) d.Key,
                                d => context.AllResourceIds.ToDictionary(x => x,
                                    x => reservedValue.GetValueOrDefault(x)?.Select(y => d.Value(y))
                                        .Where(z => z != null).ToList() as List<object>));
                    }

                    if (filters.Any(f => f.PropertyPool == PropertyPool.Custom))
                    {
                        var propertyIds = filters.Where(x => x.PropertyPool == PropertyPool.Custom)
                            .Select(d => d.PropertyId).ToHashSet();
                        var cpValues =
                            (await _customPropertyValueService.GetAll(x => propertyIds.Contains(x.PropertyId),
                                CustomPropertyValueAdditionalItem.None, false)).GroupBy(d => d.PropertyId)
                            .ToDictionary(d => d.Key,
                                d => d.GroupBy(x => x.ResourceId)
                                    .ToDictionary(a => a.Key,
                                        List<object>? (a) => a.Select(b => b.Value).Where(c => c != null).ToList()!));
                        context.PropertyValueMap[PropertyPool.Custom] = cpValues;
                    }
                }
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="group"></param>
        /// <param name="context"></param>
        /// <returns>
        /// <para>Null: all resources are valid</para>
        /// <para>Empty: all resources are invalid</para>
        /// <para>Any: valid resource id list</para>
        /// </returns>
        private HashSet<int>? SearchResourceIds(ResourceSearchFilterGroup? group, ResourceSearchContext context)
        {
            if (group == null || group.Disabled)
            {
                return null;
            }

            var steps = new List<Func<HashSet<int>?>>();

            if (group.Filters?.Any() == true)
            {
                foreach (var filter in group.Filters.Where(f => f.IsValid() && !f.Disabled))
                {
                    var propertyType = filter.Property.Type;
                    var psh = PropertySystem.Property.TryGetSearchHandler(propertyType);
                    if (psh != null)
                    {
                        steps.Add(() =>
                        {
                            return context.ResourceIdCandidates.Where(id =>
                            {
                                var values = context.PropertyValueMap?.GetValueOrDefault(filter.PropertyPool)
                                    ?.GetValueOrDefault(filter.PropertyId)?.GetValueOrDefault(id);
                                return values?.Any(v => psh.IsMatch(v, filter.Operation, filter.DbValue)) ??
                                       psh.IsMatch(null, filter.Operation, filter.DbValue);
                            }).ToHashSet();
                        });
                    }
                }
            }

            if (group.Groups?.Any() == true)
            {
                foreach (var subGroup in group.Groups.Where(g => !g.Disabled))
                {
                    steps.Add(() => SearchResourceIds(subGroup, context));
                }
            }

            HashSet<int>? result = null;

            for (var index = 0; index < steps.Count; index++)
            {
                var step = steps[index];
                var ids = step();

                if (ids == null)
                {
                    if (group.Combinator == SearchCombinator.Or)
                    {
                        break;
                    }
                    else
                    {
                        // do nothing
                    }
                }
                else
                {
                    if (!ids.Any())
                    {
                        if (group.Combinator == SearchCombinator.And)
                        {
                            return [];
                        }
                        else
                        {
                            if (index == steps.Count - 1 && result == null)
                            {
                                return [];
                            }
                            else
                            {
                                // do nothing
                            }
                        }
                    }
                    else
                    {
                        if (result == null)
                        {
                            result = ids;
                        }
                        else
                        {
                            if (group.Combinator == SearchCombinator.Or)
                            {
                                result.UnionWith(ids);
                            }
                            else
                            {
                                result.IntersectWith(ids);
                            }
                        }
                    }
                }
            }

            return result;
        }
    }
}
