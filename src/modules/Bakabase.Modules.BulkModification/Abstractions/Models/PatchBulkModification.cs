using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Modules.BulkModification.Abstractions.Models;

public record PatchBulkModification
{
    public string? Name { get; set; }
    public bool? IsActive { get; set; }
    public List<BulkModificationVariable>? Variables { get; set; }
    public ResourceSearchFilterGroup? Filter { get; set; }
    public List<BulkModificationProcess>? Processes { get; set; }
}