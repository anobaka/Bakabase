using System;
using System.Collections.Generic;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models
{
    public record BulkModificationDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public BulkModificationStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public BulkModificationFilterGroup? Filter { get; set; }
        public List<BulkModificationProcess> Modifications { get; set; } = new();
    }
}