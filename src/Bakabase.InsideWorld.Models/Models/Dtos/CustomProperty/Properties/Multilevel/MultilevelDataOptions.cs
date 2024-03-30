using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Properties.Multilevel
{
    public record MultilevelDataOptions
    {
        public string Id { get; set; } = null!;
        public string Value { get; set; } = null!;
        public string Color { get; set; } = null!;
        public MultilevelDataOptions[]? Children { get; set; }
    }
}