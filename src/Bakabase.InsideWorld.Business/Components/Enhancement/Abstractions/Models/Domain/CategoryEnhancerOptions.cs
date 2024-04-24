using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions.Models.Domain
{
    public record CategoryEnhancerOptions
    {
        public int Id { get; set; }
        public int CategoryId { get; set; }
        public EnhancerId EnhancerId { get; set; }
        public Dictionary<int, EnhancerTargetOptions>? TargetOptionsMap { get; set; }
    }
}
