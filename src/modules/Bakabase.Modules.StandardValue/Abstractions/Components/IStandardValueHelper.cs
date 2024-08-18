using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.StandardValue.Abstractions.Components;

public interface IStandardValueHelper
{
    object? Deserialize(string? serializedValue, StandardValueType valueType);
    T? Deserialize<T>(string? serializedValue, StandardValueType valueType);
    string? Serialize(object? rawValue);
}