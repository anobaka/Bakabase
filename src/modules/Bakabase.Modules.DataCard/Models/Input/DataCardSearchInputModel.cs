using Bootstrap.Models.RequestModels;

namespace Bakabase.Modules.DataCard.Models.Input;

public record DataCardSearchInputModel : SearchRequestModel
{
    public int? TypeId { get; set; }
    public string? Keyword { get; set; }
}
