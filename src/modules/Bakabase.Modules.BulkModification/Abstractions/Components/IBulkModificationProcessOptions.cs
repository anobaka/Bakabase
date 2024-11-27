using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.BulkModification.Abstractions.Components;

public interface IBulkModificationProcessOptions
{
    IBulkModificationProcessorOptions? ConvertToProcessorOptions(Dictionary<string, (StandardValueType Type, object? Value)>? variableMap, Bakabase.Abstractions.Models.Domain.Property? property);
}