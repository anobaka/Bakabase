using System;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.PlayList.Models.Domain
{
    public class PlayListItem
    {
        public PlaylistItemType Type { get; set; }
        public int? ResourceId { get; set; }
        public string? File { get; set; }
        public TimeSpan? StartTime { get; set; }
        public TimeSpan? EndTime { get; set; }
    }
}