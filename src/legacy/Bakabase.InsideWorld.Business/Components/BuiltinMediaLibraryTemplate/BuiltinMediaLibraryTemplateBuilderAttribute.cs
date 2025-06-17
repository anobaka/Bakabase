using System;

namespace Bakabase.InsideWorld.Business.Components.BuiltinMediaLibraryTemplate;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
public class BuiltinMediaLibraryTemplateBuilderAttribute(BuiltinMediaLibraryTemplateProperty[]? properties = null) : Attribute
{
    public BuiltinMediaLibraryTemplateProperty[]? Properties => properties;
}