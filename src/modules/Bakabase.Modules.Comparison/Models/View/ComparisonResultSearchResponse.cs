using Bootstrap.Models.ResponseModels;

namespace Bakabase.Modules.Comparison.Models.View;

public class ComparisonResultSearchResponse : SearchResponse<ComparisonResultGroupViewModel>
{
    public int HiddenCount { get; set; }

    public ComparisonResultSearchResponse()
    {
    }

    public ComparisonResultSearchResponse(
        IEnumerable<ComparisonResultGroupViewModel> data,
        int totalCount,
        int hiddenCount,
        int pageIndex,
        int pageSize) : base(data, totalCount, pageIndex, pageSize)
    {
        HiddenCount = hiddenCount;
    }
}
