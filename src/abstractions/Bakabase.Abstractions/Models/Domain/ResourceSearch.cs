using Bakabase.Abstractions.Models.Input;
using Bootstrap.Models.RequestModels;

namespace Bakabase.Abstractions.Models.Domain;

public record ResourceSearch : SearchRequestModel
{
    public ResourceSearchFilterGroup? Group { get; set; }
    public ResourceSearchOrderInputModel[]? Orders { get; set; }
    public string? Keyword { get; set; }
}