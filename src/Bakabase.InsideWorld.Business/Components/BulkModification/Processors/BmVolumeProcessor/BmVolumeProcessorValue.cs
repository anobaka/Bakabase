using System.Collections.Generic;
using Bakabase.InsideWorld.Business.Components.TextProcessing;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Processors.BmVolumeProcessor
{
    public record BmVolumeProcessorValue: JsonSerializableValue<BmVolumeProcessorValue>
    {
        public BmVolumeProcessorOperation Operation { get; set; }
        public Dictionary<BmVolumeProcessorProperty, TextProcessValue>? PropertyModifications { get; set; }
    }
}
