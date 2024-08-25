using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.StandardValue.Models.Domain;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Extensions;

namespace Bakabase.Modules.StandardValue.Components;

public class StandardValueHelper(ILogger<StandardValueHelper> logger) : IStandardValueHelper
{
    public object? Deserialize(string? serializedValue, StandardValueType valueType)
    {
        try
        {
            return serializedValue?.DeserializeAsStandardValue(valueType);
        }
        catch (Exception e)
        {
            if (e is ArgumentOutOfRangeException {ParamName: nameof(valueType)})
            {
                return null;
            }

            logger.LogWarning(e,
                $"Failed to deserialize standard value. {nameof(serializedValue)}:{serializedValue}. {nameof(valueType)}:{valueType}.");
            return null;
        }
    }

    public T? Deserialize<T>(string? serializedValue, StandardValueType valueType)
    {
        var v = Deserialize(serializedValue, valueType);
        return v is T v1 ? v1 : default;
    }

    public string? Serialize(object? rawValue, StandardValueType valueType) =>
        rawValue?.SerializeAsStandardValue(valueType);
}