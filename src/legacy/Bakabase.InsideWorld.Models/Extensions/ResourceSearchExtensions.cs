using Bakabase.InsideWorld.Models.Models.Aos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Extensions
{
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
    }
}