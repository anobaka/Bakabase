namespace Bakabase.InsideWorld.Business.Components.BulkModification.Processors.Infrastructures.BmSimpleValueProcessor
{
    public record BmSimpleValueProcessorValue<T> : JsonSerializableValue<BmSimpleValueProcessorValue<T>>
    {
        public BmSimpleValueProcessorOperation Operation { get; init; }
        public T? Value { get; init; }
    }
}
