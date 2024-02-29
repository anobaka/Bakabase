using System;
using System.Collections.Generic;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Dtos
{
    public record BulkModificationDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public BulkModificationStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<BulkModificationVariable> Variables { get; set; } = new();
        public BulkModificationFilterGroup? Filter { get; set; }
        public List<BulkModificationProcess> Processes { get; set; } = new();
        public List<ResourceDiff> Diffs { get; set; } = new();
        public List<int>? FilteredResourceIds { get; set; }
        public DateTime? FilteredAt { get; set; }
        public DateTime? CalculatedAt { get; set; }
        public DateTime? AppliedAt { get; set; }
        public DateTime? RevertedAt { get; set; }
    }
}