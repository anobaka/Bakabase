using System.Collections.Generic;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Models.Configs;

[Options(fileKey: "enhancer")]
public record EnhancerOptions
{
    public RegexEnhancerModel? RegexEnhancer { get; set; }

    public record RegexEnhancerModel
    {
        public List<string>? Expressions { get; set; }
    }
}