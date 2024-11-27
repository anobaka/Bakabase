using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Models.Constants;

namespace Bakabase.Modules.BulkModification.Models.Db
{
    public record BulkModificationVariableDbModel
    {
        public PropertyPool PropertyPool { get; set; }
        public int PropertyId { get; set; }
        public string Key { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string? Preprocesses { get; set; }
    }
}
