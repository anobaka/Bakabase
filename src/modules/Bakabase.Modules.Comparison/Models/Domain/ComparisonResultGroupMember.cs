namespace Bakabase.Modules.Comparison.Models.Domain;

public record ComparisonResultGroupMember
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public int ResourceId { get; set; }
    public bool IsSuggestedPrimary { get; set; }
}
