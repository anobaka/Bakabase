namespace Bakabase.InsideWorld.Business.Components.BulkModification.Processors.SingleValueProcessor
{
    public record SimpleValueProcessorValue<T>: JsonProcessValue<T>
    {
        public SimpleValueProcessorOperation Operation { get; init; }
        public T? Value { get; init; }
    }
}
