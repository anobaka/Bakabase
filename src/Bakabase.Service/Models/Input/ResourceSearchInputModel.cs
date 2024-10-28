using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Models.Input;
using Bootstrap.Models.RequestModels;

namespace Bakabase.Service.Models.Input;

public record ResourceSearchInputModel
{
    public ResourceSearchFilterGroupInputModel? Group { get; set; }
    public ResourceSearchOrderInputModel[]? Orders { get; set; }
    public string? Keyword { get; set; }
    public int PageSize { get; set; }
    public int Page { get; set; }

    public void StandardPageable()
    {
        if (PageSize <= 0)
        {
            PageSize = InternalOptions.DefaultSearchPageSize;
        }

        if (Page <= 0)
        {
            Page = 1;
        }
    }
}