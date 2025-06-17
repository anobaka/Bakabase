using System;
using System.Collections.Generic;
using System.Linq;

namespace Bakabase.InsideWorld.Business.Components.BuiltinMediaLibraryTemplate;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
public class BuiltinMediaLibraryTemplateBuilderAttribute(
    string id,
    BuiltinMediaLibraryTemplateProperty[]? properties = null) : Attribute
{
    public string Id { get; } = id;
    public List<BuiltinMediaLibraryTemplateProperty>? Properties { get; } = properties?.ToList();
}