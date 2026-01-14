namespace Bakabase.Modules.Comparison.Models.View;

public record ComparisonResultGroupMemberViewModel
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public int ResourceId { get; set; }
    public bool IsSuggestedPrimary { get; set; }
}
