using System.Collections.Generic;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Dtos
{
    public record BulkModificationPutRequestModel(string Name, BulkModificationFilterGroup? Filter, List<BulkModificationProcess>? Processes, List<BulkModificationVariable>? Variables);
}
