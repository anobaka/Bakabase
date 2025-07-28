using System;
using System.Collections.Generic;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Models.Configs;

[Options(fileKey: "enhancer")]
[Obsolete]
public record EnhancerOptions
{
    [Obsolete]
    public RegexEnhancerModel? RegexEnhancer { get; set; }

    [Obsolete]
    public record RegexEnhancerModel
    {
        public List<string>? Expressions { get; set; }
    }
}