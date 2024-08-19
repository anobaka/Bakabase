using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Abstractions.Components;

namespace Bakabase.Modules.Enhancer.Models.View;

public record ResourceEnhancements
{
    public IEnhancerDescriptor Enhancer { get; set; }
    public DateTime? EnhancedAt { get; set; }
    public TargetEnhancement[] Targets { get; set; } = [];
    public DynamicTargetEnhancements[] DynamicTargets { get; set; } = [];

    public record DynamicTargetEnhancements
    {
        public int Target { get; set; }
        public string TargetName { get; set; } = null!;
        public List<Enhancement>? Enhancements { get; set; }
    }

    public record TargetEnhancement
    {
        public int Target { get; set; }
        public string TargetName { get; set; } = null!;
        public Enhancement? Enhancement { get; set; }
    }
}