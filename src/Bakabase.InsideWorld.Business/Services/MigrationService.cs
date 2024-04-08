using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.CustomProperty;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Models.Domain;
using Bakabase.InsideWorld.Business.Models.View;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.Modules.CustomProperty.Extensions;
using Bakabase.Modules.CustomProperty.Properties.Boolean;
using Bakabase.Modules.CustomProperty.Properties.Choice;
using Bakabase.Modules.CustomProperty.Properties.DateTime;
using Bakabase.Modules.CustomProperty.Properties.Number;
using Bakabase.Modules.CustomProperty.Properties.Text;
using Bakabase.Modules.CustomProperty.Properties.Time;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.InsideWorld.Business.Services
{
    /// <summary>
    /// todo: add the automatic migrations
    /// </summary>
    public class MigrationService
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
        private readonly Dictionary<CustomPropertyType, ICustomPropertyDescriptor> _customPropertyDescriptors;
        private readonly SpecialTextService _specialTextService;

        public MigrationService(PublisherService publisherService, VolumeService volumeService,
            SeriesService seriesService, OriginalService originalService,
            CustomResourcePropertyService customResourcePropertyService, FavoritesService favoritesService,
            ResourceService resourceService, CustomPropertyService customPropertyService,
            CustomPropertyValueService customPropertyValueService,
            IEnumerable<ICustomPropertyDescriptor> customPropertyDescriptors,
            FavoritesResourceMappingService favoritesResourceMappingService,
            OriginalResourceMappingService originalResourceMappingService, SpecialTextService specialTextService)
        {
            _publisherService = publisherService;
            _volumeService = volumeService;
            _seriesService = seriesService;
            _originalService = originalService;
            _customResourcePropertyService = customResourcePropertyService;
            _favoritesService = favoritesService;
            _resourceService = resourceService;
            _customPropertyService = customPropertyService;
            _customPropertyValueService = customPropertyValueService;
            _favoritesResourceMappingService = favoritesResourceMappingService;
            _originalResourceMappingService = originalResourceMappingService;
            _specialTextService = specialTextService;
            _customPropertyDescriptors = customPropertyDescriptors.ToDictionary(x => x.Type, x => x);
        }

        public async Task<List<MigrationTargetViewModel>> GetMigrationTargets()
        {
            var publishers = (await _publisherService.GetAll()).Select(x => x.Name).ToHashSet().ToList();
            var originals = (await _originalService.GetAllDtoList()).Select(x => x.Name).ToList();
            var series = (await _seriesService.GetAll()).Select(x => x.Name).ToList();
            var volumes = (await _volumeService.GetAll()).Select(v => v.ToDto()!).ToList();
            var customResourceProperties = (await _customResourcePropertyService.GetAll())!;
            var resources = await _resourceService.GetAllEntities();
            var favorites = await _favoritesService.GetAll();

            var targets = new List<MigrationTargetViewModel>
            {
                MigrationTarget.BuildSimpleValue(publishers, ResourceProperty.Publisher, null),
                MigrationTarget.BuildSimpleValue(originals, ResourceProperty.Original, null),
                MigrationTarget.BuildSimpleValue(series, ResourceProperty.Series, null),
                MigrationTarget.BuildVolume(volumes),
                MigrationTarget.BuildSimpleValue(resources.Select(r => r.Introduction).ToHashSet().ToList(),
                    ResourceProperty.Introduction, null),
                MigrationTarget.BuildSimpleValue(resources.Select(r => r.Rate).ToHashSet().ToList(),
                    ResourceProperty.Rate, null),
                MigrationTarget.BuildSimpleValue(resources.Select(r => r.Language).ToHashSet().ToList(),
                    ResourceProperty.Language, null),
                MigrationTarget.BuildSimpleValue(resources.Select(r => r.ReleaseDt).ToHashSet().ToList(),
                    ResourceProperty.ReleaseDt, null),
                MigrationTarget.BuildCustomResourceProperties(customResourceProperties),
                MigrationTarget.BuildSimpleValue(favorites.Select(f => f.Name).ToList(),
                    ResourceProperty.Favorites, null)
            };

            return targets.Where(t => t.DataCount > 0).ToList();
        }

        public async Task<BaseResponse> ApplyMigration(MigrationTarget target)
        {
            var targetProperty = await _customPropertyService.GetByKey(target.TargetPropertyId!.Value);

            switch (target.Property)
            {
                case ResourceProperty.ReleaseDt:
                {
                    var resources = await _resourceService.GetAllEntities();
                    var resourceData = resources.Where(r => r.ReleaseDt.HasValue)
                        .ToDictionary(x => x.Id, x => x.ReleaseDt!.Value);

                    switch (targetProperty.Type)
                    {
                        case CustomPropertyType.Date:
                        case CustomPropertyType.DateTime:
                        {
                            var currentValues =
                                (await _customPropertyValueService.GetAll(x => x.PropertyId == targetProperty.Id))
                                .Where(x => !string.IsNullOrEmpty(x.Value)).ToDictionary(x => x.ResourceId, x => x);
                            var noValueResourceIds = resourceData.Keys.Except(currentValues.Keys).ToList();
                            var newValues = noValueResourceIds.Select(x => new DateTimePropertyValue
                            {
                                PropertyId = targetProperty.Id,
                                ResourceId = x,
                                Value = resourceData[x]
                            }).ToList();
                            await _customPropertyValueService.AddRange(newValues);
                            break;
                        }
                        default:
                            throw new Exception($"Invalid target property type: {targetProperty.Type}");
                    }

                    break;
                }
                case ResourceProperty.Publisher:
                {
                    var resourcePublishersMap =
                        (await _publisherService.GetByResources()).ToDictionary(x => x.Key, x => x.Value.Extract());

                    switch (targetProperty.Type)
                    {
                        case CustomPropertyType.MultipleChoice:
                        {
                            var currentValues =
                                (await _customPropertyValueService.GetAll(x => x.PropertyId == targetProperty.Id))
                                .Where(x => !string.IsNullOrEmpty(x.Value)).ToDictionary(x => x.ResourceId, x => x);

                            var choiceValues = resourcePublishersMap.Values.SelectMany(x => x).Select(x => x.Name)
                                .Distinct().ToArray();
                            var typedProperty = targetProperty as MultipleChoiceProperty;
                            typedProperty!.Options!.AddChoices(true, choiceValues!);
                            var choiceValueIdMap = typedProperty.Options!.Choices!.GroupBy(x => x.Value)
                                .ToDictionary(x => x.Key, x => x.First().Id);

                            var noValueResourceIds = resourcePublishersMap.Keys.Except(currentValues.Keys).ToList();
                            var newValues = noValueResourceIds.Select(x => new MultipleChoicePropertyValue()
                            {
                                PropertyId = targetProperty.Id,
                                ResourceId = x,
                                Value = resourcePublishersMap[x].Select(y => choiceValueIdMap[y.Name!]!).ToArray()
                            }).ToList();
                            await _customPropertyValueService.AddRange(newValues);
                            break;
                        }
                        default:
                            throw new Exception($"Invalid target property type: {targetProperty.Type}");
                    }

                    break;
                }
                case ResourceProperty.Name:
                {
                    var resources = await _resourceService.GetAllEntities();
                    var resourceData = resources.Where(r => !string.IsNullOrEmpty(r.Name))
                        .ToDictionary(x => x.Id, x => x.Name!);

                    switch (targetProperty.Type)
                    {
                        case CustomPropertyType.SingleLineText:
                        {
                            var currentValues =
                                (await _customPropertyValueService.GetAll(x => x.PropertyId == targetProperty.Id))
                                .Where(x => !string.IsNullOrEmpty(x.Value)).ToDictionary(x => x.ResourceId, x => x);
                            var noValueResourceIds = resourceData.Keys.Except(currentValues.Keys).ToList();
                            var newValues = noValueResourceIds.Select(x => new TextPropertyValue
                            {
                                PropertyId = targetProperty.Id,
                                ResourceId = x,
                                Value = resourceData[x]
                            }).ToList();
                            await _customPropertyValueService.AddRange(newValues);
                            break;
                        }
                        default:
                            throw new Exception($"Invalid target property type: {targetProperty.Type}");
                    }

                    break;
                }
                case ResourceProperty.Language:
                {
                    var resources = await _resourceService.GetAllEntities();
                    var resourceData = resources.Where(r => r.Language > 0)
                        .ToDictionary(x => x.Id, x => x.Language);

                    switch (targetProperty.Type)
                    {
                        case CustomPropertyType.SingleChoice:
                        case CustomPropertyType.MultipleChoice:
                        {
                            var currentValues =
                                (await _customPropertyValueService.GetAll(x => x.PropertyId == targetProperty.Id))
                                .Where(x => !string.IsNullOrEmpty(x.Value)).ToDictionary(x => x.ResourceId, x => x);

                            var choiceValues = resourceData.Values.ToHashSet().Select(s => s.ToString()).ToArray();
                            Dictionary<string, string> choiceValueIdMap;
                            if (targetProperty.Type == CustomPropertyType.SingleChoice)
                            {
                                var typedProperty = targetProperty as SingleChoiceProperty;
                                typedProperty!.Options!.AddChoices(true, choiceValues);
                                choiceValueIdMap = typedProperty.Options!.Choices!.GroupBy(x => x.Value)
                                    .ToDictionary(x => x.Key, x => x.First().Id);
                            }
                            else
                            {
                                var typedProperty = targetProperty as MultipleChoiceProperty;
                                typedProperty!.Options!.AddChoices(true, choiceValues);
                                choiceValueIdMap = typedProperty.Options!.Choices!.GroupBy(x => x.Value)
                                    .ToDictionary(x => x.Key, x => x.First().Id);
                            }

                            var languageChoiceMap = new Dictionary<ResourceLanguage, string>();
                            foreach (var (str, choiceId) in choiceValueIdMap)
                            {
                                if (Enum.TryParse(str, out ResourceLanguage language))
                                {
                                    languageChoiceMap[language] = choiceId;
                                }
                            }

                            var noValueResourceIds = resourceData.Keys.Except(currentValues.Keys).ToList();
                            var newValues = noValueResourceIds.Select(x => new SingleChoicePropertyValue()
                            {
                                PropertyId = targetProperty.Id,
                                ResourceId = x,
                                Value = languageChoiceMap[resourceData[x]]
                            }).ToList();
                            await _customPropertyValueService.AddRange(newValues);
                            break;
                        }
                        default:
                            throw new Exception($"Invalid target property type: {targetProperty.Type}");
                    }

                    break;
                }
                case ResourceProperty.Volume:
                {
                    var resourceVolumeMap = (await _volumeService.GetAll()).ToDictionary(x => x.ResourceId, x => x);

                    switch (target.PropertyKey)
                    {
                        case nameof(Volume.Index):
                        {
                            var resourceData = resourceVolumeMap.ToDictionary(x => x.Key, x => x.Value.Index!);
                            switch (targetProperty.Type)
                            {
                                case CustomPropertyType.Number:
                                {
                                    var currentValues =
                                        (await _customPropertyValueService.GetAll(
                                            x => x.PropertyId == targetProperty.Id))
                                        .Where(x => !string.IsNullOrEmpty(x.Value))
                                        .ToDictionary(x => x.ResourceId, x => x);
                                    var noValueResourceIds = resourceData.Keys.Except(currentValues.Keys).ToList();
                                    var newValues = noValueResourceIds.Select(x => new NumberPropertyValue()
                                    {
                                        PropertyId = targetProperty.Id,
                                        ResourceId = x,
                                        Value = resourceData[x]
                                    }).ToList();
                                    await _customPropertyValueService.AddRange(newValues);
                                    break;
                                }
                                default:
                                    throw new Exception($"Invalid target property type: {targetProperty.Type}");
                            }

                            break;
                        }
                        case nameof(Volume.Title):
                        {
                            var resourceData = resourceVolumeMap.Where(x => !string.IsNullOrEmpty(x.Value.Title))
                                .ToDictionary(x => x.Key, x => x.Value.Title!);
                            switch (targetProperty.Type)
                            {
                                case CustomPropertyType.SingleLineText:
                                {
                                    var currentValues =
                                        (await _customPropertyValueService.GetAll(
                                            x => x.PropertyId == targetProperty.Id))
                                        .Where(x => !string.IsNullOrEmpty(x.Value))
                                        .ToDictionary(x => x.ResourceId, x => x);
                                    var noValueResourceIds = resourceData.Keys.Except(currentValues.Keys).ToList();
                                    var newValues = noValueResourceIds.Select(x => new TextPropertyValue
                                    {
                                        PropertyId = targetProperty.Id,
                                        ResourceId = x,
                                        Value = resourceData[x]
                                    }).ToList();
                                    await _customPropertyValueService.AddRange(newValues);
                                    break;
                                }
                                default:
                                    throw new Exception($"Invalid target property type: {targetProperty.Type}");
                            }

                            break;
                        }
                        case nameof(Volume.Name):
                        {
                            var resourceData = resourceVolumeMap.Where(x => !string.IsNullOrEmpty(x.Value.Name))
                                .ToDictionary(x => x.Key, x => x.Value.Name!);
                            switch (targetProperty.Type)
                            {
                                case CustomPropertyType.SingleLineText:
                                {
                                    var currentValues =
                                        (await _customPropertyValueService.GetAll(
                                            x => x.PropertyId == targetProperty.Id))
                                        .Where(x => !string.IsNullOrEmpty(x.Value))
                                        .ToDictionary(x => x.ResourceId, x => x);
                                    var noValueResourceIds = resourceData.Keys.Except(currentValues.Keys).ToList();
                                    var newValues = noValueResourceIds.Select(x => new TextPropertyValue
                                    {
                                        PropertyId = targetProperty.Id,
                                        ResourceId = x,
                                        Value = resourceData[x]
                                    }).ToList();
                                    await _customPropertyValueService.AddRange(newValues);
                                    break;
                                }
                                default:
                                    throw new Exception($"Invalid target property type: {targetProperty.Type}");
                            }

                            break;
                        }
                        default:
                            throw new Exception($"Invalid target property type: {targetProperty.Type}");
                    }

                    break;
                }
                case ResourceProperty.Original:
                {
                    var originals = (await _originalService.GetAllDtoList()).ToDictionary(x => x.Id, x => x);
                    var resourceOriginals = await _originalResourceMappingService.GetAll();
                    var resourceOriginalsMap = resourceOriginals.GroupBy(x => x.ResourceId)
                        .ToDictionary(x => x.Key,
                            x => x.Select(y => originals.GetValueOrDefault(y.OriginalId)?.Name)
                                .Where(y => !string.IsNullOrEmpty(y)).ToHashSet()).Where(x => x.Value.Any())
                        .ToDictionary(x => x.Key, x => x.Value!);

                    switch (targetProperty.Type)
                    {
                        case CustomPropertyType.MultipleChoice:
                        {
                            var currentValues =
                                (await _customPropertyValueService.GetAll(x => x.PropertyId == targetProperty.Id))
                                .Where(x => !string.IsNullOrEmpty(x.Value)).ToDictionary(x => x.ResourceId, x => x);

                            var choiceValues = resourceOriginalsMap.Values.SelectMany(x => x).Distinct().ToArray();
                            var typedProperty = targetProperty as MultipleChoiceProperty;
                            typedProperty!.Options!.AddChoices(true, choiceValues!);
                            var choiceValueIdMap = typedProperty.Options!.Choices!.GroupBy(x => x.Value)
                                .ToDictionary(x => x.Key, x => x.First().Id);

                            var noValueResourceIds = resourceOriginalsMap.Keys.Except(currentValues.Keys).ToList();
                            var newValues = noValueResourceIds.Select(x => new MultipleChoicePropertyValue()
                            {
                                PropertyId = targetProperty.Id,
                                ResourceId = x,
                                Value = resourceOriginalsMap[x].Select(y => choiceValueIdMap[y!]!).ToArray()
                            }).ToList();
                            await _customPropertyValueService.AddRange(newValues);
                            break;
                        }
                        default:
                            throw new Exception($"Invalid target property type: {targetProperty.Type}");
                    }

                    break;
                }
                case ResourceProperty.Series:
                {
                    var series = (await _seriesService.GetAll()).ToDictionary(x => x.Id, x => x);
                    var volumes = (await _volumeService.GetAll()).ToDictionary(x => x.ResourceId, x => x);
                    var resourceSeriesMap = volumes.ToDictionary(x => x.Key,
                            x => series.GetValueOrDefault(x.Value.SerialId)?.Name)
                        .Where(x => !string.IsNullOrEmpty(x.Value)).ToDictionary(x => x.Key, x => x.Value);

                    switch (targetProperty.Type)
                    {
                        case CustomPropertyType.SingleChoice:
                        {
                            var currentValues =
                                (await _customPropertyValueService.GetAll(x => x.PropertyId == targetProperty.Id))
                                .Where(x => !string.IsNullOrEmpty(x.Value)).ToDictionary(x => x.ResourceId, x => x);

                            var choiceValues = resourceSeriesMap.Values.Distinct().ToArray();
                            var typedProperty = targetProperty as SingleChoiceProperty;
                            typedProperty!.Options!.AddChoices(true, choiceValues!);
                            var choiceValueIdMap = typedProperty.Options!.Choices!.GroupBy(x => x.Value)
                                .ToDictionary(x => x.Key, x => x.First().Id);

                            var noValueResourceIds = resourceSeriesMap.Keys.Except(currentValues.Keys).ToList();
                            var newValues = noValueResourceIds.Select(x => new SingleChoicePropertyValue()
                            {
                                PropertyId = targetProperty.Id,
                                ResourceId = x,
                                Value = choiceValueIdMap[resourceSeriesMap[x]!]
                            }).ToList();
                            await _customPropertyValueService.AddRange(newValues);
                            break;
                        }
                        default:
                            throw new Exception($"Invalid target property type: {targetProperty.Type}");
                    }

                    break;
                }
                case ResourceProperty.Introduction:
                {
                    var resources = await _resourceService.GetAllEntities();
                    var resourceData = resources.Where(r => !string.IsNullOrEmpty(r.Introduction))
                        .ToDictionary(x => x.Id, x => x.Introduction);

                    switch (targetProperty.Type)
                    {
                        case CustomPropertyType.MultilineText:
                        {
                            var currentValues =
                                (await _customPropertyValueService.GetAll(x => x.PropertyId == targetProperty.Id))
                                .Where(x => !string.IsNullOrEmpty(x.Value)).ToDictionary(x => x.ResourceId, x => x);
                            var noValueResourceIds = resourceData.Keys.Except(currentValues.Keys).ToList();
                            var newValues = noValueResourceIds.Select(x => new TextPropertyValue()
                            {
                                PropertyId = targetProperty.Id,
                                ResourceId = x,
                                Value = resourceData[x]
                            }).ToList();
                            await _customPropertyValueService.AddRange(newValues);
                            break;
                        }
                        default:
                            throw new Exception($"Invalid target property type: {targetProperty.Type}");
                    }

                    break;
                }
                case ResourceProperty.Rate:
                {
                    var resources = await _resourceService.GetAllEntities();
                    var resourceData = resources.Where(r => r.Rate > 0).ToDictionary(x => x.Id, x => x.Rate);

                    switch (targetProperty.Type)
                    {
                        case CustomPropertyType.Rating:
                        {
                            var currentValues =
                                (await _customPropertyValueService.GetAll(x => x.PropertyId == targetProperty.Id))
                                .Where(x => !string.IsNullOrEmpty(x.Value)).ToDictionary(x => x.ResourceId, x => x);
                            var noValueResourceIds = resourceData.Keys.Except(currentValues.Keys).ToList();
                            var newValues = noValueResourceIds.Select(x => new RatingPropertyValue()
                            {
                                PropertyId = targetProperty.Id,
                                ResourceId = x,
                                Value = resourceData[x]
                            }).ToList();
                            await _customPropertyValueService.AddRange(newValues);
                            break;
                        }
                        default:
                            throw new Exception($"Invalid target property type: {targetProperty.Type}");
                    }

                    break;
                }
                case ResourceProperty.CustomProperty:
                {
                    var propertyValues = (await _customResourcePropertyService.GetAll(x => x.Key == target.PropertyKey))
                        .GroupBy(x => x.ResourceId)
                        .ToDictionary(x => x.Key,
                            x => x.Select(y => y.Value).Where(y => !string.IsNullOrEmpty(y)).ToHashSet());
                    var currentValues =
                        (await _customPropertyValueService.GetAll(x => x.PropertyId == targetProperty.Id))
                        .Where(x => !string.IsNullOrEmpty(x.Value)).ToDictionary(x => x.ResourceId, x => x);
                    switch (targetProperty.Type)
                    {
                        case CustomPropertyType.SingleLineText:
                        {
                            var dataMap = propertyValues.ToDictionary(d => d.Key,
                                d => string.Join(BusinessConstants.TextSeparator, d.Value));
                            var noValueResourceIds = dataMap.Keys.Except(currentValues.Keys).ToList();
                            var newValues = noValueResourceIds.Select(x => new SingleLineTextPropertyValue()
                            {
                                PropertyId = targetProperty.Id,
                                ResourceId = x,
                                Value = dataMap[x]
                            }).ToList();
                            await _customPropertyValueService.AddRange(newValues);
                            break;
                        }
                        case CustomPropertyType.MultilineText:
                        {
                            var dataMap = propertyValues.ToDictionary(d => d.Key,
                                d => string.Join(Environment.NewLine, d.Value));
                            var noValueResourceIds = dataMap.Keys.Except(currentValues.Keys).ToList();
                            var newValues = noValueResourceIds.Select(x => new MultilineTextPropertyValue
                            {
                                PropertyId = targetProperty.Id,
                                ResourceId = x,
                                Value = dataMap[x]
                            }).ToList();
                            await _customPropertyValueService.AddRange(newValues);
                            break;
                        }
                        case CustomPropertyType.SingleChoice:
                        {
                            var choiceValues = propertyValues.Values
                                .Select(x => string.Join(BusinessConstants.TextSeparator, x)).Distinct().ToArray();
                            var typedProperty = targetProperty as SingleChoiceProperty;
                            typedProperty!.Options!.AddChoices(true, choiceValues!);
                            var choiceValueIdMap = typedProperty.Options!.Choices!.GroupBy(x => x.Value)
                                .ToDictionary(x => x.Key, x => x.First().Id);

                            var noValueResourceIds = propertyValues.Keys.Except(currentValues.Keys).ToList();
                            var newValues = noValueResourceIds.Select(x => new SingleChoicePropertyValue()
                            {
                                PropertyId = targetProperty.Id,
                                ResourceId = x,
                                Value = choiceValueIdMap[propertyValues[x].First()!]
                            }).ToList();
                            await _customPropertyValueService.AddRange(newValues);
                            break;
                        }
                        case CustomPropertyType.MultipleChoice:
                        {
                            var choiceValues = propertyValues.Values.SelectMany(x => x).Distinct().ToArray();
                            var typedProperty = targetProperty as MultipleChoiceProperty;
                            typedProperty!.Options!.AddChoices(true, choiceValues!);
                            var choiceValueIdMap = typedProperty.Options!.Choices!.GroupBy(x => x.Value)
                                .ToDictionary(x => x.Key, x => x.First().Id);

                            var noValueResourceIds = propertyValues.Keys.Except(currentValues.Keys).ToList();
                            var newValues = noValueResourceIds.Select(x => new MultipleChoicePropertyValue()
                            {
                                PropertyId = targetProperty.Id,
                                ResourceId = x,
                                Value = propertyValues[x].Select(y => choiceValueIdMap[y!]!).ToArray()
                            }).ToList();
                            await _customPropertyValueService.AddRange(newValues);
                            break;
                        }
                        case CustomPropertyType.Number:
                        {
                            var dataMap = propertyValues
                                .ToDictionary(d => d.Key,
                                    d => decimal.TryParse(d.Value.First(), out var n) ? n : (decimal?) null)
                                .Where(d => d.Value.HasValue).ToDictionary(d => d.Key, d => d.Value!.Value);
                            var noValueResourceIds = dataMap.Keys.Except(currentValues.Keys).ToList();
                            var newValues = noValueResourceIds.Select(x => new NumberPropertyValue()
                            {
                                PropertyId = targetProperty.Id,
                                ResourceId = x,
                                Value = dataMap[x]
                            }).ToList();
                            await _customPropertyValueService.AddRange(newValues);
                            break;
                        }
                        case CustomPropertyType.Percentage:
                        {
                            var dataMap = propertyValues
                                .ToDictionary(d => d.Key,
                                    d => decimal.TryParse(d.Value.First(), out var n) ? n : (decimal?) null)
                                .Where(d => d.Value.HasValue).ToDictionary(d => d.Key, d => d.Value!.Value);
                            var noValueResourceIds = dataMap.Keys.Except(currentValues.Keys).ToList();
                            var newValues = noValueResourceIds.Select(x => new PercentagePropertyValue()
                            {
                                PropertyId = targetProperty.Id,
                                ResourceId = x,
                                Value = dataMap[x]
                            }).ToList();
                            await _customPropertyValueService.AddRange(newValues);
                            break;
                        }
                        case CustomPropertyType.Rating:
                        {
                            var dataMap = propertyValues
                                .ToDictionary(d => d.Key,
                                    d => decimal.TryParse(d.Value.First(), out var n) ? n : (decimal?) null)
                                .Where(d => d.Value.HasValue).ToDictionary(d => d.Key, d => d.Value!.Value);
                            var noValueResourceIds = dataMap.Keys.Except(currentValues.Keys).ToList();
                            var newValues = noValueResourceIds.Select(x => new RatingPropertyValue()
                            {
                                PropertyId = targetProperty.Id,
                                ResourceId = x,
                                Value = dataMap[x]
                            }).ToList();
                            await _customPropertyValueService.AddRange(newValues);
                            break;
                        }
                        case CustomPropertyType.Boolean:
                        {
                            var dataMap = propertyValues.ToDictionary(d => d.Key, d => d.Value.Any())
                                .Where(x => x.Value).ToDictionary(x => x.Key, x => x.Value);
                            var noValueResourceIds = dataMap.Keys.Except(currentValues.Keys).ToList();
                            var newValues = noValueResourceIds.Select(x => new BooleanPropertyValue()
                            {
                                PropertyId = targetProperty.Id,
                                ResourceId = x,
                                Value = dataMap[x]
                            }).ToList();
                            await _customPropertyValueService.AddRange(newValues);
                            break;
                        }
                        case CustomPropertyType.Date:
                        {
                            var dateStrings = propertyValues.Values.Select(v => v.First()).ToHashSet();
                            var dateStringTypedDateMap = new Dictionary<string, DateTime>();
                            foreach (var ds in dateStrings)
                            {
                                var d = await _specialTextService.TryToParseDateTime(ds);
                                if (d.HasValue)
                                {
                                    dateStringTypedDateMap[ds] = d.Value;
                                }
                            }

                            var dataMap = propertyValues
                                .ToDictionary(d => d.Key,
                                    d => dateStringTypedDateMap.TryGetValue(d.Value.First(), out var dt)
                                        ? dt
                                        : (DateTime?) null).Where(x => x.Value.HasValue)
                                .ToDictionary(x => x.Key, x => x.Value!.Value);
                            var noValueResourceIds = dataMap.Keys.Except(currentValues.Keys).ToList();
                            var newValues = noValueResourceIds.Select(x => new DatePropertyValue()
                            {
                                PropertyId = targetProperty.Id,
                                ResourceId = x,
                                Value = dataMap[x]
                            }).ToList();
                            await _customPropertyValueService.AddRange(newValues);
                            break;
                        }
                        case CustomPropertyType.DateTime:
                        {
                            var dateStrings = propertyValues.Values.Select(v => v.First()).ToHashSet();
                            var dateStringTypedDateMap = new Dictionary<string, DateTime>();
                            foreach (var ds in dateStrings)
                            {
                                var d = await _specialTextService.TryToParseDateTime(ds);
                                if (d.HasValue)
                                {
                                    dateStringTypedDateMap[ds] = d.Value;
                                }
                            }

                            var dataMap = propertyValues
                                .ToDictionary(d => d.Key,
                                    d => dateStringTypedDateMap.TryGetValue(d.Value.First(), out var dt)
                                        ? dt
                                        : (DateTime?) null).Where(x => x.Value.HasValue)
                                .ToDictionary(x => x.Key, x => x.Value!.Value);
                            var noValueResourceIds = dataMap.Keys.Except(currentValues.Keys).ToList();
                            var newValues = noValueResourceIds.Select(x => new DateTimePropertyValue()
                            {
                                PropertyId = targetProperty.Id,
                                ResourceId = x,
                                Value = dataMap[x]
                            }).ToList();
                            await _customPropertyValueService.AddRange(newValues);
                            break;
                        }
                        case CustomPropertyType.Time:
                        {
                            var dataMap = propertyValues.ToDictionary(d => d.Key,
                                    d => TimeSpan.TryParse(d.Value.First(), out var ts) ? ts : (TimeSpan?) null)
                                .Where(x => x.Value.HasValue).ToDictionary(x => x.Key, x => x.Value!.Value);
                            var noValueResourceIds = dataMap.Keys.Except(currentValues.Keys).ToList();
                            var newValues = noValueResourceIds.Select(x => new TimePropertyValue()
                            {
                                PropertyId = targetProperty.Id,
                                ResourceId = x,
                                Value = dataMap[x]
                            }).ToList();
                            await _customPropertyValueService.AddRange(newValues);
                            break;
                        }
                        default:
                            throw new Exception($"Invalid target property type: {targetProperty.Type}");
                    }

                    break;
                }
                case ResourceProperty.Favorites:
                {
                    var favorites = (await _favoritesService.GetAll()).ToDictionary(x => x.Id, x => x);
                    var resourceFavoritesMappings = await _favoritesResourceMappingService.GetAll();
                    var resourceFavoritesNamesMap = resourceFavoritesMappings.GroupBy(x => x.ResourceId)
                        .ToDictionary(x => x.Key,
                            x => x.Select(y => favorites.GetValueOrDefault(y.FavoritesId)?.Name)
                                .Where(y => !string.IsNullOrEmpty(y)).ToHashSet()).Where(x => x.Value.Any())
                        .ToDictionary(x => x.Key, x => x.Value!);

                    switch (targetProperty.Type)
                    {
                        case CustomPropertyType.MultipleChoice:
                        {
                            var currentValues =
                                (await _customPropertyValueService.GetAll(x => x.PropertyId == targetProperty.Id))
                                .Where(x => !string.IsNullOrEmpty(x.Value)).ToDictionary(x => x.ResourceId, x => x);

                            var choiceValues = resourceFavoritesNamesMap.Values.SelectMany(x => x).Distinct().ToArray();
                            var typedProperty = targetProperty as MultipleChoiceProperty;
                            typedProperty!.Options!.AddChoices(true, choiceValues!);
                            var choiceValueIdMap = typedProperty.Options!.Choices!.GroupBy(x => x.Value)
                                .ToDictionary(x => x.Key, x => x.First().Id);

                            var noValueResourceIds = resourceFavoritesNamesMap.Keys.Except(currentValues.Keys).ToList();
                            var newValues = noValueResourceIds.Select(x => new MultipleChoicePropertyValue()
                            {
                                PropertyId = targetProperty.Id,
                                ResourceId = x,
                                Value = resourceFavoritesNamesMap[x].Select(y => choiceValueIdMap[y!]!).ToArray()
                            }).ToList();
                            await _customPropertyValueService.AddRange(newValues);
                            break;
                        }
                        default:
                            throw new Exception($"Invalid target property type: {targetProperty.Type}");
                    }

                    break;
                }
                case null:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return BaseResponseBuilder.Ok;
        }
    }
}