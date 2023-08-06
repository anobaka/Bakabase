using System.Collections.Generic;
using Bootstrap.Models.RequestModels;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class SerialSearchRequestModel : SearchRequestModel
    {
        public string Name { get; set; }
        public List<string> Names { get; set; }
        public List<int> AliasGroupIds { get; set; }
    }
}