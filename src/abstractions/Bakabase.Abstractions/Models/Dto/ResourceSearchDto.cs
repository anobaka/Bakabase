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
    }
}
