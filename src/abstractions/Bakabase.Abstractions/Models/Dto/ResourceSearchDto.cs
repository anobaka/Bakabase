using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Input;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bootstrap.Models.RequestModels;

namespace Bakabase.Abstractions.Models.Dto
{
    public class ResourceSearchDto : SearchRequestModel
    {
        public ResourceSearchFilterGroup? Group { get; set; }
        public ResourceSearchOrderInputModel[]? Orders { get; set; }
        public string? Keyword { get; set; }

        public ResourceSearchDto Copy() => new()
        {
            Group = Group?.Copy(), 
            Orders = Orders?.Select(o => o with { }).ToArray(), 
            Keyword = Keyword,
            PageIndex = PageIndex,
            PageSize = PageSize
        };
    }
}
