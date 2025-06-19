using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;

namespace Bakabase.InsideWorld.Business.Components.BuiltinMediaLibraryTemplate;

[AttributeUsage(AttributeTargets.Field)]
public class BuiltinMediaLibraryTemplateAttribute(
    MediaType mediaType,
    BuiltinMediaLibraryTemplateProperty[] properties,
    EnhancerId[]? enhancerIds = null
) : Attribute
{
    public MediaType MediaType { get; } = mediaType;
    public EnhancerId[]? EnhancerIds { get; } = enhancerIds;

    public List<BuiltinMediaLibraryTemplateProperty> Properties { get; } = properties.ToList();
}