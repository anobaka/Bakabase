namespace Bakabase.Modules.Comparison.Models.Input;

public record ComparisonResultSearchInputModel
{
    public int PageIndex { get; set; } = 0;
    public int PageSize { get; set; } = 20;
    public int MinMemberCount { get; set; } = 2;
    public bool IncludeHidden { get; set; } = false;
}
