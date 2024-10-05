using Bakabase.Abstractions.Models.Input;
using Bootstrap.Models.RequestModels;

namespace Bakabase.Service.Models.Input;

public record ResourceSearchInputModel : SearchRequestModel
{
    public ResourceSearchFilterGroupInputModel? Group { get; set; }
    public ResourceSearchOrderInputModel[]? Orders { get; set; }
    public string? Keyword { get; set; }
    public bool SaveSearchCriteria { get; set; }
}