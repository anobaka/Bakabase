using System.Collections.Generic;
using Bakabase.InsideWorld.Business.Components.PlayList.Models.Domain;

namespace Bakabase.InsideWorld.Business.Components.PlayList.Models.Input
{
    public class PlayListPatchInputModel
    {
        public string? Name { get; set; }
        public List<PlayListItem>? Items { get; set; }
        public int? Interval { get; set; }
        public int? Order { get; set; }
    }
}