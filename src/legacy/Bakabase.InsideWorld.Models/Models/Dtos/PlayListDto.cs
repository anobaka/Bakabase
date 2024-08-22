using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Entities;

namespace Bakabase.InsideWorld.Models.Models.Dtos
{
    public class PlaylistDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<PlaylistItemDto>? Items { get; set; }
        public int Interval { get; set; } = 3000;
        public int Order { get; set; }
    }
}