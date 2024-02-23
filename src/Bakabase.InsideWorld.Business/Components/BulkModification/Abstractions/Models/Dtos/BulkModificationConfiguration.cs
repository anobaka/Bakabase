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
        public List<PropertyOptions> Properties { get; set; } = new List<PropertyOptions>();

        public record PropertyOptions
        {
            public BulkModificationProperty Property { get; init; }

            public List<BulkModificationFilterOperation> AvailableOperations { get; set; } =
                new List<BulkModificationFilterOperation>();
        }
    }
}