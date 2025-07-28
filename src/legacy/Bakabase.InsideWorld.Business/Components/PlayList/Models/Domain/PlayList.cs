using System.Collections.Generic;

namespace Bakabase.InsideWorld.Business.Components.PlayList.Models.Domain
{
    public class PlayList
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<PlayListItem>? Items { get; set; }
        public int Interval { get; set; } = 3000;
        public int Order { get; set; }
    }
}