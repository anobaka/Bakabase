using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.BuiltinMediaLibraryTemplate;

[AttributeUsage(AttributeTargets.Field)]
public class BuiltinMediaLibraryTemplateAttribute(
    MediaType mediaType,
    BuiltinMediaLibraryTemplateProperty[] properties
) : Attribute
{
    public MediaType MediaType { get; } = mediaType;
    public List<BuiltinMediaLibraryTemplateProperty> Properties { get; } = properties.ToList();
}