using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;

namespace Bakabase.Modules.Search.Models.Db;

public record ResourceSearchDbModel
{
    public ResourceSearchFilterGroupDbModel? Group { get; set; }
    public ResourceSearchOrderInputModel[]? Orders { get; set; }
    public string? Keyword { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public ResourceTag[]? Tags { get; set; }
}