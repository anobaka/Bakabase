using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bootstrap.Models.RequestModels;

namespace Bakabase.Modules.BulkModification.Models.Input
{
    public record BulkModificationResourceDiffsSearchInputModel : SearchRequestModel
    {
        public string? Path { get; set; }
    }
}
