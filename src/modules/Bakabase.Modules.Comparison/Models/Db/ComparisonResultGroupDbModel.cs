using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.Comparison.Models.Db;

public record ComparisonResultGroupDbModel
{
    [Key]
    public int Id { get; set; }
    public int PlanId { get; set; }
    public int MemberCount { get; set; }
    public bool IsHidden { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
