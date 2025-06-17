using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Extensions;

public class MediaLibraryTemplateUtils
{
    public static MediaLibraryTemplate BuildTemplateByLayer(string name, string? description, MediaType type,
        string typeName, List<Property> properties, int layer)
    {
        return new MediaLibraryTemplate
        {
            Name = name,
            Description = description,
            PlayableFileLocator = new MediaLibraryTemplatePlayableFileLocator
            {
                ExtensionGroups =
                    [new ExtensionGroup(0, typeName, InternalOptions.MediaTypeExtensions[type].ToHashSet())]
            },
            Properties = properties.Select(p => new MediaLibraryTemplateProperty {Property = p}).ToList(),
            ResourceFilters =
            [
                new PathFilter
                {
                    Layer = layer,
                    Positioner = PathPositioner.Layer
                }
            ],
        };
    }
}