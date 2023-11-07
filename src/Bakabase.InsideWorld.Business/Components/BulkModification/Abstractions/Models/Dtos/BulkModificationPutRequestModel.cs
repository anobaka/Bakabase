using System.Collections.Generic;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Dtos
{
    public record BulkModificationPutRequestModel(string Name, List<BulkModificationFilter> Filters);
}
