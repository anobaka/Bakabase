using System.Collections.Generic;

namespace Bakabase.InsideWorld.Business.Components.BuiltinMediaLibraryTemplate;

public record BuiltinMediaLibraryTemplateDescriptor
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string[] Properties { get; set; } = [];
    public Dictionary<int, string> LayerProperties = [];
}