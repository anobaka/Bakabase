using System;
using System.Collections.Generic;
using System.Linq;

namespace Bakabase.InsideWorld.Business.Components.BuiltinMediaLibraryTemplate;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
public class BuiltinMediaLibraryTemplateBuilderAttribute(
    BuiltinMediaLibraryTemplateProperty[]? properties = null) : Attribute
{
    public List<BuiltinMediaLibraryTemplateProperty>? Properties { get; } = properties?.ToList();
}