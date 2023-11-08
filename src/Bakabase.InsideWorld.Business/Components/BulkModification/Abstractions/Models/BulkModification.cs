using System;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models
{
    public record BulkModification
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public BulkModificationStatus Status { get; set; } = BulkModificationStatus.Initial;
        public string? Filter { get; set; }
        public string? Modification { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}