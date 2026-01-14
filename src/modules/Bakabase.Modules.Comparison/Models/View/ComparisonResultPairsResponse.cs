namespace Bakabase.Modules.Comparison.Models.View;

public class ComparisonResultPairsResponse
{
    public List<ComparisonResultPairViewModel> Pairs { get; set; } = new();
    public int TotalCount { get; set; }
    public bool IsTruncated { get; set; }
    public int Limit { get; set; }

    public ComparisonResultPairsResponse(List<ComparisonResultPairViewModel> pairs, int totalCount, int limit)
    {
        Pairs = pairs;
        TotalCount = totalCount;
        Limit = limit;
        IsTruncated = totalCount > limit;
    }
}
