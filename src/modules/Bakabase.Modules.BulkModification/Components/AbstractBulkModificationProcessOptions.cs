using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Components;

namespace Bakabase.Modules.BulkModification.Components;

public abstract record AbstractBulkModificationProcessOptions<TProcessorOptions> : IBulkModificationProcessOptions
    where TProcessorOptions : IBulkModificationProcessorOptions
{
    public IBulkModificationProcessorOptions? ConvertToProcessorOptions(
        Dictionary<string, (StandardValueType Type, object? Value)>? variableMap,
        Bakabase.Abstractions.Models.Domain.Property? property)
    {
        return ConvertToProcessorOptionsInternal(variableMap, property);
    }

    protected abstract TProcessorOptions ConvertToProcessorOptionsInternal(
        Dictionary<string, (StandardValueType Type, object? Value)>? variableMap,
        Bakabase.Abstractions.Models.Domain.Property? property);
}