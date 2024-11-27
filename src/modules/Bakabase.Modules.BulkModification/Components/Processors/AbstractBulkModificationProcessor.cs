using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Components;

namespace Bakabase.Modules.BulkModification.Components.Processors;

public abstract class AbstractBulkModificationProcessor<TValue, TOperation, TOptions> : IBulkModificationProcessor
    where TOptions : class, IBulkModificationProcessorOptions
{
    public abstract StandardValueType ValueType { get; }

    public object? Process(object? currentValue, int operation, IBulkModificationProcessorOptions? options)
    {
        var typedValue = currentValue is TValue v ? v : default;
        var typedOperation = (TOperation) (object) operation;
        var newOptions = options as TOptions;

        return ProcessInternal(typedValue, typedOperation, newOptions);
    }

    protected abstract TValue? ProcessInternal(TValue? currentValue, TOperation operation, TOptions? options);
}