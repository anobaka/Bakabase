using System;

namespace Bakabase.InsideWorld.Business.Components.BuiltinMediaLibraryTemplate;

[AttributeUsage(AttributeTargets.Field)]
public class BuiltinMediaLibraryTemplateAttribute(
    BuiltinMediaLibraryTemplateProperty[] properties
) : Attribute
{
    public BuiltinMediaLibraryTemplateProperty[] Properties { get; set; } = properties;
}