using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.FileMover.Models
{
    public record FileMovingProgress
    {
        public string Source { get; set; } = null!;
        public string Target { get; set; } = null!;
        public int Percentage { get; set; }
        public string? Error { get; set; }
        public bool Moving { get; set; }
    }
}