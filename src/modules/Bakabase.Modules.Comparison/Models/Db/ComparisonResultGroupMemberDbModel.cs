using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.Comparison.Models.Db;

public record ComparisonResultGroupMemberDbModel
{
    [Key]
    public int Id { get; set; }
    public int GroupId { get; set; }
    public int ResourceId { get; set; }
    public bool IsSuggestedPrimary { get; set; }
}
