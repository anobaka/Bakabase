using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Dtos
{
    public record BulkModificationFilterResult
    {
        public string FilterKey { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public List<Bakabase.Abstractions.Models.Domain.Resource> Resources { get; set; } = new();
    }
}
