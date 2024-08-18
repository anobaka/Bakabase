using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Models.Models.Aos
{
    public record ResourceSearchFilterGroup
    {
        public Combinator Combinator { get; set; }
        public List<ResourceSearchFilterGroup>? Groups { get; set; }
        public List<ResourceSearchFilter>? Filters { get; set; }

        public ResourceSearchFilterGroup Copy()
        {
            return this with
            {
                Groups = Groups?.Select(g => g.Copy()).ToList(),
                Filters = Filters?.Select(f => f with { }).ToList()
            };
        }
    }
}