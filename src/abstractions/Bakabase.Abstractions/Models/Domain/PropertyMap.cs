using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Abstractions.Models.Domain;

public class PropertyMap : Dictionary<PropertyPool, Dictionary<int, Property>>
{
    public PropertyMap()
    {
    }

    public PropertyMap(IDictionary<PropertyPool, Dictionary<int, Property>> dictionary) : base(dictionary)
    {
    }

    public Property? GetProperty(PropertyPool pool, int id) => this.GetValueOrDefault(pool)?.GetValueOrDefault(id);
    public Property? GetCustomProperty(int id) => GetProperty(PropertyPool.Custom, id);
    public Property? GetInternalProperty(InternalProperty id) => GetProperty(PropertyPool.Internal, (int) id);
    public Property? GetReservedProperty(ReservedProperty id) => GetProperty(PropertyPool.Reserved, (int) id);
}