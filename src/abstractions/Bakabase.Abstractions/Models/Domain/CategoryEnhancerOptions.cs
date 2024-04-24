using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain
{
    public record CategoryEnhancerOptions
    {
        public int Id { get; set; }
        public int CategoryId { get; set; }
        public EnhancerId EnhancerId { get; set; }
        public Dictionary<int, EnhancerTargetOptions>? TargetOptionsMap { get; set; }
    }
}
