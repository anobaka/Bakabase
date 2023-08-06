using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bootstrap.Models.RequestModels;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class AliasSearchRequestModel: SearchRequestModel
    {
        public string[]? Names { get; set; }
        public string? Name { get; set; }

        public bool Exactly { get; set; }

        public AliasAdditionalItem AdditionalItems { get; set; }
    }
}