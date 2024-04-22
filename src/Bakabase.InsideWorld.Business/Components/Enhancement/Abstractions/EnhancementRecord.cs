using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions
{
    public record EnhancementRecord
    {
        public int Id { get; set; }
        public int ResourceId { get; set; }
        public EnhancerId EnhancerId { get; set; }
        public string? Enhancements { get; set; }
        public DateTime EnhancedAt { get; set; }
    }
}