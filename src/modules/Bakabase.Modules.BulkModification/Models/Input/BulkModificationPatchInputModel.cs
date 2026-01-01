using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.BulkModification.Abstractions.Models;

namespace Bakabase.Modules.BulkModification.Models.Input;

public record PatchBulkModification
{
    public string? Name { get; set; }
    public bool? IsActive { get; set; }
    public List<BulkModificationVariable>? Variables { get; set; }
    public ResourceSearch? Search { get; set; }
    public List<BulkModificationProcess>? Processes { get; set; }
}