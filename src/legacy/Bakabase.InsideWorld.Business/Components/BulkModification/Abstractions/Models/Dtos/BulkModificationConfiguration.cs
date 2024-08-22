using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Dtos
{
    public record BulkModificationConfiguration
    {
        public List<PropertyOptions> Properties { get; set; } = [];

        public record PropertyOptions
        {
            public BulkModificationFilterableProperty Property { get; init; }

            public HashSet<BulkModificationFilterOperation> AvailableOperations { get; set; } = [];
        }
    }
}