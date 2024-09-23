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

        public bool IsValid
        {
            get
            {
                return Operation switch
                {
                    SearchOperation.Equals or SearchOperation.NotEquals or SearchOperation.Contains
                        or SearchOperation.NotContains or SearchOperation.StartsWith or SearchOperation.NotStartsWith
                        or SearchOperation.EndsWith or SearchOperation.NotEndsWith or SearchOperation.GreaterThan
                        or SearchOperation.LessThan or SearchOperation.GreaterThanOrEquals
                        or SearchOperation.LessThanOrEquals or SearchOperation.In or SearchOperation.NotIn
                        or SearchOperation.Matches or SearchOperation.NotMatches => DbValue != null,
                    SearchOperation.IsNull or SearchOperation.IsNotNull => true,
                    _ => throw new ArgumentOutOfRangeException()
                };
            }
        }
    }
}