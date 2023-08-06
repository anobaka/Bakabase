using Bakabase.InsideWorld.Models.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Aos;

namespace Bakabase.InsideWorld.Models.Configs.Resource
{
    public class ResourceOptionsDto
    {
        public DateTime LastSyncDt { get; set; }
        public DateTime LastNfoGenerationDt { get; set; }
        public ResourceSearchDto LastSearch { get; set; }
        public List<ResourceSearchSlotItemDto> SearchSlots { get; set; }
        public AdditionalCoverDiscoveringSource[] AdditionalCoverDiscoveringSources { get; set; }
    }
}
