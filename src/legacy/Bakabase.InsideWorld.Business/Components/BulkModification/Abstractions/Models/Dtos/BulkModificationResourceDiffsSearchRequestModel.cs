using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bootstrap.Models.RequestModels;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Dtos
{
    public record BulkModificationResourceDiffsSearchRequestModel : SearchRequestModel
    {
        public int BmId { get; set; }
        public string? Path { get; set; }
    }
}
