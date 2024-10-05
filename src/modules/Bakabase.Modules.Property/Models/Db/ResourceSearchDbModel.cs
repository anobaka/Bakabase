using Bakabase.Abstractions.Models.Input;
using Bootstrap.Models.RequestModels;

namespace Bakabase.Modules.Property.Models.Db;

public record ResourceSearchDbModel: SearchRequestModel
{
    public ResourceSearchFilterGroupDbModel? Group { get; set; }
    public ResourceSearchOrderInputModel[]? Orders { get; set; }
    public string? Keyword { get; set; }
}