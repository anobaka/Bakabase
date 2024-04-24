using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Entities;

namespace Bakabase.InsideWorld.Business.Components.Migration
{
    /// <summary>
    /// todo: distinguish this from auto migration
    /// </summary>
    public class V190Migrator
    {
        private readonly PublisherService _publisherService;
        private readonly VolumeService _volumeService;
        private readonly SeriesService _seriesService;
        private readonly OriginalService _originalService;
        private readonly OriginalResourceMappingService _originalResourceMappingService;
        private readonly CustomResourcePropertyService _customResourcePropertyService;
        private readonly FavoritesService _favoritesService;
        private readonly FavoritesResourceMappingService _favoritesResourceMappingService;
        private readonly ResourceService _resourceService;
        private readonly CustomPropertyService _customPropertyService;
        private readonly CustomPropertyValueService _customPropertyValueService;

        public V190Migrator(PublisherService publisherService, VolumeService volumeService, SeriesService seriesService,
            OriginalService originalService, OriginalResourceMappingService originalResourceMappingService,
            CustomResourcePropertyService customResourcePropertyService, FavoritesService favoritesService,
            FavoritesResourceMappingService favoritesResourceMappingService, ResourceService resourceService,
            CustomPropertyService customPropertyService, CustomPropertyValueService customPropertyValueService)
        {
            _publisherService = publisherService;
            _volumeService = volumeService;
            _seriesService = seriesService;
            _originalService = originalService;
            _originalResourceMappingService = originalResourceMappingService;
            _customResourcePropertyService = customResourcePropertyService;
            _favoritesService = favoritesService;
            _favoritesResourceMappingService = favoritesResourceMappingService;
            _resourceService = resourceService;
            _customPropertyService = customPropertyService;
            _customPropertyValueService = customPropertyValueService;
        }


        public static StandardValueType GetStandardValueTypeForObsoleteProperties(ResourceProperty property,
            string? propertyKey)
        {
            return property switch
            {
                ResourceProperty.ReleaseDt => StandardValueType.DateTime,
                ResourceProperty.Publisher => StandardValueType.ListString,
                ResourceProperty.Name => StandardValueType.String,
                ResourceProperty.Language => StandardValueType.String,
                ResourceProperty.Volume => propertyKey switch
                {
                    nameof(Volume.Index) => StandardValueType.Decimal,
                    nameof(Volume.Title) => StandardValueType.String,
                    nameof(Volume.Name) => StandardValueType.String,
                },
                ResourceProperty.Original => StandardValueType.ListString,
                ResourceProperty.Series => StandardValueType.String,
                ResourceProperty.Introduction => StandardValueType.String,
                ResourceProperty.Rate => StandardValueType.Decimal,
                ResourceProperty.CustomProperty => StandardValueType.ListString,
                ResourceProperty.Favorites => StandardValueType.ListString,
                _ => throw new ArgumentOutOfRangeException(nameof(property), property, null)
            };
        }

        private static Dictionary<int, List<string>> MergeSameValue(Dictionary<int, List<string>> typedValueMap)
        {
            var listMap = new Dictionary<string, List<string>>();
            var separator = $"[{Guid.NewGuid().ToString("N")[..6]}]";
            var valueMap = typedValueMap.ToDictionary(d => d.Key, d =>
            {
                var key = string.Join(separator, d.Value);
                if (!listMap.TryGetValue(key, out var list))
                {
                    list = listMap[key] = d.Value;
                }

                return list;
            });
            return valueMap;
        }

