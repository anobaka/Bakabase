using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Modules.BulkModification.Abstractions.Models;

public record BulkModification
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<BulkModificationVariable>? Variables { get; set; }
    public ResourceSearchFilterGroup? Filter { get; set; }
    public List<BulkModificationProcess>? Processes { get; set; }
    public List<int>? FilteredResourceIds { get; set; }
}