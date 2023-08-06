using System.Collections.Generic;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class PublisherUpdateModel
    {
        public string? Name { get; set; }
        public int? Rank { get; set; }
        public bool? Favorite { get; set; }
        public List<int>? TagIds { get; set; }
    }
}
