using System.Collections.Generic;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using OllamaSharp.Models.Chat;

namespace Bakabase.InsideWorld.Business.Components.BuiltinMediaLibraryTemplate;

public record BuiltinMediaLibraryTemplateDescriptor
{
    public string Id { get; set; } = null!;
    public BuiltinMediaLibraryTemplateType Type { get; set; }
    public string TypeName { get; set; } = null!;
    public MediaType MediaType { get; set; }

    public string Name { get; set; } = null!;

    // public string? Description { get; set; }
    public List<BuiltinMediaLibraryTemplateProperty> Properties { get; set; } = [];
    public string[] PropertyNames { get; set; } = [];
    public string[]? LayeredPropertyNames { get; set; }
    public List<BuiltinMediaLibraryTemplateProperty>? LayeredProperties { get; set; }
    public Dictionary<int, List<Property>>? EnhancerProperties { get; set; }
}