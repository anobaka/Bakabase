using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Properties.Multilevel
{
    internal class MultilevelPropertyOptions
    {
        public List<MultilevelDataOptions>? Data { get; set; }
        public string? DefaultValue { get; set; }
    }
}