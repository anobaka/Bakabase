using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Modules.Enhancer.Models.View;

public record ResourceEnhancements
{
    public int EnhancerId { get; set; }
    public string EnhancerName { get; set; } = null!;
    public DateTime? EnhancedAt { get; set; }
    public TargetEnhancement[] Targets { get; set; } = [];

    public record TargetEnhancement
    {
        public int Target { get; set; }
        public string TargetName { get; set; } = null!;
        public Enhancement? Enhancement { get; set; }
    }
}