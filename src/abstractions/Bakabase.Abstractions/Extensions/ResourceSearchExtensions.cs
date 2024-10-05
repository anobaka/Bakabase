using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Models.View;
using Bakabase.Abstractions.Services;

namespace Bakabase.Abstractions.Extensions;

public static class ResourceSearchExtensions
{
    public static List<ResourceSearchFilter> ExtractFilters(this ResourceSearchFilterGroup group)
    {
        var filters = new List<ResourceSearchFilter>();
        if (group.Filters != null)
        {
            filters.AddRange(group.Filters);
        }

        if (group.Groups != null)
        {
            foreach (var g in group.Groups)
            {
                filters.AddRange(g.ExtractFilters());
            }
        }

        return filters;
    }

    public static ResourceSearch Copy(this ResourceSearch model)
    {
        return model with
        {
            Group = model.Group?.Copy(),
            Orders = model.Orders?.Select(o => o.Copy()).ToArray()
        };
    }

    public static ResourceSearchFilter Copy(this ResourceSearchFilter filter)
    {
        return filter with { };
    }

    public static ResourceSearchFilterGroup Copy(this ResourceSearchFilterGroup group)
    {
        return group with
        {
            Groups = group.Groups?.Select(g => g.Copy()).ToList(),
            Filters = group.Filters?.Select(f => f.Copy()).ToList()
        };
    }

    public static ResourceSearchOrderInputModel Copy(this ResourceSearchOrderInputModel model)
    {
        return model with { };
    }
}