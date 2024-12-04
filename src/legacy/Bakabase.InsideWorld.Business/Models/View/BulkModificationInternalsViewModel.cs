using System.Collections.Generic;
using System.Linq;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Components;

namespace Bakabase.InsideWorld.Business.Models.View;

public record BulkModificationInternalsViewModel
{
    public Dictionary<int, int[]> DisabledPropertyKeys { get; set; } =
        BulkModificationInternals.DisabledPropertyKeys.ToDictionary(d => (int) d.Key, d => d.Value.ToArray());

    public HashSet<StandardValueType> SupportedStandardValueTypes { get; set; } =
        BulkModificationInternals.ProcessorMap.Keys.ToHashSet();
}