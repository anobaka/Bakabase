using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.Abstractions.Models.View
{
    public record CustomPropertyTypeConversionLossViewModel
    {
        public int TotalDataCount { get; set; }
        public int IncompatibleDataCount { get; set; }
        public Dictionary<int, string[]>? LossData { get; set; }
    }
}
