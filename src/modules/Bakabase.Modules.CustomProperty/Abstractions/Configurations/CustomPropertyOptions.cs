using Bakabase.Modules.CustomProperty.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Extensions;
using Bakabase.Modules.StandardValue.Abstractions.Configurations;

namespace Bakabase.Modules.CustomProperty.Abstractions.Configurations;

public record CustomPropertyOptions
{
    public static
        Dictionary<CustomPropertyType,
            Dictionary<CustomPropertyType, List<(object? FromBizValue, object? ExpectedBizValue)>>>
        ExpectedConversions = StandardValueOptions.ExpectedConversions.SelectMany(
            x =>
            {
                var fromTypes = x.Key.GetCompatibleCustomPropertyTypes();
                return fromTypes.Select(fromType =>
                {
                    var toTypeValueMap = x.Value;
                    return (fromType, toTypeValueMap.SelectMany(y =>
                    {
                        var toTypes = y.Key.GetCompatibleCustomPropertyTypes() ?? [];
                        return toTypes.Select(toType => (toType, y.Value));
                    }).ToDictionary(d => d.toType, d => d.Value));
                });
            }).ToDictionary(d => d.fromType, d => d.Item2);
}