using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bootstrap.Extensions;
using NPOI.Util.Collections;

namespace Bakabase.InsideWorld.Business.Components.BuiltinMediaLibraryTemplate;

public class BuiltinMediaLibraryTemplateService(
    IBuiltinMediaLibraryTemplateLocalizer localizer,
    IBakabaseLocalizer bakabaseLocalizer)
{
    private string BuildName(BuiltinMediaLibraryTemplateType type,
        List<BuiltinMediaLibraryTemplateProperty>? properties)
    {
        var name = $"[{localizer.BuiltinMediaLibraryTemplate_TypeName(type)}]";
        if (properties?.Any() == true)
        {
            name +=
                $"{string.Join("/", properties.Select(localizer.BuiltinMediaLibraryTemplate_PropertyName))}/";
        }

        name += bakabaseLocalizer.Resource();
        return name;
    }

    public BuiltinMediaLibraryTemplateDescriptor[] GetAll()
    {
        var types = SpecificEnumUtils<BuiltinMediaLibraryTemplateType>.Values;
        var descriptors = types.SelectMany(t =>
        {
            var attr = t.GetAttribute<BuiltinMediaLibraryTemplateAttribute>()!;
            var builders = t.GetAttributes<BuiltinMediaLibraryTemplateBuilderAttribute>();

            return builders.Select(b =>
            {
                var d = new BuiltinMediaLibraryTemplateDescriptor
                {
                    Id = b.Id,
                    Type = t,
                    TypeName = localizer.BuiltinMediaLibraryTemplate_TypeName(t),
                    MediaType = attr.MediaType,
                    Name = BuildName(t, b.Properties),
                    // Description = "将视频类文件视为可播放文件。包含媒体库路径后第1级是xxx，第2级是xxx，",
                    Properties = attr.Properties,
                    PropertyNames =
                        attr.Properties.Select(localizer.BuiltinMediaLibraryTemplate_PropertyName).ToArray(),
                    LayeredProperties = b.Properties,
                    LayeredPropertyNames = b.Properties?.Select(localizer.BuiltinMediaLibraryTemplate_PropertyName)
                        .ToArray()
                };
                return d;
            });
        }).ToArray();

        return descriptors;
    }

    public MediaLibraryTemplate GenerateTemplate(string builtinTemplateId)
    {
        var descriptor = GetAll().First(x => x.Id == builtinTemplateId);

        var template = new MediaLibraryTemplate
        {
            Name = BuildName(descriptor.Type, descriptor.LayeredProperties),
            // Description = localizer.BuiltinMediaLibraryTemplate_Description(type, builder.Name),
            PlayableFileLocator = new MediaLibraryTemplatePlayableFileLocator
            {
                ExtensionGroups =
                [
                    new ExtensionGroup
                    {
                        Id = 0,
                        Name = bakabaseLocalizer.MediaType(descriptor.MediaType),
                        Extensions = InternalOptions.MediaTypeExtensions[descriptor.MediaType].ToHashSet()
                    }
                ]
            },
            Properties = descriptor.Properties.Select((p, pIdx) =>
            {
                var mp = new MediaLibraryTemplateProperty
                {
                    Property = BuiltinMediaLibraryTemplateData.PropertyMap[p] with
                    {
                        Name = descriptor.PropertyNames[pIdx]
                    }
                };
                if (descriptor.LayeredProperties?.Any() == true)
                {
                    var idx = descriptor.LayeredProperties.IndexOf(p);
                    if (idx >= 0)
                    {
                        mp.ValueLocators = [new PathFilter {Positioner = PathPositioner.Layer, Layer = idx + 1}];
                    }
                }

                return mp;
            }).ToList(),
            ResourceFilters =
            [
                new PathFilter
                    {Positioner = PathPositioner.Layer, Layer = (descriptor.LayeredProperties?.Count ?? 0) + 1}
            ]
        };

        return template;
    }
}