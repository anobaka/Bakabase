using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Regex;

public record RegexEnhancerOptions
{
    public List<int>? CategoryIds { get; set; }
    public List<string>? Regexes { get; set; }

    public record CaptureGroupOptions
    {
        public string Name { get; set; } = null!;
        public PropertyPool PropertyPool { get; set; }
        public int PropertyId { get; set; }
    }

    public List<CaptureGroupOptions>? CaptureGroups { get; set; }
}