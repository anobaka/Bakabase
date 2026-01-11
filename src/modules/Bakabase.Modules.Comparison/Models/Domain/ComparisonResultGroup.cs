namespace Bakabase.Modules.Comparison.Models.Domain;

public record ComparisonResultGroup
{
    public int Id { get; set; }
    public int PlanId { get; set; }
    public int MemberCount { get; set; }
    public List<ComparisonResultGroupMember> Members { get; set; } = [];
    public DateTime CreatedAt { get; set; }
}
