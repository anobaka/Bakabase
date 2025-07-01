using System.ComponentModel.DataAnnotations;
using System.Linq;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain.Sharable;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Presets.Abstractions;
using Bakabase.Modules.Presets.Abstractions.Components;
using Bakabase.Modules.Presets.Abstractions.Models;
using Bakabase.Modules.Presets.Abstractions.Models.Constants;
using Bakabase.Modules.Property.Components;
using Bootstrap.Extensions;
using PresetProperty = Bakabase.Modules.Presets.Abstractions.Models.Constants.PresetProperty;

namespace Bakabase.Modules.Presets.Services;

internal class PresetsService(
    IBakabaseLocalizer bakabaseLocalizer,
    IMediaLibraryTemplateService mediaLibraryTemplateService,
    IPresetsLocalizer presetsLocalizer,
    IEnhancerDescriptors enhancerDescriptors) : IPresetsService
{
    public MediaLibraryTemplatePresetDataPool GetMediaLibraryTemplatePresetDataPool()
    {
        var properties = SpecificEnumUtils<PresetProperty>.Values
            .Select(p => new MediaLibraryTemplatePresetDataPool.Property(p,
                p.GetAttribute<DisplayAttribute>()?.GetName() ?? p.ToString(),
                p.GetAttribute<PresetPropertyAttribute>()!.Type,
                null))
            .ToList();
        var resourceTypes = SpecificEnumUtils<PresetResourceType>.Values
            .Select(t =>
                new MediaLibraryTemplatePresetDataPool.ResourceType(t,
                    t.GetAttribute<DisplayAttribute>()?.GetName() ?? t.ToString(), t.GetAttribute<PresetResourceTypeAttribute>()!.MediaType, presetsLocalizer.ResourceTypeDescription(t)))
            .ToList();
        var enhancers = enhancerDescriptors.Descriptors.Select(d =>
            new MediaLibraryTemplatePresetDataPool.Enhancer((EnhancerId)d.Id, d.Name, d.Description,
                d.Targets.Where(x => x.ReservedPropertyCandidate.HasValue)
                    .Select(x => x.ReservedPropertyCandidate!.Value).ToArray(), d.Targets.Select(p =>
                {
                    if (p.ReservedPropertyCandidate.HasValue)
                    {
                        return null;
                    }

                    var cp = properties.FirstOrDefault(x => x.Name == p.Name && x.Type == p.PropertyType);
                    return cp?.Id;
                }).OfType<PresetProperty>().Distinct().ToArray())).ToList();
        var pool = new MediaLibraryTemplatePresetDataPool
        {
            ResourceTypes = resourceTypes,
            Properties = properties,
            Enhancers = enhancers,
            ResourceTypePresetPropertyIds = SpecificEnumUtils<PresetResourceType>.Values.ToDictionary(d => (int)d,
                d => d.GetAttribute<PresetResourceTypeAttribute>()!.Properties.Distinct().ToList()),
            ResourceTypeEnhancerIds = SpecificEnumUtils<PresetResourceType>.Values.ToDictionary(d => (int)d,
                d => d.GetAttribute<PresetResourceTypeAttribute>()!.EnhancerIds.Distinct().ToList())
        };
        return pool;
    }

    public async Task<int> AddMediaLibrary(MediaLibraryTemplateCompactBuilder mlBuilder)
    {
        var dataPool = GetMediaLibraryTemplatePresetDataPool();
        var propertiesMap = dataPool.Properties.ToDictionary(d => d.Id, d => d);
        var resourceTypeMap = dataPool.ResourceTypes.ToDictionary(d => d.Type, d => d);
        var sharableTemplate = new SharableMediaLibraryTemplate
        {
            Name = mlBuilder.Name,
            ResourceFilters =
                [new SharablePathFilter { Positioner = PathPositioner.Layer, Layer = mlBuilder.ResourceLayer }],
            Properties = mlBuilder.Properties.Select(pp =>
            {
                var p = propertiesMap[pp];
                var layerIdx = mlBuilder.LayeredProperties?.IndexOf(pp);
                return new SharableMediaLibraryTemplateProperty
                {
                    Property = new Bakabase.Abstractions.Models.Domain.Property(PropertyPool.Custom, 0, p.Type, p.Name),
                    ValueLocators = layerIdx > -1
                        ? [new PathFilter { Layer = layerIdx.Value + 1, Positioner = PathPositioner.Layer }]
                        : null
                };
            }).ToList(),
            PlayableFileLocator = new SharableMediaLibraryTemplatePlayableFileLocator
            {
                ExtensionGroups =
                [
                    new ExtensionGroup()
                    {
                        Name = bakabaseLocalizer.MediaType(resourceTypeMap[mlBuilder.ResourceType].MediaType),
                        Extensions = InternalOptions
                            .MediaTypeExtensions[resourceTypeMap[mlBuilder.ResourceType].MediaType]
                            .ToHashSet()
                    }
                ]
            },
            Enhancers = mlBuilder.EnhancerIds?.Select(e =>
            {
                var eo = new SharableMediaLibraryTemplateEnhancerOptions
                {
                    EnhancerId = (int)e,
                };
                var pe = dataPool.Enhancers.FirstOrDefault(x => x.Id == e);
                var ed = enhancerDescriptors[(int)e];
                if (ed.Targets.Any())
                {
                    eo.TargetOptions = [];
                    foreach (var t in ed.Targets)
                    {
                        if (t.ReservedPropertyCandidate.HasValue)
                        {
                            eo.TargetOptions.Add(new SharableMediaLibraryTemplateEnhancerTargetAllInOneOptions
                            {
                                Property = new Bakabase.Abstractions.Models.Domain.Property(PropertyPool.Reserved,
                                    (int)t.ReservedPropertyCandidate.Value,
                                    PropertyInternals.ReservedPropertyMap[t.ReservedPropertyCandidate.Value].Type,
                                    t.Name),
                                Target = t.Id
                            });
                        }
                        else
                        {
                            var cp = propertiesMap.Values.FirstOrDefault(x =>
                                x.Name == t.Name && x.Type == t.PropertyType);
                            if (cp != null && pe?.PresetProperties.Contains(cp.Id) == true)
                            {
                                eo.TargetOptions.Add(new SharableMediaLibraryTemplateEnhancerTargetAllInOneOptions
                                {
                                    Property = new Bakabase.Abstractions.Models.Domain.Property(PropertyPool.Custom, 0,
                                        cp.Type, t.Name),
                                    Target = t.Id
                                });
                            }
                        }
                    }
                }

                return eo;
            }).ToList(),
        };

        return await mediaLibraryTemplateService.Import(new MediaLibraryTemplateImportInputModel
        {
            AutomaticallyCreateMissingData = true,
            Name = mlBuilder.Name,
            ShareCode = sharableTemplate.ToShareCode()
        });
    }
}