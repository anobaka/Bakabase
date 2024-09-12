using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Abstractions.Models.Domain
{
    public record ResourceSearchFilter
    {
        public ResourcePropertyType PropertyType { get; set; }
        public int PropertyId { get; set; }
        public SearchOperation Operation { get; set; }
        public string? DbValue { get; set; }
        public string? BizValue { get; set; }
    }
}