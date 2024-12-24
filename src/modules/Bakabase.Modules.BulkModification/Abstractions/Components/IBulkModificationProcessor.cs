namespace Bakabase.Modules.BulkModification.Abstractions.Components
{
    public interface IBulkModificationProcessor
    {
        object? Process(object? currentValue, int operation, IBulkModificationProcessorOptions? options);
    }
}