        public async Task<(StandardValueType ValueType, Dictionary<int, object?> ResourceValueMap)>
            PreparePropertyValuesForObsoleteProperties(ResourceProperty property, string? propertyKey)
        {
            var valueType = GetStandardValueTypeForObsoleteProperties(property, propertyKey);
            switch (property)
            {
                case ResourceProperty.ReleaseDt:
                {
                    var resources = await _resourceService.GetAllEntities();
                    var valueMap = resources.Where(r => r.ReleaseDt.HasValue)
                        .ToDictionary(x => x.Id, x => (object?) x.ReleaseDt!.Value);
                    return (valueType, valueMap);
                }
                case ResourceProperty.Publisher:
                {
                    var typedValueMap = (await _publisherService.GetByResources()).ToDictionary(d => d.Key,
                        d => d.Value.Extract().Select(c => c.Name).Distinct().ToList());
                    var valueMap = MergeSameValue(typedValueMap).ToDictionary(d => d.Key, d => (object?) d.Value);
                    return (valueType, valueMap);
                }
                case ResourceProperty.Name:
                {
                    var resources = await _resourceService.GetAllEntities();
                    var valueMap = resources.Where(r => !string.IsNullOrEmpty(r.Name))
                        .ToDictionary(x => x.Id, x => (object?) x.Name);
                    return (valueType, valueMap);
                }
                case ResourceProperty.Language:
                {
                    var resources = await _resourceService.GetAllEntities();
                    var valueMap = resources.Where(r => r.Language > 0)
                        .ToDictionary(x => x.Id, x => (object?) x.Language);
                    return (valueType, valueMap);
                }
                case ResourceProperty.Volume:
                {
                    var resourceVolumeMap = (await _volumeService.GetAll()).ToDictionary(x => x.ResourceId, x => x);
                    switch (propertyKey)
                    {
                        case nameof(Volume.Index):
                        {
                            var valueMap =
                                resourceVolumeMap.ToDictionary(x => x.Key, x => (object?) x.Value.Index);
                            return (valueType, valueMap);
                        }
                        case nameof(Volume.Title):
                        {
                            var valueMap = resourceVolumeMap.Where(x => !string.IsNullOrEmpty(x.Value.Title))
                                .ToDictionary(x => x.Key, x => (object?) x.Value.Title);
                            return (valueType, valueMap);
                        }
                        case nameof(Volume.Name):
                        {
                            var valueMap = resourceVolumeMap.Where(x => !string.IsNullOrEmpty(x.Value.Name))
                                .ToDictionary(x => x.Key, x => (object?) x.Value.Name);
                            return (valueType, valueMap);
                        }
                        default:
                            throw new Exception($"Invalid volume property key: {propertyKey}");
                    }
                }
                case ResourceProperty.Original:
                {
                    var originals = (await _originalService.GetAllDtoList()).ToDictionary(x => x.Id, x => x);
                    var resourceOriginals = await _originalResourceMappingService.GetAll();
                    var typedValueMap = resourceOriginals.GroupBy(x => x.ResourceId)
                        .ToDictionary(x => x.Key,
                            x => x.Select(y => originals.GetValueOrDefault(y.OriginalId)?.Name)
                                .Where(y => !string.IsNullOrEmpty(y)).Distinct().ToList()).Where(x => x.Value.Any())
                        .ToDictionary(x => x.Key, x => x.Value!);
                    var valueMap = MergeSameValue(typedValueMap!).ToDictionary(d => d.Key, d => (object?) d.Value);
                    return (valueType, valueMap);
                }
                case ResourceProperty.Series:
                {
                    var series = (await _seriesService.GetAll()).ToDictionary(x => x.Id, x => x);
                    var volumes = (await _volumeService.GetAll()).ToDictionary(x => x.ResourceId, x => x);
                    var valueMap = volumes.ToDictionary(x => x.Key,
                            x => series.GetValueOrDefault(x.Value.SerialId)?.Name)
                        .Where(x => !string.IsNullOrEmpty(x.Value)).ToDictionary(x => x.Key, x => (object?) x.Value);
                    return (valueType, valueMap);
                }
                case ResourceProperty.Introduction:
                {
                    var resources = await _resourceService.GetAllEntities();
                    var valueMap = resources.Where(r => !string.IsNullOrEmpty(r.Introduction))
                        .ToDictionary(x => x.Id, x => (object?) x.Introduction);
                    return (valueType, valueMap);
                }
                case ResourceProperty.Rate:
                {
                    var resources = await _resourceService.GetAllEntities();
                    var valueMap = resources.Where(r => r.Rate > 0).ToDictionary(x => x.Id, x => (object?) x.Rate);
                    return (valueType, valueMap);
                }
                case ResourceProperty.CustomProperty:
                {
                    var typedValueMap = (await _customResourcePropertyService.GetAll(x => x.Key == propertyKey))
                        .GroupBy(x => x.ResourceId)
                        .ToDictionary(x => x.Key,
                            x => x.Select(y => y.Value).Where(y => !string.IsNullOrEmpty(y)).Distinct()
                                .ToList());
                    var valueMap = MergeSameValue(typedValueMap!).ToDictionary(d => d.Key, d => (object?) d.Value);
                    return (valueType, valueMap);
                }
                case ResourceProperty.Favorites:
                {
                    var favorites = (await _favoritesService.GetAll()).ToDictionary(x => x.Id, x => x);
                    var resourceFavoritesMappings = await _favoritesResourceMappingService.GetAll();
                    var valueMap = resourceFavoritesMappings.GroupBy(x => x.ResourceId)
                        .ToDictionary(x => x.Key,
                            x => x.Select(y => favorites.GetValueOrDefault(y.FavoritesId)?.Name)
                                .Where(y => !string.IsNullOrEmpty(y)).Distinct().ToList()).Where(x => x.Value.Any())
                        .ToDictionary(x => x.Key, x => (object?) x.Value);
                    return (valueType, valueMap);
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}