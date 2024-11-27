using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Property;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Models.Constants;

namespace Bakabase.Modules.BulkModification.Models.Db
{
    public record BulkModificationProcessDbModel : IPropertyKeyHolder
    {
        public PropertyPool PropertyPool { get; set; }
        public int PropertyId { get; set; }
        public string? Steps { get; set; }
    }
}