using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

public class ResourceSourceLink
{
    public int Id { get; set; }
    public int ResourceId { get; set; }
    public ResourceSource Source { get; set; }
    public string SourceKey { get; set; } = null!;
    public DateTime CreateDt { get; set; }
}
