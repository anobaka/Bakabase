using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Dtos
{
    public record BulkModificationFilterResult
    {
        public string FilterKey { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public List<Business.Models.Domain.Resource> Resources { get; set; } = new();
    }
}
