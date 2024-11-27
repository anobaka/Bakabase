using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Extensions;

public static class PropertyExtensions
{
    public static Dictionary<PropertyPool, Dictionary<int, Property>> ToMap(this IEnumerable<Property> properties) =>
        properties
            .GroupBy(x => x.Pool)
            .ToDictionary(x => x.Key, x => x.ToDictionary(y => y.Id, y => y));
}