using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bootstrap.Models.RequestModels;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public record EnhancementRecordSearchRequestModel : SearchRequestModel
    {
        public int? ResourceId { get; set; }
        public bool? Success { get; set; }
        public string? EnhancerDescriptorId { get; set; }
    }
}
