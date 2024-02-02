using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models
{
    public record BulkModificationVariable
    {
        public string Key { get; set; } = null!;
        public string? Name { get; set; }
        public BulkModificationVariableSource Source { get; set; }
        public string? Find { get; set; }
        public string Value { get; set; } = null!;
    }
}
