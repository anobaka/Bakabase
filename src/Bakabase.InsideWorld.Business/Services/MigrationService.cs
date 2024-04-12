using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.CustomProperty;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Business.Models.Domain;
using Bakabase.InsideWorld.Business.Models.Input;
using Bakabase.InsideWorld.Business.Models.View;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.Modules.CustomProperty.Extensions;
using Bakabase.Modules.CustomProperty.Properties.Boolean;
using Bakabase.Modules.CustomProperty.Properties.Choice;
using Bakabase.Modules.CustomProperty.Properties.DateTime;
using Bakabase.Modules.CustomProperty.Properties.Number;
using Bakabase.Modules.CustomProperty.Properties.Text;
using Bakabase.Modules.CustomProperty.Properties.Time;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using static Bakabase.InsideWorld.Business.Components.ThirdParty.Pixiv.Models.PixivSearchResponse;

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
        private readonly ConversionService _conversionService;
        private readonly IBOptionsManager<MigrationOptions> _migrationOptions;

        public MigrationService(PublisherService publisherService, VolumeService volumeService,
            SeriesService seriesService, OriginalService originalService,
            CustomResourcePropertyService customResourcePropertyService, FavoritesService favoritesService,
            ResourceService resourceService, CustomPropertyService customPropertyService,
            CustomPropertyValueService customPropertyValueService,
            IEnumerable<ICustomPropertyDescriptor> customPropertyDescriptors,
            FavoritesResourceMappingService favoritesResourceMappingService,
            OriginalResourceMappingService originalResourceMappingService, SpecialTextService specialTextService,
            ConversionService conversionService, IBOptionsManager<MigrationOptions> migrationOptions)
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
            _conversionService = conversionService;
            _migrationOptions = migrationOptions;
            _customPropertyDescriptors = customPropertyDescriptors.ToDictionary(x => x.Type, x => x);
        }

        public async Task<List<MigrationTargetViewModel>> GetMigrationTargets()
        {
            var targets = new List<MigrationTargetViewModel>();
            var doneProperties = _migrationOptions.Value?.Properties?.Select(x => x.Property).ToHashSet() ??
                                 new HashSet<ResourceProperty>();
            if (!doneProperties.Contains(ResourceProperty.Publisher))
            {
                var publishers = (await _publisherService.GetAll()).Select(x => x.Name).ToHashSet().ToList();
                targets.Add(await _buildSimpleMigrationTargetViewModel(publishers, ResourceProperty.Publisher, null,
                    CustomPropertyType.MultipleChoice, [CustomPropertyType.MultipleChoice], s => s));
            }

            if (!doneProperties.Contains(ResourceProperty.Name) ||
                !doneProperties.Contains(ResourceProperty.Introduction) ||
                !doneProperties.Contains(ResourceProperty.Rate) ||
                !doneProperties.Contains(ResourceProperty.Language) ||
                !doneProperties.Contains(ResourceProperty.ReleaseDt))
            {
                var resources = await _resourceService.GetAllEntities();

                if (!doneProperties.Contains(ResourceProperty.Name))
                {
                    var names = resources.Select(x => x.Name).Where(x => !string.IsNullOrEmpty(x)).ToHashSet().ToList();
                    targets.Add(await _buildSimpleMigrationTargetViewModel(names, ResourceProperty.Name, null,
                        CustomPropertyType.SingleLineText, [CustomPropertyType.SingleLineText], s => s));
                }

                if (!doneProperties.Contains(ResourceProperty.Introduction))
                {
                    var introductions = resources.Select(x => x.Introduction).Where(x => !string.IsNullOrEmpty(x))
                        .ToHashSet().ToList();
                    targets.Add(await _buildSimpleMigrationTargetViewModel(introductions, ResourceProperty.Introduction,
                        null, CustomPropertyType.MultilineText, [CustomPropertyType.MultilineText], s => s));
                }

                if (!doneProperties.Contains(ResourceProperty.Rate))
                {
                    var rates = resources.Select(x => x.Rate).Where(x => x > 0).ToHashSet().ToList();
                    targets.Add(await _buildSimpleMigrationTargetViewModel(rates, ResourceProperty.Rate, null,
                        CustomPropertyType.Rating, [CustomPropertyType.Rating], r => r.ToString("F2")));
                }

                if (!doneProperties.Contains(ResourceProperty.Language))
                {
                    var languages = resources.Select(x => x.Language).Where(x => x > 0).ToHashSet().ToList();
                    targets.Add(await _buildSimpleMigrationTargetViewModel(languages, ResourceProperty.Language, null,
                        CustomPropertyType.SingleChoice, [CustomPropertyType.SingleChoice], l => l.ToString()));
                }

                if (!doneProperties.Contains(ResourceProperty.ReleaseDt))
                {
                    var releaseDtList = resources.Select(x => x.ReleaseDt).Where(x => x.HasValue).Select(x => x!.Value)
                        .ToHashSet().ToList();
                    targets.Add(await _buildSimpleMigrationTargetViewModel(releaseDtList, ResourceProperty.ReleaseDt,
                        null, CustomPropertyType.Date, [CustomPropertyType.Date, CustomPropertyType.DateTime],
                        r => r.ToString("yyyy-MM-dd HH:mm:ss")));
                }
            }

            if (!doneProperties.Contains(ResourceProperty.Original))
            {
                var originals = (await _originalService.GetAllDtoList()).Select(x => x.Name).ToList();
                targets.Add(await _buildSimpleMigrationTargetViewModel(originals, ResourceProperty.Original, null,
                    CustomPropertyType.MultipleChoice, [CustomPropertyType.MultipleChoice], s => s));
            }

            if (!doneProperties.Contains(ResourceProperty.Series))
            {
                var series = (await _seriesService.GetAll()).Select(x => x.Name).ToList();
                targets.Add(await _buildSimpleMigrationTargetViewModel(series, ResourceProperty.Series, null,
                    CustomPropertyType.SingleChoice, [CustomPropertyType.SingleChoice], s => s));
            }

            var allVolumeKeys = new string[] {nameof(VolumeDto.Index), nameof(VolumeDto.Title), nameof(VolumeDto.Name)};
            var doneVolumeKeys = _migrationOptions.Value?.Properties?.Where(p => p.Property == ResourceProperty.Volume)
                .Select(p => p.PropertyKey).Where(s => !string.IsNullOrEmpty(s)).ToHashSet() ?? [];
            var restVolumeKeys = allVolumeKeys.Except(doneVolumeKeys).ToHashSet();
            if (restVolumeKeys.Any())
            {
                var volumes = (await _volumeService.GetAll()).Select(v => v.ToDto()!).ToList();
                var target = await _buildVolumeMigrationTargetViewModel(volumes, restVolumeKeys!);
                if (target != null)
                {
                    targets.Add(target);
                }
            }

            if (!doneProperties.Contains(ResourceProperty.Favorites))
            {
                var favorites = (await _favoritesService.GetAll()).Select(f => f.Name).Distinct().ToList();
                targets.Add(await _buildSimpleMigrationTargetViewModel(favorites, ResourceProperty.Favorites, null,
                    CustomPropertyType.SingleChoice, [CustomPropertyType.SingleChoice], s => s));
            }

            var doneCustomPropertyKeys = _migrationOptions.Value?.Properties?
                .Where(p => p.Property == ResourceProperty.CustomProperty).Select(p => p.PropertyKey)
                .Where(s => !string.IsNullOrEmpty(s)).ToHashSet() ?? [];
            var customResourceProperties = (await _customResourcePropertyService.GetAll())!;
            targets.Add(
                await _buildCustomPropertyMigrationTargetViewModel(customResourceProperties, doneCustomPropertyKeys!));

            return targets.Where(t => t.DataCount > 0).ToList();
        }

        private async Task<MigrationTargetViewModel?> _buildVolumeMigrationTargetViewModel(List<VolumeDto> volumes, HashSet<string> includedKeys)
        {
            var indexes = volumes.Select(v => v.Index).ToHashSet();
            var titles = volumes.Select(v => v.Title).Where(x => !string.IsNullOrEmpty(x)).ToHashSet();
            var names = volumes.Select(v => v.Name).Where(x => !string.IsNullOrEmpty(x)).ToHashSet();

            var subTargets = new List<MigrationTargetViewModel>();

            if (indexes.Any() && includedKeys.Contains(nameof(VolumeDto.Index)))
            {
                subTargets.Add(await _buildSimpleMigrationTargetViewModel(indexes.ToList(), ResourceProperty.Volume,
                    nameof(VolumeDto.Index),
                    CustomPropertyType.Number, [CustomPropertyType.Number], s => s.ToString()));
            }

            if (titles.Any() && includedKeys.Contains(nameof(VolumeDto.Title)))
            {
                subTargets.Add(await _buildSimpleMigrationTargetViewModel(titles.ToList(), ResourceProperty.Volume,
                    nameof(VolumeDto.Title),
                    CustomPropertyType.SingleLineText, [CustomPropertyType.SingleLineText], s => s!));
            }

            if (names.Any() && includedKeys.Contains(nameof(VolumeDto.Name)))
            {
                subTargets.Add(await _buildSimpleMigrationTargetViewModel(names.ToList(), ResourceProperty.Volume,
                    nameof(VolumeDto.Name),
                    CustomPropertyType.SingleLineText, [CustomPropertyType.SingleLineText], s => s!));
            }

            if (subTargets.Any())
            {
                return new MigrationTargetViewModel
                {
                    Property = ResourceProperty.Volume,
                    DataCount = volumes.Count,
                    SubTargets = subTargets
                };
            }

            return null;
        }

        private async Task<MigrationTargetViewModel> _buildCustomPropertyMigrationTargetViewModel(
            List<CustomResourceProperty> customResourceProperties, HashSet<string> ignoredKeys)
        {
            var customPropertiesGroups = customResourceProperties.GroupBy(x => x.ResourceId)
                .ToDictionary(d => d.Key,
                    d => d.GroupBy(c => c.Key)
                        .ToDictionary(e => e.Key, e => e.Select(f => f.Value).ToList()))
                .SelectMany(a => a.Value).GroupBy(a => a.Key)
                .Where(a => !ignoredKeys.Contains(a.Key))
                .ToDictionary(a => a.Key, a =>
                {
                    var lists = a.Select(b => b.Value.Distinct().OrderBy(c => c).ToList()).ToList();
                    var uniqueLists = new List<List<string>>();
                    foreach (var list in lists)
                    {
                        if (list.Any() && uniqueLists.Any(x => x.Count == list.Count && x.All(y => list.Contains(y))))
                        {
                            continue;
                        }

                        uniqueLists.Add(list);
                    }

                    return uniqueLists;
                });

            var subModels = new List<MigrationTargetViewModel>();
            var treatCustomPropertyAsType = StandardValueType.MultipleChoice;

            foreach (var (key, list) in customPropertiesGroups)
            {
                var sm = new MigrationTargetViewModel
                {
                    Property = ResourceProperty.CustomProperty,
                    PropertyKey = key,
                    Data = list.Select(l => string.Join(BusinessConstants.TextSeparator, l)).ToList(),
                    DataCount = list.Count,
                    TargetCandidates = []
                };

                var candidates = treatCustomPropertyAsType.GetAllConversionCandidates();
                foreach (var (toType, _) in candidates)
                {
                    var lossData = new Dictionary<StandardValueConversionLoss, List<string>>();
                    foreach (var d in list)
                    {
                        var loss = await _conversionService.CheckConversionLoss(d, treatCustomPropertyAsType, toType);
                        if (loss != StandardValueConversionLoss.None)
                        {
                            var displayData = string.Join(BusinessConstants.TextSeparator, d);
                            foreach (var f in loss.GetFlags())
                            {
                                if (f != StandardValueConversionLoss.None)
                                {
                                    lossData.GetOrAdd(f, () => []).Add(displayData);
                                }
                            }
                        }
                    }

                    sm.TargetCandidates.Add(new MigrationTargetViewModel.PropertyTypeCandidate
                    {
                        Type = (CustomPropertyType) toType,
                        LossData = lossData.ToDictionary(s => (int) s.Key, s => s.Value)
                    });
                }

                subModels.Add(sm);
            }

            return new MigrationTargetViewModel
            {
                Property = ResourceProperty.CustomProperty,
                DataCount = subModels.Sum(x => x.DataCount),
                SubTargets = subModels
            };
        }

        private async Task<MigrationTargetViewModel> _buildSimpleMigrationTargetViewModel<T>(List<T> data,
            ResourceProperty property, string? propertyKey, CustomPropertyType fromType,
            CustomPropertyType[] candidateTargetTypes, Func<T, string> toString)
        {
            var distinctData = data.Distinct().ToArray();
            var model = new MigrationTargetViewModel
            {
                Data = distinctData,
                DataCount = data.Count,
                Property = property,
                PropertyKey = propertyKey,
                TargetCandidates = [] 
            };

            foreach (var tc in candidateTargetTypes)
            {
                var lossData = new Dictionary<StandardValueConversionLoss, List<string>>();
                foreach (var d in data)
                {
                    var loss = await _conversionService.CheckConversionLoss(fromType,
                        (StandardValueType) fromType, (StandardValueType) tc);
                    if (loss != StandardValueConversionLoss.None)
                    {
                        foreach (var f in loss.GetFlags())
                        {
                            if (f != StandardValueConversionLoss.None)
                            {
                                lossData.GetOrAdd(f, () => []).Add(toString(d));
                            }
                        }
                    }
                }

                var candidate = new MigrationTargetViewModel.PropertyTypeCandidate
                {
                    Type = tc,
                    LossData = lossData.ToDictionary(s => (int) s.Key, s => s.Value)
                };

                model.TargetCandidates.Add(candidate);
            }

            return model;
        }

        public async Task<BaseResponse> ApplyMigration(MigrationTargetApplyInputModel target)
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

                            var propertyDbModel = typedProperty.ToDbModel()!;
                            await _customPropertyService.Put(typedProperty.Id, new CustomPropertyAddOrPutDto
                            {
                                Name = propertyDbModel.Name,
                                Options = propertyDbModel.Options,
                                Type = propertyDbModel.Type
                            });
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
                            CustomProperty propertyDbModel;
                            if (targetProperty.Type == CustomPropertyType.SingleChoice)
                            {
                                var typedProperty = targetProperty as SingleChoiceProperty;
                                typedProperty!.Options!.AddChoices(true, choiceValues);
                                choiceValueIdMap = typedProperty.Options!.Choices!.GroupBy(x => x.Value)
                                    .ToDictionary(x => x.Key, x => x.First().Id);
                                propertyDbModel = typedProperty.ToDbModel()!;
                            }
                            else
                            {
                                var typedProperty = targetProperty as MultipleChoiceProperty;
                                typedProperty!.Options!.AddChoices(true, choiceValues);
                                choiceValueIdMap = typedProperty.Options!.Choices!.GroupBy(x => x.Value)
                                    .ToDictionary(x => x.Key, x => x.First().Id);
                                propertyDbModel = typedProperty.ToDbModel()!;
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
                            await _customPropertyService.Put(targetProperty.Id, new CustomPropertyAddOrPutDto
                            {
                                Name = propertyDbModel.Name,
                                Options = propertyDbModel.Options,
                                Type = propertyDbModel.Type
                            });
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
                            var propertyDbModel = typedProperty.ToDbModel()!;
                            await _customPropertyService.Put(typedProperty.Id, new CustomPropertyAddOrPutDto
                            {
                                Name = propertyDbModel.Name,
                                Options = propertyDbModel.Options,
                                Type = propertyDbModel.Type
                            });
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
                            var propertyDbModel = typedProperty.ToDbModel()!;
                            await _customPropertyService.Put(typedProperty.Id, new CustomPropertyAddOrPutDto
                            {
                                Name = propertyDbModel.Name,
                                Options = propertyDbModel.Options,
                                Type = propertyDbModel.Type
                            });
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
                            var resourceValues = propertyValues.ToDictionary(d => d.Key,
                                d => string.Join(BusinessConstants.TextSeparator, d.Value));
                            var choiceValues = resourceValues.Values.ToHashSet().ToArray();
                            var typedProperty = targetProperty as SingleChoiceProperty;
                            typedProperty!.Options!.AddChoices(true, choiceValues!);
                            var choiceValueIdMap = typedProperty.Options!.Choices!.GroupBy(x => x.Value)
                                .ToDictionary(x => x.Key, x => x.First().Id);

                            var noValueResourceIds = propertyValues.Keys.Except(currentValues.Keys).ToList();
                            var newValues = noValueResourceIds.Select(x => new SingleChoicePropertyValue()
                            {
                                PropertyId = targetProperty.Id,
                                ResourceId = x,
                                Value = choiceValueIdMap[resourceValues[x]!]
                            }).ToList();
                            var propertyDbModel = typedProperty.ToDbModel()!;
                            await _customPropertyService.Put(typedProperty.Id, new CustomPropertyAddOrPutDto
                            {
                                Name = propertyDbModel.Name,
                                Options = propertyDbModel.Options,
                                Type = propertyDbModel.Type
                            });
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
                            var propertyDbModel = typedProperty.ToDbModel()!;
                            await _customPropertyService.Put(typedProperty.Id, new CustomPropertyAddOrPutDto
                            {
                                Name = propertyDbModel.Name,
                                Options = propertyDbModel.Options,
                                Type = propertyDbModel.Type
                            });
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
                            var propertyDbModel = typedProperty.ToDbModel()!;
                            await _customPropertyService.Put(typedProperty.Id, new CustomPropertyAddOrPutDto
                            {
                                Name = propertyDbModel.Name,
                                Options = propertyDbModel.Options,
                                Type = propertyDbModel.Type
                            });
                            await _customPropertyValueService.AddRange(newValues);
                            break;
                        }
                        default:
                            throw new Exception($"Invalid target property type: {targetProperty.Type}");
                    }

                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }

            await _migrationOptions.SaveAsync(m =>
            {
                if (m.Properties?.Any(x => x.Property == target.Property && x.PropertyKey == target.PropertyKey) !=
                    true)
                {
                    (m.Properties ??= []).Add(new MigrationOptions.PropertyMigrationOptions
                        {PropertyKey = target.PropertyKey, Property = target.Property});
                }
            });

            return BaseResponseBuilder.Ok;
        }
    }
}