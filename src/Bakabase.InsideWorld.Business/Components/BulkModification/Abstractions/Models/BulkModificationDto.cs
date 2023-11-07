using System;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models
{
    public record BulkModificationDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public BulkModificationStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public BulkModificationFilterGroup? Filter { get; set; }
    }
}