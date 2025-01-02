using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Extensions;

public static class PropertyExtensions
{
    public static PropertyMap ToMap(this IEnumerable<Property> properties) => new PropertyMap(properties
        .GroupBy(x => x.Pool).ToDictionary(x => x.Key, x => x.ToDictionary(y => y.Id, y => y)));
}