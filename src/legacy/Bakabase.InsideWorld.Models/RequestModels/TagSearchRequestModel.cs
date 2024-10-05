using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bootstrap.Models.RequestModels;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public record TagSearchRequestModel : SearchRequestModel
    {
        public int? GroupId { get; set; }
        public TagAdditionalItem AdditionalItems { get; set; }
    }
}
