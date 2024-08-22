using System.Collections.Generic;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Processors.Infrastructures.BmMultipleValueProcessor
{
    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="TKey">Key type of existed data</typeparam>
    /// <typeparam name="TNewData">Key type for new data, which presents business key.</typeparam>
    /// <typeparam name="TChildProcessorValue"></typeparam>
    public record BmMultipleValueProcessorValue<TKey, TNewData, TChildProcessorValue> : JsonSerializableValue<BmMultipleValueProcessorValue<TKey, TNewData, TChildProcessorValue>>
    {
        public BmMultipleValueProcessorOperation Operation { get; init; }
        public BmMultipleValueProcessorFilterBy? FilterBy { get; set; }
        public string? Find { get; set; }
        public List<TKey>? SelectedKeys { get; init; }
        public List<TNewData>? NewData { get; set; }
        public TChildProcessorValue? ChildProcessorValue { get; init; }
    }
}