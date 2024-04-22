using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.StandardValue.Abstractions.Models
{
    public record MultilevelValue
    {
        public string Value { get; set; } = null!;
        public List<MultilevelValue>? Children { get; set; }
    }
}
