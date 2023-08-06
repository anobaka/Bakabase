using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Models.Entities
{
    public class Playlist
    {
        public int Id { get; set; }
        [Required]
        public string Name { get; set; }
        public string? ItemsJson { get; set; }
        public int Interval { get; set; } = 3000;
        public int Order { get; set; }
    }
}