using Bakabase.InsideWorld.Models.Configs.Resource;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Bakabase.InsideWorld.Models.Configs.Resource.ResourceOptions;

namespace Bakabase.InsideWorld.Models.RequestModels.Options
{
    public class ResourceOptionsPatchRequestModel
    {
        public DateTime? LastSyncDt { get; set; }
        public DateTime? LastNfoGenerationDt { get; set; }
        public ResourceSearchDto? LastSearch { get; set; }
        public List<ResourceSearchSlotItemDto>? SearchSlots { get; set; }
        public AdditionalCoverDiscoveringSource[]? AdditionalCoverDiscoveringSources { get; set; }
        public CoverOptionsModel? CoverOptions { get; set; }
    }
}
