using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Db
{
    public record CategoryEnhancerOptions
    {
        public int Id { get; set; }
        public int CategoryId { get; set; }
        public int EnhancerId { get; set; }
        public bool Active { get; set; }
        public string? Options { get; set; }
    }
}
