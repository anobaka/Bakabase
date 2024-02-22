using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models
{
    public record BulkModificationProcess
    {
        public BulkModificationProperty Property { get; set; }
        public string? PropertyKey { get; set; }

        /// <summary>
        /// Serialized
        /// </summary>
        public string? Value { get; set; }
    }
}