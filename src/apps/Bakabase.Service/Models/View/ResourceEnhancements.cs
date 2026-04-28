using System;
using System.Collections.Generic;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;

namespace Bakabase.Service.Models.View;

public record ResourceEnhancements
{
    public IEnhancerDescriptor Enhancer { get; set; } = null!;
    public DateTime? ContextCreatedAt { get; set; }
    public DateTime? ContextAppliedAt { get; set; }
    public EnhancementRecordStatus Status { get; set; }
    public TargetEnhancement[] Targets { get; set; } = [];
    public DynamicTargetEnhancements[] DynamicTargets { get; set; } = [];
    public List<EnhancementLogViewModel>? Logs { get; set; }
    public EnhancerFullOptions? OptionsSnapshot { get; set; }
    public string? ErrorMessage { get; set; }

    public record EnhancementLogViewModel
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = null!;
        public string Event { get; set; } = null!;
        public string Message { get; set; } = null!;
        public object? Data { get; set; }
    }

    public record DynamicTargetEnhancements
    {
        public int Target { get; set; }
        public string TargetName { get; set; } = null!;
        public List<EnhancementViewModel>? Enhancements { get; set; }
    }

    public record TargetEnhancement
    {
        public int Target { get; set; }
        public string TargetName { get; set; } = null!;
        public EnhancementViewModel? Enhancement { get; set; }
    }
}