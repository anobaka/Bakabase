using Bakabase.Abstractions.Components.Search;
using Bakabase.Abstractions.Models.Domain;
using NPOI.OpenXmlFormats.Spreadsheet;

namespace Bakabase.Abstractions.Extensions;

public static class ResourceSearchExtensions
{
    public static List<TFilter> ExtractFilters<TGroup, TFilter>(this IFilterExtractable<TGroup, TFilter> group)
        where TGroup : IFilterExtractable<TGroup, TFilter>
    {
        var filters = new List<TFilter>();
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
}