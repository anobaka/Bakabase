namespace Bakabase.Modules.Comparison.Models.View;

public record ComparisonResultGroupViewModel
{
    public int Id { get; set; }
    public int PlanId { get; set; }
    public int MemberCount { get; set; }
    public bool IsHidden { get; set; }
    public List<ComparisonResultGroupMemberViewModel>? Members { get; set; }
    public List<string>? PreviewCovers { get; set; }
    public DateTime CreatedAt { get; set; }
}
