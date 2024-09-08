using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;

namespace Bakabase.Abstractions.Models.Domain
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