using Bakabase.InsideWorld.Models.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Models.Dtos
{
    public class PlaylistItemDto
    {
        public PlaylistItemType Type { get; set; }
        public int? ResourceId { get; set; }
        public string? File { get; set; }
        public TimeSpan? StartTime { get; set; }
        public TimeSpan? EndTime { get; set; }
    }
}