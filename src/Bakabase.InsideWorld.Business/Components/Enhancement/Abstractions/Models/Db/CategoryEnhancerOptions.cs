using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions.Models.Db
{
    public record CategoryEnhancerOptions
    {
        public int Id { get; set; }
        public int CategoryId { get; set; }
        public EnhancerId EnhancerId { get; set; }
        public string? TargetPropertyIdMap { get; set; }
    }
}
