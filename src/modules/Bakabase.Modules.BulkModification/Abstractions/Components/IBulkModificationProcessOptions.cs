using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Models.Constants;

namespace Bakabase.Modules.BulkModification.Abstractions.Components;

/// <summary>
/// Options for a bulk modification process which may have more concepts than <see cref="IBulkModificationProcessorOptions"/> such as <see cref="BulkModificationProcessorValueType"/>.
/// </summary>
public interface IBulkModificationProcessOptions
{
    IBulkModificationProcessorOptions? ConvertToProcessorOptions(
        Dictionary<string, (StandardValueType Type, object? Value)>? variableMap,
        Bakabase.Abstractions.Models.Domain.Property? property);
}