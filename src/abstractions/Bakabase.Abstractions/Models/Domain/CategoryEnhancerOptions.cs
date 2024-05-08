namespace Bakabase.Abstractions.Models.Domain
{
    public record CategoryEnhancerOptions
    {
        public int Id { get; set; }
        public int CategoryId { get; set; }
        public int EnhancerId { get; set; }
        public bool Active { get; set; }
    }
}