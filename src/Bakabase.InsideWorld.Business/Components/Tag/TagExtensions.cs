using System.Collections.Generic;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Components.Tag;

public static class TagExtensions
{
    public static List<Abstractions.Models.Domain.Tag>? AsTags(this Models.Domain.Resource.Property.PropertyValue value)
    {
        return value.Value as List<Abstractions.Models.Domain.Tag>;
    }
}